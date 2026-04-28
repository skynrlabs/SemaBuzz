using System.Text;

namespace SemaBuzz.Protocol;

/// <summary>
/// Variable-length wire format for exchanging peer metadata (handle + avatar image).
/// Format:
///   [0x53][0x42]    2-byte magic
///   [0x07]          metadata type marker
///   [handle_len:u8] handle length in bytes (max 32)
///   [handle:utf8]   handle bytes
///   [img_len:u32le] avatar PNG length (0 = no image)
///   [img_bytes]     PNG bytes
/// </summary>
public static class SemaBuzzMetadata
{
    internal const byte MetaByte = 0x07;

    public static bool IsMetadataPacket(byte[] data) =>
        data.Length >= 8 &&
        data[0] == SemaBuzzPacket.MagicByte1 &&
        data[1] == SemaBuzzPacket.MagicByte2 &&
        data[2] == MetaByte;

    public static byte[] Serialize(string handle, byte[]? avatarPng,
        SemaBuzzStatus status = SemaBuzzStatus.Available, string statusMessage = "")
    {
        string trimmedHandle;
        if (handle.Length > 32)
            trimmedHandle = handle[..32];
        else
            trimmedHandle = handle;
        var handleBytes = Encoding.UTF8.GetBytes(trimmedHandle);
        byte[] imgBytes;
        if (avatarPng != null)
            imgBytes = avatarPng;
        else
            imgBytes = Array.Empty<byte>();

        // Clamp status message to 127 UTF-8 bytes
        var msgRaw  = statusMessage ?? string.Empty;
        var msgBytes = Encoding.UTF8.GetBytes(msgRaw.Length > 60 ? msgRaw[..60] : msgRaw);
        if (msgBytes.Length > 127) msgBytes = msgBytes[..127];

        var buf = new byte[3 + 1 + handleBytes.Length + 4 + imgBytes.Length + 1 + 1 + msgBytes.Length];
        buf[0] = SemaBuzzPacket.MagicByte1;
        buf[1] = SemaBuzzPacket.MagicByte2;
        buf[2] = MetaByte;
        buf[3] = (byte)handleBytes.Length;
        handleBytes.CopyTo(buf, 4);

        var lo = 4 + handleBytes.Length;
        buf[lo] = (byte)(imgBytes.Length & 0xFF);
        buf[lo + 1] = (byte)((imgBytes.Length >> 8) & 0xFF);
        buf[lo + 2] = (byte)((imgBytes.Length >> 16) & 0xFF);
        buf[lo + 3] = (byte)((imgBytes.Length >> 24) & 0xFF);
        imgBytes.CopyTo(buf, lo + 4);

        // Append status byte + message (backward-compatible extension)
        var so = lo + 4 + imgBytes.Length;
        buf[so]     = (byte)status;
        buf[so + 1] = (byte)msgBytes.Length;
        msgBytes.CopyTo(buf, so + 2);
        return buf;
    }

    public static (string Handle, byte[]? AvatarPng, SemaBuzzStatus Status, string StatusMessage)? Deserialize(byte[] data)
    {
        if (!IsMetadataPacket(data)) return null;

        var handleLen = data[3];
        // M-3: reject handles that exceed the sender's own cap (32 bytes).
        // A crafted peer sending up to 64 bytes was previously accepted, creating
        // an inconsistency that could surprise future validation code.
        if (handleLen > 32) return null;
        if (data.Length < 4 + handleLen + 4) return null;

        var handle = Encoding.UTF8.GetString(data, 4, handleLen);
        var lo = 4 + handleLen;

        // Read imgLen as uint to avoid signed-integer overflow in the bounds check.
        // The MaxPayload cap in the listener guarantees data.Length <= 16 384, so
        // any imgLen that would actually exceed the buffer is caught cleanly here.
        var imgLen = (uint)(data[lo] | (data[lo + 1] << 8) | (data[lo + 2] << 16) | (data[lo + 3] << 24));
        if (imgLen > (uint)(data.Length - lo - 4)) return null;

        byte[]? img;
        if (imgLen > 0)
            img = data[(lo + 4)..(lo + 4 + (int)imgLen)];
        else
            img = null;

        // Read optional status extension (backward compatible)
        SemaBuzzStatus status = SemaBuzzStatus.Available;
        string statusMessage  = string.Empty;
        var so = lo + 4 + (int)imgLen;
        if (so + 2 <= data.Length)
        {
            status = (SemaBuzzStatus)data[so];
            if (!Enum.IsDefined(status)) status = SemaBuzzStatus.Available;
            var msgLen2 = data[so + 1];
            if (so + 2 + msgLen2 <= data.Length)
                statusMessage = Encoding.UTF8.GetString(data, so + 2, msgLen2);
        }

        return (handle, img, status, statusMessage);
    }
}
