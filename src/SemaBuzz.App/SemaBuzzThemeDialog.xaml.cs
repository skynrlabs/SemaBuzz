using System.Windows;
using System.Windows.Input;

namespace SemaBuzz.App;

public partial class SemaBuzzThemeDialog : Window
{
    private readonly SemaBuzzThemeId _originalTheme;

    public SemaBuzzThemeDialog()
    {
        InitializeComponent();
        _originalTheme = SemaBuzzThemeManager.Current;

        // Pre-select the currently active theme
        switch (SemaBuzzThemeManager.Current)
        {
            case SemaBuzzThemeId.Neon:      ThemeNeon.IsChecked      = true; break;
            case SemaBuzzThemeId.Matrix:    ThemeMatrix.IsChecked    = true; break;
            case SemaBuzzThemeId.BloodMoon: ThemeBloodMoon.IsChecked = true; break;
            case SemaBuzzThemeId.Arctic:    ThemeArctic.IsChecked    = true; break;
            case SemaBuzzThemeId.Sepia:     ThemeSepia.IsChecked     = true; break;
            case SemaBuzzThemeId.Midnight:  ThemeMidnight.IsChecked  = true; break;
            case SemaBuzzThemeId.Sunset:    ThemeSunset.IsChecked    = true; break;
            case SemaBuzzThemeId.Rose:      ThemeRose.IsChecked      = true; break;
            case SemaBuzzThemeId.Violet:    ThemeViolet.IsChecked    = true; break;
            case SemaBuzzThemeId.Emerald:   ThemeEmerald.IsChecked   = true; break;
            case SemaBuzzThemeId.Steel:     ThemeSteel.IsChecked     = true; break;
            default:                        ThemeObsidian.IsChecked  = true; break;
        }

        // Gate PRO themes for free-tier users
        if (!SemaBuzzLicense.IsProUnlocked)
        {
            ThemeNeonRow.IsEnabled      = false;
            ThemeMatrixRow.IsEnabled    = false;
            ThemeBloodMoonRow.IsEnabled = false;
            ThemeArcticRow.IsEnabled    = false;
            ThemeSepiaRow.IsEnabled     = false;
            ThemeMidnightRow.IsEnabled  = false;
            ThemeSunsetRow.IsEnabled    = false;
            ThemeRoseRow.IsEnabled      = false;
            ThemeVioletRow.IsEnabled    = false;
            ThemeEmeraldRow.IsEnabled   = false;
            ThemeSteelRow.IsEnabled     = false;

            // If somehow a PRO theme is active, fall back to Obsidian
            if (SemaBuzzThemeManager.Current != SemaBuzzThemeId.Obsidian)
            {
                ThemeObsidian.IsChecked = true;
                SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Obsidian);
            }
        }
        else
        {
            BuyNowThemeButton.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SemaBuzzThemeManager.ApplyChrome(this);
        SemaBuzzTheme.HideCloseButton(this);
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    // ── Theme preview handlers ────────────────────────────────────────────────

    private void ThemeObsidian_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Obsidian);

    private void ThemeNeon_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Neon);

    private void ThemeMatrix_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Matrix);

    private void ThemeBloodMoon_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.BloodMoon);

    private void ThemeArctic_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Arctic);

    private void ThemeSepia_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Sepia);

    private void ThemeMidnight_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Midnight);

    private void ThemeSunset_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Sunset);

    private void ThemeRose_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Rose);

    private void ThemeViolet_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Violet);

    private void ThemeEmerald_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Emerald);

    private void ThemeSteel_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Steel);

    // ── Footer ────────────────────────────────────────────────────────────────

    private async void BuyNow_Click(object sender, RoutedEventArgs e)
    {
        var purchased = await SemaBuzzLicense.PurchaseAsync();
        if (purchased)
        {
            ThemeNeonRow.IsEnabled      = true;
            ThemeMatrixRow.IsEnabled    = true;
            ThemeBloodMoonRow.IsEnabled = true;
            ThemeArcticRow.IsEnabled    = true;
            ThemeSepiaRow.IsEnabled     = true;
            ThemeMidnightRow.IsEnabled  = true;
            ThemeSunsetRow.IsEnabled    = true;
            ThemeRoseRow.IsEnabled      = true;
            ThemeVioletRow.IsEnabled    = true;
            ThemeEmeraldRow.IsEnabled   = true;
            ThemeSteelRow.IsEnabled     = true;
            BuyNowThemeButton.IsEnabled = false;
            BuyNowThemeButton.Content   = "\u2713 SemaBuzz Pro";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
