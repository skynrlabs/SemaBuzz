using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SemaBuzz.App;

public partial class SemaBuzzLogViewerDialog : Window
{
    public SemaBuzzLogViewerDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PopulateLog();
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

    private void PopulateLog()
    {
        var entries = SemaBuzzChatLog.LoadAll();

        if (entries.Count == 0)
        {
            var msg = App.Settings.LogPersistence == LogPersistenceMode.PermanentEncrypted
                ? "No log entries found."
                : "Log persistence is set to Session Only.\nEnable Permanent Encrypted in Settings to save logs.";

            LogPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text              = msg,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontStyle         = FontStyles.Italic,
                TextWrapping      = TextWrapping.Wrap,
                Margin            = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        var accent      = SemaBuzzThemeManager.AccentColor;
        var accentBrush = new SolidColorBrush(accent);
        var greyBrush   = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var dimBrush    = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var textBrush   = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

        string? lastDate = null;

        foreach (var entry in entries)
        {
            DateTime.TryParse(entry.Time, null, DateTimeStyles.RoundtripKind, out var utc);
            var local    = utc.ToLocalTime();
            var dateStr  = local.ToString("dddd, MMMM d, yyyy");
            var timeStr  = local.ToString("h:mm tt");

            // Date divider on day boundary
            if (dateStr != lastDate)
            {
                lastDate = dateStr;
                LogPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text                = $" {dateStr} ",
                    Foreground          = dimBrush,
                    FontSize            = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 14, 0, 8),
                });
            }

            var isOut      = entry.Direction == "out";
            var handleBrush = isOut ? accentBrush : greyBrush;

            var tb = new System.Windows.Controls.TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 2, 0, 2),
            };

            tb.Inlines.Add(new Run(timeStr)          { Foreground = dimBrush,    FontSize = 10 });
            tb.Inlines.Add(new Run("  "));
            tb.Inlines.Add(new Run(entry.Handle)     { Foreground = handleBrush, FontWeight = FontWeights.Bold });
            tb.Inlines.Add(new Run("  \u203a  ")    { Foreground = dimBrush });
            tb.Inlines.Add(new Run(entry.Message)    { Foreground = textBrush });

            LogPanel.Children.Add(tb);
        }

        LogScroller.ScrollToEnd();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
