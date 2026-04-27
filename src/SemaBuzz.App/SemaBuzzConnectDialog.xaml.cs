using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SemaBuzz.Protocol;

namespace SemaBuzz.App;

/// <summary>
/// Dialog where the user configures a new connection — either hosting (creating a Buzz Code)
/// or dialing (entering a peer's Buzz Code or direct host:port). Result properties are populated
/// when <see cref="System.Windows.Window.DialogResult"/> is <see langword="true"/>.
/// </summary>
public partial class SemaBuzzConnectDialog : Window
{
    /// <summary>True if the user chose to host; false if they chose to dial.</summary>
    public bool    IsHost     { get; private set; }
    /// <summary>The display name the user selected for this session.</summary>
    public string  Handle     { get; private set; } = "anonymous";
    /// <summary>Avatar PNG bytes for the selected profile, or null if no avatar was set.</summary>
    public byte[]? AvatarPng  { get; private set; }
    /// <summary>Six-character relay room token entered or generated (empty for direct TCP connections).</summary>
    public string  RelayToken { get; private set; } = string.Empty;
    /// <summary>WebSocket relay endpoint URI (defaults to the hosted SemaBuzz relay).</summary>
    public string  RelayUri   { get; private set; } = SemaBuzzRelayPacket.DefaultRelayUri;
    /// <summary>Hostname or IP entered for a direct TCP dial (empty if using relay).</summary>
    public string  PeerHost   { get; private set; } = string.Empty;
    /// <summary>Port entered for a direct TCP connection (0 if using relay).</summary>
    public int     Port       { get; private set; } = 0;

    private TextBox[] _cells = [];

    public SemaBuzzConnectDialog()
    {
        InitializeComponent();
        _cells = [C0, C1, C2, C3, C4, C5];
        LoadActiveProfile();
        Loaded += (_, _) => C0.Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SemaBuzzThemeManager.ApplyChrome(this);
        SemaBuzzTheme.HideCloseButton(this);
    }

    private void LoadActiveProfile()
    {
        var profiles = SemaBuzzProfileStore.Load();
        var activeId = App.Settings.ActiveProfileId;
        SemaBuzzProfile? active = null;
        if (activeId != null)
            active = profiles.FirstOrDefault(p => p.Id == activeId);
        if (active == null && profiles.Count > 0)
            active = profiles[0];
        if (active != null)
        {
            Handle    = string.IsNullOrWhiteSpace(active.Handle) ? "anonymous" : active.Handle;
            AvatarPng = active.AvatarPng;
        }
    }

    private void Header_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void CodeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBox tb) return;
        var upper = e.Text.ToUpperInvariant();
        if (upper.Length == 0 || !char.IsLetterOrDigit(upper[0])) return;
        tb.Text = upper[0].ToString();
        tb.CaretIndex = 1;
        MoveFocusForward(tb);
    }

    private void CodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        switch (e.Key)
        {
            case Key.Back:
                if (tb.Text.Length > 0) tb.Text = string.Empty;
                else MoveFocusPrev(tb);
                e.Handled = true;
                break;
            case Key.Delete:
                tb.Text = string.Empty;
                e.Handled = true;
                break;
            case Key.Left:
                MoveFocusPrev(tb);
                e.Handled = true;
                break;
            case Key.Right:
                MoveFocusForward(tb);
                e.Handled = true;
                break;
            case Key.Enter:
                if (ConnectBtn.IsEnabled) CommitJoin();
                e.Handled = true;
                break;
            case Key.V when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                PasteToken();
                e.Handled = true;
                break;
        }
    }

    private void PasteToken()
    {
        var chars = Clipboard.GetText()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .Take(6)
            .ToArray();
        for (int i = 0; i < _cells.Length; i++)
            _cells[i].Text = i < chars.Length ? chars[i].ToString() : string.Empty;
        int focus = Math.Min(chars.Length, _cells.Length - 1);
        _cells[focus].Focus();
        UpdateConnectButton();
    }

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
        => UpdateConnectButton();

    private void UpdateConnectButton()
        => ConnectBtn.IsEnabled = _cells.All(c => c.Text.Length == 1);

    private void MoveFocusForward(TextBox current)
    {
        int idx = Array.IndexOf(_cells, current);
        if (idx is >= 0 and < 5)
        {
            _cells[idx + 1].Focus();
            _cells[idx + 1].SelectAll();
        }
    }

    private void MoveFocusPrev(TextBox current)
    {
        int idx = Array.IndexOf(_cells, current);
        if (idx > 0)
        {
            _cells[idx - 1].Focus();
            _cells[idx - 1].SelectAll();
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => CommitJoin();

    private void CommitJoin()
    {
        var token = string.Concat(_cells.Select(c => c.Text)).ToUpperInvariant();
        if (token.Length != 6) return;
        IsHost = false;
        RelayToken = token;
        RelayUri   = App.Settings.RelayUri;
        PeerHost   = string.Empty;
        Port       = 0;
        DialogResult = true;
    }

    private void StartBuzz_Click(object sender, RoutedEventArgs e)
    {
        IsHost     = true;
        RelayToken = SemaBuzzRelayPacket.GenerateToken();
        RelayUri   = App.Settings.RelayUri;
        PeerHost   = string.Empty;
        Port       = 0;

        if (Uri.TryCreate(RelayUri, UriKind.Absolute, out var u)
            && (u.Host is "localhost" or "127.0.0.1" or "::1"))
        {
            MessageBox.Show(
                "Your relay is set to " + RelayUri + ", which is your local machine.\n\n" +
                "The peer will receive your Buzz Code and try to connect to their own localhost " +
                "— it won't work.\n\nIn Settings, Relay Server, enter the public address of your relay.",
                "Relay address is localhost",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}