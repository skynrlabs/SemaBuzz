using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SemaBuzz.App;

/// <summary>Saved identity: handle name + optional avatar PNG stored as Base64.</summary>
public sealed class SemaBuzzProfile
{
    /// <summary>Unique stable identifier for this profile (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Display name shown in the chat pane header and sent to the peer on connect.</summary>
    public string Handle { get; set; } = "anonymous";
    /// <summary>Avatar image serialised as a Base64-encoded PNG string, or null if none.</summary>
    public string? AvatarBase64 { get; set; }

    /// <summary>Decoded avatar PNG bytes, or null if no avatar is set.</summary>
    [JsonIgnore]
    public byte[]? AvatarPng
    {
        get
        {
            if (AvatarBase64 is null)
                return null;
            return Convert.FromBase64String(AvatarBase64);
        }
    }
}

/// <summary>Loads and saves profiles to %APPDATA%\SemaBuzz\profiles.json.</summary>
public static class SemaBuzzProfileStore
{
    private static readonly string _dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SemaBuzz");
    private static readonly string _path = Path.Combine(_dir, "profiles.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    /// <summary>Loads all saved profiles from disk, returning an empty list if the file is missing or corrupt.</summary>
    public static List<SemaBuzzProfile> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var loaded = JsonSerializer.Deserialize<List<SemaBuzzProfile>>(
                File.ReadAllText(_path), _opts);
            if (loaded != null)
                return loaded;
            return [];
        }
        catch { return []; }
    }

    /// <summary>Serialises and writes all profiles to disk. Failures are silently ignored.</summary>
    public static void Save(IEnumerable<SemaBuzzProfile> profiles)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(profiles, _opts));
        }
        catch { /* best-effort  a disk error never breaks the session */ }
    }
}
