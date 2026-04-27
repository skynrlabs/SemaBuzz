using System.Windows;
using System.Windows.Input;

namespace SemaBuzz.App;

public partial class WalkUrlDialog : Window
{
    public string? Url { get; private set; }

    public WalkUrlDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => UrlBox.Focus();
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
            MessageBox.Show(this, "Please enter a valid http:// or https:// URL.", "SemaBuzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            UrlBox.Focus();
            return;
        }
        Url = uri.AbsoluteUri;
        DialogResult = true;
        Close();
    }
}
