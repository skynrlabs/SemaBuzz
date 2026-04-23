using Microsoft.Win32;

namespace SemaBuzz.App;

/// <summary>
/// Handles the custom <c>buzz://</c> URI scheme.
///
/// URI formats:
///   buzz://host:port                dial host:port directly
///   buzz://handle@host:port         dial with a display hint (handle ignored locally)
///   buzz://TOKEN                    dial via relay using a 6-char room token
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

    //  Parse

    /// <summary>
    /// Parsed representation of a <c>buzz://</c> URI.
    /// When <see cref="RelayToken"/> is set, <see cref="Host"/> and <see cref="Port"/> are ignored.
    /// <see cref="RelayUri"/> is non-null when the URI embeds a custom relay via <c>?r=</c>.
    /// </summary>
    internal sealed record BuzzUri(string Host, int Port, string? Handle, string? RelayToken = null, string? RelayUri = null);

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

        // Detect relay token: buzz://X7K2QP  host looks like a token (≤8 chars, no dots, no colons)
        if (uri.Port < 0 && !host.Contains('.') && host.Length is >= 4 and <= 8)
        {
            // Extract optional embedded relay: buzz://TOKEN?r=ws%3A%2F%2Fhost%3Aport
            string? embeddedRelay = null;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var r = query["r"];
            if (!string.IsNullOrWhiteSpace(r) && (r.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
                                                || r.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)))
                embeddedRelay = r;
            return new BuzzUri(string.Empty, 0, handle, host.ToUpperInvariant(), embeddedRelay);
        }

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
    /// When <paramref name="relayUri"/> is non-null and differs from the default relay,
    /// it is embedded as a <c>?r=</c> query parameter so the recipient's app can use it automatically.
    /// </summary>
    public static string BuildRelay(string token, string? relayUri = null)
    {
        var t = token.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(relayUri)
            && !string.Equals(relayUri, SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri, StringComparison.OrdinalIgnoreCase))
            return $"buzz://{t}?r={Uri.EscapeDataString(relayUri)}";
        return $"buzz://{t}";
    }

    //  Registry

    /// <summary>
    /// Registers the <c>buzz://</c> URI scheme in HKCU so Windows opens
    /// SemaBuzz when a user clicks a buzz:// link in a browser or document.
    ///
    /// Writes to HKCU (no elevation required).  Safe to call on every launch
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

            // Check if already registered with the same exe  avoid unnecessary writes
            using var cmdKey = key.OpenSubKey(@"shell\open\command");
            if (cmdKey?.GetValue(null) is string existing && existing == command) return;

            key.SetValue(null,                       $"URL:{Scheme} Protocol");
            key.SetValue("URL Protocol",             string.Empty);

            using var iconKey    = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue(null, $"\"{exePath}\",0");

            using var shellKey   = key.CreateSubKey(@"shell\open\command");
            shellKey.SetValue(null, command);
        }
        catch { /* registry unavailable  silently skip */ }
    }
}
