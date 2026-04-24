using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace SemaBuzz.App;

public partial class SemaBuzzAboutDialog : Window
{
    public SemaBuzzAboutDialog()
    {
        InitializeComponent();

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        if (ver is null)
            VersionLabel.Text = "Version 1.0";
        else
            VersionLabel.Text = $"Version {ver.Major}.{ver.Minor}.{ver.Build}";
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

    private void Website_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://semabuzz.me") { UseShellExecute = true });

    private void Close_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;
}
