using System.Net;
using System.Net.WebSockets;
using SemaBuzz.Protocol;

namespace SemaBuzz.Relay;

/// <summary>
/// Represents an active relay room — a matched host + dialer pair connected via WebSocket.
/// Serialises all sends per-socket so there are never concurrent SendAsync calls on the same WebSocket.
/// </summary>
internal sealed class RelayRoom
{
    private readonly SemaphoreSlim _hostLock = new(1, 1);
    private SemaphoreSlim? _dialerLock;

    public string Token { get; }
    public WebSocket HostWs { get; }
    public WebSocket? DialerWs { get; private set; }
    public DateTime LastActive { get; private set; } = DateTime.UtcNow;
    public bool IsPaired => DialerWs != null;

    // External UDP endpoints received via PunchReady frames.
    public IPEndPoint? HostExternalEp { get; private set; }
    public IPEndPoint? DialerExternalEp { get; private set; }

    public RelayRoom(string token, WebSocket hostWs)
    {
        Token = token;
        HostWs = hostWs;
    }

    public void SetDialer(WebSocket ws)
    {
        DialerWs = ws;
        _dialerLock = new SemaphoreSlim(1, 1);
        Touch();
    }

    public void SetHostExternalEp(IPEndPoint ep) => HostExternalEp = ep;
    public void SetDialerExternalEp(IPEndPoint ep) => DialerExternalEp = ep;

    public void Touch() => LastActive = DateTime.UtcNow;

    /// <summary>Send data to the host, serialised.</summary>
    public async Task SendToHostAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await _hostLock.WaitAsync(ct);
        try
        {
            if (HostWs.State == WebSocketState.Open)
                await HostWs.SendAsync(data, WebSocketMessageType.Binary, true, ct);
        }
        catch { /* peer disconnected */ }
        finally { _hostLock.Release(); }
    }

    /// <summary>Send data to the dialer, serialised.</summary>
    public async Task SendToDialerAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (DialerWs == null || _dialerLock == null) return;
        await _dialerLock.WaitAsync(ct);
        try
        {
            if (DialerWs.State == WebSocketState.Open)
                await DialerWs.SendAsync(data, WebSocketMessageType.Binary, true, ct);
        }
        catch { /* peer disconnected */ }
        finally { _dialerLock.Release(); }
    }

    /// <summary>Forward data from <paramref name="from"/> to the other peer.</summary>
    public Task ForwardToAsync(WebSocket from, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (ReferenceEquals(from, HostWs)) return SendToDialerAsync(data, ct);
        if (ReferenceEquals(from, DialerWs)) return SendToHostAsync(data, ct);
        return Task.CompletedTask;
    }
}

