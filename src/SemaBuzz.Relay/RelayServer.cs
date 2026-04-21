using System.Collections.Concurrent;
using System.Net.WebSockets;
using SemaBuzz.Protocol;

namespace SemaBuzz.Relay;

/// <summary>
/// WebSocket relay server. Each client connects to /relay, sends a JoinHost or
/// JoinDial control frame, and the relay pairs them and forwards all subsequent
/// binary frames transparently  no parsing of the SemaBuzz wire protocol.
/// TLS is handled by the hosting platform's reverse proxy (Railway, Fly.io, etc.).
/// </summary>
internal sealed class RelayServer
{
    private static readonly TimeSpan RoomTtl       = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan JoinTimeout   = TimeSpan.FromSeconds(10);
    private const            int     MaxRooms       = 500;   // global cap
    private const            int     MaxPerIp       = 5;     // concurrent sockets per IP

    private readonly ConcurrentDictionary<string, RelayRoom> _rooms =
        new(StringComparer.OrdinalIgnoreCase);

    // IP → number of currently-open WebSocket connections from that IP.
    private readonly ConcurrentDictionary<string, int> _connByIp = new();

    private readonly System.Timers.Timer _sweepTimer;

    public RelayServer()
    {
        _sweepTimer = new System.Timers.Timer(TimeSpan.FromMinutes(2)) { AutoReset = true };
        _sweepTimer.Elapsed += (_, _) => Sweep();
        _sweepTimer.Start();
    }

    // Entry point  one call per accepted WebSocket connection

    public async Task HandleClientAsync(WebSocket ws, string remoteIp, CancellationToken ct)
    {
        // --- Per-IP connection cap ---
        var count = _connByIp.AddOrUpdate(remoteIp, 1, (_, c) => c + 1);
        if (count > MaxPerIp)
        {
            _connByIp.AddOrUpdate(remoteIp, 0, (_, c) => Math.Max(0, c - 1));
            await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "Too many connections", ct);
            return;
        }

        try
        {
            await HandleInnerAsync(ws, remoteIp, ct);
        }
        finally
        {
            _connByIp.AddOrUpdate(remoteIp, 0, (_, c) => Math.Max(0, c - 1));
        }
    }

    private async Task HandleInnerAsync(WebSocket ws, string remoteIp, CancellationToken ct)
    {
        // --- Join phase: client must send a valid join frame within JoinTimeout ---
        var buf = new byte[64];
        WebSocketReceiveResult recv;
        using var joinCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        joinCts.CancelAfter(JoinTimeout);
        try { recv = await ws.ReceiveAsync(buf, joinCts.Token); }
        catch { await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "Join timeout", ct); return; }

        if (recv.MessageType == WebSocketMessageType.Close || recv.Count < SemaBuzzRelayPacket.Size)
        {
            await CloseAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Expected join frame", ct);
            return;
        }

        var parsed = SemaBuzzRelayPacket.Parse(buf);
        if (parsed == null)
        {
            await CloseAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Bad join packet", ct);
            return;
        }

        var (type, token) = parsed.Value;

        if (type == SemaBuzzRelayPacketType.JoinHost)
        {
            // --- Global room cap ---
            if (_rooms.Count >= MaxRooms)
            {
                await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "Server busy", ct);
                return;
            }

            var room = new RelayRoom(token, ws);
            _rooms[token] = room;
            try
            {
                // Block here until the WebSocket closes (or the dialer arrives and the session ends).
                await ForwardLoopAsync(ws, room, ct);
            }
            finally
            {
                _rooms.TryRemove(token, out _);
            }
        }
        else if (type == SemaBuzzRelayPacketType.JoinDial)
        {
            if (!_rooms.TryGetValue(token, out var room) || room.HostWs.State != WebSocketState.Open)
            {
                var err = SemaBuzzRelayPacket.Build(SemaBuzzRelayPacketType.RelayError, token);
                try { await ws.SendAsync(err, WebSocketMessageType.Binary, true, ct); } catch { }
                await CloseAsync(ws, WebSocketCloseStatus.NormalClosure, "Token not found", ct);
                return;
            }

            room.SetDialer(ws);

            var paired = SemaBuzzRelayPacket.Build(SemaBuzzRelayPacketType.Paired, token);
            await room.SendToHostAsync(paired, ct);
            await room.SendToDialerAsync(paired, ct);

            await ForwardLoopAsync(ws, room, ct);
        }
        else
        {
            await CloseAsync(ws, WebSocketCloseStatus.InvalidPayloadData, "Unknown join type", ct);
        }
    }

    // Forward loop — read from this peer, send to the other
    // PunchReady frames (0x06) are intercepted and used to exchange external endpoints;
    // all other frames are forwarded transparently.

    private static async Task ForwardLoopAsync(WebSocket ws, RelayRoom room, CancellationToken ct)
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

                room.Touch();
                var frame = buf.AsMemory(0, recv.Count);

                // Intercept PunchReady — do NOT forward to peer.
                if (recv.Count == SemaBuzzRelayPacket.PunchPacketSize
                    && SemaBuzzRelayPacket.IsRelayPacket(buf)
                    && (SemaBuzzRelayPacketType)buf[3] == SemaBuzzRelayPacketType.PunchReady)
                {
                    var ep = SemaBuzzRelayPacket.ParseEndpoint(buf[..recv.Count]);
                    if (ep != null)
                    {
                        bool isHost = ReferenceEquals(ws, room.HostWs);
                        if (isHost) room.SetHostExternalEp(ep);
                        else        room.SetDialerExternalEp(ep);

                        // Once both endpoints are known, tell each peer about the other.
                        if (room.HostExternalEp != null && room.DialerExternalEp != null)
                        {
                            var toHost   = SemaBuzzRelayPacket.BuildPeerAddress(room.Token, room.DialerExternalEp);
                            var toDialer = SemaBuzzRelayPacket.BuildPeerAddress(room.Token, room.HostExternalEp);
                            await room.SendToHostAsync(toHost, ct);
                            await room.SendToDialerAsync(toDialer, ct);
                        }
                    }
                    continue; // never forward PunchReady frames
                }

                await room.ForwardToAsync(ws, frame, ct);
            }
        }
        finally
        {
            await CloseAsync(ws, WebSocketCloseStatus.NormalClosure, "Session ended", ct);
        }
    }

    // Expiry sweep

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - RoomTtl;
        foreach (var kvp in _rooms.ToArray())
        {
            if (kvp.Value.LastActive < cutoff)
                _rooms.TryRemove(kvp.Key, out _);
        }
    }

    // Helper

    private static async Task CloseAsync(WebSocket ws, WebSocketCloseStatus status, string desc, CancellationToken ct)
    {
        if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            try { await ws.CloseAsync(status, desc, CancellationToken.None); } catch { }
    }
}
