using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
            case SemaBuzzThemeId.Steel:     ThemeSteel.IsChecked    = true; break;
            case SemaBuzzThemeId.Forest:    ThemeForest.IsChecked    = true; break;
            case SemaBuzzThemeId.Chrome:        ThemeChrome.IsChecked        = true; break;
            case SemaBuzzThemeId.MutedTerminal: ThemeMutedTerminal.IsChecked = true; break;
            case SemaBuzzThemeId.Retro95:         ThemeWin95.IsChecked         = true; break;
            default:                            ThemeObsidian.IsChecked      = true; break;
        }

        if (!SemaBuzzLicense.IsProUnlocked)
        {
            (RadioButton Rb, string Label)[] proThemes =
            [
                (ThemeNeon,      "Neon  (pink \u00b7 purple)"),
                (ThemeMatrix,    "Matrix  (CRT green \u00b7 black)"),
                (ThemeBloodMoon, "Blood Moon  (crimson \u00b7 black)"),
                (ThemeArctic,    "Arctic  (icy cyan \u00b7 deep navy)"),
                (ThemeSepia,     "Sepia  (old gold \u00b7 dark oak)"),
                (ThemeMidnight,  "Midnight  (electric blue \u00b7 void)"),
                (ThemeSunset,    "Sunset  (deep orange \u00b7 char)"),
                (ThemeRose,      "Rose  (hot pink \u00b7 dark)"),
                (ThemeViolet,    "Violet  (electric purple \u00b7 void)"),
                (ThemeEmerald,   "Emerald  (rich green \u00b7 deep)"),
                (ThemeSteel,     "Steel  (blue-grey \u00b7 industrial)"),
                (ThemeForest,    "Forest  (turquoise \u00b7 earth)"),
                (ThemeChrome,        "Chrome  (blue \u00b7 dark grey)"),
                (ThemeMutedTerminal, "Muted Terminal  (teal \u00b7 lime green)"),
                (ThemeWin95,         "Retro \u201995  (navy \u00b7 silver grey)"),
            ];
            foreach (var (rb, label) in proThemes)
            {
                rb.IsEnabled = false;
                rb.Content   = MakeProContent(label);
            }
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

    private void ThemeForest_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Forest);

    private void ThemeChrome_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Chrome);

    private void ThemeMutedTerminal_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.MutedTerminal);

    private void ThemeWin95_Checked(object sender, RoutedEventArgs e)
        => SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Retro95);

    //  Footer

    private async void BuyNow_Click(object sender, RoutedEventArgs e)
    {
        var purchased = await SemaBuzzLicense.PurchaseAsync();
        if (purchased)
        {
            (RadioButton Rb, string Label)[] proThemes =
            [
                (ThemeNeon,      "Neon  (pink \u00b7 purple)"),
                (ThemeMatrix,    "Matrix  (CRT green \u00b7 black)"),
                (ThemeBloodMoon, "Blood Moon  (crimson \u00b7 black)"),
                (ThemeArctic,    "Arctic  (icy cyan \u00b7 deep navy)"),
                (ThemeSepia,     "Sepia  (old gold \u00b7 dark oak)"),
                (ThemeMidnight,  "Midnight  (electric blue \u00b7 void)"),
                (ThemeSunset,    "Sunset  (deep orange \u00b7 char)"),
                (ThemeRose,      "Rose  (hot pink \u00b7 dark)"),
                (ThemeViolet,    "Violet  (electric purple \u00b7 void)"),
                (ThemeEmerald,   "Emerald  (rich green \u00b7 deep)"),
                (ThemeSteel,     "Steel  (blue-grey \u00b7 industrial)"),
                (ThemeForest,    "Forest  (turquoise \u00b7 earth)"),
                (ThemeChrome,        "Chrome  (blue \u00b7 dark grey)"),
                (ThemeMutedTerminal, "Muted Terminal  (teal \u00b7 lime green)"),
                (ThemeWin95,         "Retro \u201995  (navy \u00b7 silver grey)"),
            ];
            foreach (var (rb, label) in proThemes)
            {
                rb.IsEnabled = true;
                rb.Content   = label;
            }
            BuyNowThemeButton.IsEnabled = false;
            BuyNowThemeButton.Content   = "\u2713 SemaBuzz Pro";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    //  Pro badge helpers

    private static StackPanel MakeProContent(string label) => BuildProPanel(label);

    private static Border MakeProBadge()
    {
        var accentBrush = new SolidColorBrush(SemaBuzzThemeManager.AccentColor);
        var badge = new Border
        {
            CornerRadius      = new CornerRadius(3),
            BorderBrush       = accentBrush,
            BorderThickness   = new Thickness(1),
            Background        = Brushes.Transparent,
            Padding           = new Thickness(5, 1, 5, 1),
            Margin            = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock
        {
            Text       = "PRO",
            Foreground = accentBrush,
            FontSize   = 9,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
        };
        return badge;
    }

    private static StackPanel BuildProPanel(string label)
    {
        var accentBrush = new SolidColorBrush(SemaBuzzThemeManager.AccentColor);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text              = label,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new Border
        {
            CornerRadius      = new CornerRadius(3),
            BorderBrush       = accentBrush,
            BorderThickness   = new Thickness(1),
            Background        = Brushes.Transparent,
            Padding           = new Thickness(5, 1, 5, 1),
            Margin            = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child             = new TextBlock
            {
                Text       = "PRO",
                Foreground = accentBrush,
                FontSize   = 9,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
            },
        });
        return panel;
    }

}
