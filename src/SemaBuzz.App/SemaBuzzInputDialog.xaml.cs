using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace SemaBuzz.App;

public partial class SemaBuzzInputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;

    private readonly Regex? _allowedChars;

    public SemaBuzzInputDialog(string title, string prompt, string initial = "", Regex? allowedChars = null)
    {
        InitializeComponent();
        _allowedChars    = allowedChars;
        TitleLabel.Text  = title.ToUpperInvariant();
        PromptLabel.Text = prompt.ToUpperInvariant();

        // Strip disallowed chars from any pre-populated value (e.g. saved before validation existed)
        InputBox.Text = allowedChars is not null
            ? new string(initial.Where(c => allowedChars.IsMatch(c.ToString())).ToArray())
            : initial;

        if (allowedChars is not null)
        {
            InputBox.PreviewTextInput += (_, e) =>
            {
                e.Handled = !e.Text.All(c => allowedChars.IsMatch(c.ToString()));
            };
            DataObject.AddPastingHandler(InputBox, (object _, DataObjectPastingEventArgs e) =>
            {
                if (e.DataObject.GetDataPresent(typeof(string)))
                {
                    var text      = (string)e.DataObject.GetData(typeof(string));
                    var sanitized = new string(text.Where(c => allowedChars.IsMatch(c.ToString())).ToArray());
                    if (sanitized != text)
                    {
                        e.CancelCommand();
                        if (sanitized.Length > 0)
                        {
                            var tb        = InputBox;
                            var start     = tb.SelectionStart;
                            var current   = tb.Text;
                            var remaining = tb.MaxLength - (current.Length - tb.SelectionLength);
                            sanitized     = sanitized[..Math.Min(sanitized.Length, remaining)];
                            tb.Text       = current[..tb.SelectionStart] + sanitized + current[(tb.SelectionStart + tb.SelectionLength)..];
                            tb.CaretIndex = start + sanitized.Length;
                        }
                    }
                }
                else
                {
                    e.CancelCommand();
                }
            });
        }

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
        var text  = InputBox.Text;
        InputText = _allowedChars is not null
            ? new string(text.Where(c => _allowedChars.IsMatch(c.ToString())).ToArray())
            : text;
        DialogResult = true;
        Close();
    }
}
