using Windows.Services.Store;

namespace SemaBuzz.App;

/// <summary>
/// Manages the freemium license model via the Windows Store add-on.
/// </summary>
internal static class SemaBuzzLicense
{
    // Store add-on product ID for SemaBuzz Pro (set in Partner Center)
    private const string ProAddonStoreId = "9N000000000PRO"; // TODO: replace with real Store ID

    // ─── State ────────────────────────────────────────────────────────────────

#if DEBUG
    // Make all PRO features available during development without a Store purchase.
    public static bool IsProUnlocked { get; private set; } = true;
#else
    public static bool IsProUnlocked { get; private set; } = false;
#endif

    // ─── Check (called on startup) ────────────────────────────────────────────

    public static async Task CheckAsync()
    {
        try
        {
            var context    = StoreContext.GetDefault();
            var appLicense = await context.GetAppLicenseAsync();

            if (appLicense.AddOnLicenses.TryGetValue(ProAddonStoreId, out var addon))
                IsProUnlocked = addon.IsActive;
        }
        catch
        {
            // Not running in a Store context (e.g. side-loaded) — leave IsProUnlocked as-is.
        }
    }

    // ─── Purchase ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers the Store purchase UI for the Pro add-on.
    /// Returns true if the purchase succeeded.
    /// </summary>
    public static async Task<bool> PurchaseAsync()
    {
        try
        {
            var context = StoreContext.GetDefault();
            var result  = await context.RequestPurchaseAsync(ProAddonStoreId);

            if (result.Status == StorePurchaseStatus.Succeeded ||
                result.Status == StorePurchaseStatus.AlreadyPurchased)
            {
                IsProUnlocked = true;
                return true;
            }
        }
        catch { /* Store not available */ }
        return false;
    }
}
