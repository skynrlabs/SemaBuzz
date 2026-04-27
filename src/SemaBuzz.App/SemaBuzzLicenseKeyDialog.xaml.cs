using System.Windows;
using System.Windows.Input;

namespace SemaBuzz.App;

public partial class SemaBuzzLicenseKeyDialog : Window
{
    public SemaBuzzLicenseKeyDialog()
    {
        InitializeComponent();
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

    private void KeyBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Hide the error banner while the user is editing
        ErrorLabel.Visibility = Visibility.Collapsed;
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Text.Trim();
        if (SemaBuzzLicense.Activate(key))
        {
            DialogResult = true;
        }
        else
        {
            ErrorLabel.Visibility = Visibility.Visible;
            KeyBox.Focus();
            KeyBox.SelectAll();
        }
    }

    private void BuyNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(SemaBuzzLicense.PurchaseUrl)
                { UseShellExecute = true });
        }
        catch { /* browser unavailable */ }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
