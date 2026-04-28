using System.Windows;
using System.Windows.Input;

namespace SemaBuzz.App;

public partial class SemaBuzzInputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;

    public SemaBuzzInputDialog(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        TitleLabel.Text  = title.ToUpperInvariant();
        PromptLabel.Text = prompt.ToUpperInvariant();
        InputBox.Text    = initial;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
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

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { Commit(); e.Handled = true; }
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Commit();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Commit()
    {
        InputText    = InputBox.Text;
        DialogResult = true;
        Close();
    }
}
