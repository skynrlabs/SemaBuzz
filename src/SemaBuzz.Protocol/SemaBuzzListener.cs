using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace SemaBuzz.Protocol;

/// <summary>
/// Listens for an incoming SemaBuzz connection on a UDP socket.
/// Handles ECDH key exchange and transitions the wire to Secured state.
/// </summary>
public sealed class SemaBuzzListener : IDisposable
{
    private UdpClient? _udp;
    private ClientWebSocket? _wsClient;        // non-null when in WebSocket relay mode
    private Func<byte[], Task>? _wsSend;         // send delegate set after relay pairing
    private readonly SemaphoreSlim _wsSendLock = new(1, 1); // serializes concurrent ws.SendAsync calls
    private bool _isRelayMode;      // true when connected via WebSocket relay
    private CancellationTokenSource? _cts;
    private int _port;
    private bool _disposed;
    private string? _lastStateMessage;
    private string _pendingDeadMessage = "Wire closed."; // overridden to "peer-disconnect" when peer sends Disconnect
    private byte[]? _localPubKeyBytes; // saved so we can resend on client retransmit
    private ECDiffieHellman? _pendingEcdh;  // set in relay mode so we reuse the initiated key pair

    public event EventHandler<SemaBuzzPacketEventArgs>? PacketReceived;
    private const int MaxBatchPacketsPerSend = 8;
    public event EventHandler<SemaBuzzWireStateEventArgs>? WireStateChanged;
    public event EventHandler<SemaBuzzMetadataEventArgs>? MetadataReceived;
    public event EventHandler<SemaBuzzUrlPushEventArgs>? UrlPushReceived;

    /// <summary>
    /// Optional async callback invoked when an incoming Handshake arrives.
    /// Return <c>true</c> to accept the connection, <c>false</c> to reject it.
    /// If null, all connections are accepted automatically.
    /// </summary>
    public Func<IPEndPoint, Task<bool>>? ConnectionApprovalCallback { get; set; }

    public SemaBuzzWireState State { get; private set; } = SemaBuzzWireState.Cold;
    public IPEndPoint? PeerEndPoint { get; private set; }

    public SemaBuzzShield? Shield { get; private set; }

    /// <summary>
    /// Start listening via a relay server over WebSocket. Connects to the relay,
    /// registers with the given token, waits for a dialer to join, then runs the
    /// full ECDH handshake and wire protocol through the relay's WebSocket tunnel.
    /// </summary>
    public async Task ListenViaRelayAsync(string relayUri, string token,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _wsClient = new ClientWebSocket();
        _isRelayMode = true;
        _pendingDeadMessage = "Wire closed.";

        try { await _wsClient.ConnectAsync(new Uri(relayUri), _cts.Token); }
        catch (Exception ex)
        {
            SetState(SemaBuzzWireState.Dead, $"relay unreachable: {ex.Message}");
            return;
        }

        SetState(SemaBuzzWireState.Warming, $"Waiting for peer (token: {token})...");

        // Register as host.
        var join = SemaBuzzRelayPacket.Build(SemaBuzzRelayPacketType.JoinHost, token);
        await _wsClient.SendAsync(join, WebSocketMessageType.Binary, true, _cts.Token);

        // Wait for Paired.
        var ctrlBuf = new byte[64];
        bool paired = false;
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var r = await _wsClient.ReceiveAsync(ctrlBuf, _cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) break;
                var p = SemaBuzzRelayPacket.Parse(ctrlBuf[..r.Count]);
                if (p == null) continue;
                if (p.Value.Type == SemaBuzzRelayPacketType.RelayError)
                {
                    SetState(SemaBuzzWireState.Dead, "relay error");
                    _cts.Cancel(); return;
                }
                if (p.Value.Type == SemaBuzzRelayPacketType.Paired) { paired = true; break; }
            }
        }
        catch (OperationCanceledException) { /* handled below */ }

        if (!paired) { SetState(SemaBuzzWireState.Dead, "Wire closed."); return; }

        // -- STUN / UDP hole-punch attempt -----------------------------------------
        // Bind a fresh UDP socket and use STUN to discover our external endpoint.
        // Send it to the relay via PunchReady; wait up to 5 s for the peer's endpoint
        // (PeerAddress frame), then try direct UDP. If successful we drop the relay.
        // If anything fails we proceed with the relay session transparently.
        UdpClient? directUdp = null;
        IPEndPoint? peerDirectEp = null;
        try
        {
            directUdp = new UdpClient(0); // ephemeral port
            var myExternalEp = await SemaBuzzStun.DiscoverAsync(directUdp, _cts.Token);
            if (myExternalEp != null)
            {
                var punchReady = SemaBuzzRelayPacket.BuildPunchReady(token, myExternalEp);
                await _wsClient.SendAsync(punchReady, WebSocketMessageType.Binary, true, _cts.Token);

                // Wait for PeerAddress (relay sends it once both sides have checked in).
                using var peerAddrCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                peerAddrCts.CancelAfter(TimeSpan.FromSeconds(5));
                var ctrlBuf2 = new byte[32];
                try
                {
                    while (!peerAddrCts.Token.IsCancellationRequested)
                    {
                        var r2 = await _wsClient.ReceiveAsync(ctrlBuf2, peerAddrCts.Token);
                        if (r2.MessageType == WebSocketMessageType.Close) break;
                        if (r2.Count < SemaBuzzRelayPacket.PunchPacketSize) continue;
                        if (SemaBuzzRelayPacket.IsRelayPacket(ctrlBuf2)
                            && (SemaBuzzRelayPacketType)ctrlBuf2[3] == SemaBuzzRelayPacketType.PeerAddress)
                        {
                            peerDirectEp = SemaBuzzRelayPacket.ParseEndpoint(ctrlBuf2[..r2.Count]);
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) { /* punch exchange timed out -- fall back */ }

                if (peerDirectEp != null)
                {
                    SetState(SemaBuzzWireState.Warming, "Trying direct UDP...");
                    var directEp = await SemaBuzzPunchThrough.TryAsync(
                        directUdp, peerDirectEp, TimeSpan.FromSeconds(4), _cts.Token);

                    if (directEp != null)
                    {
                        // Direct path confirmed -- switch to UDP socket.
                        _udp = directUdp;
                        directUdp = null; // ownership transferred
                        _isRelayMode = false;
                        _port = ((System.Net.IPEndPoint)_udp.Client.LocalEndPoint!).Port;

                        // Close the relay WebSocket (best effort).
                        try { await _wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "direct", default); } catch { }
                        _wsClient.Dispose();
                        _wsClient = null;

                        SetState(SemaBuzzWireState.Warming, "Direct UDP -- completing handshake...");

                        // Run the standard listener loop over the direct socket.
                        try
                        {
                            while (!_cts.Token.IsCancellationRequested)
                            {
                                var res = await _udp.ReceiveAsync(_cts.Token);
                                // Filter out any stray punch probe/ack frames.
                                if (SemaBuzzPunchThrough.IsPunchProbe(res.Buffer)
                                 || SemaBuzzPunchThrough.IsPunchAck(res.Buffer)) continue;
                                await HandleIncomingAsync(res);
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (SocketException) { }
                        finally { SetState(SemaBuzzWireState.Dead, _pendingDeadMessage); }
                        return; // done -- skip relay session below
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* STUN/punch failed -- continue with relay */ }
        finally
        {
            if (directUdp != null)
                directUdp.Dispose();
        }
        // -- end punch-through attempt ---------------------------------------------

        // Wire up the WebSocket send delegate used by all internal send helpers.
        var ws = _wsClient!;
        _wsSend = async data =>
        {
            if (ws.State != WebSocketState.Open) return;
            await _wsSendLock.WaitAsync();
            try
            {
                if (ws.State != WebSocketState.Open) return;
                await ws.SendAsync(data, WebSocketMessageType.Binary, true, _cts!.Token);
            }
            catch { }
            finally { _wsSendLock.Release(); }
        };

        _port = 0;

        // Host initiates ECDH so web-based dialers (which wait for the host to go first) work.
        _pendingEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _localPubKeyBytes = _pendingEcdh.PublicKey.ExportSubjectPublicKeyInfo();
        await _wsSend(SemaBuzzKeyExchange.Serialize(_localPubKeyBytes));

        SetState(SemaBuzzWireState.Warming, "Peer connected via relay -- completing handshake...");

        // Relay dummy endpoint: used as the stand-in remote address in HandleIncomingAsync.
        var relayPeer = new IPEndPoint(IPAddress.Loopback, 0);
        var dataBuf = new byte[65_536];
        try
        {
            while (!_cts.Token.IsCancellationRequested && _wsClient!.State == WebSocketState.Open)
            {
                var data = await ReceiveWsMessageAsync(_wsClient, dataBuf, _cts.Token);
                if (data == null) break;
                if (data.Length < SemaBuzzPacket.WireSize) continue;
                // Skip stray relay control frames.
                if (data.Length == SemaBuzzRelayPacket.Size && SemaBuzzRelayPacket.IsRelayPacket(data)) continue;
                await HandleIncomingAsync(new UdpReceiveResult(data, relayPeer));
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _wsSend = null;
            _isRelayMode = false;
            SetState(SemaBuzzWireState.Dead, _pendingDeadMessage);
        }
    }

    /// <summary>
    /// Start listening on the given port. Blocks until a peer connects,
    /// then fires events as packets arrive.
    /// </summary>
    public async Task ListenAsync(int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _port = port;
        _udp = new UdpClient(port);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        SetState(SemaBuzzWireState.Warming, $"Listening on port {port}...");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var result = await _udp.ReceiveAsync(_cts.Token);
                await HandleIncomingAsync(result);
            }
        }
        catch (OperationCanceledException) { /* Clean shutdown */ }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted) { /* Disposed */ }
        finally
        {
            SetState(SemaBuzzWireState.Dead, "Wire closed.");
        }
    }

    private async Task HandleIncomingAsync(UdpReceiveResult result)
    {
        var data = result.Buffer;

        //  Hard size bounds
        // Smallest valid payload: 6-byte plaintext packet.
        // Largest valid payload: encrypted metadata with a thumbnail avatar.
        // Anything outside [6, 16384] cannot be a legitimate SemaBuzz packet.
        const int MaxPayload = 16_384;
        if (data.Length < SemaBuzzPacket.WireSize || data.Length > MaxPayload) return;

        //  Source filter
        // Once the wire is Live or Secured, only accept traffic from the
        // established peer. Drop everything else silently.
        if (State is SemaBuzzWireState.Live or SemaBuzzWireState.Secured
            && PeerEndPoint != null
            && !result.RemoteEndPoint.Equals(PeerEndPoint))
            return;

        //  ECDH key exchange (plaintext  must be handled before Shield check)
        if (SemaBuzzKeyExchange.IsKeyExchangePacket(data))
        {
            if (Shield != null)
            {
                if (_wsSend != null && State == SemaBuzzWireState.Secured) return;

                // Client is retransmitting  our KE response or HandshakeAck was lost.
                // Resend whatever is appropriate for the current state.
                if (PeerEndPoint != null && PeerEndPoint.Equals(result.RemoteEndPoint) && _localPubKeyBytes != null)
                {
                    await SendRawAsync(SemaBuzzKeyExchange.Serialize(_localPubKeyBytes), result.RemoteEndPoint);
                    if (State == SemaBuzzWireState.Secured)
                        await SendEncryptedControlToAsync(SemaBuzzPacketType.HandshakeAck, result.RemoteEndPoint);
                    else if (State == SemaBuzzWireState.Warming)
                        await SendEncryptedControlToAsync(SemaBuzzPacketType.HandshakeHold, result.RemoteEndPoint);
                }
                return;
            }

            var peerPubKeyBytes = SemaBuzzKeyExchange.Deserialize(data);
            if (peerPubKeyBytes == null) return;

            // If we already sent our KE (relay host-initiates flow), reuse that key pair.
            // Otherwise generate a fresh ephemeral pair now (classic dialer-initiates flow).
            ECDiffieHellman? newLocalEcdh;
            if (_pendingEcdh == null)
                newLocalEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            else
                newLocalEcdh = null;
            ECDiffieHellman localEcdh;
            if (_pendingEcdh != null)
                localEcdh = _pendingEcdh;
            else
                localEcdh = newLocalEcdh!;
            byte[] localPubKeyBytes;
            if (_pendingEcdh != null)
                localPubKeyBytes = _localPubKeyBytes!;
            else
                localPubKeyBytes = localEcdh.PublicKey.ExportSubjectPublicKeyInfo();
            if (_pendingEcdh == null) _localPubKeyBytes = localPubKeyBytes;

            using var peerEcdh = ECDiffieHellman.Create();
            peerEcdh.ImportSubjectPublicKeyInfo(peerPubKeyBytes, out _);
            var rawSecret = localEcdh.DeriveRawSecretAgreement(peerEcdh.PublicKey);
            Shield = SemaBuzzShield.FromEcdhSecret(rawSecret);

            if (_pendingEcdh != null)
            {
                // Host-initiates flow: our key was already sent -- don't send again.
                _pendingEcdh.Dispose();
                _pendingEcdh = null;
            }
            else
            {
                // Classic flow: send our public key back now.
                await SendRawAsync(SemaBuzzKeyExchange.Serialize(localPubKeyBytes), result.RemoteEndPoint);
                newLocalEcdh!.Dispose();
            }

            PeerEndPoint = result.RemoteEndPoint;
            SetState(SemaBuzzWireState.Warming, "Handshake received -- awaiting host approval...");

            if (ConnectionApprovalCallback != null)
            {
                await SendEncryptedControlToAsync(SemaBuzzPacketType.HandshakeHold, result.RemoteEndPoint);

                bool approved = await ConnectionApprovalCallback(result.RemoteEndPoint);
                if (!approved)
                {
                    await SendEncryptedControlToAsync(SemaBuzzPacketType.ConnectRejected, result.RemoteEndPoint);
                    PeerEndPoint = null;
                    Shield = null;
                    SetState(SemaBuzzWireState.Warming, $"Listening on port {_port}...");
                    return;
                }
            }

            await SendEncryptedControlToAsync(SemaBuzzPacketType.HandshakeAck, result.RemoteEndPoint);
            SetState(SemaBuzzWireState.Secured, "Wire is live.");
            return;
        }

        if (Shield != null)
        {
            var decrypted = Shield.Decrypt(data);
            if (decrypted == null)
            {
                // Corrupted or out-of-order packet  drop silently.
                // Fatal only if we're already live (session key mismatch shouldn't happen post-ECDH).
                if (State is SemaBuzzWireState.Live or SemaBuzzWireState.Secured)
                {
                    SetState(SemaBuzzWireState.Dead, "received unreadable packet  session key mismatch");
                    if (_cts != null)
                        _cts.Cancel();
                }
                return;
            }
            data = decrypted;
        }

        // Variable-length metadata packets arrive before char packets
        if (SemaBuzzMetadata.IsMetadataPacket(data))
        {
            var meta = SemaBuzzMetadata.Deserialize(data);
            if (meta.HasValue)
            {
                var metaHandler = MetadataReceived;
                if (metaHandler != null)
                    metaHandler(this, new SemaBuzzMetadataEventArgs(meta.Value.Handle, meta.Value.AvatarPng));
            }
            return;
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
            return;
        }


        for (var offset = 0; offset + SemaBuzzPacket.WireSize <= data.Length; offset += SemaBuzzPacket.WireSize)
        {
            var packet = SemaBuzzPacket.FromWireBytes(data[offset..(offset + SemaBuzzPacket.WireSize)]);
            if (packet == null) break;

            switch (packet.Value.Type)
            {
                case SemaBuzzPacketType.Handshake:
                    PeerEndPoint = result.RemoteEndPoint;
                    SetState(SemaBuzzWireState.Warming, "Handshake received  awaiting host approval...");

                    if (ConnectionApprovalCallback != null)
                    {
                        // Tell the dialer to hold on while the host decides
                        await SendControlToAsync(SemaBuzzPacketType.HandshakeHold, result.RemoteEndPoint);

                        bool approved = await ConnectionApprovalCallback(result.RemoteEndPoint);
                        if (!approved)
                        {
                            await SendControlToAsync(SemaBuzzPacketType.ConnectRejected, result.RemoteEndPoint);
                            PeerEndPoint = null;
                            SetState(SemaBuzzWireState.Warming, $"Listening on port {_port}...");
                            return;
                        }
                    }

                    await SendAckAsync(result.RemoteEndPoint);
                    if (Shield != null)
                        SetState(SemaBuzzWireState.Secured);
                    else
                        SetState(SemaBuzzWireState.Live);
                    break;

                case SemaBuzzPacketType.Disconnect:
                    // Peer walked away.
                    PeerEndPoint = null;
                    Shield = null;
                    _localPubKeyBytes = null;
                    if (_isRelayMode)
                    {
                        _pendingDeadMessage = "peer-disconnect";
                        if (_cts != null)
                            _cts.Cancel(); // relay session is one-to-one; close it out
                    }
                    else
                        SetState(SemaBuzzWireState.Warming, $"Listening on port {_port}...");
                    return;

                case SemaBuzzPacketType.Ping:
                    // Keepalive  no event needed
                    break;

                case SemaBuzzPacketType.Buzz:
                case SemaBuzzPacketType.Char:
                    {
                        var packetHandler = PacketReceived;
                        if (packetHandler != null)
                            packetHandler(this, new SemaBuzzPacketEventArgs(packet.Value));
                        break;
                    }

                default:
                    // Unknown or unexpected type  drop silently
                    break;
            }
        }
    }

    private async Task SendAckAsync(IPEndPoint peer)
    {
        var bytes = SemaBuzzPacket.Control(SemaBuzzPacketType.HandshakeAck).ToWireBytes();
        await SendRawAsync(bytes, peer);
    }

    /// <summary>Send a control packet to the given peer (plaintext  for pre-handshake signals).</summary>
    private async Task SendControlToAsync(SemaBuzzPacketType type, IPEndPoint peer)
    {
        var bytes = SemaBuzzPacket.Control(type).ToWireBytes();
        await SendRawAsync(bytes, peer);
    }

    /// <summary>Send a control packet to the given peer, encrypted when a Shield is active.</summary>
    private async Task SendEncryptedControlToAsync(SemaBuzzPacketType type, IPEndPoint peer)
    {
        var bytes = SemaBuzzPacket.Control(type).ToWireBytes();
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        await SendRawAsync(bytes, peer);
    }

    /// <summary>
    /// Send a Disconnect packet to the connected peer so they see a clean close,
    /// then release the peer endpoint. Call before Dispose() when host disconnects.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_udp == null && _wsSend == null) return;
        if (PeerEndPoint == null && _wsSend == null) return;
        try
        {
            var bytes = SemaBuzzPacket.Control(SemaBuzzPacketType.Disconnect).ToWireBytes();
            if (Shield != null) bytes = Shield.Encrypt(bytes);
            await SendRawAsync(bytes, PeerEndPoint);
        }
        catch { /* socket may already be closed */ }
        if (_wsClient != null && _wsClient.State == WebSocketState.Open)
            try { await _wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", default); } catch { }
        PeerEndPoint = null;
        Shield = null;
        _localPubKeyBytes = null;
    }

    /// <summary>Send raw bytes to a peer without any further processing.</summary>
    private async Task SendRawAsync(byte[] bytes, IPEndPoint? peer)
    {
        if (_wsSend != null) { await _wsSend(bytes); return; }
        if (_udp == null || peer == null) return;
        await _udp.SendAsync(bytes, peer);
    }

    /// <summary>Send peer identity metadata to the connected peer.</summary>
    public async Task SendMetadataAsync(string handle, byte[]? avatarPng)
    {
        if (_udp == null && _wsSend == null) return;
        if (PeerEndPoint == null && _wsSend == null) return;
        var bytes = SemaBuzzMetadata.Serialize(handle, avatarPng);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        await SendRawAsync(bytes, PeerEndPoint);
    }

    /// <summary>Push a URL to the connected peer.</summary>
    public async Task SendUrlPushAsync(string url)
    {
        if (_udp == null && _wsSend == null) return;
        if (PeerEndPoint == null && _wsSend == null) return;
        var bytes = SemaBuzzUrlPush.Serialize(url);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        await SendRawAsync(bytes, PeerEndPoint);
    }

    /// <summary>Send a packet back to the connected peer.</summary>
    public async Task SendAsync(SemaBuzzPacket packet)
    {
        if ((_udp == null && _wsSend == null) || (PeerEndPoint == null && _wsSend == null)) return;
        var bytes = packet.ToWireBytes();
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        await SendRawAsync(bytes, PeerEndPoint);
    }

    /// <summary>
    /// Send multiple packets coalesced into a single encrypted UDP datagram.
    /// </summary>
    public async Task SendBatchAsync(IReadOnlyList<SemaBuzzPacket> packets)
    {
        if ((_udp == null && _wsSend == null) || (PeerEndPoint == null && _wsSend == null) || packets.Count == 0) return;
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
            await SendRawAsync(bytes, PeerEndPoint);
        }
    }

    /// <summary>Send a Buzz to the peer  spikes their filament and shakes their window.</summary>
    public Task SendBuzzAsync() => SendAsync(SemaBuzzPacket.Control(SemaBuzzPacketType.Buzz));


    private static async Task<byte[]?> ReceiveWsMessageAsync(WebSocket ws, byte[] buffer, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult recv;
            try { recv = await ws.ReceiveAsync(buffer, ct); }
            catch (OperationCanceledException) { throw; }
            catch { return null; }

            if (recv.MessageType == WebSocketMessageType.Close) return null;
            if (recv.Count > 0) stream.Write(buffer, 0, recv.Count);
            if (recv.EndOfMessage) return stream.ToArray();
        }
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

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_cts != null)
                _cts.Cancel();
            if (_udp != null)
                _udp.Dispose();
            if (_wsClient != null)
                _wsClient.Dispose();
            if (_cts != null)
                _cts.Dispose();
            if (_pendingEcdh != null)
            {
                _pendingEcdh.Dispose();
                _pendingEcdh = null;
            }
            _disposed = true;
        }
    }
}
