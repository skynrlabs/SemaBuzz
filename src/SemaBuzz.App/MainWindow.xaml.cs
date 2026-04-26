using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Forms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SemaBuzz.App.Controls;
using SemaBuzz.Protocol;
using EmojiWpf = Emoji.Wpf;

namespace SemaBuzz.App;

/// <summary>
/// MainWindow — the SemaBuzz command center.
/// Coordinates the wire (SemaBuzzClient/Listener), the streamer,
/// and all visual feedback.
/// </summary>
public partial class MainWindow : Window
{
    private SemaBuzzClient?   _client;
    private SemaBuzzListener? _listener;
    private SemaBuzzStreamer  _streamer = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _warmingCts;
    private bool _warmingTimedOut;

    // Spaced display overlay for the inline token input
    private TextBlock? _spacedDisplay;
    // Glow effects that need their Color updated when the theme changes
    private DropShadowEffect? _caretGlow;

    // Track the previous input text so we can detect deletions
    private string _previousInputText = string.Empty;

    // Batch-send queue: packets accumulated for 50 ms before flushing
    private readonly List<SemaBuzzPacket> _pendingPackets = [];
    private DispatcherTimer? _batchTimer;

    // Sequence number tracking for duplicate / out-of-order rejection
    private const int MaxPendingPeerPackets = 8;
    private static readonly TimeSpan PendingPeerResyncDelay = TimeSpan.FromMilliseconds(120);
    private ushort _lastPeerSeq;
    private bool   _peerSeqInitialized;
    private readonly Dictionary<ushort, SemaBuzzPacket> _pendingPeerPackets = [];
    private readonly DispatcherTimer _pendingPeerResyncTimer;

    // Chat row containers (tracked so they can be removed when cleared)
    private Grid?               _peerLiveRow;
    private EmojiWpf.TextBlock? _livePeerBlock;

    // Hosting session params — set when we start listening so we can resume after a peer disconnects
    private string? _hostingToken;
    private string? _hostingRelayUri;
    private int     _hostingPort;

    // Inline connection-approval state
    private TaskCompletionSource<bool>? _approvalTcs;
    private DispatcherTimer?            _approvalTimer;
    private int                         _approvalSecondsLeft;

    // Local identity (set from connect dialog)
    private string  _localHandle    = "anonymous";
    private byte[]? _localAvatarPng;

    // Remote peer identity (received via metadata exchange)
    private string  _peerHandle     = "peer";
    private byte[]? _peerAvatarPng;
    private readonly Forms.NotifyIcon _trayIcon;
    private bool _trayTipShown;

    public MainWindow()
    {
        InitializeComponent();
        _streamer.PacketReady += OnLocalPacketReady;
        _batchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _batchTimer.Tick += FlushPacketBatch;
        _pendingPeerResyncTimer = new DispatcherTimer { Interval = PendingPeerResyncDelay };
        _pendingPeerResyncTimer.Tick += PendingPeerResyncTimer_Tick;
        ApplyIndicatorSettings();
        InputBox.Focus();
        Loaded += (_, _) =>
        {
            ApplyLicenseBanner();
            LoadActiveProfile();
            InlineTokenInput.Focus();
            // Wire the spaced display TextBlock and caret glow from the template
            _spacedDisplay = InlineTokenInput.Template.FindName("SpacedDisplay", InlineTokenInput) as TextBlock;
            var fakeCaret  = InlineTokenInput.Template.FindName("FakeCaret", InlineTokenInput) as Rectangle;
            _caretGlow     = fakeCaret?.Effect as DropShadowEffect;
            UpdateGlowColors();
        };

        SemaBuzzThemeManager.ThemeChanged += UpdateGlowColors;
        SemaBuzzThemeManager.ThemeChanged += RefreshProfileBadge;

        _trayIcon = CreateTrayIcon();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SemaBuzzThemeManager.ApplyChrome(this);
        // Win10 has square window corners — flatten the overlay border to match
        if (Environment.OSVersion.Version.Build < 22000)
            AccentBorderOverlay.CornerRadius = new CornerRadius(0);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Toggle the glyph between maximize and restore glyphs
        if (WindowState == WindowState.Maximized)
            MaximizeGlyph.Text = "\u2750";
        else
            MaximizeGlyph.Text = "\u25A1";
        if (WindowState == WindowState.Maximized)
            MaximizeButton.ToolTip = "Restore";
        else
            MaximizeButton.ToolTip = "Maximize";

        if (WindowState == WindowState.Minimized && App.Settings.MinimizeToTray)
            HideToTray();
    }

    private void WinBtn_Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void WinBtn_Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;
    }

    private void WinBtn_Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("Open SemaBuzz", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Exit", null, (_, _) => Close());

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        var trayIcon = new Forms.NotifyIcon
        {
            Text = "SemaBuzz",
            Visible = false,
            ContextMenuStrip = trayMenu,
        };

        if (File.Exists(iconPath))
            trayIcon.Icon = new System.Drawing.Icon(iconPath);

        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        return trayIcon;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _trayIcon.Visible = true;

        if (_trayTipShown) return;

        _trayIcon.BalloonTipTitle = "SemaBuzz";
        _trayIcon.BalloonTipText = "SemaBuzz is still running in the system tray.";
        _trayIcon.ShowBalloonTip(2500);
        _trayTipShown = true;
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
        _trayIcon.Visible = false;
    }

    // ---------------------------------------------
    // Connection dialog
    // ---------------------------------------------

    // Show the Buzz Code center card so the host can share their code.
    private void ShowBuzzCode(string token)
    {
        BuzzCodeBannerLabel.Text        = string.Join("  ", token.ToUpperInvariant().ToCharArray());
        BuzzIdleState.Visibility        = Visibility.Collapsed;
        BuzzWaitingState.Visibility     = Visibility.Visible;
        BuzzRequestState.Visibility     = Visibility.Collapsed;
        BuzzCodeBanner.Visibility       = Visibility.Visible;
        ChatPanesGrid.Visibility        = Visibility.Collapsed;
        // Allow disconnect while waiting for a peer
        DisconnectMenuItem.IsEnabled    = true;

        if (_hostingRelayUri != null && _hostingRelayUri != SemaBuzzRelayPacket.DefaultRelayUri)
        {
            CustomRelayWarningMsg.Text    = "Custom relay active — your peer must set their relay to:";
            CustomRelayWarningUri.Text    = _hostingRelayUri;
            CustomRelayWarning.Visibility = Visibility.Visible;
        }
        else
        {
            CustomRelayWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void HideBuzzCode()
    {
        // Cancel any pending approval before hiding
        _approvalTimer?.Stop();
        _approvalTimer = null;
        _approvalTcs?.TrySetResult(false);
        _approvalTcs = null;
        BuzzCodeBanner.Visibility = Visibility.Collapsed;
        ChatPanesGrid.Visibility  = Visibility.Visible;
    }

    private void CopyBuzzCode_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_hostingToken))
            Clipboard.SetText(_hostingToken);
        BuzzCodeCopyBtn.Content = "COPIED!";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { BuzzCodeCopyBtn.Content = "COPY"; timer.Stop(); };
        timer.Start();
    }

    private void CancelWait_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts = null;
        FadeToIdle();
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        _approvalTimer?.Stop();
        _approvalTimer = null;
        _approvalTcs?.TrySetResult(true);
        _approvalTcs = null;
        RestoreWaitingState();
    }

    private void Deny_Click(object sender, RoutedEventArgs e)
    {
        _approvalTimer?.Stop();
        _approvalTimer = null;
        _approvalTcs?.TrySetResult(false);
        _approvalTcs = null;
        RestoreWaitingState();
    }

    private void RestoreWaitingState()
    {
        BuzzRequestState.Visibility = Visibility.Collapsed;
        BuzzWaitingState.Visibility = Visibility.Visible;
    }

    // ── Inline idle-state: join or create ────────────────────────────────────

    private void LoadActiveProfile()
    {
        var profiles = SemaBuzzProfileStore.Load();
        var activeId = App.Settings.ActiveProfileId;
        var active   = profiles.FirstOrDefault(p => p.Id == activeId)
                    ?? (profiles.Count > 0 ? profiles[0] : null);
        if (active != null)
        {
            _localHandle    = string.IsNullOrWhiteSpace(active.Handle) ? "anonymous" : active.Handle;
            _localAvatarPng = active.AvatarPng;
        }
        else
        {
            _localHandle    = "anonymous";
            _localAvatarPng = null;
        }
        RefreshProfileBadge();
    }

    private void RefreshProfileBadge()
    {
        ProfileBadgeLabel.Text = _localHandle.ToUpperInvariant();
        if (_localAvatarPng is { } png)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(png);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            ProfileBadgeAvatar.Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }
        else
        {
            ProfileBadgeAvatar.Fill = MakeInitialsBrush(_localHandle, SemaBuzzThemeManager.AccentColor, 22);
        }
    }

    private static ImageBrush MakeInitialsBrush(string handle, Color accent, int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null, new Rect(0, 0, size, size));
            var initial = handle.Length > 0 ? handle[0].ToString().ToUpper() : "?";
            var ft = new FormattedText(initial,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Code"),
                size * 0.44,
                new SolidColorBrush(accent),
                VisualTreeHelper.GetDpi(dv).PixelsPerDip);
            dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
        }
        var rt = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rt.Render(dv);
        rt.Freeze();
        return new ImageBrush(rt) { Stretch = Stretch.None };
    }

    /// <summary>
    /// Sync all DropShadowEffect.Color values that can't use DynamicResource
    /// (Freezable properties) with the current theme accent color.
    /// </summary>
    private void UpdateGlowColors()
    {
        var accent = SemaBuzzThemeManager.AccentColor;
        if (_caretGlow != null)
            _caretGlow.Color = accent;
        if (BuzzCodeBannerLabel.Effect is DropShadowEffect bannerGlow)
            bannerGlow.Color = accent;
    }

    /// <summary>
    /// Fades out the chat panes (if visible) then snaps to the idle connect screen.
    /// Call this from all wire-end paths instead of ShowIdleState() directly.
    /// </summary>
    private void FadeToIdle()
    {
        if (ChatPanesGrid.Visibility == Visibility.Visible)
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) =>
            {
                ChatPanesGrid.BeginAnimation(UIElement.OpacityProperty, null);
                ShowIdleState();
            };
            ChatPanesGrid.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        else
        {
            ShowIdleState();
        }
    }

    private void ShowIdleState()
    {
        _approvalTimer?.Stop();
        _approvalTimer = null;
        _approvalTcs?.TrySetResult(false);
        _approvalTcs = null;
        InlineTokenInput.Text        = string.Empty;
        if (_spacedDisplay != null) _spacedDisplay.Text = string.Empty;
        InlineConnectBtn.IsEnabled   = false;
        BuzzIdleState.Visibility     = Visibility.Visible;
        BuzzWaitingState.Visibility  = Visibility.Collapsed;
        BuzzRequestState.Visibility  = Visibility.Collapsed;
        BuzzCodeBanner.Visibility    = Visibility.Visible;
        ChatPanesGrid.Visibility     = Visibility.Collapsed;
        ChatPanesGrid.Opacity        = 1;   // reset after any fade
        DisconnectMenuItem.IsEnabled = false;
        ClearChatPanels();                      // no wire → no chat
        InlineTokenInput.Focus();
    }

    private void InlineStartBuzz_Click(object sender, RoutedEventArgs e)
    {
        LoadActiveProfile();
        LocalPaneLabel.Text = _localHandle.ToUpperInvariant();
        var token    = SemaBuzzRelayPacket.GenerateToken();
        var relayUri = App.Settings.RelayUri;
        if (_cts != null) _cts.Cancel();
        _cts = new CancellationTokenSource();
        StartListeningViaRelay(token, relayUri, _cts.Token);
        ShowBuzzCode(token);
    }

    private void InlineConnect_Click(object sender, RoutedEventArgs e)
    {
        var token = InlineTokenInput.Text.Trim().ToUpperInvariant();
        if (token.Length != 6) return;
        LoadActiveProfile();
        LocalPaneLabel.Text = _localHandle.ToUpperInvariant();
        if (_cts != null) _cts.Cancel();
        _cts = new CancellationTokenSource();
        BuzzCodeBanner.Visibility = Visibility.Collapsed;
        ChatPanesGrid.Visibility  = Visibility.Visible;
        ClearChatPanels();
        StartConnectingViaRelay(token, App.Settings.RelayUri, _cts.Token);
    }

    private void InlineTokenInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Only allow alphanumeric; block everything else
        e.Handled = !e.Text.All(c => char.IsLetterOrDigit(c));
    }

    private void InlineTokenInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && InlineConnectBtn.IsEnabled)
        {
            InlineConnect_Click(this, e);
            e.Handled = true;
        }
        else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Let default paste happen but we coerce it in TextChanged
        }
    }

    private void InlineTokenInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Coerce: uppercase alphanumeric only, max 6
        var raw = InlineTokenInput.Text;
        var coerced = new string(raw.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(6).ToArray());
        if (coerced != raw)
        {
            InlineTokenInput.TextChanged -= InlineTokenInput_TextChanged;
            InlineTokenInput.Text = coerced;
            InlineTokenInput.CaretIndex = coerced.Length;
            InlineTokenInput.TextChanged += InlineTokenInput_TextChanged;
        }
        // Sync the spaced display (thin-space \u2009 between each char for visual letter-spacing)
        if (_spacedDisplay != null)
            _spacedDisplay.Text = string.Join("\u2009\u2009", coerced.ToCharArray());
        InlineConnectBtn.IsEnabled = coerced.Length == 6;
    }

    // ---------------------------------------------
    // Menu handlers
    // ---------------------------------------------

    private async void Wire_Disconnect_Click(object sender, RoutedEventArgs e)
    {
        // Clear hosting params first so the Dead handler doesn't auto-resume
        _hostingToken    = null;
        _hostingRelayUri = null;
        _hostingPort     = 0;
        if (_client   != null) await _client.DisconnectAsync();
        if (_listener != null) await _listener.DisconnectAsync();
        if (_cts != null)
            _cts.Cancel();
        _cts      = null;
        _client   = null;
        _listener = null;
        // Update UI immediately — Dead event may not fire when cancelling while waiting
        ResetToIdle();
        FadeToIdle();
    }

    private void Wire_AlwaysOnTop_Click(object sender, RoutedEventArgs e)
        => Topmost = AlwaysOnTopMenuItem.IsChecked;

    private void Wire_Exit_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    private void View_ClearChat_Click(object sender, RoutedEventArgs e)
        => ClearChatPanels();

    private void ClearChatPanels()
    {
        LocalPanel.Children.Clear();
        PeerPanel.Children.Clear();
        _peerLiveRow       = null;
        _livePeerBlock     = null;
        _previousInputText = string.Empty;
    }

    //  SETTINGS menu

    private void Settings_Themes_Click(object sender, RoutedEventArgs e)
    {
        var originalTheme = SemaBuzzThemeManager.Current;
        var dlg = new SemaBuzzThemeDialog { Owner = this };
        if (dlg.ShowDialog() != true)
        {
            // Cancelled  revert to what was active before
            SemaBuzzThemeManager.Apply(originalTheme);
            return;
        }
        App.Settings.Theme = SemaBuzzThemeManager.Current;
        App.Settings.Save();
    }

    private void Settings_Profiles_Click(object sender, RoutedEventArgs e)
        => OpenProfilesDialog();

    private void ProfileBadge_Click(object sender, RoutedEventArgs e)
        => OpenProfilesDialog();

    private void OpenProfilesDialog()
    {
        new SemaBuzzProfilesDialog { Owner = this }.ShowDialog();
        LoadActiveProfile();
    }

    private void Settings_Preferences_Click(object sender, RoutedEventArgs e)
    {
        bool buzzWaiting = BuzzWaitingState.Visibility == Visibility.Visible;
        var dlg = new SemaBuzzSettingsDialog(lockRelay: buzzWaiting) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        App.Settings.DefaultListenPort    = dlg.SelectedDefaultListenPort;
        App.Settings.IndicatorSensitivity = dlg.SelectedIndicatorSensitivity;
        App.Settings.IndicatorStyle       = dlg.SelectedIndicatorStyle;
        App.Settings.ChatFontSize         = dlg.SelectedChatFontSize;
        App.Settings.LivePreview          = dlg.SelectedLivePreview;
        App.Settings.MinimizeToTray       = dlg.SelectedMinimizeToTray;
        App.Settings.StartWithWindows     = dlg.SelectedStartWithWindows;
        App.Settings.AutoApprove          = dlg.SelectedAutoApprove;
        App.Settings.BuzzSoundEnabled     = dlg.SelectedBuzzSoundEnabled;
        App.Settings.BuzzSoundVolume      = dlg.SelectedBuzzSoundVolume;
        App.Settings.RelayUri             = dlg.SelectedRelayUri;
        App.Settings.Save();

        SemaBuzzStartup.Apply(App.Settings.StartWithWindows);
        ApplyIndicatorSettings();
    }

    //  HELP menu

    private void Help_About_Click(object sender, RoutedEventArgs e)
        => new SemaBuzzAboutDialog { Owner = this }.ShowDialog();

    private void Help_FAQ_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://semabuzz.me/faq") { UseShellExecute = true });

    private void Help_NewsUpdates_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://x.com/semabuzzlive") { UseShellExecute = true });

    /// <summary>Push sensitivity and style from saved settings to the live indicator control.</summary>
    private void ApplyIndicatorSettings()
    {
        BuzzIndicator.Sensitivity     = App.Settings.IndicatorSensitivity;
        BuzzIndicator.IndicatorStyle = App.Settings.IndicatorStyle;
    }

    private void StartListening(int port, CancellationToken ct, bool clearChat = true)
    {
        _hostingToken    = null;
        _hostingRelayUri = null;
        _hostingPort     = port;
        if (clearChat) ClearChatPanels();
        _listener = new SemaBuzzListener();
        _listener.PacketReceived             += OnRemotePacketReceived;
        _listener.WireStateChanged           += OnWireStateChanged;
        _listener.MetadataReceived           += OnMetadataReceived;
        _listener.UrlPushReceived            += OnUrlPushReceived;
        _listener.ConnectionApprovalCallback  = OnConnectionApprovalRequested;

        SetStatus($"› listening on port {port}...");
        _ = _listener.ListenAsync(port, ct);
    }

    private void StartListeningViaRelay(string token, string relayUri, CancellationToken ct, bool clearChat = true)
    {
        _hostingToken    = token;
        _hostingRelayUri = relayUri;
        _hostingPort     = 0;
        if (clearChat) ClearChatPanels();
        _listener = new SemaBuzzListener();
        _listener.PacketReceived             += OnRemotePacketReceived;
        _listener.WireStateChanged           += OnWireStateChanged;
        _listener.MetadataReceived           += OnMetadataReceived;
        _listener.UrlPushReceived            += OnUrlPushReceived;
        _listener.ConnectionApprovalCallback  = OnConnectionApprovalRequested;

        SetStatus($"› waiting via relay (token: {token}) via {relayUri}...");
        _ = _listener.ListenViaRelayAsync(
            relayUri,
            token, ct);
    }

    private void StartConnecting(string host, int port, CancellationToken ct)
    {
        ClearChatPanels();
        _client = new SemaBuzzClient();
        _client.PacketReceived      += OnRemotePacketReceived;
        _client.WireStateChanged    += OnWireStateChanged;
        _client.MetadataReceived    += OnMetadataReceived;
        _client.UrlPushReceived     += OnUrlPushReceived;

        SetStatus($"› dialing {host}:{port}...");
        _ = _client.ConnectAsync(host, port, ct);
    }

    private void StartConnectingViaRelay(string token, string relayUri, CancellationToken ct)
    {
        ClearChatPanels();
        _client = new SemaBuzzClient();
        _client.PacketReceived      += OnRemotePacketReceived;
        _client.WireStateChanged    += OnWireStateChanged;
        _client.MetadataReceived    += OnMetadataReceived;
        _client.UrlPushReceived     += OnUrlPushReceived;

        SetStatus($"› joining relay room {token} via {relayUri}...");
        _ = _client.ConnectViaRelayAsync(
            relayUri,
            token, ct);
    }

    // ---------------------------------------------
    // Input handling -- Live-Wire typing
    // ---------------------------------------------

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && InputBox.IsEnabled)
        {
            e.Handled = true;
            CommitLine();
        }
        else if (e.Key == Key.Escape)
        {
            InputBox.Text = string.Empty;
            _previousInputText = string.Empty;
            e.Handled = true;
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (InputBox.IsEnabled && !string.IsNullOrEmpty(InputBox.Text))
            CommitLine();
        InputBox.Focus();
    }

    private async void BuzzButton_Click(object sender, RoutedEventArgs e)
    {
        // Send the Buzz packet to the peer
        if (_client   != null) await _client.SendBuzzAsync();
        if (_listener != null) await _listener.SendBuzzAsync();

        // Also pulse our own filament so the sender feels it
        PlayBuzzSound();
        BuzzIndicator.MaxBurst();
        InputBox.Focus();
    }

    private async void WalkButton_Click(object sender, RoutedEventArgs e)
    {
        // If the input box already has a URL, use it; otherwise prompt
        var preText = InputBox.Text?.Trim() ?? string.Empty;
        string url;
        if (Uri.TryCreate(preText, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            url = preText;
        }
        else
        {
            var dlg = new WalkUrlDialog { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.Url)) return;
            url = dlg.Url;
        }

        // Send to peer
        if (_client   != null) await _client.SendUrlPushAsync(url);
        if (_listener != null) await _listener.SendUrlPushAsync(url);

        // Clear input box if that's where the URL came from
        if (InputBox.Text?.Trim() == url)
            InputBox.Text = string.Empty;

        // Render sent card in local pane
        AppendUrlCard(url, isSent: true, LocalPanel, LocalScrollViewer);

        InputBox.Focus();
    }

    private void OnUrlPushReceived(object? sender, SemaBuzzUrlPushEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AppendUrlCard(e.Url, isSent: false, PeerPanel, PeerScrollViewer);
            ShowToastIfUnfocused(_peerHandle, $"🔗 {e.Url}");
        });
    }

    private void AppendUrlCard(string url, bool isSent, Panel panel, ScrollViewer scrollViewer)
    {
        var handle   = isSent ? _localHandle : _peerHandle;
        var avatar   = isSent ? _localAvatarPng : _peerAvatarPng;
        var nameColor = isSent ? SemaBuzzThemeManager.AccentColor : Color.FromRgb(0x9E, 0x9E, 0x9E);
        var accentKey = isSent ? "AmberBrush" : (string?)null;

        // Header row (handle + label)
        var (headerRow, headerTb) = MakeChatLine(handle, avatar, nameColor, accentKey);
        headerTb.Text = (string)headerTb.Tag + "shared a link";
        panel.Children.Add(headerRow);

        // Card border
        var card = new Border
        {
            Margin          = new Thickness(40, 2, 0, 6),
            Padding         = new Thickness(12, 10, 12, 10),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
        };
        card.SetResourceReference(Border.BorderBrushProperty, "ObsidianBorderBrush");
        card.SetResourceReference(Border.BackgroundProperty, "InputBackgroundBrush");

        var cardStack = new StackPanel { Orientation = Orientation.Vertical };

        // URL text (truncated for display)
        var urlDisplay = url.Length > 80 ? url[..80] + "…" : url;
        var urlText = new TextBlock
        {
            Text         = urlDisplay,
            FontFamily   = new FontFamily("Cascadia Code, JetBrains Mono, Consolas"),
            FontSize     = App.Settings.ChatFontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 8),
        };
        urlText.SetResourceReference(TextBlock.ForegroundProperty, "AmberBrush");
        cardStack.Children.Add(urlText);

        // OPEN button
        var openBtn = new Button
        {
            Content             = "OPEN",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding             = new Thickness(14, 4, 14, 4),
        };
        openBtn.SetResourceReference(Button.StyleProperty, "SemaBuzzButton");
        var capturedUrl = url;
        openBtn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(capturedUrl) { UseShellExecute = true }); }
            catch { }
        };
        cardStack.Children.Add(openBtn);

        card.Child = cardStack;
        panel.Children.Add(card);
        scrollViewer.ScrollToEnd();
    }

    private void EmoticonPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!InputBox.IsEnabled || EmoticonPickerButton.ContextMenu == null)
            return;

        EmoticonPickerButton.ContextMenu.PlacementTarget = EmoticonPickerButton;
        EmoticonPickerButton.ContextMenu.IsOpen = true;
    }

    private void EmoticonButton_Click(object sender, RoutedEventArgs e)
    {
        if (!InputBox.IsEnabled)
            return;

        var emoticon = sender switch
        {
            Button { Tag: string buttonTag } => buttonTag,
            MenuItem { Tag: string menuTag } => menuTag,
            _ => null,
        };

        if (string.IsNullOrEmpty(emoticon))
            return;

        var range = new TextRange(InputBox.CaretPosition, InputBox.CaretPosition);
        range.Text = emoticon;
        InputBox.CaretPosition = range.End;
        InputBox.Focus();
    }

    private static Brush LoadAvatarBrush(byte[] avatarPng)
    {
        try
        {
            using var ms = new MemoryStream(avatarPng);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource      = ms;
            bmp.CacheOption       = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth  = 28;
            bmp.DecodePixelHeight = 28;
            bmp.EndInit();
            bmp.Freeze();
            return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }
        catch { return Brushes.Transparent; }
    }

    private void CommitLine()
    {
        var msg = InputBox.Text;
        if (string.IsNullOrEmpty(msg)) return;

        // When LivePreview is off the streamer hasn't seen the chars yet — feed them now.
        if (!App.Settings.LivePreview)
            foreach (var c in msg)
                _streamer.Feed(c);

        // Add the committed row to the local pane
        var (row, tb) = MakeChatLine(_localHandle, _localAvatarPng, SemaBuzzThemeManager.AccentColor, "AmberBrush");
        tb.Text = (string)tb.Tag + msg;
        HyperlinkifyTextBlock(tb);
        LocalPanel.Children.Add(row);
        LocalScrollViewer.ScrollToEnd();

        // Clear the box.
        // Set _previousInputText to "" BEFORE Clear() so TextChanged
        // sees no length change and doesn't send spurious backspaces.
        _previousInputText = string.Empty;
        InputBox.Text = string.Empty;
        SendButton.IsEnabled = false;

        // Send a newline packet with the next outbound sequence number so the
        // peer's duplicate filter does not drop the sentence commit.
        var nlPacket = new SemaBuzzPacket('\n', 0, SemaBuzzPacketType.Char, _streamer.NextSequence());
        if (_client   != null) _ = _client.SendAsync(nlPacket);
        if (_listener != null) _ = _listener.SendAsync(nlPacket);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        // Emoji.Wpf.RichTextBox updates its Text DP *after* firing base.OnTextChanged,
        // so InputBox.Text is still the previous value when TextChanged fires.
        // Defer to the next dispatcher frame so we read the correct updated Text.
        => Dispatcher.InvokeAsync(ProcessInputBoxTextChange);

    private void ProcessInputBoxTextChange()
    {
        var text = InputBox.Text;
        var prev = _previousInputText;
        _previousInputText = text;

        // Enable the send button only when there is something to send
        SendButton.IsEnabled = InputBox.IsEnabled && text.Length > 0;

        // When LivePreview is off, don't stream keystrokes live
        if (!App.Settings.LivePreview) return;

        if (text.Length < prev.Length)
        {
            // Characters were deleted — send a backspace for each one removed
            var deleted = prev.Length - text.Length;
            for (var i = 0; i < deleted; i++)
                _streamer.Feed('\b');
        }
        else if (text.Length > prev.Length)
        {
            // Characters were added — feed each new character
            for (var i = prev.Length; i < text.Length; i++)
                _streamer.Feed(text[i]);
        }
    }

    // Called by the streamer - fires on UI thread already
    private void OnLocalPacketReady(object? sender, SemaBuzzPacketEventArgs e)
    {
        // Queue for batch send (flushed every 50 ms by _batchTimer)
        _pendingPackets.Add(e.Packet);
        if (!_batchTimer!.IsEnabled) _batchTimer.Start();
    }

    private async void FlushPacketBatch(object? sender, EventArgs e)
    {
        _batchTimer!.Stop();
        if (_pendingPackets.Count == 0) return;
        var batch = _pendingPackets.ToArray();
        _pendingPackets.Clear();
        if (_client   != null) await _client.SendBatchAsync(batch);
        if (_listener != null) await _listener.SendBatchAsync(batch);
    }

    // ---------------------------------------------
    // Connection approval (host must accept dial-in)
    // ---------------------------------------------

    private async Task<bool> OnConnectionApprovalRequested(System.Net.IPEndPoint remote)
    {
        if (App.Settings.AutoApprove)
            return true;

        _approvalTcs = new TaskCompletionSource<bool>();
        await Dispatcher.InvokeAsync(() =>
        {
            SemaBuzzConnectRequestDialog.PlayRequestSoundOnce();
            if (!IsVisible)
                RestoreFromTray();
            else if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();

            BuzzRequestFromLabel.Text = $"Peer at  {remote}  wants to open a wire.";
            _approvalSecondsLeft      = 30;
            BuzzRequestCountdown.Text = $"Auto-declining in {_approvalSecondsLeft}s...";
            BuzzWaitingState.Visibility  = Visibility.Collapsed;
            BuzzRequestState.Visibility  = Visibility.Visible;

            _approvalTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _approvalTimer.Tick += (_, _) =>
            {
                _approvalSecondsLeft--;
                BuzzRequestCountdown.Text = $"Auto-declining in {_approvalSecondsLeft}s...";
                if (_approvalSecondsLeft <= 0)
                {
                    _approvalTimer?.Stop();
                    _approvalTimer = null;
                    _approvalTcs?.TrySetResult(false);
                    _approvalTcs = null;
                    RestoreWaitingState();
                }
            };
            _approvalTimer.Start();
        });
        return await _approvalTcs.Task;
    }

    // ---------------------------------------------
    // Remote packet received
    // ---------------------------------------------

    private void OnRemotePacketReceived(object? sender, SemaBuzzPacketEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Packet.Type == SemaBuzzPacketType.Buzz)
            {
                PlayBuzzSound();
                BuzzIndicator.MaxBurst();
                ShakeWindow();
                ShowToastIfUnfocused(_peerHandle, "Buzzed you!");
                return;
            }

            // Sequence-number duplicate / reorder check for Char packets
            if (e.Packet.Type == SemaBuzzPacketType.Char)
            {
                if (!_peerSeqInitialized)
                {
                    _lastPeerSeq        = e.Packet.SeqNum;
                    _peerSeqInitialized = true;
                    RenderPeerPacket(e.Packet);
                    FlushPendingPeerPackets();
                    return;
                }

                var d = (ushort)(e.Packet.SeqNum - _lastPeerSeq);

                if (d == 0) return;         // exact duplicate
                if (d > 0x8000) return;     // older packet that arrived late

                if (d == 1)
                {
                    _lastPeerSeq = e.Packet.SeqNum;
                    RenderPeerPacket(e.Packet);
                    FlushPendingPeerPackets();
                    return;
                }

                // Packet arrived ahead of one or more earlier chars. Buffer it and
                // wait for the missing sequence(s) so fast typing stays in order.
                _pendingPeerPackets.TryAdd(e.Packet.SeqNum, e.Packet);
                RestartPendingPeerResyncTimer();
                if (_pendingPeerPackets.Count >= MaxPendingPeerPackets)
                    ResyncPendingPeerPackets();
                return;
            }

            RenderPeerPacket(e.Packet);
        });
    }

    private void RenderPeerPacket(SemaBuzzPacket packet)
    {
        BuzzIndicator.Pulse(packet.Intensity);
        AppendPeerCharacter(packet.Character);
    }

    private void FlushPendingPeerPackets()
    {
        while (true)
        {
            var nextSeq = (ushort)(_lastPeerSeq + 1);
            if (!_pendingPeerPackets.Remove(nextSeq, out var packet))
            {
                if (_pendingPeerPackets.Count == 0)
                    _pendingPeerResyncTimer.Stop();
                return;
            }

            _lastPeerSeq = nextSeq;
            RenderPeerPacket(packet);
        }
    }

    private void ResyncPendingPeerPackets()
    {
        _pendingPeerResyncTimer.Stop();

        if (_pendingPeerPackets.Count == 0)
            return;

        // One missing packet should not permanently stall the remote transcript.
        // When the small reorder buffer fills up, resume from the earliest packet
        // we do have and continue rendering from there.
        var nextAvailableSeq = _pendingPeerPackets.Keys.Min();
        _lastPeerSeq = (ushort)(nextAvailableSeq - 1);
        FlushPendingPeerPackets();

        if (_pendingPeerPackets.Count > 0)
            RestartPendingPeerResyncTimer();
    }

    private void RestartPendingPeerResyncTimer()
    {
        _pendingPeerResyncTimer.Stop();
        _pendingPeerResyncTimer.Start();
    }

    private void PendingPeerResyncTimer_Tick(object? sender, EventArgs e)
    {
        _pendingPeerResyncTimer.Stop();
        ResyncPendingPeerPackets();
    }

    // ---------------------------------------------
    // Wire state changes
    // ---------------------------------------------

    /// <summary>Reset all UI chrome back to the cold/idle state.</summary>
    private void ResetToIdle()
    {
        SetStatus("› wire is cold");
        TitleSessionLabel.Text         = "NO WIRE";
        DisconnectMenuItem.IsEnabled   = false;
        InputBox.IsEnabled             = false;
        SendButton.IsEnabled           = false;        BuzzButton.IsEnabled           = false;        WalkButton.IsEnabled           = false;        _peerLiveRow                   = null;
        _livePeerBlock                 = null;
        _peerHandle                    = "peer";
        _peerAvatarPng                 = null;
        PeerLabel.Text                 = string.Empty;
        UpdateWireStateDot(SemaBuzzWireState.Cold);
        BuzzIndicator.Flatline();
        ClearChatMenuItem.IsEnabled    = false;
        _peerSeqInitialized = false;
        _pendingPeerPackets.Clear();
        _pendingPeerResyncTimer.Stop();
    }

    private void OnWireStateChanged(object? sender, SemaBuzzWireStateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            string statusText;
            if (e.Message != null)
                statusText = e.Message;
            else
                statusText = e.State.ToString().ToLower();
            SetStatus($"› {statusText}");
            UpdateWireStateDot(e.State);

            if (e.State == SemaBuzzWireState.Warming)
            {
                if (InputBox.IsEnabled)
                {
                    // We were in a live chat — peer disconnected. Tear down and go to idle.
                    PlayErrorSound();
                    TitleSessionLabel.Text       = "NO WIRE";
                    InputBox.IsEnabled           = false;
                    SendButton.IsEnabled         = false;
                    BuzzButton.IsEnabled         = false;
                    WalkButton.IsEnabled         = false;
                    _peerLiveRow                 = null;
                    _livePeerBlock               = null;
                    var savedHandle2             = _peerHandle;
                    _peerHandle                  = "peer";
                    _peerAvatarPng               = null;
                    PeerLabel.Text               = string.Empty;
                    BuzzIndicator.Flatline();
                    AddChatDivider($"× {savedHandle2} disconnected");
                    _hostingToken    = null;
                    _hostingRelayUri = null;
                    _hostingPort     = 0;
                    if (_cts != null) _cts.Cancel();
                    _cts = null;
                    _warmingCts?.Cancel();
                    _warmingCts = null;
                    ResetToIdle();
                    FadeToIdle();
                }
                // else: Warming during relay handshake / waiting — status bar already
                // updated above; leave the waiting screen as-is.
            }
            else if (e.State is SemaBuzzWireState.Live or SemaBuzzWireState.Secured)
            {
                // Hide the buzz code banner and reveal the chat panes
                HideBuzzCode();
                if (_warmingCts != null)
                    _warmingCts.Cancel();
                _warmingCts = null;
                string stateTag;
                if (e.State == SemaBuzzWireState.Secured)
                    stateTag = "[ENC] ";
                else
                    stateTag = "";
                TitleSessionLabel.Text = $"{stateTag}WIRE LIVE";
                InputBox.IsEnabled   = true;
                SendButton.IsEnabled = false; // no text yet
                BuzzButton.IsEnabled = true;
                WalkButton.IsEnabled = true;
                InputBox.Focus();
                DisconnectMenuItem.IsEnabled = true;
                ClearChatMenuItem.IsEnabled  = true;
                string wireDivider;
                if (e.State == SemaBuzzWireState.Secured)
                    wireDivider = "› sema secured · wire is live";
                else
                    wireDivider = "› wire is live";
                AddChatDivider(wireDivider);

                // Exchange identity with the peer
                if (_client   != null) _ = _client.SendMetadataAsync(_localHandle, _localAvatarPng);
                if (_listener != null) _ = _listener.SendMetadataAsync(_localHandle, _localAvatarPng);
            }
            else if (e.State == SemaBuzzWireState.Dead)
            {
                // Cancel any pending inline approval
                _approvalTimer?.Stop();
                _approvalTimer = null;
                _approvalTcs?.TrySetResult(false);
                _approvalTcs = null;

                PlayErrorSound();
                if (_warmingCts != null)
                    _warmingCts.Cancel();
                _warmingCts = null;
                TitleSessionLabel.Text       = "NO WIRE";
                InputBox.IsEnabled           = false;
                SendButton.IsEnabled         = false;
                BuzzButton.IsEnabled         = false;
                _peerLiveRow                 = null;
                _livePeerBlock               = null;
                var savedHandle              = _peerHandle;
                _peerHandle                  = "peer";
                _peerAvatarPng               = null;
                PeerLabel.Text               = string.Empty;
                BuzzIndicator.Flatline();
                DisconnectMenuItem.IsEnabled = false;
                ClearChatMenuItem.IsEnabled  = false;

                string divider;
                if (_warmingTimedOut)
                {
                    divider = "› no dialer arrived · session closed after 5 minutes";
                }
                else
                {
                    divider = e.Message switch
                    {
                        "peer-disconnect" => $"× {savedHandle} disconnected · wire has been closed",
                        "not-available"  => $"× {savedHandle} is not available at this time",
                        _                => "× wire is dead",
                    };
                }
                _warmingTimedOut = false;
                AddChatDivider(divider);

                // Always return to the connect screen
                _hostingToken    = null;
                _hostingRelayUri = null;
                _hostingPort     = 0;
                FadeToIdle();
            }
        });
    }

    /// <summary>
    /// Waits 5 minutes while in Warming state. If no dialer arrives, cancels the
    /// session and shows a friendly timeout message in the chat.
    /// </summary>
    private async Task StartWarmingTimeoutAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(5), ct); }
        catch (OperationCanceledException) { return; }

        // Timeout fired  cancel the main CTS so the relay loop closes,
        // which raises WireStateChanged(Dead). Set the flag so the Dead
        // handler shows the right divider. Clear hosting params so we don't auto-resume.
        _warmingTimedOut = true;
        _hostingToken    = null;
        _hostingRelayUri = null;
        _hostingPort     = 0;
        if (_cts != null)
            _cts.Cancel();
        _cts = null;
    }

    // ---------------------------------------------
    // Chat rendering
    // ---------------------------------------------

    private void OnMetadataReceived(object? sender, SemaBuzzMetadataEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _peerHandle    = e.Handle;
            _peerAvatarPng = e.AvatarPng;
            PeerLabel.Text     = e.Handle;
            PeerPaneLabel.Text = e.Handle.ToUpperInvariant();
        });
    }

    /// <summary>
    /// Jitter the window left/right rapidly to simulate a physical buzz.
    /// Uses a DoubleAnimation on Window.Left so it cleans itself up automatically.
    /// </summary>
    private void ShakeWindow()
    {
        var origin = Left;
        const double magnitude = 8.0;
        const int    steps     = 8;
        var duration = TimeSpan.FromMilliseconds(40);

        var anim = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(steps * 40)) };
        for (var i = 0; i < steps; i++)
        {
            double sign;
            if (i % 2 == 0)
                sign = magnitude;
            else
                sign = -magnitude;
            var offset = sign * (1.0 - i / (double)steps);
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(origin + offset, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 40))));
        }
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(origin, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(steps * 40))));
        BeginAnimation(LeftProperty, anim);
    }

    private void AppendPeerCharacter(char ch)
    {
        // '\n' commits a live line -- if there's no live line, nothing to freeze.
        // '\b' removes a character -- if there's no live line, nothing to delete.
        // Creating a row just to immediately freeze/no-op it would leave an empty row.
        if (_livePeerBlock == null && ch is '\n' or '\b') return;

        if (_livePeerBlock == null)
        {
            var (row, tb) = MakeChatLine(_peerHandle, _peerAvatarPng, Color.FromRgb(0x9E, 0x9E, 0x9E));
            _peerLiveRow   = row;
            _livePeerBlock = tb;
            PeerPanel.Children.Add(_peerLiveRow);
        }

        if (ch == '\b')
        {
            var prefix = (string)_livePeerBlock.Tag;
            if (_livePeerBlock.Text.Length > prefix.Length)
                _livePeerBlock.Text = _livePeerBlock.Text[..^1];

            // All text removed -- remove the row
            if (_livePeerBlock.Text == prefix)
            {
                PeerPanel.Children.Remove(_peerLiveRow);
                _peerLiveRow   = null;
                _livePeerBlock = null;
                return;
            }
        }
        else if (ch == '\n')
        {
            if (_livePeerBlock != null)
            {
                // Capture the message text before HyperlinkifyTextBlock switches
                // the TextBlock from .Text to .Inlines mode
                var prefix  = (string)_livePeerBlock.Tag;
                string msgText;
                if (_livePeerBlock.Text.Length > prefix.Length)
                    msgText = _livePeerBlock.Text[prefix.Length..];
                else
                    msgText = string.Empty;
                HyperlinkifyTextBlock(_livePeerBlock);
                ShowToastIfUnfocused(_peerHandle, msgText);

            }
            _peerLiveRow   = null;
            _livePeerBlock = null;
            return;
        }
        else
        {
            _livePeerBlock.Text += ch;
        }
        PeerScrollViewer.ScrollToEnd();
    }

    /// <summary>
    /// Returns a Grid row containing an avatar Ellipse (or initials fallback) and a TextBlock.
    /// TextBlock.Tag stores the prefix string so we can clear content back to it.
    /// Pass <paramref name="accentResourceKey"/> (e.g. "AmberBrush") to bind Foreground as a
    /// DynamicResource so the text recolors automatically when the theme changes.
    /// </summary>
    private static (Grid Row, EmojiWpf.TextBlock TextBlock) MakeChatLine(
        string handle, byte[]? avatarPng, Color nameColor, string? accentResourceKey = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Avatar circle
        var ellipse = new Ellipse
        {
            Width  = 28, Height = 28,
            Stroke = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            StrokeThickness = 1,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };

        if (avatarPng is { Length: > 0 })
        {
            try
            {
                using var ms  = new MemoryStream(avatarPng);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource     = ms;
                bmp.CacheOption      = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth  = 28;
                bmp.DecodePixelHeight = 28;
                bmp.EndInit();
                bmp.Freeze();
                ellipse.Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            catch
            {
                ellipse.Fill = InitialsBrush(handle, nameColor);
            }
        }
        else
        {
            ellipse.Fill = InitialsBrush(handle, nameColor);
        }

        Grid.SetColumn(ellipse, 0);
        grid.Children.Add(ellipse);

        // Text area
        var prefix = $"{handle} \u203a ";
        var tb = new EmojiWpf.TextBlock
        {
            Text         = prefix,
            Tag          = prefix,
            Foreground   = new SolidColorBrush(nameColor),
            FontFamily   = new FontFamily("Cascadia Code, JetBrains Mono, Consolas"),
            FontSize     = App.Settings.ChatFontSize,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            Margin       = new Thickness(4, 2, 0, 0),
        };
        if (accentResourceKey != null)
            tb.SetResourceReference(TextBlock.ForegroundProperty, accentResourceKey);

        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        return (grid, tb);
    }

    // ---------------------------------------------
    // Windows toast notifications (unfocused only)
    // ---------------------------------------------

    private void ShowToastIfUnfocused(string handle, string body)
    {
        if (IsActive || string.IsNullOrWhiteSpace(body)) return;
        try
        {
            new ToastContentBuilder()
                .AddText(handle)
                .AddText(body)
                .Show();
        }
        catch { /* silently ignore if toasts are unavailable */ }
    }

    // ---------------------------------------------
    // URL detection & hyperlinking
    // ---------------------------------------------

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Scans a committed TextBlock's plain text for http(s):// URLs and
    /// replaces it with a mix of Runs and clickable Hyperlinks.
    /// Must be called before the TextBlock reference is released (committed).
    /// </summary>
    private static void HyperlinkifyTextBlock(EmojiWpf.TextBlock tb)
    {
        var fullText = tb.Text;
        var matches  = UrlRegex.Matches(fullText);
        if (matches.Count == 0) return; // no URLs -- leave as plain text

        // Switching to Inlines mode clears .Text automatically
        tb.Inlines.Clear();

        var pos = 0;
        foreach (Match m in matches)
        {
            // Plain text before this URL
            if (m.Index > pos)
                tb.Inlines.Add(new Run(fullText[pos..m.Index]));

            // Strip trailing punctuation unlikely to be part of the URL
            var urlText  = m.Value.TrimEnd('.', ',', ')', ']', '\'', '"', '>');
            var trailing = m.Value[urlText.Length..];

            if (Uri.TryCreate(urlText, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var link = new Hyperlink(new Run(urlText))
                {
                    NavigateUri     = uri,
                    Foreground      = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x54)),
                    TextDecorations = TextDecorations.Underline,
                    Cursor          = Cursors.Hand,
                };
                link.RequestNavigate += (_, e) =>
                {
                    // H-3: confirm before opening a peer-supplied URL to prevent phishing.
                    var url = e.Uri.AbsoluteUri;
                    var answer = MessageBox.Show(
                        $"Open this link in your browser?\n\n{url}",
                        "Open Link",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Yes)
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    e.Handled = true;
                };
                tb.Inlines.Add(link);
            }
            else
            {
                tb.Inlines.Add(new Run(urlText));
            }

            if (trailing.Length > 0)
                tb.Inlines.Add(new Run(trailing));

            pos = m.Index + m.Length;
        }

        // Any plain text after the last URL
        if (pos < fullText.Length)
            tb.Inlines.Add(new Run(fullText[pos..]));
    }

    private static Brush InitialsBrush(string handle, Color nameColor)    {
        string initials;
        if (handle.Length > 0)
            initials = handle[0].ToString().ToUpper();
        else
            initials = "?";
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x33, nameColor.R, nameColor.G, nameColor.B)),
                null, new Rect(0, 0, 28, 28));
            var ft = new FormattedText(
                initials,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Code"),
                14, new SolidColorBrush(nameColor),
                96);
            dc.DrawText(ft, new System.Windows.Point((28 - ft.Width) / 2, (28 - ft.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(28, 28, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return new ImageBrush(rtb) { Stretch = Stretch.Fill };
    }

    private void AddChatDivider(string message)
    {
        // Commit any live blocks as finalized
        _peerLiveRow       = null;
        _livePeerBlock     = null;
        _previousInputText = string.Empty;

        var makeDiv = () => new TextBlock
        {
            Text       = message,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
            FontSize   = 11,
            FontStyle  = FontStyles.Italic,
            Margin     = new Thickness(0, 8, 0, 8),
        };
        LocalPanel.Children.Add(makeDiv());
        PeerPanel.Children.Add(makeDiv());
        LocalScrollViewer.ScrollToEnd();
        PeerScrollViewer.ScrollToEnd();
    }

    // ---------------------------------------------
    // Status helpers
    // ---------------------------------------------

    // Keep players alive until playback finishes (prevents GC cut-off)
    private static readonly HashSet<MediaPlayer> _activePlayers = [];

    private static void PlayErrorSound()
    {
        if (!App.Settings.BuzzSoundEnabled) return;
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "error.mp3");
        if (!File.Exists(path)) return;
        var player = new MediaPlayer();
        _activePlayers.Add(player);
        player.MediaEnded += (_, _) => { player.Close(); _activePlayers.Remove(player); };
        player.Open(new Uri(path));
        player.Play();
    }

    private static void PlayBuzzSound()
    {
        if (!App.Settings.BuzzSoundEnabled) return;

        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "buzz.mp3");
        if (!File.Exists(path)) return;

        var player = new MediaPlayer
        {
            Volume = Math.Clamp(App.Settings.BuzzSoundVolume, 0.0, 1.0)
        };
        _activePlayers.Add(player);
        player.MediaEnded += (_, _) => { player.Close(); _activePlayers.Remove(player); };
        player.Open(new Uri(path));
        player.Play();
    }

    private void SetStatus(string text) => StatusLabel.Text = text;

    private void UpdateWireStateDot(SemaBuzzWireState state)
    {
        WireStateDot.Fill = state switch
        {
            SemaBuzzWireState.Live     => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
            SemaBuzzWireState.Secured  => new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x41)),
            SemaBuzzWireState.Warming  => new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x00)),
            _                          => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
        };

        // Animate a glow pulse when secured
        if (state == SemaBuzzWireState.Secured)
            PulseGlow(WireStateDot);
    }

    private static void PulseGlow(System.Windows.Shapes.Ellipse dot)
    {
        var anim = new DoubleAnimation(1.0, 0.3, TimeSpan.FromSeconds(0.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        dot.BeginAnimation(OpacityProperty, anim);
    }

    // ---------------------------------------------
    // ---------------------------------------------

    /// <summary>Updates the Pro upgrade banner visibility based on the current license state.</summary>
    public void ApplyLicenseBanner() { }

    private void BuyNowButton_Click(object sender, RoutedEventArgs e) { }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        if (_cts != null)
            _cts.Cancel();
        if (_client != null)
            _client.Dispose();
        if (_listener != null)
            _listener.Dispose();
        base.OnClosed(e);
    }
}
