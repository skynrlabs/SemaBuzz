namespace SemaBuzz.App;

/// <summary>
/// SemaBuzz is sold as a one-time purchase on the Microsoft Store.
/// All features are available to every user who has purchased the app.
/// </summary>
internal static class SemaBuzzLicense
{
    /// <summary>Always true — the app is a paid Store app with no feature tiers.</summary>
    public static bool IsProUnlocked => true;

    /// <summary>No-op. License is validated by the Store at install time.</summary>
    public static Task CheckAsync() => Task.CompletedTask;
}
