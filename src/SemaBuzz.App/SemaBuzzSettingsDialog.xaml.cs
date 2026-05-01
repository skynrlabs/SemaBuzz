using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace SemaBuzz.App;

public partial class SemaBuzzSettingsDialog : Window
{
    public double             SelectedIndicatorSensitivity { get; private set; }
    public IndicatorStyleId   SelectedIndicatorStyle       { get; private set; }
    public double             SelectedChatFontSize         { get; private set; }
    public bool               SelectedLivePreview          { get; private set; }
    public bool               SelectedMinimizeToTray       { get; private set; }
    public bool               SelectedStartWithWindows     { get; private set; }
    public bool               SelectedAutoApprove          { get; private set; }
    public bool               SelectedBuzzSoundEnabled     { get; private set; }
    public double             SelectedBuzzSoundVolume      { get; private set; }
    public string             SelectedRelayUri             { get; private set; } = SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;

    public SemaBuzzSettingsDialog(bool lockRelay = false)
    {
        InitializeComponent();

        // Pre-select current settings
        var s = App.Settings;
        SensitivitySlider.Value    = s.IndicatorSensitivity;
        StyleFlicker.IsChecked     = s.IndicatorStyle == IndicatorStyleId.Flicker;
        StylePulse.IsChecked       = s.IndicatorStyle == IndicatorStyleId.Pulse;
        StyleWave.IsChecked        = s.IndicatorStyle == IndicatorStyleId.Wave;
        FontSizeSlider.Value       = s.ChatFontSize;
        LivePreviewCheck.IsChecked = s.LivePreview;
        MinimizeToTrayCheck.IsChecked   = s.MinimizeToTray;
        StartWithWindowsCheck.IsChecked = SemaBuzzStartup.IsRegistered();
        AutoApproveCheck.IsChecked      = s.AutoApprove;
        BuzzSoundEnabledCheck.IsChecked = s.BuzzSoundEnabled;
        BuzzVolumeSlider.Value     = s.BuzzSoundVolume;
        BuzzVolumeSlider.IsEnabled = s.BuzzSoundEnabled;
        BuzzVolumeLabel.IsEnabled  = s.BuzzSoundEnabled;
        RelayUriBox.Text           = s.RelayUri;

        // Disable relay editing while a buzz session is waiting for a peer
        if (lockRelay)
        {
            RelayUriBox.IsEnabled      = false;
            ResetRelayButton.IsEnabled = false;
            RelayLabelRow.Children.Add(new TextBlock
            {
                Text              = "(in use — cancel the buzz to change)",
                Foreground        = new SolidColorBrush(Colors.Gray),
                FontSize          = 10,
                Margin            = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // Gate Pro features when the user has not purchased the Pro add-on
        if (!SemaBuzzLicense.IsProUnlocked)
        {
            StylePulse.IsEnabled = false;
            StylePulse.Content   = MakeProContent("Pulse");
            StyleWave.IsEnabled  = false;
            StyleWave.Content    = MakeProContent("Wave");
            var indicatorStyleLabel = new TextBlock
            {
                Text              = "INDICATOR STYLE",
                FontWeight        = FontWeights.Bold,
                FontSize          = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            indicatorStyleLabel.SetResourceReference(TextBlock.ForegroundProperty, "AmberBrush");
            IndicatorStyleLabelRow.Children.Add(indicatorStyleLabel);
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
            var indicatorLabel = new TextBlock
            {
                Text              = "INDICATOR STYLE",
                FontWeight        = FontWeights.Bold,
                FontSize          = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            indicatorLabel.SetResourceReference(TextBlock.ForegroundProperty, "AmberBrush");
            IndicatorStyleLabelRow.Children.Add(indicatorLabel);
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
        SelectedIndicatorSensitivity = SensitivitySlider.Value;

        if (StylePulse.IsChecked == true)
            SelectedIndicatorStyle = IndicatorStyleId.Pulse;
        else if (StyleWave.IsChecked == true)
            SelectedIndicatorStyle = IndicatorStyleId.Wave;
        else
            SelectedIndicatorStyle = IndicatorStyleId.Flicker;

        SelectedChatFontSize = FontSizeSlider.Value;

        SelectedLivePreview = LivePreviewCheck.IsChecked == true;

        SelectedMinimizeToTray = MinimizeToTrayCheck.IsChecked == true;

        SelectedStartWithWindows = StartWithWindowsCheck.IsChecked == true;
        SelectedAutoApprove      = AutoApproveCheck.IsChecked == true;

        SelectedBuzzSoundEnabled = BuzzSoundEnabledCheck.IsChecked == true;
        SelectedBuzzSoundVolume  = BuzzVolumeSlider.Value;

        var relayUri = RelayUriBox.Text.Trim();
        if (SemaBuzzLicense.IsProUnlocked && !string.IsNullOrWhiteSpace(relayUri))
            SelectedRelayUri = relayUri;
        else
            SelectedRelayUri = SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;

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

    private void BuzzSoundEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (BuzzVolumeSlider is null) return;
        bool enabled = BuzzSoundEnabledCheck.IsChecked == true;
        BuzzVolumeSlider.IsEnabled = enabled;
        BuzzVolumeLabel.IsEnabled  = enabled;
    }
    private async void BuyNow_Click(object sender, RoutedEventArgs e)
    {
        var purchased = await SemaBuzzLicense.PurchaseAsync(this);
        if (purchased)
        {
            // Unlock all gated controls in-place
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

    /// <summary>Standalone PRO badge — appended to an existing label panel.</summary>
    private static Border MakeProBadge()
    {
        var badge = new Border
        {
            CornerRadius      = new CornerRadius(3),
            BorderThickness   = new Thickness(1),
            Background        = Brushes.Transparent,
            Padding           = new Thickness(5, 1, 5, 1),
            Margin            = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.SetResourceReference(Border.BorderBrushProperty, "AmberBrush");
        var tb = new TextBlock
        {
            Text       = "PRO",
            FontSize   = 9,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "AmberBrush");
        badge.Child = tb;
        return badge;
    }

    private static StackPanel MakeProContent(string label)
        => BuildProPanel(label);

    private static StackPanel BuildProPanel(string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text              = label,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(MakeProBadge());
        return panel;
    }

}
