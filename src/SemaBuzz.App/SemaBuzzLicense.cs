using Windows.Services.Store;

namespace SemaBuzz.App;

/// <summary>
/// Manages the freemium license model via the Windows Store add-on.
/// Standard is included with the app purchase. Pro is an in-app upgrade.
/// </summary>
internal static class SemaBuzzLicense
{
    // Store add-on product ID for SemaBuzz Pro (set in Partner Center before Store submission)
    private const string ProAddonStoreId = "9N000000000PRO"; // development placeholder

#if DEBUG
    // Make all Pro features available during development without a Store purchase.
    /// <summary>True when the SemaBuzz Pro add-on is active. Always true in DEBUG builds.</summary>
    public static bool IsProUnlocked { get; private set; } = true;
#else
    /// <summary>True when the SemaBuzz Pro add-on is active; checked asynchronously at startup.</summary>
    public static bool IsProUnlocked { get; private set; } = false;
#endif

    /// <summary>Checks the Store license. Called once at startup.</summary>
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

    /// <summary>
    /// Triggers the Store purchase UI for the Pro add-on.
    /// Returns true if the purchase succeeded or was already purchased.
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
