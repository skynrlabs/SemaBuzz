using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace SemaBuzz.App;

/// <summary>
/// Manages the SemaBuzz Pro license via offline HMAC license key validation.
/// Keys are purchased at semabuzz.com/pro and delivered by Gumroad.
/// </summary>
internal static class SemaBuzzLicense
{
    // HMAC-SHA256 secret used to validate license keys offline.
    //
    // Key format (after normalisation / dash removal):
    //   SBPRO + <8 hex serial> + <8 hex checksum>  = 21 chars
    //
    //   serial   — unique 32-bit number issued per sale (0x00000001 … 0xFFFFFFFF)
    //   checksum — first 4 bytes of HMAC-SHA256(LicenseSecret, "sbpro-v1:" + serial-hex)
    //
    // To generate a key for serial 0x00000001:
    //   var msg  = "sbpro-v1:00000001";
    //   var mac  = HMACSHA256(LicenseSecret, UTF8(msg));
    //   var key  = "SBPRO-" + "00000001" + "-" + HEX(mac[0..4]);
    //   → e.g.  SBPRO-00000001-A3F2C81D
    //
    // Keep LicenseSecret secret — it never leaves this binary.
    private static readonly byte[] LicenseSecret =
    [
        0x4B, 0x7E, 0x2A, 0xD3, 0x91, 0x5F, 0xC8, 0x04,
        0xB6, 0x3C, 0xEA, 0x70, 0x18, 0xDF, 0x55, 0xA2,
        0x9D, 0x47, 0x6B, 0xFC, 0x23, 0x81, 0xC0, 0x5E,
        0xA4, 0x39, 0x7D, 0xBB, 0x62, 0x14, 0x98, 0xF7,
    ];
    private const string KeyPrefix   = "SBPRO";
    private const string HmacPrefix  = "sbpro-v1:";
    public  const string PurchaseUrl = "https://semabuzz.gumroad.com/l/dgeyxz";

    private static readonly string LicenseFilePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SemaBuzz", "license.key");

#if DEBUG
    /// <summary>True when SemaBuzz Pro features are unlocked. Always true in DEBUG builds.</summary>
    public static bool IsProUnlocked { get; private set; } = false;
#else
    /// <summary>True when the user has entered a valid SemaBuzz Pro license key.</summary>
    public static bool IsProUnlocked { get; private set; } = false;
#endif

    /// <summary>
    /// Reads the saved license key from disk and validates it.
    /// Call once at startup — fast, synchronous, no network.
    /// </summary>
    public static void Check()
    {
        if (!File.Exists(LicenseFilePath)) return;
        try
        {
            var key = File.ReadAllText(LicenseFilePath).Trim();
            if (ValidateKey(key))
                IsProUnlocked = true;
        }
        catch { /* corrupt/missing — stay locked */ }
    }

    /// <summary>
    /// Validates <paramref name="rawKey"/> offline and, if valid, saves it to disk
    /// and sets <see cref="IsProUnlocked"/>. Returns true on success.
    /// </summary>
    public static bool Activate(string rawKey)
    {
        if (!ValidateKey(rawKey)) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LicenseFilePath)!);
            File.WriteAllText(LicenseFilePath, NormalizeKey(rawKey));
        }
        catch { /* can't persist — still unlock for this session */ }
        IsProUnlocked = true;
        return true;
    }

    /// <summary>Shows the key activation dialog. Returns true when activated.</summary>
    public static async Task<bool> PurchaseAsync(Window? owner = null)
    {
        await Task.Yield();
        var dialog = new SemaBuzzLicenseKeyDialog();
        if (owner != null) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    internal static bool ValidateKey(string raw)
    {
        var norm = NormalizeKey(raw);
        // SBPRO(5) + serial(8) + checksum(8) = 21
        if (norm.Length != 21) return false;
        if (!norm.StartsWith(KeyPrefix, StringComparison.Ordinal)) return false;

        var serialHex   = norm[5..13];
        var checksumHex = norm[13..];
        if (!IsHex(serialHex) || !IsHex(checksumHex)) return false;

        using var hmac     = new HMACSHA256(LicenseSecret);
        var       mac      = hmac.ComputeHash(Encoding.UTF8.GetBytes(HmacPrefix + serialHex));
        var       expected = Convert.ToHexString(mac[..4]);
        return checksumHex == expected;
    }

    private static string NormalizeKey(string raw)
        => raw.Replace("-", "").Replace(" ", "").ToUpperInvariant();

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                return false;
        return true;
    }
}
