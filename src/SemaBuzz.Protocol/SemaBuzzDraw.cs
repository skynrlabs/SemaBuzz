namespace SemaBuzz.Protocol;

/// <summary>Identifies a whiteboard drawing action.</summary>
public enum SemaBuzzDrawAction : byte
{
    Down = 0,  // Pen down — start a new stroke
    Move = 1,  // Pen move — extend the current stroke
    Up = 2,  // Pen up — end the current stroke
    Clear = 3,  // Clear the entire board
}

/// <summary>A single whiteboard draw event transmitted over the wire.</summary>
public readonly struct SemaBuzzDrawEvent
{
    public SemaBuzzDrawAction Action { get; init; }
    /// <summary>X position normalized to 0–65535 relative to canvas width.</summary>
    public ushort X { get; init; }
    /// <summary>Y position normalized to 0–65535 relative to canvas height.</summary>
    public ushort Y { get; init; }
    /// <summary>Index into the whiteboard color palette (0-based, 0–5).</summary>
    public byte ColorIndex { get; init; }
    /// <summary>Stroke thickness: 0 = thin (2 px), 1 = medium (4 px), 2 = thick (8 px).</summary>
    public byte SizeIndex { get; init; }
}

/// <summary>
/// Variable-length wire format for a whiteboard draw event.
/// Format:
///   [0x53][0x42]   2-byte magic ("SB")
///   [0x0A]         draw-event type marker
///   [action:u8]    SemaBuzzDrawAction
///   [x_hi][x_lo]   X normalized 0–65535 (big-endian)
///   [y_hi][y_lo]   Y normalized 0–65535 (big-endian)
///   [color:u8]     palette index
///   [size:u8]      size index (0/1/2)
/// Total: 10 bytes.
/// </summary>
public static class SemaBuzzDraw
{
    internal const byte DrawByte = 0x0A;
    public const int WireSize = 10;
    public const int PaletteCount = 6;

    public static bool IsDrawPacket(byte[] data) =>
        data.Length == WireSize &&
        data[0] == SemaBuzzPacket.MagicByte1 &&
        data[1] == SemaBuzzPacket.MagicByte2 &&
        data[2] == DrawByte;

    public static byte[] Serialize(SemaBuzzDrawEvent ev)
    {
        var buf = new byte[WireSize];
        buf[0] = SemaBuzzPacket.MagicByte1;
        buf[1] = SemaBuzzPacket.MagicByte2;
        buf[2] = DrawByte;
        buf[3] = (byte)ev.Action;
        buf[4] = (byte)(ev.X >> 8);
        buf[5] = (byte)(ev.X & 0xFF);
        buf[6] = (byte)(ev.Y >> 8);
        buf[7] = (byte)(ev.Y & 0xFF);
        buf[8] = ev.ColorIndex;
        buf[9] = ev.SizeIndex;
        return buf;
    }

    public static SemaBuzzDrawEvent? Deserialize(byte[] data)
    {
        if (!IsDrawPacket(data)) return null;
        var action = (SemaBuzzDrawAction)data[3];
        if (action > SemaBuzzDrawAction.Clear) return null;
        var x = (ushort)((data[4] << 8) | data[5]);
        var y = (ushort)((data[6] << 8) | data[7]);
        return new SemaBuzzDrawEvent
        {
            Action = action,
            X = x,
            Y = y,
            ColorIndex = data[8],
            SizeIndex = data[9],
        };
    }
}
