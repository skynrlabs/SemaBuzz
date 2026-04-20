using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SemaBuzz.App;

public partial class SemaBuzzSettingsDialog : Window
{
    public LogPersistenceMode SelectedLogPersistence    { get; private set; }
    /// <summary>The port the user chose, or null to keep the built-in default.</summary>
    public int?               SelectedDefaultListenPort { get; private set; }
    public double             SelectedIndicatorSensitivity { get; private set; }
    public IndicatorStyleId   SelectedIndicatorStyle       { get; private set; }
    public double             SelectedChatFontSize         { get; private set; }
    public bool               SelectedLivePreview          { get; private set; }
    public string             SelectedRelayUri             { get; private set; } = SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;

    public SemaBuzzSettingsDialog()
    {
        InitializeComponent();

        // Pre-select current settings
        var s = App.Settings;
        LogSessionOnly.IsChecked = s.LogPersistence == LogPersistenceMode.SessionOnly;
        LogPermanent.IsChecked   = s.LogPersistence == LogPersistenceMode.PermanentEncrypted;
        DefaultPortBox.Text      = s.DefaultListenPort?.ToString() ?? "7070";
        SensitivitySlider.Value  = s.IndicatorSensitivity;
        StyleFlicker.IsChecked   = s.IndicatorStyle == IndicatorStyleId.Flicker;
        StylePulse.IsChecked     = s.IndicatorStyle == IndicatorStyleId.Pulse;
        StyleWave.IsChecked      = s.IndicatorStyle == IndicatorStyleId.Wave;
        FontSizeSlider.Value     = s.ChatFontSize;
        LivePreviewCheck.IsChecked = s.LivePreview;
        RelayUriBox.Text           = s.RelayUri;

        // Gate PRO features when the user has not purchased the Pro add-on
        if (!SemaBuzzLicense.IsProUnlocked)
        {
            LogPermanent.IsEnabled = false;
            LogPermanent.Content   = MakeProContent("Permanent Encrypted");
            DefaultPortBox.IsEnabled = false;
            DefaultPortLabelRow.Children.Add(MakeProBadge());
            StylePulse.IsEnabled = false;
            StylePulse.Content   = MakeProContent("Pulse");
            StyleWave.IsEnabled  = false;
            StyleWave.Content    = MakeProContent("Wave");
            IndicatorStyleLabelRow.Children.Add(new TextBlock
            {
                Text       = "INDICATOR STYLE",
                Foreground = new SolidColorBrush(SemaBuzzThemeManager.AccentColor),
                FontWeight = FontWeights.Bold,
                FontSize   = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            IndicatorStyleLabelRow.Children.Add(MakeProBadge());
            if (s.IndicatorStyle != IndicatorStyleId.Flicker)
                StyleFlicker.IsChecked = true;

            // Fall back to free options if a gated one is currently active
            if (s.LogPersistence == LogPersistenceMode.PermanentEncrypted)
                LogSessionOnly.IsChecked = true;
        }


        // For Pro users the if-block above is skipped, so populate the style label here
        if (SemaBuzzLicense.IsProUnlocked)
        {
            IndicatorStyleLabelRow.Children.Add(new TextBlock
            {
                Text       = "INDICATOR STYLE",
                Foreground = new SolidColorBrush(SemaBuzzThemeManager.AccentColor),
                FontWeight = FontWeights.Bold,
                FontSize   = 11,
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
        SelectedLogPersistence = LogPermanent.IsChecked == true
            ? LogPersistenceMode.PermanentEncrypted
            : LogPersistenceMode.SessionOnly;

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

        var relayUri = RelayUriBox.Text.Trim();
        SelectedRelayUri = string.IsNullOrWhiteSpace(relayUri)
            ? SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri
            : relayUri;

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

    private async void BuyNow_Click(object sender, RoutedEventArgs e)
    {
        var purchased = await SemaBuzzLicense.PurchaseAsync();
        if (purchased)
        {
            // Unlock all gated controls in-place
            LogPermanent.IsEnabled = true;
            LogPermanent.Content   = "Permanent Encrypted";
            DefaultPortBox.IsEnabled = true;
            if (DefaultPortLabelRow.Children.Count > 1)
                DefaultPortLabelRow.Children.RemoveAt(DefaultPortLabelRow.Children.Count - 1);
            StylePulse.IsEnabled = true;
            StylePulse.Content   = "Pulse";
            StyleWave.IsEnabled  = true;
            StyleWave.Content    = "Wave";
            if (IndicatorStyleLabelRow.Children.Count > 1)
                IndicatorStyleLabelRow.Children.RemoveAt(IndicatorStyleLabelRow.Children.Count - 1);
            BuyNowSettingsButton.IsEnabled = false;
            BuyNowSettingsButton.Content   = "\u2713 SemaBuzz Pro";
        }
    }

    /// <summary>
    /// Builds a StackPanel containing the label text and a rounded PRO badge
    /// whose border and text color match the current theme accent.
    /// </summary>
    private static StackPanel MakeProContent(string label)
    {
        var panel = BuildProPanel(label);
        return panel;
    }

    /// <summary>Standalone PRO badge â€” appended to an existing label panel.</summary>
    private static Border MakeProBadge()
    {
        var accent      = SemaBuzzThemeManager.AccentColor;
        var accentBrush = new SolidColorBrush(accent);
        var badge = new Border
        {
            CornerRadius    = new CornerRadius(3),
            BorderBrush     = accentBrush,
            BorderThickness = new Thickness(1),
            Background      = Brushes.Transparent,
            Padding         = new Thickness(5, 1, 5, 1),
            Margin          = new Thickness(8, 0, 0, 0),
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
        var accent = SemaBuzzThemeManager.AccentColor;
        var accentBrush = new SolidColorBrush(accent);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        panel.Children.Add(new TextBlock
        {
            Text              = label,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var badge = new Border
        {
            CornerRadius    = new CornerRadius(3),
            BorderBrush     = accentBrush,
            BorderThickness = new Thickness(1),
            Background      = Brushes.Transparent,
            Padding         = new Thickness(5, 1, 5, 1),
            Margin          = new Thickness(8, 0, 0, 0),
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

        panel.Children.Add(badge);
        return panel;
    }
}
