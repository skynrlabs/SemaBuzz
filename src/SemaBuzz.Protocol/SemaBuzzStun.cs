using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace SemaBuzz.Protocol;

/// <summary>
/// Minimal RFC 5389 STUN Binding Request client.
///
/// Sends a 20-byte Binding Request to a public STUN server and parses the
/// XOR-MAPPED-ADDRESS (or MAPPED-ADDRESS fallback) from the response to
/// discover the external IP:port the NAT has assigned to the local socket.
///
/// Only IPv4 is handled — IPv6 peers don't need NAT traversal assistance.
/// </summary>
public static class SemaBuzzStun
{
    // Well-known, always-free STUN servers — tried in order until one responds.
    private static readonly (string Host, int Port)[] Servers =
    [
        ("stun.l.google.com",    19302),
        ("stun1.l.google.com",   19302),
        ("stun.cloudflare.com",  3478),
    ];

    private const uint   MagicCookie      = 0x2112_A442;
    private const ushort BindingRequest   = 0x0001;
    private const ushort BindingResponse  = 0x0101;
    private const ushort AttrXorMapped    = 0x0020; // RFC 5389
    private const ushort AttrMapped       = 0x0001; // RFC 3489 compat

    /// <summary>
    /// Query each configured STUN server in turn.
    /// Returns the first successful XOR-MAPPED-ADDRESS, or null if all fail.
    /// </summary>
    /// <param name="localPort">
    ///   Local UDP port to bind before querying. Use the same port the listener
    ///   will bind so the NAT mapping corresponds to the actual session port.
    ///   Pass 0 for an ephemeral port (IP-only interest, port may differ).
    /// </param>
    public static async Task<IPEndPoint?> DiscoverAsync(
        int localPort = 0,
        CancellationToken cancellationToken = default)
    {
        foreach (var (host, port) in Servers)
        {
            try
            {
                var ep = await QueryServerAsync(host, port, localPort, cancellationToken);
                if (ep != null) return ep;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* server unreachable — try next */ }
        }
        return null;
    }

    // ─────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────

    private static async Task<IPEndPoint?> QueryServerAsync(
        string host, int stunPort, int localPort, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        // Prefer IPv4 so the mapped address is always IPv4
        var addr      = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? addresses[0];
        var serverEp  = new IPEndPoint(addr, stunPort);

        using var udp = new UdpClient(localPort);
        udp.Connect(serverEp);

        // Build a fresh transaction ID for each attempt
        var txId    = new byte[12];
        var request = BuildBindingRequest(txId);

        // Up to 3 attempts with a 2-second per-attempt timeout (RFC recommends Rc=7,
        // but 3 is enough for discovery UX — we fall back to the next server otherwise)
        for (var attempt = 0; attempt < 3; attempt++)
        {
            RandomNumberGenerator.Fill(txId);           // fresh txId per attempt
            BuildBindingRequest(txId).CopyTo(request, 0);

            await udp.SendAsync(request, ct);

            using var attemptCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, attemptCts.Token);
            try
            {
                var result = await udp.ReceiveAsync(linked.Token);
                var ep     = ParseBindingResponse(result.Buffer, txId);
                if (ep != null) return ep;
            }
            catch (OperationCanceledException) when (attemptCts.IsCancellationRequested)
            {
                // Per-attempt timeout — retry
                continue;
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────
    // Message construction (RFC 5389 §6)
    // ─────────────────────────────────────────────

    private static byte[] BuildBindingRequest(byte[] txId)
    {
        // 20-byte STUN header, no attributes
        var msg = new byte[20];

        // Message type: Binding Request (0x0001)
        msg[0] = 0x00; msg[1] = 0x01;

        // Message length: 0 (no body)
        msg[2] = 0x00; msg[3] = 0x00;

        // Magic cookie: 0x2112A442
        msg[4] = 0x21; msg[5] = 0x12; msg[6] = 0xA4; msg[7] = 0x42;

        // Transaction ID (12 bytes)
        Buffer.BlockCopy(txId, 0, msg, 8, 12);
        return msg;
    }

    // ─────────────────────────────────────────────
    // Response parsing (RFC 5389 §7 + §15.2)
    // ─────────────────────────────────────────────

    private static IPEndPoint? ParseBindingResponse(byte[] data, byte[] txId)
    {
        // Minimum: 20-byte header
        if (data.Length < 20) return null;

        // Message type must be Binding Success Response (0x0101)
        var type = (ushort)((data[0] << 8) | data[1]);
        if (type != BindingResponse) return null;

        // Magic cookie (bytes 4–7)
        if (data[4] != 0x21 || data[5] != 0x12 || data[6] != 0xA4 || data[7] != 0x42)
            return null;

        // Transaction ID must match what we sent (bytes 8–19)
        for (var i = 0; i < 12; i++)
            if (data[8 + i] != txId[i]) return null;

        var bodyLen = (data[2] << 8) | data[3];
        if (data.Length < 20 + bodyLen) return null;

        // Walk TLV attributes
        IPEndPoint? xorMapped = null;
        IPEndPoint? mapped    = null;
        var offset            = 20;

        while (offset + 4 <= 20 + bodyLen)
        {
            var attrType   = (ushort)((data[offset] << 8) | data[offset + 1]);
            var attrLen    =          (data[offset + 2] << 8) | data[offset + 3];
            var valueStart = offset + 4;

            if (valueStart + attrLen > data.Length) break; // malformed — stop

            if (attrType == AttrXorMapped && attrLen >= 8)
                xorMapped = ParseXorMappedAddress(data, valueStart);
            else if (attrType == AttrMapped && attrLen >= 8)
                mapped = ParseMappedAddress(data, valueStart);

            // Attributes are padded to 4-byte boundaries
            offset = valueStart + ((attrLen + 3) & ~3);
        }

        // XOR-MAPPED-ADDRESS takes priority over legacy MAPPED-ADDRESS
        return xorMapped ?? mapped;
    }

    /// <summary>
    /// RFC 5389 §15.2 — XOR-MAPPED-ADDRESS
    /// Layout at value offset:
    ///   [0]    reserved
    ///   [1]    family (0x01 = IPv4)
    ///   [2-3]  port XOR (MagicCookie >> 16)
    ///   [4-7]  address XOR MagicCookie  (IPv4)
    /// </summary>
    private static IPEndPoint? ParseXorMappedAddress(byte[] data, int offset)
    {
        if (data[offset + 1] != 0x01) return null; // IPv6 — not handled

        var xorPort = (ushort)((data[offset + 2] << 8) | data[offset + 3]);
        var port    = (int)(xorPort ^ (MagicCookie >> 16));

        var xorIp = ((uint)data[offset + 4] << 24)
                  | ((uint)data[offset + 5] << 16)
                  | ((uint)data[offset + 6] <<  8)
                  |  (uint)data[offset + 7];
        var rawIp = xorIp ^ MagicCookie;

        var ip = new IPAddress(new byte[]
        {
            (byte)(rawIp >> 24),
            (byte)(rawIp >> 16),
            (byte)(rawIp >>  8),
            (byte) rawIp,
        });
        return new IPEndPoint(ip, port);
    }

    /// <summary>
    /// RFC 3489 §11.2.1 — MAPPED-ADDRESS (legacy, no XOR)
    /// Layout at value offset:
    ///   [0]    reserved
    ///   [1]    family (0x01 = IPv4)
    ///   [2-3]  port
    ///   [4-7]  address (IPv4)
    /// </summary>
    private static IPEndPoint? ParseMappedAddress(byte[] data, int offset)
    {
        if (data[offset + 1] != 0x01) return null; // IPv6 — not handled

        var port = (data[offset + 2] << 8) | data[offset + 3];
        var ip   = new IPAddress(new byte[]
        {
            data[offset + 4],
            data[offset + 5],
            data[offset + 6],
            data[offset + 7],
        });
        return new IPEndPoint(ip, port);
    }
}
