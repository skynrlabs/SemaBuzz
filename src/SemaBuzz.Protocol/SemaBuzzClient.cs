using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace SemaBuzz.Protocol;

/// <summary>
/// Dials out to a peer's SemaBuzz endpoint and maintains the wire.
/// ECDH P-256 is performed during every handshake so all sessions are
/// encrypted with a fresh AES-256-GCM session key -- no passphrase required.
/// </summary>
public sealed class SemaBuzzClient : IDisposable
{
    private UdpClient? _udp;
    private ClientWebSocket? _wsClient;          // non-null when in WebSocket relay mode
    private Func<byte[], Task>? _wsSend;          // send delegate set after relay pairing
    private readonly SemaphoreSlim _wsSendLock = new(1, 1); // serializes concurrent ws.SendAsync calls
    private IPEndPoint? _peer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private string? _lastStateMessage;

    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);
    private const int MaxBatchPacketsPerSend = 8;
    private static readonly TimeSpan ApprovalWaitTimeout = TimeSpan.FromSeconds(60);

    public event EventHandler<SemaBuzzPacketEventArgs>? PacketReceived;
    public event EventHandler<SemaBuzzWireStateEventArgs>? WireStateChanged;
    public event EventHandler<SemaBuzzMetadataEventArgs>? MetadataReceived;
    public event EventHandler<SemaBuzzUrlPushEventArgs>? UrlPushReceived;
    public event EventHandler<SemaBuzzDrawEventArgs>? DrawReceived;

    // -- File transfer events --
    public event EventHandler<SemaBuzzFileOfferEventArgs>? FileOfferReceived;
    public event EventHandler<SemaBuzzFileChunkEventArgs>? FileChunkReceived;
    public event EventHandler<SemaBuzzFileControlEventArgs>? FileAcceptReceived;
    public event EventHandler<SemaBuzzFileControlEventArgs>? FileRejectReceived;
    public event EventHandler<SemaBuzzFileControlEventArgs>? FileCompleteReceived;
    public event EventHandler<SemaBuzzFileControlEventArgs>? FileCancelReceived;

    public SemaBuzzWireState State { get; private set; } = SemaBuzzWireState.Cold;
    public SemaBuzzShield? Shield { get; private set; }
    public bool IsRelayMode => _wsSend != null;

    private volatile bool _waitingForApproval;
    private NetworkAddressChangedEventHandler? _networkChangeHandler;

    /// <summary>Fired when the host sends a HandshakeHold, giving the app a chance to send pre-approval metadata so the host can show the peer's handle in the approval dialog.</summary>
    public event EventHandler? HandshakeHoldReceived;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Generate an ephemeral ECDH P-256 key pair for this session.
        // The private key never leaves this object; the public key is sent to the host.
        using var localEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var localPubKeyBytes = localEcdh.PublicKey.ExportSubjectPublicKeyInfo();

        // Resolve host to an IPv4 address. IPAddress.Parse only handles numeric literals;
        // use DNS for hostnames. Prefer IPv4 so the NAT behaviour is predictable.
        IPAddress address;
        if (IPAddress.TryParse(host, out var parsed))
        {
            address = parsed;
        }
        else
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            var ipv4 = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 != null)
                address = ipv4;
            else
                address = addresses[0];
        }

        _peer = new IPEndPoint(address, port);
        _udp = new UdpClient();
        _udp.Connect(_peer);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SubscribeNetworkChange();

        SetState(SemaBuzzWireState.Warming, $"Dialing {host}:{port}...");

        // Start receive loop BEFORE sending so the ACK is never missed.
        var receiveTask = ReceiveLoopAsync(localEcdh, _cts.Token);
        var keepaliveTask = KeepaliveLoopAsync(_cts.Token);
        var timeoutTask = HandshakeTimeoutAsync(_cts.Token);
        var retransmitTask = HandshakeRetransmitAsync(localPubKeyBytes, _cts.Token);

        // Send our public key to the host -- this IS the handshake initiation.
        await _udp.SendAsync(SemaBuzzKeyExchange.Serialize(localPubKeyBytes));

        await Task.WhenAll(receiveTask, keepaliveTask, timeoutTask, retransmitTask);
    }

    /// <summary>
    /// Dial into a relay room by token via WebSocket. Connects to the relay server,
    /// sends JoinDial, waits for Paired, then runs the full ECDH handshake through
    /// the relay. The relay forwards all subsequent binary frames transparently.
    /// </summary>
    public async Task ConnectViaRelayAsync(string relayUri, string token,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var localEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var localPubKeyBytes = localEcdh.PublicKey.ExportSubjectPublicKeyInfo();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _wsClient = new ClientWebSocket();
        _wsClient.Options.KeepAliveInterval = TimeSpan.FromSeconds(8);
        _wsClient.Options.KeepAliveTimeout = TimeSpan.FromSeconds(4);
        SubscribeNetworkChange();

        try { await _wsClient.ConnectAsync(new Uri(relayUri), _cts.Token); }
        catch (Exception ex)
        {
            SetState(SemaBuzzWireState.Dead, $"relay unreachable: {ex.Message}");
            return;
        }

        SetState(SemaBuzzWireState.Warming, $"Joining relay room {token}...");

        var join = SemaBuzzRelayPacket.Build(SemaBuzzRelayPacketType.JoinDial, token);
        await _wsClient.SendAsync(join, WebSocketMessageType.Binary, true, _cts.Token);

        // Wait for Paired (30 s timeout).
        var ctrlBuf = new byte[64];
        bool paired = false;
        try
        {
            using var pairTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            pairTimeout.CancelAfter(TimeSpan.FromSeconds(30));

            while (!pairTimeout.Token.IsCancellationRequested)
            {
                var r = await _wsClient.ReceiveAsync(ctrlBuf, pairTimeout.Token);
                if (r.MessageType == WebSocketMessageType.Close) break;
                var p = SemaBuzzRelayPacket.Parse(ctrlBuf[..r.Count]);
                if (p == null) continue;
                if (p.Value.Type == SemaBuzzRelayPacketType.RelayError)
                {
                    SetState(SemaBuzzWireState.Dead, "token not found -- host may not be waiting");
                    _cts.Cancel(); return;
                }
                if (p.Value.Type == SemaBuzzRelayPacketType.Paired) { paired = true; break; }
            }
        }
        catch (OperationCanceledException) { /* handled below */ }

        if (!paired)
        {
            SetState(SemaBuzzWireState.Dead, "relay did not respond in time");
            _cts.Cancel(); return;
        }

        // -- STUN / UDP hole-punch attempt -----------------------------------------
        UdpClient? directUdp = null;
        IPEndPoint? peerDirectEp = null;
        try
        {
            directUdp = new UdpClient(0);
            var myExternalEp = await SemaBuzzStun.DiscoverAsync(directUdp, _cts.Token);
            if (myExternalEp != null)
            {
                var punchReady = SemaBuzzRelayPacket.BuildPunchReady(token, myExternalEp);
                await _wsClient.SendAsync(punchReady, WebSocketMessageType.Binary, true, _cts.Token);

                using var peerAddrCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                peerAddrCts.CancelAfter(TimeSpan.FromSeconds(5));
                var ctrlBuf2 = new byte[32];
                try
                {
                    while (!peerAddrCts.Token.IsCancellationRequested)
                    {
                        var r2 = await _wsClient.ReceiveAsync(ctrlBuf2, peerAddrCts.Token);
                        if (r2.MessageType == WebSocketMessageType.Close) break;
                        // Only consider complete relay control frames -- skip any partial
                        // reads or non-relay application data that arrived early.
                        if (r2.Count < SemaBuzzRelayPacket.PunchPacketSize) continue;
                        if (!r2.EndOfMessage) continue;
                        if (SemaBuzzRelayPacket.IsRelayPacket(ctrlBuf2)
                            && (SemaBuzzRelayPacketType)ctrlBuf2[3] == SemaBuzzRelayPacketType.PeerAddress)
                        {
                            peerDirectEp = SemaBuzzRelayPacket.ParseEndpoint(ctrlBuf2[..r2.Count]);
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) { /* punch exchange timed out */ }

                if (peerDirectEp != null)
                {
                    SetState(SemaBuzzWireState.Warming, "Trying direct UDP...");
                    var directEp = await SemaBuzzPunchThrough.TryAsync(
                        directUdp, peerDirectEp, TimeSpan.FromSeconds(4), _cts.Token);

                    if (directEp != null)
                    {
                        // Direct path confirmed -- switch to UDP.
                        _udp = directUdp;
                        _peer = directEp;
                        directUdp = null;
                        _udp.Connect(_peer);

                        try { await _wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "direct", default); } catch { }
                        _wsClient.Dispose();
                        _wsClient = null;

                        SetState(SemaBuzzWireState.Warming, "Direct UDP -- completing handshake...");

                        var rcvTask = ReceiveLoopAsync(localEcdh, _cts.Token);
                        var kaTask = KeepaliveLoopAsync(_cts.Token);
                        var toTask = HandshakeTimeoutAsync(_cts.Token);
                        var rtTask = HandshakeRetransmitAsync(localPubKeyBytes, _cts.Token);

                        await _udp.SendAsync(SemaBuzzKeyExchange.Serialize(localPubKeyBytes));
                        await Task.WhenAll(rcvTask, kaTask, toTask, rtTask);
                        return; // done
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* STUN/punch failed -- proceed with relay */ }
        finally
        {
            if (directUdp != null)
                directUdp.Dispose();
        }
        // -- end punch-through attempt ---------------------------------------------

        // Wire up the WebSocket send delegate used by all public send methods.
        // Every message is prefixed with a 4-byte big-endian length so the receiver can
        // reassemble complete logical messages even when the relay forwards partial TCP
        // segments as separate WebSocket messages (relay-independent framing).
        var ws = _wsClient!;
        _wsSend = async data =>
        {
            if (ws.State != WebSocketState.Open) return;
            var framed = new byte[4 + data.Length];
            framed[0] = (byte)(data.Length >> 24);
            framed[1] = (byte)(data.Length >> 16);
            framed[2] = (byte)(data.Length >> 8);
            framed[3] = (byte)(data.Length & 0xFF);
            data.CopyTo(framed, 4);
            await _wsSendLock.WaitAsync();
            try
            {
                if (ws.State != WebSocketState.Open) return;
                await ws.SendAsync(framed, WebSocketMessageType.Binary, true, _cts!.Token);
            }
            catch { }
            finally { _wsSendLock.Release(); }
        };

        SetState(SemaBuzzWireState.Warming, "Relay paired -- completing handshake...");

        var receiveTask = WsReceiveLoopAsync(ws, localEcdh, _cts.Token);
        var keepaliveTask = KeepaliveLoopAsync(_cts.Token);
        var timeoutTask = HandshakeTimeoutAsync(_cts.Token);
        var retransmitTask = HandshakeRetransmitAsync(localPubKeyBytes, _cts.Token);

        await _wsSend(SemaBuzzKeyExchange.Serialize(localPubKeyBytes));

        await Task.WhenAll(receiveTask, keepaliveTask, timeoutTask, retransmitTask);
    }

    /// <summary>
    /// Receive loop for WebSocket relay mode -- mirrors ReceiveLoopAsync but reads
    /// from a WebSocket frame stream instead of UDP datagrams.
    /// </summary>
    private async Task WsReceiveLoopAsync(WebSocket ws, ECDiffieHellman localEcdh, CancellationToken ct)
    {
        var buf = new byte[65_536];
        // Persists across calls: accumulates partial relay forwards until a complete
        // length-prefixed frame is available (see ReceiveRelaySizedMessageAsync).
        var leftovers = new MemoryStream(65_536);
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var data = await ReceiveRelaySizedMessageAsync(ws, buf, leftovers, ct);
                if (data == null) break;
                const int MaxPayload = 16_384;
                if (data.Length < SemaBuzzPacket.WireSize || data.Length > MaxPayload) continue;
                if (SemaBuzzRelayPacket.IsRelayPacket(data)) continue; // stray control frame

                // -- ECDH key exchange --------------------------------------------------
                if (SemaBuzzKeyExchange.IsKeyExchangePacket(data))
                {
                    if (Shield != null) continue;
                    var peerPub = SemaBuzzKeyExchange.Deserialize(data);
                    if (peerPub == null) continue;
                    using var peerEcdh = ECDiffieHellman.Create();
                    peerEcdh.ImportSubjectPublicKeyInfo(peerPub, out _);
                    var raw = localEcdh.DeriveRawSecretAgreement(peerEcdh.PublicKey);
                    Shield = SemaBuzzShield.FromEcdhSecret(raw);
                    continue;
                }

                // -- Decrypt --------------------------------------------------
                if (Shield != null)
                {
                    var dec = Shield.Decrypt(data);
                    if (dec == null)
                    {
                        // Decryption failure after handshake means a session key
                        // mismatch -- surface it rather than silently dropping.
                        if (State is SemaBuzzWireState.Secured or SemaBuzzWireState.Live)
                        {
                            SetState(SemaBuzzWireState.Dead, "session key mismatch");
                            _cts?.Cancel();
                        }
                        return;
                    }
                    data = dec;
                }

                // -- Metadata --------------------------------------------------
                if (SemaBuzzMetadata.IsMetadataPacket(data))
                {
                    var meta = SemaBuzzMetadata.Deserialize(data);
                    if (meta.HasValue)
                    {
                        var metaHandler = MetadataReceived;
                        if (metaHandler != null)
                            metaHandler(this, new SemaBuzzMetadataEventArgs(meta.Value.Handle, meta.Value.AvatarPng, meta.Value.Status, meta.Value.StatusMessage));
                    }
                    continue;
                }

                // -- URL push --------------------------------------------------
                if (SemaBuzzUrlPush.IsUrlPushPacket(data))
                {
                    var url = SemaBuzzUrlPush.Deserialize(data);
                    if (url != null)
                    {
                        var urlHandler = UrlPushReceived;
                        if (urlHandler != null)
                            urlHandler(this, new SemaBuzzUrlPushEventArgs(url));
                    }
                    continue;
                }

                // -- Draw event --------------------------------------------------
                if (SemaBuzzDraw.IsDrawPacket(data))
                {
                    var ev = SemaBuzzDraw.Deserialize(data);
                    if (ev.HasValue)
                        DrawReceived?.Invoke(this, new SemaBuzzDrawEventArgs(ev.Value));
                    continue;
                }

                // -- File transfer variable-length packets -----------------------
                if (SemaBuzzFileTransfer.IsFileOfferPacket(data))
                {
                    var offer = SemaBuzzFileTransfer.DeserializeFileOffer(data);
                    if (offer.HasValue)
                        FileOfferReceived?.Invoke(this, new SemaBuzzFileOfferEventArgs(
                            offer.Value.TransferId, offer.Value.Filename, offer.Value.FileSize,
                            offer.Value.TotalChunks, offer.Value.Sha256));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileChunkPacket(data))
                {
                    var chunk = SemaBuzzFileTransfer.DeserializeFileChunk(data);
                    if (chunk.HasValue)
                        FileChunkReceived?.Invoke(this, new SemaBuzzFileChunkEventArgs(
                            chunk.Value.TransferId, chunk.Value.ChunkIdx, chunk.Value.Data));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileAcceptPacket(data))
                {
                    var tid = SemaBuzzFileTransfer.DeserializeTransferId(data);
                    if (tid.HasValue) FileAcceptReceived?.Invoke(this, new SemaBuzzFileControlEventArgs(tid.Value));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileRejectPacket(data))
                {
                    var tid = SemaBuzzFileTransfer.DeserializeTransferId(data);
                    if (tid.HasValue) FileRejectReceived?.Invoke(this, new SemaBuzzFileControlEventArgs(tid.Value));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileCompletePacket(data))
                {
                    var tid = SemaBuzzFileTransfer.DeserializeTransferId(data);
                    if (tid.HasValue) FileCompleteReceived?.Invoke(this, new SemaBuzzFileControlEventArgs(tid.Value));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileCancelPacket(data))
                {
                    var tid = SemaBuzzFileTransfer.DeserializeTransferId(data);
                    if (tid.HasValue) FileCancelReceived?.Invoke(this, new SemaBuzzFileControlEventArgs(tid.Value));
                    continue;
                }

                // -- Fixed-size control/data frames --------------------------------------------------
                for (var offset = 0; offset + SemaBuzzPacket.WireSize <= data.Length; offset += SemaBuzzPacket.WireSize)
                {
                    var packet = SemaBuzzPacket.FromWireBytes(data[offset..(offset + SemaBuzzPacket.WireSize)]);
                    if (packet == null) break;
                    switch (packet.Value.Type)
                    {
                        case SemaBuzzPacketType.HandshakeHold:
                            _waitingForApproval = true;
                            SetState(SemaBuzzWireState.Warming, "waiting for host to approve connection...");
                            HandshakeHoldReceived?.Invoke(this, EventArgs.Empty);
                            break;
                        case SemaBuzzPacketType.ConnectRejected:
                            SetState(SemaBuzzWireState.Dead, "not-available");
                            if (_cts != null)
                                _cts.Cancel();
                            return;
                        case SemaBuzzPacketType.HandshakeAck:
                            SetState(SemaBuzzWireState.Secured, "Wire is live.");
                            break;
                        case SemaBuzzPacketType.Disconnect:
                            SetState(SemaBuzzWireState.Dead, "peer-disconnect");
                            if (_cts != null)
                                _cts.Cancel();
                            return;
                        case SemaBuzzPacketType.Ping:
                            break;

                        case SemaBuzzPacketType.Buzz:
                        case SemaBuzzPacketType.Char:
                            {
                                var packetHandler = PacketReceived;
                                if (packetHandler != null)
                                    packetHandler(this, new SemaBuzzPacketEventArgs(packet.Value));
                                break;
                            }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) { SetState(SemaBuzzWireState.Dead, $"relay ws error: {ex.Message}"); }
        finally
        {
            _wsSend = null;
            // Capture the relay's close reason before we send our own close frame (it may
            // be cleared once we complete the handshake).
            var closeStatus = ws.CloseStatus;
            var relayCloseDesc = ws.CloseStatusDescription;
            // Attempt a graceful WebSocket close with a short deadline.  If the network is
            // already dead the send will never be ACKed and CloseAsync would hang forever
            // with CancellationToken.None — preventing the relay from detecting the dropout.
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", closeCts.Token); } catch { }
            // Force-abort if still open so the relay's ReceiveAsync sees the connection
            // drop immediately rather than waiting for the keepalive cycle.
            if (ws.State != WebSocketState.Closed)
                try { ws.Abort(); } catch { }
            // If the relay loop exited for any reason other than explicit cancellation,
            // transition to Dead so the UI reflects the loss of connection.
            if (!ct.IsCancellationRequested && State is SemaBuzzWireState.Secured or SemaBuzzWireState.Live)
            {
                string msg;
                if (closeStatus.HasValue)
                    msg = string.IsNullOrEmpty(relayCloseDesc)
                        ? $"relay closed [{closeStatus.Value}]"
                        : $"relay closed [{closeStatus.Value}]: {relayCloseDesc}";
                else
                    msg = string.IsNullOrEmpty(relayCloseDesc)
                        ? "relay connection closed"
                        : $"relay closed: {relayCloseDesc}";
                SetState(SemaBuzzWireState.Dead, msg);
            }
        }
    }

    private async Task HandshakeTimeoutAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(HandshakeTimeout, ct);

            if (_waitingForApproval)
            {
                // Host is reviewing the request -- give them time to decide
                await Task.Delay(ApprovalWaitTimeout, ct);
                if (State == SemaBuzzWireState.Warming)
                {
                    SetState(SemaBuzzWireState.Dead, "host did not respond to connection request");
                    if (_cts != null)
                        _cts.Cancel();
                }
                return;
            }

            if (State == SemaBuzzWireState.Warming)
            {
                SetState(SemaBuzzWireState.Dead, "no response -- host may not be listening");
                if (_cts != null)
                    _cts.Cancel();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReceiveLoopAsync(ECDiffieHellman localEcdh, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _udp!.ReceiveAsync(ct);
                var data = result.Buffer;

                // Reject implausible sizes immediately
                const int MaxPayload = 16_384;
                if (data.Length < SemaBuzzPacket.WireSize || data.Length > MaxPayload) continue;

                // Only accept traffic from the host we dialed
                if (_peer != null && !result.RemoteEndPoint.Equals(_peer)) continue;

                //  ECDH key exchange (plaintext, during handshake only)
                if (SemaBuzzKeyExchange.IsKeyExchangePacket(data))
                {
                    if (Shield != null) continue; // already established -- ignore
                    var peerPubKeyBytes = SemaBuzzKeyExchange.Deserialize(data);
                    if (peerPubKeyBytes == null) continue;

                    // Import the host's public key and derive the shared AES key.
                    using var peerEcdh = ECDiffieHellman.Create();
                    peerEcdh.ImportSubjectPublicKeyInfo(peerPubKeyBytes, out _);
                    var rawSecret = localEcdh.DeriveRawSecretAgreement(peerEcdh.PublicKey);
                    Shield = SemaBuzzShield.FromEcdhSecret(rawSecret);
                    continue;
                }

                //  Decrypt if shield is active
                if (Shield != null)
                {
                    var decrypted = Shield.Decrypt(data);
                    if (decrypted == null) continue; // tampered or wrong key -- drop
                    data = decrypted;
                }

                //  Variable-length packets
                if (SemaBuzzMetadata.IsMetadataPacket(data))
                {
                    var meta = SemaBuzzMetadata.Deserialize(data);
                    if (meta.HasValue)
                    {
                        var metaHandler = MetadataReceived;
                        if (metaHandler != null)
                            metaHandler(this, new SemaBuzzMetadataEventArgs(meta.Value.Handle, meta.Value.AvatarPng, meta.Value.Status, meta.Value.StatusMessage));
                    }
                    continue;
                }

                if (SemaBuzzUrlPush.IsUrlPushPacket(data))
                {
                    var url = SemaBuzzUrlPush.Deserialize(data);
                    if (url != null)
                    {
                        var urlHandler = UrlPushReceived;
                        if (urlHandler != null)
                            urlHandler(this, new SemaBuzzUrlPushEventArgs(url));
                    }
                    continue;
                }

                //  Draw event
                if (SemaBuzzDraw.IsDrawPacket(data))
                {
                    var ev = SemaBuzzDraw.Deserialize(data);
                    if (ev.HasValue)
                        DrawReceived?.Invoke(this, new SemaBuzzDrawEventArgs(ev.Value));
                    continue;
                }

                //  File transfer variable-length packets
                if (SemaBuzzFileTransfer.IsFileOfferPacket(data))
                {
                    var offer = SemaBuzzFileTransfer.DeserializeFileOffer(data);
                    if (offer.HasValue)
                        FileOfferReceived?.Invoke(this, new SemaBuzzFileOfferEventArgs(
                            offer.Value.TransferId, offer.Value.Filename, offer.Value.FileSize,
                            offer.Value.TotalChunks, offer.Value.Sha256));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileChunkPacket(data))
                {
                    var chunk = SemaBuzzFileTransfer.DeserializeFileChunk(data);
                    if (chunk.HasValue)
                        FileChunkReceived?.Invoke(this, new SemaBuzzFileChunkEventArgs(
                            chunk.Value.TransferId, chunk.Value.ChunkIdx, chunk.Value.Data));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileAcceptPacket(data))
                {
                    var tid = SemaBuzzFileTransfer.DeserializeTransferId(data);
                    if (tid.HasValue) FileAcceptReceived?.Invoke(this, new SemaBuzzFileControlEventArgs(tid.Value));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileRejectPacket(data))
                {
                    var tid = SemaBuzzFileTransfer.DeserializeTransferId(data);
                    if (tid.HasValue) FileRejectReceived?.Invoke(this, new SemaBuzzFileControlEventArgs(tid.Value));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileCompletePacket(data))
                {
                    var tid = SemaBuzzFileTransfer.DeserializeTransferId(data);
                    if (tid.HasValue) FileCompleteReceived?.Invoke(this, new SemaBuzzFileControlEventArgs(tid.Value));
                    continue;
                }
                if (SemaBuzzFileTransfer.IsFileCancelPacket(data))
                {
                    var tid = SemaBuzzFileTransfer.DeserializeTransferId(data);
                    if (tid.HasValue) FileCancelReceived?.Invoke(this, new SemaBuzzFileControlEventArgs(tid.Value));
                    continue;
                }

                //  Fixed-size packet frame(s) -- may be batched
                for (var offset = 0; offset + SemaBuzzPacket.WireSize <= data.Length;
                         offset += SemaBuzzPacket.WireSize)
                {
                    var frame = data[offset..(offset + SemaBuzzPacket.WireSize)];
                    var packet = SemaBuzzPacket.FromWireBytes(frame);
                    if (packet == null) break; // bad frame

                    switch (packet.Value.Type)
                    {
                        case SemaBuzzPacketType.HandshakeHold:
                            _waitingForApproval = true;
                            SetState(SemaBuzzWireState.Warming, "waiting for host to approve connection...");
                            HandshakeHoldReceived?.Invoke(this, EventArgs.Empty);
                            break;

                        case SemaBuzzPacketType.ConnectRejected:
                            SetState(SemaBuzzWireState.Dead, "not-available");
                            if (_cts != null)
                                _cts.Cancel();
                            return;

                        case SemaBuzzPacketType.HandshakeAck:
                            // ECDH has already set up the shield -- session is always Secured.
                            SetState(SemaBuzzWireState.Secured, "Wire is live.");
                            break;

                        case SemaBuzzPacketType.Disconnect:
                            SetState(SemaBuzzWireState.Dead, "peer-disconnect");
                            if (_cts != null)
                                _cts.Cancel();
                            return;

                        case SemaBuzzPacketType.Ping:
                            break;

                        case SemaBuzzPacketType.Buzz:
                        case SemaBuzzPacketType.Char:
                            {
                                var packetHandler = PacketReceived;
                                if (packetHandler != null)
                                    packetHandler(this, new SemaBuzzPacketEventArgs(packet.Value));
                                break;
                            }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { SetState(SemaBuzzWireState.Dead, "Socket error."); }
    }

    private async Task KeepaliveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                if (State is SemaBuzzWireState.Live or SemaBuzzWireState.Secured)
                    await SendControlAsync(SemaBuzzPacketType.Ping);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Retransmits the ECDH key-exchange packet every 2 s while the handshake is
    /// still pending. This punches through NAT mappings that drop the first UDP
    /// packet and handles ACK loss after the Shield is already established.
    /// </summary>
    private async Task HandshakeRetransmitAsync(byte[] pubKeyBytes, CancellationToken ct)
    {
        var packet = SemaBuzzKeyExchange.Serialize(pubKeyBytes);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                if (State != SemaBuzzWireState.Warming || (_udp == null && _wsSend == null)) return;
                if (_wsSend != null) await _wsSend(packet);
                else await _udp!.SendAsync(packet);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { /* socket closed during shutdown */ }
    }

    /// <summary>Send peer identity metadata to the host.</summary>
    public async Task SendMetadataAsync(string handle, byte[]? avatarPng,
        SemaBuzzStatus status = SemaBuzzStatus.Available, string statusMessage = "")
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = SemaBuzzMetadata.Serialize(handle, avatarPng, status, statusMessage);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) { await _wsSend(bytes); return; }
        await _udp!.SendAsync(bytes);
    }

    /// <summary>
    /// Sends compact metadata (handle only, no avatar) to the host while in the Warming state
    /// after receiving a HandshakeHold. The host can use this to display the peer's handle in
    /// the approval dialog before the full handshake completes.
    /// Only valid when the ECDH shield is already established.
    /// </summary>
    public async Task SendPreApprovalMetadataAsync(string handle)
    {
        if (Shield == null) return;
        var bytes = SemaBuzzMetadata.Serialize(handle, null, SemaBuzzStatus.Available, string.Empty);
        bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) { await _wsSend(bytes); return; }
        if (_udp != null) await _udp.SendAsync(bytes);
    }

    public async Task SendAsync(SemaBuzzPacket packet)
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = packet.ToWireBytes();
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) { await _wsSend(bytes); return; }
        await _udp!.SendAsync(bytes);
    }

    /// <summary>
    /// Send multiple packets coalesced into a single encrypted UDP datagram.
    /// All frames are concatenated as plaintext before the single Encrypt() call,
    /// which saves nonce + tag overhead per character on fast-typing bursts.
    /// </summary>
    public async Task SendBatchAsync(IReadOnlyList<SemaBuzzPacket> packets)
    {
        if ((_udp == null && _wsSend == null) || packets.Count == 0) return;
        if (State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;

        for (var offset = 0; offset < packets.Count; offset += MaxBatchPacketsPerSend)
        {
            var chunkCount = Math.Min(MaxBatchPacketsPerSend, packets.Count - offset);
            var plaintext = new byte[chunkCount * SemaBuzzPacket.WireSize];
            for (var i = 0; i < chunkCount; i++)
                packets[offset + i].ToWireBytes().CopyTo(plaintext, i * SemaBuzzPacket.WireSize);

            byte[] bytes;
            if (Shield != null)
                bytes = Shield.Encrypt(plaintext);
            else
                bytes = plaintext;
            if (_wsSend != null) await _wsSend(bytes);
            else await _udp!.SendAsync(bytes);
        }
    }

    /// <summary>Send a Buzz to the peer -- spikes their filament and shakes their window.</summary>
    public Task SendBuzzAsync() => SendAsync(SemaBuzzPacket.Control(SemaBuzzPacketType.Buzz));

    /// <summary>Send a whiteboard draw event to the peer.</summary>
    public async Task SendDrawAsync(SemaBuzzDrawEvent drawEvent)
    {
        var bytes = SemaBuzzDraw.Serialize(drawEvent);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) await _wsSend(bytes);
        else await _udp!.SendAsync(bytes);
    }

    /// <summary>Push a URL to the peer.</summary>
    public async Task SendUrlPushAsync(string url)
    {
        var bytes = SemaBuzzUrlPush.Serialize(url);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) await _wsSend(bytes);
        else await _udp!.SendAsync(bytes);
    }

    /// <summary>Send a file-transfer offer to the peer.</summary>
    public async Task SendFileOfferAsync(byte transferId, string filename, long fileSize, ushort totalChunks, byte[] sha256)
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = SemaBuzzFileTransfer.SerializeFileOffer(transferId, filename, fileSize, totalChunks, sha256);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) await _wsSend(bytes);
        else await _udp!.SendAsync(bytes);
    }

    /// <summary>Send one chunk of a file transfer.</summary>
    public async Task SendFileChunkAsync(byte transferId, ushort chunkIdx, byte[] chunkData)
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = SemaBuzzFileTransfer.SerializeFileChunk(transferId, chunkIdx, chunkData);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) await _wsSend(bytes);
        else await _udp!.SendAsync(bytes);
    }

    /// <summary>Accept an incoming file offer.</summary>
    public async Task SendFileAcceptAsync(byte transferId)
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = SemaBuzzFileTransfer.SerializeFileAccept(transferId);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) await _wsSend(bytes);
        else await _udp!.SendAsync(bytes);
    }

    /// <summary>Decline an incoming file offer.</summary>
    public async Task SendFileRejectAsync(byte transferId)
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = SemaBuzzFileTransfer.SerializeFileReject(transferId);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) await _wsSend(bytes);
        else await _udp!.SendAsync(bytes);
    }

    /// <summary>Signal that all chunks have been transmitted.</summary>
    public async Task SendFileCompleteAsync(byte transferId)
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = SemaBuzzFileTransfer.SerializeFileComplete(transferId);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) await _wsSend(bytes);
        else await _udp!.SendAsync(bytes);
    }

    /// <summary>Cancel an in-progress transfer (either side may call this).</summary>
    public async Task SendFileCancelAsync(byte transferId)
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = SemaBuzzFileTransfer.SerializeFileCancel(transferId);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) await _wsSend(bytes);
        else await _udp!.SendAsync(bytes);
    }

    private async Task SendControlAsync(SemaBuzzPacketType type)
    {
        if (_udp == null && _wsSend == null) return;
        var bytes = SemaBuzzPacket.Control(type).ToWireBytes();
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) { await _wsSend(bytes); return; }
        await _udp!.SendAsync(bytes);
    }

    /// <summary>Gracefully close the wire.</summary>
    public async Task DisconnectAsync()
    {
        if ((_udp != null || _wsSend != null) && State is not SemaBuzzWireState.Cold and not SemaBuzzWireState.Dead)
        {
            try { await SendControlAsync(SemaBuzzPacketType.Disconnect); }
            catch { /* best-effort */ }
        }
        if (_wsClient != null && _wsClient.State == WebSocketState.Open)
            try { await _wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", default); } catch { }
        if (_cts != null)
            _cts.Cancel();
        SetState(SemaBuzzWireState.Dead, "Wire closed by local peer.");
    }

    private void SetState(SemaBuzzWireState state, string? message = null)
    {
        if (State == state && string.Equals(message, _lastStateMessage, StringComparison.Ordinal))
            return;
        State = state;
        _lastStateMessage = message;
        var wireHandler = WireStateChanged;
        if (wireHandler != null)
            wireHandler(this, new SemaBuzzWireStateEventArgs(state, message));
    }

    /// <summary>
    /// Reads one complete length-prefixed relay message, accumulating raw bytes from
    /// multiple WebSocket messages when the relay forwards partial TCP segments as
    /// separate complete WS messages.  <paramref name="acc"/> must persist between calls.
    /// </summary>
    private static async Task<byte[]?> ReceiveRelaySizedMessageAsync(
        WebSocket ws, byte[] buffer, MemoryStream acc, CancellationToken ct)
    {
        const int MaxMsg = 65_536;
        while (true)
        {
            // Try to extract a complete frame from whatever is already buffered.
            if (acc.Length >= 4)
            {
                var hdr = acc.GetBuffer();
                int needed = (hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3];
                if (needed <= 0 || needed > MaxMsg)
                {
                    acc.SetLength(0); // corrupt length — discard and resync
                }
                else if (acc.Length >= needed + 4)
                {
                    var result = new byte[needed];
                    Array.Copy(hdr, 4, result, 0, needed);
                    var used = needed + 4;
                    var remaining = (int)acc.Length - used;
                    if (remaining > 0)
                    {
                        var leftover = new byte[remaining];
                        Array.Copy(hdr, used, leftover, 0, remaining);
                        acc.SetLength(0);
                        acc.Write(leftover, 0, remaining);
                    }
                    else
                    {
                        acc.SetLength(0);
                    }
                    return result;
                }
            }

            // Need more bytes — read one WebSocket message (may itself be fragmented).
            while (true)
            {
                WebSocketReceiveResult recv;
                try { recv = await ws.ReceiveAsync(buffer, ct); }
                catch (OperationCanceledException) { throw; }
                catch { return null; }

                if (recv.MessageType == WebSocketMessageType.Close) return null;
                if (recv.Count > 0) acc.Write(buffer, 0, recv.Count);
                if (recv.EndOfMessage) break;
            }
            // Loop back to retry frame extraction with the newly accumulated bytes.
        }
    }

    private static string GetOutboundLocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 80);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch { return string.Empty; }
    }

    private void SubscribeNetworkChange()
    {
        UnsubscribeNetworkChange();
        var baseline = GetOutboundLocalIp();
        _networkChangeHandler = (_, _) =>
        {
            if (_cts != null && !_cts.IsCancellationRequested && GetOutboundLocalIp() != baseline)
            {
                // Best-effort: send Disconnect to the host through the existing relay connection
                // before the old network interface goes away, so the host disconnects immediately
                // rather than waiting up to ~12 s for the relay keepalive to time out.
                if (_wsSend is { } send && Shield is { } shield
                    && State is SemaBuzzWireState.Live or SemaBuzzWireState.Secured)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var bytes = SemaBuzzPacket.Control(SemaBuzzPacketType.Disconnect).ToWireBytes();
                            bytes = shield.Encrypt(bytes);
                            await send(bytes);
                        }
                        catch { }
                        _cts?.Cancel();
                        SetState(SemaBuzzWireState.Dead, "network-changed");
                    });
                }
                else
                {
                    _cts.Cancel();
                    SetState(SemaBuzzWireState.Dead, "network-changed");
                }
            }
        };
        NetworkChange.NetworkAddressChanged += _networkChangeHandler;
    }

    private void UnsubscribeNetworkChange()
    {
        if (_networkChangeHandler != null)
        {
            NetworkChange.NetworkAddressChanged -= _networkChangeHandler;
            _networkChangeHandler = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UnsubscribeNetworkChange();
            if (_cts != null)
                _cts.Cancel();
            if (_udp != null)
                _udp.Dispose();
            if (_wsClient != null)
                _wsClient.Dispose();
            if (_cts != null)
                _cts.Dispose();
            _disposed = true;
        }
    }
}
