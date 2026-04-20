using System.Windows;
using System.Windows.Input;

namespace SemaBuzz.App;

public partial class SemaBuzzPassphraseDialog : Window
{
    public string Passphrase { get; private set; } = string.Empty;

    /// <summary>Optional hint shown as subtitle (e.g. "wrong passphrase  try again").</summary>
    public string? Hint
    {
        set { if (value != null) HintLabel.Text = $"Â» {value}"; }
    }

    public SemaBuzzPassphraseDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PassphraseBox.Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SemaBuzzThemeManager.ApplyChrome(this);
        SemaBuzzTheme.HideCloseButton(this);
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PassphraseBox.Password))
        {
            PassphraseBox.BorderBrush = System.Windows.Media.Brushes.Red;
            return;
        }
        Passphrase   = PassphraseBox.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PassphraseBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Connect_Click(sender, e);
    }
}
