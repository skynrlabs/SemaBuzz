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
    private UdpClient?       _udp;
    private ClientWebSocket?  _wsClient;        // non-null when in WebSocket relay mode
    private Func<byte[], Task>? _wsSend;         // send delegate set after relay pairing
    private bool              _isRelayMode;      // true when connected via WebSocket relay
    private CancellationTokenSource? _cts;
    private int _port;
    private bool _disposed;
    private byte[]? _localPubKeyBytes; // saved so we can resend on client retransmit

    public event EventHandler<SemaBuzzPacketEventArgs>?    PacketReceived;
    public event EventHandler<SemaBuzzWireStateEventArgs>? WireStateChanged;
    public event EventHandler<SemaBuzzMetadataEventArgs>?   MetadataReceived;

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

        _cts       = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _wsClient  = new ClientWebSocket();
        _isRelayMode = true;

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

        // Wire up the WebSocket send delegate used by all internal send helpers.
        var ws = _wsClient;
        _wsSend = async data =>
        {
            if (ws.State != WebSocketState.Open) return;
            try { await ws.SendAsync(data, WebSocketMessageType.Binary, true, _cts!.Token); } catch { }
        };

        _port = 0;
        SetState(SemaBuzzWireState.Warming, "Peer connected via relay \u2014 completing handshake...");

        // Relay dummy endpoint: used as the stand-in remote address in HandleIncomingAsync.
        var relayPeer = new IPEndPoint(IPAddress.Loopback, 0);
        var dataBuf   = new byte[65_536];
        try
        {
            while (!_cts.Token.IsCancellationRequested && _wsClient.State == WebSocketState.Open)
            {
                WebSocketReceiveResult recv;
                try { recv = await _wsClient.ReceiveAsync(dataBuf, _cts.Token); }
                catch (OperationCanceledException) { break; }
                catch { break; }

                if (recv.MessageType == WebSocketMessageType.Close) break;
                if (recv.Count < SemaBuzzPacket.WireSize) continue;
                // Skip stray relay control frames.
                if (recv.Count == SemaBuzzRelayPacket.Size && SemaBuzzRelayPacket.IsRelayPacket(dataBuf)) continue;

                var data = dataBuf[..recv.Count];
                await HandleIncomingAsync(new UdpReceiveResult(data, relayPeer));
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _wsSend = null;
            _isRelayMode = false;
            SetState(SemaBuzzWireState.Dead, "Wire closed.");
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
        _udp  = new UdpClient(port);
        _cts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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

        // ── Hard size bounds ────────────────────────────────────────────────
        // Smallest valid payload: 6-byte plaintext packet.
        // Largest valid payload: encrypted metadata with a thumbnail avatar.
        // Anything outside [6, 16384] cannot be a legitimate SemaBuzz packet.
        const int MaxPayload = 16_384;
        if (data.Length < SemaBuzzPacket.WireSize || data.Length > MaxPayload) return;

        // ── Source filter ───────────────────────────────────────────────────
        // Once the wire is Live or Secured, only accept traffic from the
        // established peer. Drop everything else silently.
        if (State is SemaBuzzWireState.Live or SemaBuzzWireState.Secured
            && PeerEndPoint != null
            && !result.RemoteEndPoint.Equals(PeerEndPoint))
            return;

        // ── ECDH key exchange (plaintext — must be handled before Shield check) ────────
        if (SemaBuzzKeyExchange.IsKeyExchangePacket(data))
        {
            if (Shield != null)
            {
                // Client is retransmitting — our KE response or HandshakeAck was lost.
                // Resend whatever is appropriate for the current state.
                if (PeerEndPoint?.Equals(result.RemoteEndPoint) == true && _localPubKeyBytes != null)
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

            using var localEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var localPubKeyBytes = localEcdh.PublicKey.ExportSubjectPublicKeyInfo();
            _localPubKeyBytes = localPubKeyBytes; // save for retransmits

            using var peerEcdh = ECDiffieHellman.Create();
            peerEcdh.ImportSubjectPublicKeyInfo(peerPubKeyBytes, out _);
            var rawSecret = localEcdh.DeriveKeyFromHash(
                peerEcdh.PublicKey, HashAlgorithmName.SHA256,
                secretPrepend: "SemaBuzz-ecdh-v2"u8.ToArray(),
                secretAppend:  null);
            Shield = new SemaBuzzShield(rawSecret);
            CryptographicOperations.ZeroMemory(rawSecret);

            // Send our public key back (key exchange is always plaintext)
            await SendRawAsync(SemaBuzzKeyExchange.Serialize(localPubKeyBytes), result.RemoteEndPoint);

            PeerEndPoint = result.RemoteEndPoint;
            SetState(SemaBuzzWireState.Warming, "Handshake received \u2014 awaiting host approval...");

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
                // Corrupted or out-of-order packet — drop silently.
                // Fatal only if we're already live (session key mismatch shouldn't happen post-ECDH).
                if (State is SemaBuzzWireState.Live or SemaBuzzWireState.Secured)
                {
                    SetState(SemaBuzzWireState.Dead, "received unreadable packet — session key mismatch");
                    _cts?.Cancel();
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
                MetadataReceived?.Invoke(this, new SemaBuzzMetadataEventArgs(meta.Value.Handle, meta.Value.AvatarPng));
            return;
        }


        var packet = SemaBuzzPacket.FromWireBytes(data);
        if (packet == null) return;

        switch (packet.Value.Type)
        {
            case SemaBuzzPacketType.Handshake:
                PeerEndPoint = result.RemoteEndPoint;
                SetState(SemaBuzzWireState.Warming, "Handshake received — awaiting host approval...");

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
                SetState(Shield != null ? SemaBuzzWireState.Secured : SemaBuzzWireState.Live);
                break;

            case SemaBuzzPacketType.Disconnect:
                // Peer walked away.
                PeerEndPoint = null;
                Shield = null;
                _localPubKeyBytes = null;
                if (_isRelayMode)
                    _cts?.Cancel(); // relay session is one-to-one; close it out
                else
                    SetState(SemaBuzzWireState.Warming, $"Listening on port {_port}...");
                break;

            case SemaBuzzPacketType.Ping:
                // Keepalive — no event needed
                break;

            case SemaBuzzPacketType.Buzz:
            case SemaBuzzPacketType.Char:
                PacketReceived?.Invoke(this, new SemaBuzzPacketEventArgs(packet.Value));
                break;

            default:
                // Unknown or unexpected type — drop silently
                break;
        }
    }

    private async Task SendAckAsync(IPEndPoint peer)
    {
        var bytes = SemaBuzzPacket.Control(SemaBuzzPacketType.HandshakeAck).ToWireBytes();
        await SendRawAsync(bytes, peer);
    }

    /// <summary>Send a control packet to the given peer (plaintext — for pre-handshake signals).</summary>
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
        if (_wsClient?.State == WebSocketState.Open)
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

        var plaintext = new byte[packets.Count * SemaBuzzPacket.WireSize];
        for (var i = 0; i < packets.Count; i++)
            packets[i].ToWireBytes().CopyTo(plaintext, i * SemaBuzzPacket.WireSize);

        var bytes = Shield != null ? Shield.Encrypt(plaintext) : plaintext;
        await SendRawAsync(bytes, PeerEndPoint);
    }

    /// <summary>Send a Buzz to the peer — spikes their filament and shakes their window.</summary>
    public Task SendBuzzAsync() => SendAsync(SemaBuzzPacket.Control(SemaBuzzPacketType.Buzz));


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
