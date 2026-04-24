using System.IO;
using System.Text.Json;

namespace SemaBuzz.App;

public enum SemaBuzzThemeId
{
    Obsidian  = 0,
    Neon      = 1,
    Matrix    = 2,
    BloodMoon = 3,
    Arctic    = 4,
    Sepia     = 5,
    Midnight  = 6,
    Sunset    = 7,
    Rose      = 8,
    Violet    = 9,
    Emerald   = 10,
    Steel     = 11,
    Powwow    = 12,
}

public enum IndicatorStyleId
{
    Flicker = 0, // Default (free)  chaotic multi-harmonic
    Pulse   = 1, // PRO  clean single-harmonic heartbeat
    Wave    = 2, // PRO  slow rolling sine
}

public sealed class SemaBuzzSettings
{
    public SemaBuzzThemeId    Theme             { get; set; } = SemaBuzzThemeId.Obsidian;
    /// <summary>Default port pre-filled in the connect dialog's listen port field. Null means use the built-in default (7070).</summary>
    public int?               DefaultListenPort { get; set; } = null;

    /// <summary>Multiplier (0.5–2.0) applied to packet intensity before driving the filament.</summary>
    public double             IndicatorSensitivity { get; set; } = 1.0;

    /// <summary>Filament animation style.</summary>
    public IndicatorStyleId   IndicatorStyle       { get; set; } = IndicatorStyleId.Flicker;

    /// <summary>Font size used for chat message text (11–20).</summary>
    public double             ChatFontSize         { get; set; } = 13.0;

    /// <summary>When true, keystrokes are streamed live to the peer as the user types.</summary>
    public bool               LivePreview          { get; set; } = true;

    /// <summary>When true, minimizing the main window hides it to the system tray.</summary>
    public bool               MinimizeToTray       { get; set; } = false;

    /// <summary>When true, buzz actions play the configured local sound.</summary>
    public bool               BuzzSoundEnabled     { get; set; } = true;

    /// <summary>Playback volume for the local buzz sound (0.0-1.0).</summary>
    public double             BuzzSoundVolume      { get; set; } = 0.75;

    /// <summary>
    /// WebSocket relay endpoint. Defaults to the hosted SemaBuzz relay.
    /// Users can override this to point at a self-hosted relay.
    /// </summary>
    public string             RelayUri             { get; set; } = SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;

    //  Persistence

    internal static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SemaBuzz");

    private static readonly string SettingsFile =
        Path.Combine(DataDir, "settings.json");

    public static SemaBuzzSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var deserialized = JsonSerializer.Deserialize<SemaBuzzSettings>(json);
                if (deserialized != null)
                    return deserialized;
            }
        }
        catch { /* return defaults on any error */ }

        return new SemaBuzzSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }
}
