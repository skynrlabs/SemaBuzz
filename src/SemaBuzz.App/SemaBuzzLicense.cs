using System.Runtime.InteropServices;
using System.Windows;
using Windows.Services.Store;

namespace SemaBuzz.App;

/// <summary>
/// Manages SemaBuzz Pro licensing via the Microsoft Store in-app purchase.
/// The Pro add-on unlocks themes and advanced settings.
/// </summary>
internal static class SemaBuzzLicense
{
    /// <summary>
    /// Store add-on product ID for SemaBuzz Pro.
    /// Replace with the real ID from Partner Center once the add-on is created.
    /// </summary>
    public const string ProAddOnStoreId = "9XXXXXXXXXX";

    private static bool          _isProUnlocked;
    private static StoreContext? _context;

    /// <summary>True when the user owns the SemaBuzz Pro add-on.</summary>
    public static bool IsProUnlocked => _isProUnlocked;

    /// <summary>
    /// Fires off an async Store license check and caches the result.
    /// Safe to call on the UI thread at startup — fire and forget.
    /// </summary>
    public static void Check() => _ = CheckCoreAsync();

    private static async Task CheckCoreAsync()
    {
        try
        {
            _context ??= StoreContext.GetDefault();
            var license = await _context.GetAppLicenseAsync();
            if (license?.AddOnLicenses.TryGetValue(ProAddOnStoreId, out var addon) == true)
                _isProUnlocked = addon.IsActive;
        }
        catch { /* Store unavailable in dev / unpackaged build — stay locked */ }
    }

    /// <summary>
    /// Opens the Microsoft Store purchase UI for the SemaBuzz Pro add-on.
    /// Returns true if the purchase succeeded or the add-on was already owned.
    /// </summary>
    public static async Task<bool> PurchaseAsync(Window? owner = null)
    {
        try
        {
            _context ??= StoreContext.GetDefault();
            if (owner != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
                ((IInitializeWithWindow)(object)_context).Initialize(hwnd);
            }
            var result = await _context.RequestPurchaseAsync(ProAddOnStoreId);
            if (result.Status is StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased)
            {
                _isProUnlocked = true;
                return true;
            }
        }
        catch { /* Store unavailable */ }
        return false;
    }

    // -- COM interop for setting the owner window --------------------------

    [ComImport]
    [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithWindow
    {
        void Initialize(IntPtr hwnd);
    }
}
