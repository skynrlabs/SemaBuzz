using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SemaBuzz.App;

public partial class SemaBuzzSettingsDialog : Window
{
    /// <summary>The port the user chose, or null to keep the built-in default.</summary>
    public int?               SelectedDefaultListenPort { get; private set; }
    public double             SelectedIndicatorSensitivity { get; private set; }
    public IndicatorStyleId   SelectedIndicatorStyle       { get; private set; }
    public double             SelectedChatFontSize         { get; private set; }
    public bool               SelectedLivePreview          { get; private set; }
    public bool               SelectedMinimizeToTray       { get; private set; }
    public bool               SelectedBuzzSoundEnabled     { get; private set; }
    public double             SelectedBuzzSoundVolume      { get; private set; }
    public string             SelectedRelayUri             { get; private set; } = SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;

    public SemaBuzzSettingsDialog()
    {
        InitializeComponent();

        // Pre-select current settings
        var s = App.Settings;
        DefaultPortBox.Text        = s.DefaultListenPort?.ToString() ?? "7070";
        SensitivitySlider.Value    = s.IndicatorSensitivity;
        StyleFlicker.IsChecked     = s.IndicatorStyle == IndicatorStyleId.Flicker;
        StylePulse.IsChecked       = s.IndicatorStyle == IndicatorStyleId.Pulse;
        StyleWave.IsChecked        = s.IndicatorStyle == IndicatorStyleId.Wave;
        FontSizeSlider.Value       = s.ChatFontSize;
        LivePreviewCheck.IsChecked = s.LivePreview;
        MinimizeToTrayCheck.IsChecked = s.MinimizeToTray;
        BuzzSoundEnabledCheck.IsChecked = s.BuzzSoundEnabled;
        BuzzVolumeSlider.Value     = s.BuzzSoundVolume;
        RelayUriBox.Text           = s.RelayUri;

        // Gate Pro features when the user has not purchased the Pro add-on
        if (!SemaBuzzLicense.IsProUnlocked)
        {
            DefaultPortBox.IsEnabled = false;
            DefaultPortLabelRow.Children.Add(MakeProBadge());
            StylePulse.IsEnabled = false;
            StylePulse.Content   = MakeProContent("Pulse");
            StyleWave.IsEnabled  = false;
            StyleWave.Content    = MakeProContent("Wave");
            IndicatorStyleLabelRow.Children.Add(new TextBlock
            {
                Text              = "INDICATOR STYLE",
                Foreground        = new SolidColorBrush(SemaBuzzThemeManager.AccentColor),
                FontWeight        = FontWeights.Bold,
                FontSize          = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            IndicatorStyleLabelRow.Children.Add(MakeProBadge());

            RelayUriBox.IsEnabled       = false;
            ResetRelayButton.IsEnabled  = false;
            RelayLabelRow.Children.Add(MakeProBadge());
            RelayUriBox.Text            = SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;

            // Fall back to free options if a gated one is currently active
            if (s.IndicatorStyle != IndicatorStyleId.Flicker)
                StyleFlicker.IsChecked = true;
        }
        else
        {
            IndicatorStyleLabelRow.Children.Add(new TextBlock
            {
                Text              = "INDICATOR STYLE",
                Foreground        = new SolidColorBrush(SemaBuzzThemeManager.AccentColor),
                FontWeight        = FontWeights.Bold,
                FontSize          = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            BuyNowSettingsButton.Visibility = Visibility.Collapsed;
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

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(DefaultPortBox.Text.Trim(), out var parsedPort)
            && parsedPort is >= 1024 and <= 65535)
            SelectedDefaultListenPort = parsedPort;
        else
            SelectedDefaultListenPort = null; // revert to built-in default

        SelectedIndicatorSensitivity = SensitivitySlider.Value;

        SelectedIndicatorStyle = StylePulse.IsChecked == true ? IndicatorStyleId.Pulse
                               : StyleWave.IsChecked  == true ? IndicatorStyleId.Wave
                               :                                IndicatorStyleId.Flicker;

        SelectedChatFontSize = FontSizeSlider.Value;

        SelectedLivePreview = LivePreviewCheck.IsChecked == true;

        SelectedMinimizeToTray = MinimizeToTrayCheck.IsChecked == true;

        SelectedBuzzSoundEnabled = BuzzSoundEnabledCheck.IsChecked == true;
        SelectedBuzzSoundVolume  = BuzzVolumeSlider.Value;

        var relayUri = RelayUriBox.Text.Trim();
        SelectedRelayUri = SemaBuzzLicense.IsProUnlocked && !string.IsNullOrWhiteSpace(relayUri)
            ? relayUri
            : SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void ResetRelayUri_Click(object sender, RoutedEventArgs e)
        => RelayUriBox.Text = SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;

    private void SensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SensitivityLabel is null) return;
        SensitivityLabel.Text = $"{e.NewValue:F1}\u00d7";
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontSizeLabel is null) return;
        FontSizeLabel.Text = $"{(int)e.NewValue}px";
    }

    private void BuzzVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BuzzVolumeLabel is null) return;
        BuzzVolumeLabel.Text = $"{(int)Math.Round(e.NewValue * 100):0}%";
    }

    private async void BuyNow_Click(object sender, RoutedEventArgs e)
    {
        var purchased = await SemaBuzzLicense.PurchaseAsync();
        if (purchased)
        {
            // Unlock all gated controls in-place
            DefaultPortBox.IsEnabled = true;
            if (DefaultPortLabelRow.Children.Count > 1)
                DefaultPortLabelRow.Children.RemoveAt(DefaultPortLabelRow.Children.Count - 1);
            StylePulse.IsEnabled = true;
            StylePulse.Content   = "Pulse";
            StyleWave.IsEnabled  = true;
            StyleWave.Content    = "Wave";
            if (IndicatorStyleLabelRow.Children.Count > 1)
                IndicatorStyleLabelRow.Children.RemoveAt(IndicatorStyleLabelRow.Children.Count - 1);
            RelayUriBox.IsEnabled      = true;
            ResetRelayButton.IsEnabled = true;
            if (RelayLabelRow.Children.Count > 1)
                RelayLabelRow.Children.RemoveAt(RelayLabelRow.Children.Count - 1);
            BuyNowSettingsButton.IsEnabled = false;
            BuyNowSettingsButton.Content   = "\u2713 SemaBuzz Pro";
        }
    }

    /// <summary>
    /// Builds a StackPanel containing the label text and a rounded PRO badge
    /// whose border and text colour match the current theme accent.
    /// </summary>
    private static StackPanel MakeProContent(string label)
        => BuildProPanel(label);

    /// <summary>Standalone PRO badge — appended to an existing label panel.</summary>
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
