namespace SemaBuzz.Protocol;

/// <summary>
/// Converts a raw string into a stream of SemaBuzzPackets, one character at a time.
/// The intensity is computed from typing rhythm â€” faster typing = higher intensity.
/// This is the "Live-Wire" engine.
/// </summary>
public sealed class SemaBuzzStreamer
{
    private DateTime _lastKeyTime = DateTime.UtcNow;
    private const double MaxIntervalMs = 500.0; // Slower than this = minimum intensity

    /// <summary>Per-session sequence counter â€” wraps at 65 535.</summary>
    private ushort _seqNum;

    public event EventHandler<SemaBuzzPacketEventArgs>? PacketReady;

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
        var seq    = _seqNum++;   // post-increment; wraps naturally as ushort
        var packet = new SemaBuzzPacket(character, intensity, SemaBuzzPacketType.Char, seq);
        PacketReady?.Invoke(this, new SemaBuzzPacketEventArgs(packet));
    }

    /// <summary>
    /// Map a keystroke interval to a 0â€“255 intensity byte.
    /// Short interval (fast typing) â†’ high intensity.
    /// Long interval (slow typing)  â†’ low intensity.
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
