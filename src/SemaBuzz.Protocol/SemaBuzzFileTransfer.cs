using System.Text;

namespace SemaBuzz.Protocol;

/// <summary>
/// Variable-length wire format for file transfer offers and control signals.
///
/// All packets use the same [SB][marker][payload] framing as other
/// variable-length packet types (metadata, draw, url-push).
///
/// File bytes are transferred out-of-band via HTTP (POST /file on the relay).
/// Only the offer metadata (filename, size, SHA-256, relay token) and the
/// accept/reject control signals travel through the WebSocket channel.
///
/// Markers (data[2]):
///   0x0B  FileOffer  — sender proposes a file; carries relay token for download
///   0x0D  FileAccept — receiver accepts the offer
///   0x0E  FileReject — receiver declines the offer
/// </summary>
public static class SemaBuzzFileTransfer
{
    public const byte FileOfferByte  = 0x0B;
    public const byte FileAcceptByte = 0x0D;
    public const byte FileRejectByte = 0x0E;

    /// <summary>Maximum file size accepted by the protocol (10 MB).</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024;

    // -------------------------------------------------------------------------
    // FileOffer
    //
    // Format:
    //   [SB][0x0B][transfer_id:u8][filename_len:u8][filename:utf8]
    //   [file_size:u32le][sha256:32 bytes][token_len:u8][token:ascii]
    //
    // Minimum serialized size: 3 + 1 + 1 + 0 + 4 + 32 + 1 + 1 = 43 bytes
    // -------------------------------------------------------------------------

    public static bool IsFileOfferPacket(byte[] data) =>
        data.Length >= 43 &&
        data[0] == SemaBuzzPacket.MagicByte1 &&
        data[1] == SemaBuzzPacket.MagicByte2 &&
        data[2] == FileOfferByte;

    public static byte[] SerializeFileOffer(
        byte transferId, string filename, long fileSize, byte[] sha256, string token)
    {
        var nameBytes  = Encoding.UTF8.GetBytes(filename);
        if (nameBytes.Length > 255) nameBytes = nameBytes[..255];
        var tokenBytes = Encoding.ASCII.GetBytes(token);
        if (tokenBytes.Length > 255) tokenBytes = tokenBytes[..255];

        var buf = new byte[3 + 1 + 1 + nameBytes.Length + 4 + 32 + 1 + tokenBytes.Length];
        buf[0] = SemaBuzzPacket.MagicByte1;
        buf[1] = SemaBuzzPacket.MagicByte2;
        buf[2] = FileOfferByte;
        buf[3] = transferId;
        buf[4] = (byte)nameBytes.Length;
        nameBytes.CopyTo(buf, 5);

        var o = 5 + nameBytes.Length;
        buf[o]     = (byte)(fileSize & 0xFF);
        buf[o + 1] = (byte)((fileSize >>  8) & 0xFF);
        buf[o + 2] = (byte)((fileSize >> 16) & 0xFF);
        buf[o + 3] = (byte)((fileSize >> 24) & 0xFF);
        sha256.AsSpan(0, 32).CopyTo(buf.AsSpan(o + 4));
        o += 36; // 4 (fileSize) + 32 (sha256)
        buf[o] = (byte)tokenBytes.Length;
        tokenBytes.CopyTo(buf, o + 1);
        return buf;
    }

    public static (byte TransferId, string Filename, long FileSize, byte[] Sha256, string Token)?
        DeserializeFileOffer(byte[] data)
    {
        if (!IsFileOfferPacket(data)) return null;
        var nameLen = data[4];
        if (data.Length < 5 + nameLen + 4 + 32 + 1) return null;

        var filename = Encoding.UTF8.GetString(data, 5, nameLen);
        var o = 5 + nameLen;
        var fileSize = (long)(data[o] | (data[o + 1] << 8) | (data[o + 2] << 16) | (data[o + 3] << 24));
        var sha256   = data[(o + 4)..(o + 4 + 32)];
        o += 36;
        var tokenLen = data[o];
        if (data.Length < o + 1 + tokenLen) return null;
        var token = Encoding.ASCII.GetString(data, o + 1, tokenLen);
        return (data[3], filename, fileSize, sha256, token);
    }

    // -------------------------------------------------------------------------
    // FileAccept / FileReject
    //
    // Both share the same minimal format: [SB][marker][transfer_id:u8]
    // Total: 4 bytes each.
    // -------------------------------------------------------------------------

    private static bool IsControlPacket(byte[] data, byte marker) =>
        data.Length == 4 &&
        data[0] == SemaBuzzPacket.MagicByte1 &&
        data[1] == SemaBuzzPacket.MagicByte2 &&
        data[2] == marker;

    private static byte[] SerializeControl(byte marker, byte transferId) =>
        [SemaBuzzPacket.MagicByte1, SemaBuzzPacket.MagicByte2, marker, transferId];

    public static bool IsFileAcceptPacket(byte[] data) => IsControlPacket(data, FileAcceptByte);
    public static bool IsFileRejectPacket(byte[] data) => IsControlPacket(data, FileRejectByte);

    public static byte[] SerializeFileAccept(byte transferId) => SerializeControl(FileAcceptByte, transferId);
    public static byte[] SerializeFileReject(byte transferId) => SerializeControl(FileRejectByte, transferId);

    /// <summary>Extract the transfer_id byte from any 4-byte file-control packet.</summary>
    public static byte? DeserializeTransferId(byte[] data) =>
        data.Length >= 4 ? data[3] : null;
}
