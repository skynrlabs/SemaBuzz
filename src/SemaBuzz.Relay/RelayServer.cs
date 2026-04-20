using System.Collections.Concurrent;
using System.Net.WebSockets;
using SemaBuzz.Protocol;

namespace SemaBuzz.Relay;

/// <summary>
/// WebSocket relay server. Each client connects to /relay, sends a JoinHost or
/// JoinDial control frame, and the relay pairs them and forwards all subsequent
/// binary frames transparently — no parsing of the SemaBuzz wire protocol.
/// TLS is handled by the hosting platform's reverse proxy (Railway, Fly.io, etc.).
/// </summary>
internal sealed class RelayServer
{
    private static readonly TimeSpan RoomTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, RelayRoom> _rooms =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly System.Timers.Timer _sweepTimer;

    public RelayServer()
    {
        _sweepTimer = new System.Timers.Timer(TimeSpan.FromMinutes(2)) { AutoReset = true };
        _sweepTimer.Elapsed += (_, _) => Sweep();
        _sweepTimer.Start();
    }

    // ─────────────────────────────────────────────────────────
    // Entry point — one call per accepted WebSocket connection
    // ─────────────────────────────────────────────────────────

    public async Task HandleClientAsync(WebSocket ws, CancellationToken ct)
    {
        // Every client must begin with a JoinHost or JoinDial frame.
        var buf = new byte[64];
        WebSocketReceiveResult recv;
        try { recv = await ws.ReceiveAsync(buf, ct); }
        catch { return; }

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
            var room = new RelayRoom(token, ws);
            _rooms[token] = room;
            Console.WriteLine($"[relay] host   token={token}");
            // Block here until the WebSocket closes (or the dialer arrives and the session ends).
            await ForwardLoopAsync(ws, room, ct);
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
            Console.WriteLine($"[relay] dialer token={token}");

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

    // ─────────────────────────────────────────────────────────
    // Forward loop — read from this peer, send to the other
    // ─────────────────────────────────────────────────────────

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
                await room.ForwardToAsync(ws, buf.AsMemory(0, recv.Count), ct);
            }
        }
        finally
        {
            await CloseAsync(ws, WebSocketCloseStatus.NormalClosure, "Session ended", ct);
        }
    }

    // ─────────────────────────────────────────────────────────
    // Expiry sweep
    // ─────────────────────────────────────────────────────────

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - RoomTtl;
        foreach (var kvp in _rooms.ToArray())
        {
            if (kvp.Value.LastActive < cutoff && _rooms.TryRemove(kvp.Key, out _))
                Console.WriteLine($"[relay] swept  token={kvp.Key}");
        }
    }

    // ─────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────

    private static async Task CloseAsync(WebSocket ws, WebSocketCloseStatus status, string desc, CancellationToken ct)
    {
        if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            try { await ws.CloseAsync(status, desc, CancellationToken.None); } catch { }
    }
}
