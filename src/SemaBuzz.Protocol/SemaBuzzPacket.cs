using System.Text;

namespace SemaBuzz.Protocol;

/// <summary>
/// The fundamental unit of the SemaBuzz wire â€” a single character transmission
/// with a header and intensity marker. Total overhead is minimal by design.
///
/// Wire format (binary):
///   [0x53][0x42]  â€” 2-byte magic header ("SB")
///   [char_hi][char_lo] â€” UTF-16 character (2 bytes)
///   [intensity]   â€” 1-byte signal intensity (0â€“255)
///   [type]        â€” 1-byte packet type
/// Total: 6 bytes per packet.
/// </summary>
public readonly struct SemaBuzzPacket
{
    public const byte MagicByte1 = 0x53; // 'S'
    public const byte MagicByte2 = 0x42; // 'B'
    /// <summary>
    /// Total wire bytes per packet.
    /// Layout: [SB][ch_hi][ch_lo][intensity][type][seq_hi][seq_lo][reserved]
    /// </summary>
    public const int WireSize = 8;

    public char   Character { get; init; }
    public byte   Intensity { get; init; }
    public SemaBuzzPacketType Type { get; init; }
    /// <summary>Per-sender sequence counter for out-of-order detection.</summary>
    public ushort SeqNum    { get; init; }

    public SemaBuzzPacket(char character, byte intensity,
        SemaBuzzPacketType type = SemaBuzzPacketType.Char, ushort seqNum = 0)
    {
        Character = character;
        Intensity = intensity;
        Type      = type;
        SeqNum    = seqNum;
    }

    /// <summary>Serialize to the 8-byte wire format.</summary>
    public byte[] ToWireBytes()
    {
        var bytes = new byte[WireSize];
        bytes[0] = MagicByte1;
        bytes[1] = MagicByte2;
        bytes[2] = (byte)(Character >> 8);
        bytes[3] = (byte)(Character & 0xFF);
        bytes[4] = Intensity;
        bytes[5] = (byte)Type;
        bytes[6] = (byte)(SeqNum >> 8);
        bytes[7] = (byte)(SeqNum  & 0xFF);
        return bytes;
    }

    /// <summary>Deserialize from an 8-byte buffer. Returns null if the header, size, or type is invalid.</summary>
    public static SemaBuzzPacket? FromWireBytes(byte[] buffer)
    {
        if (buffer.Length != WireSize) return null;                           // must be exactly 8 bytes
        if (buffer[0] != MagicByte1 || buffer[1] != MagicByte2) return null; // magic check

        var ch        = (char)((buffer[2] << 8) | buffer[3]);
        var intensity = buffer[4];
        var type      = (SemaBuzzPacketType)buffer[5];
        var seqNum    = (ushort)((buffer[6] << 8) | buffer[7]);

        if (!IsKnownType(type)) return null;                                  // reject unknown type bytes

        return new SemaBuzzPacket(ch, intensity, type, seqNum);
    }

    /// <summary>Returns true only for type bytes that are defined in the protocol.</summary>
    public static bool IsKnownType(SemaBuzzPacketType type) => type is
        SemaBuzzPacketType.Char                or
        SemaBuzzPacketType.Handshake           or
        SemaBuzzPacketType.HandshakeAck        or
        SemaBuzzPacketType.Disconnect          or
        SemaBuzzPacketType.Ping                or
        SemaBuzzPacketType.HandshakeEncRequired or
        SemaBuzzPacketType.HandshakeHold       or
        SemaBuzzPacketType.ConnectRejected     or
        SemaBuzzPacketType.Buzz;

    /// <summary>Create a control packet (no character content).</summary>
    public static SemaBuzzPacket Control(SemaBuzzPacketType type) =>
        new('\0', 0xFF, type);
}

public enum SemaBuzzPacketType : byte
{
    /// <summary>A live character transmission.</summary>
    Char = 0x01,
    /// <summary>Handshake initiation â€” "I am here."</summary>
    Handshake = 0x02,
    /// <summary>Handshake acknowledgement â€” "I hear you."</summary>
    HandshakeAck = 0x03,
    /// <summary>Peer is disconnecting cleanly.</summary>
    Disconnect = 0x04,
    /// <summary>Keepalive ping to sustain the wire.</summary>
    Ping = 0x05,
    /// <summary>Host requires encryption â€” dialer must provide a passphrase.</summary>
    HandshakeEncRequired = 0x06,
    /// <summary>Buzz the remote peer â€” spikes their filament and shakes their window.</summary>
    Buzz = 0x07,
    /// <summary>Host rejected the incoming connection request.</summary>
    ConnectRejected = 0x08,
    /// <summary>Host is reviewing the connection request â€” dialer should hold.</summary>
    HandshakeHold = 0x09,
}
