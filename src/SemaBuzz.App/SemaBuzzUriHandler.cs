using Microsoft.Win32;

namespace SemaBuzz.App;

/// <summary>
/// Handles the custom <c>buzz://</c> URI scheme.
///
/// URI formats:
///   buzz://host:port               â€” dial host:port directly
///   buzz://handle@host:port        â€” dial with a display hint (handle ignored locally)
///   buzz://TOKEN                   â€” dial via relay using a 6-char room token
///
/// Examples:
///   buzz://192.168.1.42:7070
///   buzz://alice@192.168.1.42:7070
///   buzz://X7K2QP                  â† relay token
/// </summary>
internal static class SemaBuzzUriHandler
{
    private const string Scheme      = "buzz";
    private const string ProgId      = "SemaBuzz.BuzzUri";
    private const int    DefaultPort = 7070;

    // â”€â”€â”€ Parse â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Parsed representation of a <c>buzz://</c> URI.
    /// When <see cref="RelayToken"/> is set, <see cref="Host"/> and <see cref="Port"/> are ignored.
    /// </summary>
    internal sealed record BuzzUri(string Host, int Port, string? Handle, string? RelayToken = null);

    /// <summary>
    /// Tries to parse a <c>buzz://</c> URI string.
    /// Returns null if the string is not a valid buzz URI.
    /// </summary>
    public static BuzzUri? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        var host   = uri.Host;
        var port   = uri.Port > 0 ? uri.Port : DefaultPort;
        var handle = string.IsNullOrEmpty(uri.UserInfo) ? null : uri.UserInfo;

        if (string.IsNullOrWhiteSpace(host)) return null;

        // Detect relay token: buzz://X7K2QP â€” host looks like a token (â‰¤8 chars, no dots, no colons)
        if (uri.Port < 0 && !host.Contains('.') && host.Length is >= 4 and <= 8)
            return new BuzzUri(string.Empty, 0, handle, host.ToUpperInvariant());

        return new BuzzUri(host, port, handle);
    }

    /// <summary>
    /// Builds a direct <c>buzz://</c> URI string from host, port, and optional handle.
    /// </summary>
    public static string Build(string host, int port, string? handle = null)
        => string.IsNullOrWhiteSpace(handle)
            ? $"buzz://{host}:{port}"
            : $"buzz://{handle}@{host}:{port}";

    /// <summary>
    /// Builds a relay <c>buzz://</c> URI string from a room token.
    /// </summary>
    public static string BuildRelay(string token) => $"buzz://{token.ToUpperInvariant()}";

    // â”€â”€â”€ Registry â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Registers the <c>buzz://</c> URI scheme in HKCU so Windows opens
    /// SemaBuzz when a user clicks a buzz:// link in a browser or document.
    ///
    /// Writes to HKCU (no elevation required).  Safe to call on every launch â€”
    /// skipped when the exe path has not changed since last registration.
    /// </summary>
    public static void EnsureRegistered()
    {
        try
        {
            var exePath  = Environment.ProcessPath ?? string.Empty;
            var command  = $"\"{exePath}\" \"%1\"";

            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{Scheme}", writable: true);

            // Check if already registered with the same exe â€” avoid unnecessary writes
            using var cmdKey = key.OpenSubKey(@"shell\open\command");
            if (cmdKey?.GetValue(null) is string existing && existing == command) return;

            key.SetValue(null,                       $"URL:{Scheme} Protocol");
            key.SetValue("URL Protocol",             string.Empty);

            using var iconKey    = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue(null, $"\"{exePath}\",0");

            using var shellKey   = key.CreateSubKey(@"shell\open\command");
            shellKey.SetValue(null, command);
        }
        catch { /* registry unavailable â€” silently skip */ }
    }
}
