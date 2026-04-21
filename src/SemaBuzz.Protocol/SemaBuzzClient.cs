using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace SemaBuzz.Protocol;

/// <summary>
/// Dials out to a peer's SemaBuzz endpoint and maintains the wire.
/// ECDH P-256 is performed during every handshake so all sessions are
/// encrypted with a fresh AES-256-GCM session key — no passphrase required.
/// </summary>
public sealed class SemaBuzzClient : IDisposable
{
    private UdpClient?     _udp;
    private ClientWebSocket? _wsClient;          // non-null when in WebSocket relay mode
    private Func<byte[], Task>? _wsSend;          // send delegate set after relay pairing
    private IPEndPoint? _peer;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private static readonly TimeSpan HandshakeTimeout   = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ApprovalWaitTimeout = TimeSpan.FromSeconds(60);

    public event EventHandler<SemaBuzzPacketEventArgs>?    PacketReceived;
    public event EventHandler<SemaBuzzWireStateEventArgs>? WireStateChanged;
    public event EventHandler<SemaBuzzMetadataEventArgs>?   MetadataReceived;

    public SemaBuzzWireState State  { get; private set; } = SemaBuzzWireState.Cold;
    public SemaBuzzShield?   Shield { get; private set; }

    private volatile bool _waitingForApproval;

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
            address  = ipv4 ?? addresses[0];
        }

        _peer = new IPEndPoint(address, port);
        _udp = new UdpClient();
        _udp.Connect(_peer);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        SetState(SemaBuzzWireState.Warming, $"Dialing {host}:{port}...");

        // Start receive loop BEFORE sending so the ACK is never missed.
        var receiveTask    = ReceiveLoopAsync(localEcdh, _cts.Token);
        var keepaliveTask  = KeepaliveLoopAsync(_cts.Token);
        var timeoutTask    = HandshakeTimeoutAsync(_cts.Token);
        var retransmitTask = HandshakeRetransmitAsync(localPubKeyBytes, _cts.Token);

        // Send our public key to the host — this IS the handshake initiation.
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

        _cts      = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _wsClient = new ClientWebSocket();

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
                    SetState(SemaBuzzWireState.Dead, "token not found \u2014 host may not be waiting");
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

        // Wire up the WebSocket send delegate used by all public send methods.
        var ws = _wsClient;
        _wsSend = async data =>
        {
            if (ws.State != WebSocketState.Open) return;
            try { await ws.SendAsync(data, WebSocketMessageType.Binary, true, _cts!.Token); } catch { }
        };

        SetState(SemaBuzzWireState.Warming, "Relay paired \u2014 completing handshake...");

        var receiveTask    = WsReceiveLoopAsync(ws, localEcdh, _cts.Token);
        var keepaliveTask  = KeepaliveLoopAsync(_cts.Token);
        var timeoutTask    = HandshakeTimeoutAsync(_cts.Token);
        var retransmitTask = HandshakeRetransmitAsync(localPubKeyBytes, _cts.Token);

        await _wsSend(SemaBuzzKeyExchange.Serialize(localPubKeyBytes));

        await Task.WhenAll(receiveTask, keepaliveTask, timeoutTask, retransmitTask);
    }

    /// <summary>
    /// Receive loop for WebSocket relay mode — mirrors ReceiveLoopAsync but reads
    /// from a WebSocket frame stream instead of UDP datagrams.
    /// </summary>
    private async Task WsReceiveLoopAsync(WebSocket ws, ECDiffieHellman localEcdh, CancellationToken ct)
    {
        var buf = new byte[65_536];
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult recv;
                try { recv = await ws.ReceiveAsync(buf, ct); }
                catch (OperationCanceledException) { break; }
                catch { break; }

                if (recv.MessageType == WebSocketMessageType.Close) break;

                var data = buf[..recv.Count];
                const int MaxPayload = 16_384;
                if (data.Length < SemaBuzzPacket.WireSize || data.Length > MaxPayload) continue;
                if (SemaBuzzRelayPacket.IsRelayPacket(data)) continue; // stray control frame

                // \u2500\u2500 ECDH key exchange \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
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

                // \u2500\u2500 Decrypt \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
                if (Shield != null)
                {
                    var dec = Shield.Decrypt(data);
                    if (dec == null) continue;
                    data = dec;
                }

                // \u2500\u2500 Metadata \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
                if (SemaBuzzMetadata.IsMetadataPacket(data))
                {
                    var meta = SemaBuzzMetadata.Deserialize(data);
                    if (meta.HasValue)
                        MetadataReceived?.Invoke(this, new SemaBuzzMetadataEventArgs(meta.Value.Handle, meta.Value.AvatarPng));
                    continue;
                }


                // \u2500\u2500 Fixed-size control/data frames \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
                for (var offset = 0; offset + SemaBuzzPacket.WireSize <= data.Length; offset += SemaBuzzPacket.WireSize)
                {
                    var packet = SemaBuzzPacket.FromWireBytes(data[offset..(offset + SemaBuzzPacket.WireSize)]);
                    if (packet == null) break;
                    switch (packet.Value.Type)
                    {
                        case SemaBuzzPacketType.HandshakeHold:
                            _waitingForApproval = true;
                            SetState(SemaBuzzWireState.Warming, "waiting for host to approve connection...");
                            break;
                        case SemaBuzzPacketType.ConnectRejected:
                            SetState(SemaBuzzWireState.Dead, "not-available");
                            _cts?.Cancel(); return;
                        case SemaBuzzPacketType.HandshakeAck:
                            SetState(SemaBuzzWireState.Secured, "Wire is live.");
                            break;
                        case SemaBuzzPacketType.Disconnect:
                            SetState(SemaBuzzWireState.Dead, "peer-disconnect");
                            _cts?.Cancel(); return;
                        case SemaBuzzPacketType.Ping:
                            break;
                        case SemaBuzzPacketType.Buzz:
                        case SemaBuzzPacketType.Char:
                            PacketReceived?.Invoke(this, new SemaBuzzPacketEventArgs(packet.Value));
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { SetState(SemaBuzzWireState.Dead, "Connection error."); }
        finally { _wsSend = null; }
    }

    private async Task HandshakeTimeoutAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(HandshakeTimeout, ct);

            if (_waitingForApproval)
            {
                // Host is reviewing the request — give them time to decide
                await Task.Delay(ApprovalWaitTimeout, ct);
                if (State == SemaBuzzWireState.Warming)
                {
                    SetState(SemaBuzzWireState.Dead, "host did not respond to connection request");
                    _cts?.Cancel();
                }
                return;
            }

            if (State == SemaBuzzWireState.Warming)
            {
                SetState(SemaBuzzWireState.Dead, "no response — host may not be listening");
                _cts?.Cancel();
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
                var data   = result.Buffer;

                // Reject implausible sizes immediately
                const int MaxPayload = 16_384;
                if (data.Length < SemaBuzzPacket.WireSize || data.Length > MaxPayload) continue;

                // Only accept traffic from the host we dialed
                if (_peer != null && !result.RemoteEndPoint.Equals(_peer)) continue;

                //  ECDH key exchange (plaintext, during handshake only)
                if (SemaBuzzKeyExchange.IsKeyExchangePacket(data))
                {
                    if (Shield != null) continue; // already established — ignore
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
                    if (decrypted == null) continue; // tampered or wrong key — drop
                    data = decrypted;
                }

                //  Variable-length packets
                if (SemaBuzzMetadata.IsMetadataPacket(data))
                {
                    var meta = SemaBuzzMetadata.Deserialize(data);
                    if (meta.HasValue)
                        MetadataReceived?.Invoke(this, new SemaBuzzMetadataEventArgs(meta.Value.Handle, meta.Value.AvatarPng));
                    continue;
                }

                //  Fixed-size packet frame(s) — may be batched
                for (var offset = 0; offset + SemaBuzzPacket.WireSize <= data.Length;
                         offset += SemaBuzzPacket.WireSize)
                {
                    var frame  = data[offset..(offset + SemaBuzzPacket.WireSize)];
                    var packet = SemaBuzzPacket.FromWireBytes(frame);
                    if (packet == null) break; // bad frame

                    switch (packet.Value.Type)
                    {
                        case SemaBuzzPacketType.HandshakeHold:
                            _waitingForApproval = true;
                            SetState(SemaBuzzWireState.Warming, "waiting for host to approve connection...");
                            break;

                        case SemaBuzzPacketType.ConnectRejected:
                            SetState(SemaBuzzWireState.Dead, "not-available");
                            _cts?.Cancel();
                            return;

                        case SemaBuzzPacketType.HandshakeAck:
                            // ECDH has already set up the shield — session is always Secured.
                            SetState(SemaBuzzWireState.Secured, "Wire is live.");
                            break;

                        case SemaBuzzPacketType.Disconnect:
                            SetState(SemaBuzzWireState.Dead, "peer-disconnect");
                            _cts?.Cancel();
                            return;

                        case SemaBuzzPacketType.Ping:
                            break;

                        case SemaBuzzPacketType.Buzz:
                        case SemaBuzzPacketType.Char:
                            PacketReceived?.Invoke(this, new SemaBuzzPacketEventArgs(packet.Value));
                            break;
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
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
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
    public async Task SendMetadataAsync(string handle, byte[]? avatarPng)
    {
        if ((_udp == null && _wsSend == null) || State is not (SemaBuzzWireState.Live or SemaBuzzWireState.Secured)) return;
        var bytes = SemaBuzzMetadata.Serialize(handle, avatarPng);
        if (Shield != null) bytes = Shield.Encrypt(bytes);
        if (_wsSend != null) { await _wsSend(bytes); return; }
        await _udp!.SendAsync(bytes);
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

        var plaintext = new byte[packets.Count * SemaBuzzPacket.WireSize];
        for (var i = 0; i < packets.Count; i++)
            packets[i].ToWireBytes().CopyTo(plaintext, i * SemaBuzzPacket.WireSize);

        var bytes = Shield != null ? Shield.Encrypt(plaintext) : plaintext;
        if (_wsSend != null) { await _wsSend(bytes); return; }
        await _udp!.SendAsync(bytes);
    }

    /// <summary>Send a Buzz to the peer — spikes their filament and shakes their window.</summary>
    public Task SendBuzzAsync() => SendAsync(SemaBuzzPacket.Control(SemaBuzzPacketType.Buzz));

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
        if (_wsClient?.State == WebSocketState.Open)
            try { await _wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", default); } catch { }
        _cts?.Cancel();
        SetState(SemaBuzzWireState.Dead, "Wire closed by local peer.");
    }

    private void SetState(SemaBuzzWireState state, string? message = null)
    {
        State = state;
        WireStateChanged?.Invoke(this, new SemaBuzzWireStateEventArgs(state, message));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts?.Cancel();
            _udp?.Dispose();
            _wsClient?.Dispose();
            _cts?.Dispose();
            _disposed = true;
        }
    }
}
