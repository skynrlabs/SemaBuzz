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

        var indicatorLabel = new TextBlock
        {
            Text              = "INDICATOR STYLE",
            FontWeight        = FontWeights.Bold,
            FontSize          = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        indicatorLabel.SetResourceReference(TextBlock.ForegroundProperty, "AmberBrush");
        IndicatorStyleLabelRow.Children.Add(indicatorLabel);
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
        if (!string.IsNullOrWhiteSpace(relayUri))
            SelectedRelayUri = relayUri;

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
}
