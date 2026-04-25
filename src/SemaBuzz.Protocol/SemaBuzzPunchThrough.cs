using System.Net;
using System.Net.Sockets;

namespace SemaBuzz.Protocol;

/// <summary>
/// UDP hole-punching helper used in the relay + STUN ICE-lite flow.
///
/// How it works
/// -------------
/// 1. Both peers bind a UDP socket on their listen port and run STUN to find
///    their external IP:port (SemaBuzzStun.DiscoverAsync on the same UdpClient).
/// 2. They each send a PunchReady frame to the relay, which echoes back a
///    PeerAddress frame carrying the other side's external endpoint.
/// 3. Both peers simultaneously send a series of small "punch" probes to the
///    other's external endpoint, which pokes holes in both NATs.
/// 4. The first valid probe reply confirms a direct path; at that point the
///    relay connection is abandoned and the ECDH handshake continues directly.
/// 5. If no direct path is established within the timeout, the caller falls back
///    to the relay transparently.
/// </summary>
public static class SemaBuzzPunchThrough
{
    private static readonly TimeSpan PunchProbeInterval = TimeSpan.FromMilliseconds(200);
    private const int PunchMagic = 0x42_5A; // 'BZ' -- used to distinguish probe from wire traffic
    private const int PunchProbeSize = 4;
    private static readonly byte[] PunchProbe = [0x42, 0x5A, 0x01, 0x00]; // probe
    private static readonly byte[] PunchAck = [0x42, 0x5A, 0x01, 0x01]; // reply

    public static bool IsPunchProbe(byte[] data) =>
        data.Length == PunchProbeSize && data[0] == 0x42 && data[1] == 0x5A && data[2] == 0x01 && data[3] == 0x00;

    public static bool IsPunchAck(byte[] data) =>
        data.Length == PunchProbeSize && data[0] == 0x42 && data[1] == 0x5A && data[2] == 0x01 && data[3] == 0x01;

    /// <summary>
    /// Attempt to punch through NAT to <paramref name="peerEp"/> using the already-bound
    /// <paramref name="udp"/> socket.  Sends PunchProbe datagrams and returns the external
    /// endpoint that successfully replied, or <c>null</c> if no reply was received within
    /// <paramref name="timeout"/>.
    /// </summary>
    public static async Task<IPEndPoint?> TryAsync(
        UdpClient udp,
        IPEndPoint peerEp,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        innerCts.CancelAfter(timeout);

        // Start a receive task that looks for either a probe or an ack from the peer.
        var replyTask = ReceivePunchReplyAsync(udp, peerEp, innerCts.Token);

        // Send probes in a loop until either we get a reply or time out.
        try
        {
            while (!innerCts.Token.IsCancellationRequested)
            {
                try { await udp.SendAsync(PunchProbe, peerEp, innerCts.Token); } catch { break; }
                try { await Task.Delay(PunchProbeInterval, innerCts.Token); } catch { break; }
            }
        }
        catch (OperationCanceledException) { /* expected at timeout */ }

        var confirmedEp = await replyTask;
        return confirmedEp;
    }

    /// <summary>
    /// Receive loop that watches for punch probes / acks from the expected peer.
    /// When a probe arrives, sends back a PunchAck so the other side can confirm.
    /// Returns the peer's endpoint on success, null on cancellation.
    /// </summary>
    private static async Task<IPEndPoint?> ReceivePunchReplyAsync(
        UdpClient udp,
        IPEndPoint expectedPeer,
        CancellationToken ct)
    {
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await udp.ReceiveAsync(ct); }
                catch { break; }

                var data = result.Buffer;
                var remoteEp = result.RemoteEndPoint;

                // Only accept punch traffic from the expected peer address.
                if (!remoteEp.Address.Equals(expectedPeer.Address)) continue;

                if (IsPunchProbe(data))
                {
                    // Reply with an ack so the sender knows the path is open.
                    try { await udp.SendAsync(PunchAck, remoteEp, ct); } catch { }
                    // A probe from the peer also means we can reach them -- success.
                    return remoteEp;
                }

                if (IsPunchAck(data))
                    return remoteEp;
            }
        }
        catch (OperationCanceledException) { }
        return null;
    }
}
