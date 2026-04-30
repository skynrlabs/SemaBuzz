using System.Text;

namespace SemaBuzz.Protocol;

/// <summary>
/// Variable-length wire format for peer-to-peer file transfers.
///
/// All file-transfer packets use the same [SB][marker][payload] framing as other
/// variable-length packet types (metadata, draw, url-push).
///
/// Markers (data[2]):
///   0x0B  FileOffer    — sender proposes a file transfer
///   0x0C  FileChunk    — one 8 KB chunk of file data
///   0x0D  FileAccept   — receiver accepts the offer
///   0x0E  FileReject   — receiver declines the offer
///   0x0F  FileComplete — sender signals all chunks transmitted
///   0x10  FileCancel   — either side aborts an in-progress transfer
/// </summary>
public static class SemaBuzzFileTransfer
{
    public const byte FileOfferByte = 0x0B;
    public const byte FileChunkByte = 0x0C;
    public const byte FileAcceptByte = 0x0D;
    public const byte FileRejectByte = 0x0E;
    public const byte FileCompleteByte = 0x0F;
    public const byte FileCancelByte = 0x10;

    /// <summary>Maximum size of a single chunk's data payload.</summary>
    public const int ChunkSize = 8192;

    /// <summary>Maximum file size accepted by the protocol (10 MB).</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024;

    // -------------------------------------------------------------------------
    // FileOffer
    //
    // Format:
    //   [SB][0x0B][transfer_id:u8][filename_len:u8][filename:utf8]
    //   [file_size:u32le][total_chunks:u16le][sha256:32 bytes]
    //
    // Minimum serialized size: 3 + 1 + 1 + 0 + 4 + 2 + 32 = 43 bytes
    // -------------------------------------------------------------------------

    public static bool IsFileOfferPacket(byte[] data) =>
        data.Length >= 43 &&
        data[0] == SemaBuzzPacket.MagicByte1 &&
        data[1] == SemaBuzzPacket.MagicByte2 &&
        data[2] == FileOfferByte;

    public static byte[] SerializeFileOffer(
        byte transferId, string filename, long fileSize, ushort totalChunks, byte[] sha256)
    {
        var nameBytes = Encoding.UTF8.GetBytes(filename);
        if (nameBytes.Length > 255) nameBytes = nameBytes[..255];

        var buf = new byte[3 + 1 + 1 + nameBytes.Length + 4 + 2 + 32];
        buf[0] = SemaBuzzPacket.MagicByte1;
        buf[1] = SemaBuzzPacket.MagicByte2;
        buf[2] = FileOfferByte;
        buf[3] = transferId;
        buf[4] = (byte)nameBytes.Length;
        nameBytes.CopyTo(buf, 5);

        var o = 5 + nameBytes.Length;
        buf[o] = (byte)(fileSize & 0xFF);
        buf[o + 1] = (byte)((fileSize >> 8) & 0xFF);
        buf[o + 2] = (byte)((fileSize >> 16) & 0xFF);
        buf[o + 3] = (byte)((fileSize >> 24) & 0xFF);
        buf[o + 4] = (byte)(totalChunks & 0xFF);
        buf[o + 5] = (byte)((totalChunks >> 8) & 0xFF);
        sha256.AsSpan(0, 32).CopyTo(buf.AsSpan(o + 6));
        return buf;
    }

    public static (byte TransferId, string Filename, long FileSize, ushort TotalChunks, byte[] Sha256)?
        DeserializeFileOffer(byte[] data)
    {
        if (!IsFileOfferPacket(data)) return null;
        var nameLen = data[4];
        if (data.Length < 5 + nameLen + 4 + 2 + 32) return null;

        var filename = Encoding.UTF8.GetString(data, 5, nameLen);
        var o = 5 + nameLen;
        var fileSize = (long)(data[o] | (data[o + 1] << 8) | (data[o + 2] << 16) | (data[o + 3] << 24));
        var totalChunks = (ushort)(data[o + 4] | (data[o + 5] << 8));
        var sha256 = data[(o + 6)..(o + 6 + 32)];
        return (data[3], filename, fileSize, totalChunks, sha256);
    }

    // -------------------------------------------------------------------------
    // FileChunk
    //
    // Format:
    //   [SB][0x0C][transfer_id:u8][chunk_idx_hi][chunk_idx_lo]
    //   [data_len_hi][data_len_lo][data: 1..8192 bytes]
    //
    // Minimum size: 8 bytes (header only, no data would be rejected)
    // Maximum size: 8 + 8192 = 8200 bytes
    // -------------------------------------------------------------------------

    public static bool IsFileChunkPacket(byte[] data) =>
        data.Length >= 9 &&                             // at least 1 byte of payload
        data[0] == SemaBuzzPacket.MagicByte1 &&
        data[1] == SemaBuzzPacket.MagicByte2 &&
        data[2] == FileChunkByte;

    public static byte[] SerializeFileChunk(byte transferId, ushort chunkIdx, byte[] chunkData)
    {
        var buf = new byte[3 + 1 + 2 + 2 + chunkData.Length];
        buf[0] = SemaBuzzPacket.MagicByte1;
        buf[1] = SemaBuzzPacket.MagicByte2;
        buf[2] = FileChunkByte;
        buf[3] = transferId;
        buf[4] = (byte)((chunkIdx >> 8) & 0xFF);
        buf[5] = (byte)(chunkIdx & 0xFF);
        buf[6] = (byte)((chunkData.Length >> 8) & 0xFF);
        buf[7] = (byte)(chunkData.Length & 0xFF);
        chunkData.CopyTo(buf, 8);
        return buf;
    }

    public static (byte TransferId, ushort ChunkIdx, byte[] Data)? DeserializeFileChunk(byte[] data)
    {
        if (!IsFileChunkPacket(data)) return null;
        var chunkIdx = (ushort)((data[4] << 8) | data[5]);
        var dataLen = (data[6] << 8) | data[7];
        if (dataLen <= 0 || dataLen > ChunkSize) return null;
        if (data.Length < 8 + dataLen) return null;
        return (data[3], chunkIdx, data[8..(8 + dataLen)]);
    }

    // -------------------------------------------------------------------------
    // FileAccept / FileReject / FileComplete / FileCancel
    //
    // All share the same minimal format: [SB][marker][transfer_id:u8]
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
    public static bool IsFileCompletePacket(byte[] data) => IsControlPacket(data, FileCompleteByte);
    public static bool IsFileCancelPacket(byte[] data) => IsControlPacket(data, FileCancelByte);

    public static byte[] SerializeFileAccept(byte transferId) => SerializeControl(FileAcceptByte, transferId);
    public static byte[] SerializeFileReject(byte transferId) => SerializeControl(FileRejectByte, transferId);
    public static byte[] SerializeFileComplete(byte transferId) => SerializeControl(FileCompleteByte, transferId);
    public static byte[] SerializeFileCancel(byte transferId) => SerializeControl(FileCancelByte, transferId);

    /// <summary>Extract the transfer_id byte from any 4-byte file-control packet.</summary>
    public static byte? DeserializeTransferId(byte[] data) =>
        data.Length >= 4 ? data[3] : null;
}
