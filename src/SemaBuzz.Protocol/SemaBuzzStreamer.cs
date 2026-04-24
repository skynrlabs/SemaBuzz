namespace SemaBuzz.Protocol;

/// <summary>
/// Converts a raw string into a stream of SemaBuzzPackets, one character at a time.
/// The intensity is computed from typing rhythm  faster typing = higher intensity.
/// This is the "Live-Wire" engine.
/// </summary>
public sealed class SemaBuzzStreamer
{
    private DateTime _lastKeyTime = DateTime.UtcNow;
    private const double MaxIntervalMs = 500.0; // Slower than this = minimum intensity

    /// <summary>Per-session sequence counter  wraps at 65 535.</summary>
    private ushort _seqNum;

    public event EventHandler<SemaBuzzPacketEventArgs>? PacketReady;

    /// <summary>
    /// Reserve the next per-session sequence number for manually constructed
    /// packets that still participate in Char packet ordering.
    /// </summary>
    public ushort NextSequence() => _seqNum++;

    /// <summary>
    /// Feed a single character into the streamer. Computes intensity from typing
    /// velocity and fires PacketReady with the resulting SemaBuzzPacket.
    /// </summary>
    public void Feed(char character)
    {
        var now = DateTime.UtcNow;
        var intervalMs = (now - _lastKeyTime).TotalMilliseconds;
        _lastKeyTime = now;

        var intensity = ComputeIntensity(intervalMs);
        var seq    = NextSequence();
        var packet = new SemaBuzzPacket(character, intensity, SemaBuzzPacketType.Char, seq);
        var packetHandler = PacketReady;
        if (packetHandler != null)
            packetHandler(this, new SemaBuzzPacketEventArgs(packet));
    }

    /// <summary>
    /// Map a keystroke interval to a 0–255 intensity byte.
    /// Short interval (fast typing)  high intensity.
    /// Long interval (slow typing)   low intensity.
    /// </summary>
    private static byte ComputeIntensity(double intervalMs)
    {
        if (intervalMs <= 0) return 255;
        var clamped = Math.Min(intervalMs, MaxIntervalMs);
        // Invert: fast = high
        var ratio = 1.0 - (clamped / MaxIntervalMs);
        return (byte)(ratio * 255);
    }
}
