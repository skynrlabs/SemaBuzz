using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SemaBuzz.App;

/// <summary>
/// Persists chat messages to an encrypted binary log file using Windows DPAPI
/// (machine + current-user bound; no passphrase required).
///
/// File format: a sequence of [4-byte uint LE: cipherLen][cipherLen bytes: DPAPI blob]
/// entries, where each DPAPI blob decrypts to a UTF-8 JSON ChatLogEntry.
/// </summary>
internal static class SemaBuzzChatLog
{
    private static readonly string LogFile =
        Path.Combine(SemaBuzzSettings.DataDir, "chatlog.bin");

    //  Write

    /// <param name="direction">"out" for sent, "in" for received.</param>
    public static void Append(string direction, string handle, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            Directory.CreateDirectory(SemaBuzzSettings.DataDir);

            var entry = new { t = DateTime.UtcNow.ToString("o"), d = direction, h = handle, m = message };
            var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry));
            var cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);

            using var fs = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.Read);
            fs.Write(BitConverter.GetBytes((uint)cipher.Length), 0, 4);
            fs.Write(cipher, 0, cipher.Length);
        }
        catch { }
    }

    //  Read

    public static IReadOnlyList<ChatLogEntry> LoadAll()
    {
        var results = new List<ChatLogEntry>();
        if (!File.Exists(LogFile)) return results;

        try
        {
            using var fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var lenBuf = new byte[4];

            while (fs.Read(lenBuf, 0, 4) == 4)
            {
                var cipherLen = BitConverter.ToUInt32(lenBuf, 0);
                if (cipherLen > 1_000_000) break; // sanity cap  corrupt entry

                var cipher = new byte[cipherLen];
                if (fs.Read(cipher, 0, (int)cipherLen) != (int)cipherLen) break;

                try
                {
                    var plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                    using var doc = JsonDocument.Parse(plain);
                    var root = doc.RootElement;

                    results.Add(new ChatLogEntry(
                        Time:      root.GetProperty("t").GetString() ?? string.Empty,
                        Direction: root.GetProperty("d").GetString() ?? "in",
                        Handle:    root.GetProperty("h").GetString() ?? string.Empty,
                        Message:   root.GetProperty("m").GetString() ?? string.Empty));
                }
                catch { /* skip corrupted or undecryptable entries */ }
            }
        }
        catch { }

        return results;
    }

    //  Housekeeping

    public static void Clear()
    {
        try { if (File.Exists(LogFile)) File.Delete(LogFile); }
        catch { }
    }
}

internal sealed record ChatLogEntry(string Time, string Direction, string Handle, string Message);
