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
            case SemaBuzzThemeId.Steel:    ThemeSteel.IsChecked    = true; break;
            case SemaBuzzThemeId.Powwow:   ThemePowwow.IsChecked   = true; break;
            default:                       ThemeObsidian.IsChecked = true; break;
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

    //  Theme preview handlers

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

    private void ThemePowwow_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Powwow);

    //  Footer

    private void Close_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

}
