namespace SemaBuzz.Protocol;

/// <summary>
/// Variable-length wire packet carrying an ECDH public key for in-handshake
/// key exchange. All SemaBuzz connections use ephemeral ECDH P-256 to establish
/// a per-session AES-256-GCM shield  no passphrase required.
///
/// Wire format:
///   [0x4B][0x45]      2-byte magic ("KE")
///   [0x01]            version byte
///   [len_hi][len_lo]  public key length (big-endian ushort)
///   [key_bytes...]    SubjectPublicKeyInfo-encoded ECDH P-256 public key (~91 bytes)
/// </summary>
public static class SemaBuzzKeyExchange
{
    private const byte MagicByte1 = 0x4B; // 'K'
    private const byte MagicByte2 = 0x45; // 'E'
    private const byte Version = 0x01;

    private const int HeaderSize = 5;   // 2 magic + 1 version + 2 length
    private const int MaxKeyBytes = 512; // plenty for any SubjectPublicKeyInfo blob

    public static bool IsKeyExchangePacket(byte[] data) =>
        data.Length >= HeaderSize &&
        data[0] == MagicByte1 &&
        data[1] == MagicByte2 &&
        data[2] == Version;

    /// <summary>Serialize a public key blob into a KE wire packet.</summary>
    public static byte[] Serialize(byte[] publicKeyBytes)
    {
        var len = publicKeyBytes.Length;
        var buf = new byte[HeaderSize + len];
        buf[0] = MagicByte1;
        buf[1] = MagicByte2;
        buf[2] = Version;
        buf[3] = (byte)((len >> 8) & 0xFF);
        buf[4] = (byte)(len & 0xFF);
        publicKeyBytes.CopyTo(buf, HeaderSize);
        return buf;
    }

    /// <summary>
    /// Deserialize a KE wire packet back to the raw public key bytes.
    /// Returns null if the packet is malformed or the key length is suspicious.
    /// </summary>
    public static byte[]? Deserialize(byte[] data)
    {
        if (!IsKeyExchangePacket(data)) return null;

        var len = (data[3] << 8) | data[4];
        if (len <= 0 || len > MaxKeyBytes) return null;
        if (data.Length < HeaderSize + len) return null;

        return data[HeaderSize..(HeaderSize + len)];
    }
}
