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

        IndicatorStyleLabelRow.Children.Add(new TextBlock
        {
            Text       = "INDICATOR STYLE",
            Foreground = new SolidColorBrush(SemaBuzzThemeManager.AccentColor),
            FontWeight = FontWeights.Bold,
            FontSize   = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
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

    private void BuyNow_Click(object sender, RoutedEventArgs e) { }
}
