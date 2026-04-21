using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SemaBuzz.Protocol;

namespace SemaBuzz.App;

/// <summary>
/// Connection dialog  lets the user choose to host (listen) or dial (connect),
/// enter a peer address, port, and identity.
/// </summary>
public partial class SemaBuzzConnectDialog : Window
{
    public bool    IsHost      { get; private set; }
    public string  PeerHost    { get; private set; } = "127.0.0.1";
    public int     Port        { get; private set; } = 7070;
    public string  Handle      { get; private set; } = "anonymous";
    public byte[]? AvatarPng   { get; private set; }
    /// <summary>Set when the user chose relay mode (host or dial). Empty otherwise.</summary>
    public string  RelayToken  { get; private set; } = string.Empty;


    // Profile management
    private List<SemaBuzzProfile>    _profiles       = [];
    private SemaBuzzProfile?         _selectedProfile;
    private bool                     _isEditingProfile;

    // Wraps a saved profile or null (= "new profile" sentinel) for the ComboBox.
    private sealed record ProfileItem(string Display, SemaBuzzProfile? Profile)
    {
        public override string ToString() => Display;
    }

    public SemaBuzzConnectDialog()
    {
        InitializeComponent();
        LoadProfiles();

        Loaded += (_, _) =>
        {
            HostBuzzAddressBox.Text = SemaBuzzRelayPacket.GenerateToken().ToUpperInvariant();
        };
    }

    /// <summary>
    /// Opens the dialog pre-set to dial mode with the given buzz:// URI
    /// (used when the app is launched or focused via a buzz:// link).
    /// </summary>
    public SemaBuzzConnectDialog(string dialBuzzUri) : this()
    {
        Loaded += (_, _) =>
        {
            DialMode.IsChecked = true;
            BuzzUrlBox.Text    = dialBuzzUri;
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

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (HostBuzzPanel == null) return;
        var hosting = HostMode.IsChecked == true;
        HostBuzzPanel.Visibility = hosting ? Visibility.Visible   : Visibility.Collapsed;
        BuzzUrlPanel.Visibility  = hosting ? Visibility.Collapsed : Visibility.Visible;
    }

    // Buzz address handlers

    private void CopyHostBuzz_Click(object sender, RoutedEventArgs e)
    {
        var addr = HostBuzzAddressBox.Text?.Trim();
        if (string.IsNullOrEmpty(addr)) return;
        Clipboard.SetText(addr);
        CopyHostBuzzBtn.Content = "COPIED!";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { CopyHostBuzzBtn.Content = "COPY"; timer.Stop(); };
        timer.Start();
    }

    private void NewHostBuzz_Click(object sender, RoutedEventArgs e)
        => HostBuzzAddressBox.Text = SemaBuzzRelayPacket.GenerateToken().ToUpperInvariant();

    protected override void OnClosed(EventArgs e) => base.OnClosed(e);

    private void ChooseAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Choose Avatar Image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource       = new Uri(dlg.FileName);
            src.DecodePixelWidth  = 48;
            src.DecodePixelHeight = 48;
            src.CacheOption     = BitmapCacheOption.OnLoad;
            src.EndInit();
            src.Freeze();

            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            encoder.Save(ms);
            AvatarPng = ms.ToArray();

            AvatarPreview.Fill      = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
            ClearAvatarBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load image: {ex.Message}", "SemaBuzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearAvatar_Click(object sender, RoutedEventArgs e)
    {
        AvatarPng                = null;
        AvatarPreview.Fill       = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        ClearAvatarBtn.IsEnabled = false;
    }

    // Profile management

    private void LoadProfiles()
    {
        _profiles = SemaBuzzProfileStore.Load();
        RefreshPicker();
    }

    private void RefreshPicker()
    {
        var items = new List<ProfileItem> { new("\u002B  new profile", null) };
        items.AddRange(_profiles.Select(p => new ProfileItem(p.Handle, p)));

        ProfilePicker.ItemsSource = null;
        ProfilePicker.ItemsSource = items;

        // Re-select the previously active profile; fall back to first real profile or "new"
        int idx = _selectedProfile is null
            ? (_profiles.Count > 0 ? 1 : 0)
            : items.FindIndex(i => i.Profile?.Id == _selectedProfile.Id);
        ProfilePicker.SelectedIndex = idx < 0 ? 0 : idx;
    }

    private void ProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilePicker.SelectedItem is not ProfileItem item) return;

        _isEditingProfile = false;

        if (item.Profile is null)
        {
            _selectedProfile           = null;
            DeleteProfileBtn.IsEnabled = false;
            ShowIdentityFields(prefill: false);
        }
        else
        {
            _selectedProfile           = item.Profile;
            DeleteProfileBtn.IsEnabled = true;
            ShowProfileCard(item.Profile);
        }
    }

    private void ShowProfileCard(SemaBuzzProfile profile)
    {
        CardHandleLabel.Text   = profile.Handle;
        CardAvatarEllipse.Fill = profile.AvatarPng is { } png
            ? new ImageBrush(BitmapFromBytes(png)) { Stretch = Stretch.UniformToFill }
            : (Brush)new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        ProfileCard.Visibility    = Visibility.Visible;
        IdentityFields.Visibility = Visibility.Collapsed;
    }

    private void ShowIdentityFields(bool prefill)
    {
        if (prefill && _selectedProfile is { } p)
        {
            HandleBox.Text           = p.Handle;
            AvatarPng                = p.AvatarPng;
            AvatarPreview.Fill       = p.AvatarPng is { } png
                ? new ImageBrush(BitmapFromBytes(png)) { Stretch = Stretch.UniformToFill }
                : (Brush)new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            ClearAvatarBtn.IsEnabled = p.AvatarPng is not null;
        }

        ProfileCard.Visibility    = Visibility.Collapsed;
        IdentityFields.Visibility = Visibility.Visible;
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        _isEditingProfile = true;
        ShowIdentityFields(prefill: true);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var handle = string.IsNullOrWhiteSpace(HandleBox.Text) ? "anonymous" : HandleBox.Text.Trim();

        if (_isEditingProfile && _selectedProfile is not null)
        {
            _selectedProfile.Handle       = handle;
            _selectedProfile.AvatarBase64 = AvatarPng is null ? null : Convert.ToBase64String(AvatarPng);
        }
        else
        {
            var p = new SemaBuzzProfile
            {
                Handle       = handle,
                AvatarBase64 = AvatarPng is null ? null : Convert.ToBase64String(AvatarPng),
            };
            _profiles.Add(p);
            _selectedProfile = p;
        }

        SemaBuzzProfileStore.Save(_profiles);
        _isEditingProfile = false;
        RefreshPicker(); // re-selects _selectedProfile  triggers ShowProfileCard
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null) return;
        _profiles.Remove(_selectedProfile);
        _selectedProfile = null;
        SemaBuzzProfileStore.Save(_profiles);
        RefreshPicker();
    }

    private static BitmapImage BitmapFromBytes(byte[] png)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(png);
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        IsHost = HostMode.IsChecked == true;

        if (IsHost)
        {
            var tok = (HostBuzzAddressBox.Text?.Trim() ?? string.Empty).ToUpperInvariant();
            if (tok.Length is < 4 or > 8)
            {
                MessageBox.Show("No session token — click NEW to generate one.", "SemaBuzz",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RelayToken = tok;
            PeerHost   = string.Empty;
            Port       = 0;
        }
        else
        {
            var raw = BuzzUrlBox.Text.Trim();

            // Allow entering a bare token (e.g. "X7K2QP") without the buzz:// prefix.
            if (!raw.StartsWith("buzz://", StringComparison.OrdinalIgnoreCase)
                && !raw.Contains('.')
                && raw.Length is >= 4 and <= 8)
                raw = $"buzz://{raw}";

            var parsed = SemaBuzzUriHandler.TryParse(raw);
            if (parsed == null)
            {
                MessageBox.Show("Enter a valid Buzz address (e.g. buzz://X7K2QP).", "SemaBuzz",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (parsed.RelayToken is { } relayTok)
            {
                RelayToken = relayTok;
                PeerHost   = string.Empty;
                Port       = 0;
            }
            else
            {
                PeerHost   = parsed.Host;
                Port       = parsed.Port;
                RelayToken = string.Empty;
            }
        }

        // Resolve identity: saved profile card or the live form fields
        if (ProfileCard.Visibility == Visibility.Visible && _selectedProfile is not null)
        {
            Handle    = _selectedProfile.Handle;
            AvatarPng = _selectedProfile.AvatarPng;
        }
        else
        {
            Handle = string.IsNullOrWhiteSpace(HandleBox.Text.Trim()) ? "anonymous" : HandleBox.Text.Trim();
            // AvatarPng already set by ChooseAvatar_Click / ClearAvatar_Click
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
