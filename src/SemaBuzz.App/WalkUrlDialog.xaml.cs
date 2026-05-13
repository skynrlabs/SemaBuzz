using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SemaBuzz.App;

public partial class WalkUrlDialog : Window
{
    public string? Url { get; private set; }

    public WalkUrlDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => UrlBox.Focus();
        UrlBox.TextChanged += (_, _) => ClearError();
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

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Push_Click(object sender, RoutedEventArgs e)
    {
        var text = UrlBox.Text?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowError("Enter a valid http:// or https:// URL.");
            UrlBox.Focus();
            return;
        }
        Url = uri.AbsoluteUri;
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        UrlErrorText.Text = message;
        UrlErrorText.Visibility = Visibility.Visible;
        OuterBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
    }

    private void ClearError()
    {
        UrlErrorText.Visibility = Visibility.Collapsed;
        OuterBorder.ClearValue(System.Windows.Controls.Border.BorderBrushProperty);
    }
}
