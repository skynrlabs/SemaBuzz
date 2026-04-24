using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SemaBuzz.App;

/// <summary>Saved identity: handle name + optional avatar PNG stored as Base64.</summary>
public sealed class SemaBuzzProfile
{
    public string  Id           { get; set; } = Guid.NewGuid().ToString();
    public string  Handle       { get; set; } = "anonymous";
    public string? AvatarBase64 { get; set; }

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
    private static readonly string _dir  =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SemaBuzz");
    private static readonly string _path = Path.Combine(_dir, "profiles.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

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
