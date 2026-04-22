using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

namespace SemaBuzz.App;

/// <summary>
/// MainWindow ï¿½ the SemaBuzz command center.
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

    // Prevents more than one connect dialog from being open simultaneously.
    private bool _connectDialogOpen;

    // Track the previous input text so we can detect deletions
    private string _previousInputText = string.Empty;

    // Batch-send queue: packets accumulated for 50 ms before flushing
    private readonly List<SemaBuzzPacket> _pendingPackets = [];
    private DispatcherTimer? _batchTimer;

    // Sequence number tracking for duplicate / out-of-order rejection
    private const int MaxPendingPeerPackets = 8;
    private ushort _lastPeerSeq;
    private bool   _peerSeqInitialized;
    private readonly Dictionary<ushort, SemaBuzzPacket> _pendingPeerPackets = [];

    // Chat row containers (tracked so they can be removed when cleared)
    private Grid?      _peerLiveRow;
    private TextBlock? _livePeerBlock;

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
        ApplyIndicatorSettings();
        InputBox.Focus();
        Loaded += (_, _) =>
        {
            ApplyLicenseBanner();
        };

        _trayIcon = CreateTrayIcon();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SemaBuzzThemeManager.ApplyChrome(this);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Toggle the glyph between ? (maximize) and ? (restore)
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\u2750" : "\u25A1";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";

        if (WindowState == WindowState.Minimized && App.Settings.MinimizeToTray)
            HideToTray();
    }

    private void WinBtn_Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void WinBtn_Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

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
    // buzz:// URI handling
    // ---------------------------------------------

    /// <summary>
    /// Called when the app is launched (or focused) with a buzz:// URI
    /// either from the command line or forwarded by a secondary instance.
    /// Pre-populates and opens the connect dialog in dial mode.
    /// </summary>
    public void OpenBuzzUri(string rawUri)
    {
        if (SemaBuzzUriHandler.TryParse(rawUri) == null) return; // ignore malformed URIs
        if (_connectDialogOpen) return;
        _connectDialogOpen = true;
        try
        {
            var dialog = new SemaBuzzConnectDialog(dialBuzzUri: rawUri)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _localHandle    = dialog.Handle;
            _localAvatarPng = dialog.AvatarPng;
            LocalPaneLabel.Text = dialog.Handle.ToUpperInvariant();

            StartConnecting(dialog.PeerHost, dialog.Port, _cts.Token);
        }
        finally
        {
            _connectDialogOpen = false;
        }
    }

    // ---------------------------------------------
    // Connection dialog
    // ---------------------------------------------

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_connectDialogOpen) return;
        _connectDialogOpen = true;
        try
        {
            var dialog = new SemaBuzzConnectDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _localHandle    = dialog.Handle;
            _localAvatarPng = dialog.AvatarPng;
            LocalPaneLabel.Text = dialog.Handle.ToUpperInvariant();

            if (dialog.IsHost)
            {
                if (!string.IsNullOrEmpty(dialog.RelayToken))
                    StartListeningViaRelay(dialog.RelayToken, _cts.Token);
                else
                    StartListening(dialog.Port, _cts.Token);
            }
            else
            {
                if (!string.IsNullOrEmpty(dialog.RelayToken))
                    StartConnectingViaRelay(dialog.RelayToken, _cts.Token);
                else
                    StartConnecting(dialog.PeerHost, dialog.Port, _cts.Token);
            }
        }
        finally
        {
            _connectDialogOpen = false;
        }
    }

    // ---------------------------------------------
    // Menu handlers
    // ---------------------------------------------

    private async void Wire_Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_client   != null) await _client.DisconnectAsync();
        if (_listener != null) await _listener.DisconnectAsync();
        _cts?.Cancel();
        _cts      = null;
        _client   = null;
        _listener = null;
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

    private void Settings_Preferences_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SemaBuzzSettingsDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        App.Settings.DefaultListenPort    = dlg.SelectedDefaultListenPort;
        App.Settings.IndicatorSensitivity = dlg.SelectedIndicatorSensitivity;
        App.Settings.IndicatorStyle       = dlg.SelectedIndicatorStyle;
        App.Settings.ChatFontSize         = dlg.SelectedChatFontSize;
        App.Settings.LivePreview          = dlg.SelectedLivePreview;
        App.Settings.MinimizeToTray       = dlg.SelectedMinimizeToTray;
        App.Settings.RelayUri             = dlg.SelectedRelayUri;
        App.Settings.Save();

        ApplyIndicatorSettings();
    }

    //  HELP menu

    private void Help_About_Click(object sender, RoutedEventArgs e)
        => new SemaBuzzAboutDialog { Owner = this }.ShowDialog();

    private void Help_FAQ_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://semabuzz.me/faq") { UseShellExecute = true });

    private void Help_NewsUpdates_Click(object sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://x.com/semabuzzp2p") { UseShellExecute = true });

    /// <summary>Push sensitivity and style from saved settings to the live indicator control.</summary>
    private void ApplyIndicatorSettings()
    {
        BuzzIndicator.Sensitivity     = App.Settings.IndicatorSensitivity;
        BuzzIndicator.IndicatorStyle = App.Settings.IndicatorStyle;
    }

    private void StartListening(int port, CancellationToken ct)
    {
        ClearChatPanels();
        ConnectMenuItem.IsEnabled = false;
        _listener = new SemaBuzzListener();
        _listener.PacketReceived             += OnRemotePacketReceived;
        _listener.WireStateChanged           += OnWireStateChanged;
        _listener.MetadataReceived           += OnMetadataReceived;
        _listener.ConnectionApprovalCallback  = OnConnectionApprovalRequested;

        SetStatus($"› listening on port {port}...");
        _ = _listener.ListenAsync(port, ct);
    }

    private void StartListeningViaRelay(string token, CancellationToken ct)
    {
        ClearChatPanels();
        ConnectMenuItem.IsEnabled = false;
        _listener = new SemaBuzzListener();
        _listener.PacketReceived             += OnRemotePacketReceived;
        _listener.WireStateChanged           += OnWireStateChanged;
        _listener.MetadataReceived           += OnMetadataReceived;
        _listener.ConnectionApprovalCallback  = OnConnectionApprovalRequested;

        SetStatus($"› waiting via relay (token: {token})...");
        _ = _listener.ListenViaRelayAsync(
            App.Settings.RelayUri,
            token, ct);
    }

    private void StartConnecting(string host, int port, CancellationToken ct)
    {
        ClearChatPanels();
        ConnectMenuItem.IsEnabled = false;
        _client = new SemaBuzzClient();
        _client.PacketReceived      += OnRemotePacketReceived;
        _client.WireStateChanged    += OnWireStateChanged;
        _client.MetadataReceived    += OnMetadataReceived;

        SetStatus($"› dialing {host}:{port}...");
        _ = _client.ConnectAsync(host, port, ct);
    }

    private void StartConnectingViaRelay(string token, CancellationToken ct)
    {
        ClearChatPanels();
        ConnectMenuItem.IsEnabled = false;
        _client = new SemaBuzzClient();
        _client.PacketReceived      += OnRemotePacketReceived;
        _client.WireStateChanged    += OnWireStateChanged;
        _client.MetadataReceived    += OnMetadataReceived;

        SetStatus($"› joining relay room {token}...");
        _ = _client.ConnectViaRelayAsync(
            App.Settings.RelayUri,
            token, ct);
    }

    // ---------------------------------------------
    // Input handling ï¿½ Live-Wire typing
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
            InputBox.Clear();
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
        BuzzIndicator.MaxBurst();
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
        InputBox.Clear();
        SendButton.IsEnabled = false;

        // Send a newline packet with the next outbound sequence number so the
        // peer's duplicate filter does not drop the sentence commit.
        var nlPacket = new SemaBuzzPacket('\n', 0, SemaBuzzPacketType.Char, _streamer.NextSequence());
        if (_client   != null) _ = _client.SendAsync(nlPacket);
        if (_listener != null) _ = _listener.SendAsync(nlPacket);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
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
            // Characters were deleted ï¿½ send a backspace for each one removed
            var deleted = prev.Length - text.Length;
            for (var i = 0; i < deleted; i++)
                _streamer.Feed('\b');
        }
        else if (text.Length > prev.Length)
        {
            // Characters were added ï¿½ feed each new character
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
        bool approved = false;
        await Dispatcher.InvokeAsync(() =>
        {
            SemaBuzzConnectRequestDialog.PlayRequestSoundOnce();
            if (!IsVisible)
                RestoreFromTray();
            else if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();
            var dlg = new SemaBuzzConnectRequestDialog(remote) { Owner = this };
            dlg.ShowDialog();
            approved = dlg.Accepted;
        });
        return approved;
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
                return;

            _lastPeerSeq = nextSeq;
            RenderPeerPacket(packet);
        }
    }

    private void ResyncPendingPeerPackets()
    {
        if (_pendingPeerPackets.Count == 0)
            return;

        // One missing packet should not permanently stall the remote transcript.
        // When the small reorder buffer fills up, resume from the earliest packet
        // we do have and continue rendering from there.
        var nextAvailableSeq = _pendingPeerPackets.Keys.Min();
        _lastPeerSeq = (ushort)(nextAvailableSeq - 1);
        FlushPendingPeerPackets();
    }

    // ---------------------------------------------
    // Wire state changes
    // ---------------------------------------------

    /// <summary>Reset all UI chrome back to the cold/idle state.</summary>
    private void ResetToIdle()
    {
        SetStatus("› wire is cold");
        TitleSessionLabel.Text         = "NO WIRE";
        ConnectMenuItem.IsEnabled      = true;
        DisconnectMenuItem.IsEnabled   = false;
        InputBox.IsEnabled             = false;
        SendButton.IsEnabled           = false;        BuzzButton.IsEnabled           = false;        _peerLiveRow                   = null;
        _livePeerBlock                 = null;
        _peerHandle                    = "peer";
        _peerAvatarPng                 = null;
        PeerLabel.Text                 = string.Empty;
        UpdateWireStateDot(SemaBuzzWireState.Cold);
        BuzzIndicator.Flatline();
        _peerSeqInitialized = false;
        _pendingPeerPackets.Clear();
    }

    private void OnWireStateChanged(object? sender, SemaBuzzWireStateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            SetStatus($"› {e.Message ?? e.State.ToString().ToLower()}");
            UpdateWireStateDot(e.State);

            if (e.State == SemaBuzzWireState.Warming)
            {
                DisconnectMenuItem.IsEnabled = true;
                _warmingCts?.Cancel();
                _warmingCts = new CancellationTokenSource();
                _ = StartWarmingTimeoutAsync(_warmingCts.Token);
            }
            else if (e.State is SemaBuzzWireState.Live or SemaBuzzWireState.Secured)
            {
                _warmingCts?.Cancel();
                _warmingCts = null;
                var stateTag = e.State == SemaBuzzWireState.Secured ? "[ENC] " : "";
                TitleSessionLabel.Text = $"{stateTag}WIRE LIVE";
                InputBox.IsEnabled   = true;
                SendButton.IsEnabled = false; // no text yet
                BuzzButton.IsEnabled = true;
                InputBox.Focus();
                DisconnectMenuItem.IsEnabled = true;
                AddChatDivider(e.State == SemaBuzzWireState.Secured
                    ? "› sema secured · wire is live"
                    : "› wire is live");

                // Exchange identity with the peer
                if (_client   != null) _ = _client.SendMetadataAsync(_localHandle, _localAvatarPng);
                if (_listener != null) _ = _listener.SendMetadataAsync(_localHandle, _localAvatarPng);
            }
            else if (e.State == SemaBuzzWireState.Dead)
            {
                PlayErrorSound();
                _warmingCts?.Cancel();
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
                ConnectMenuItem.IsEnabled  = true;
                DisconnectMenuItem.IsEnabled = false;

                var divider = _warmingTimedOut
                    ? "› no dialer arrived · session closed after 5 minutes"
                    : e.Message switch
                    {
                        "peer-disconnect" => $"× {savedHandle} disconnected · wire has been closed",
                        "not-available"  => $"× {savedHandle} is not available at this time",
                        _                => "× wire is dead",
                    };
                _warmingTimedOut = false;
                AddChatDivider(divider);
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
        // handler shows the right divider.
        _warmingTimedOut = true;
        _cts?.Cancel();
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
            var offset = (i % 2 == 0 ? magnitude : -magnitude) * (1.0 - i / (double)steps);
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(origin + offset, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 40))));
        }
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(origin, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(steps * 40))));
        BeginAnimation(LeftProperty, anim);
    }

    private void AppendPeerCharacter(char ch)
    {
        // '\n' commits a live line ï¿½ if there's no live line, nothing to freeze.
        // '\b' removes a character ï¿½ if there's no live line, nothing to delete.
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

            // All text removed ï¿½ remove the row
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
                var msgText = _livePeerBlock.Text.Length > prefix.Length
                                  ? _livePeerBlock.Text[prefix.Length..]
                                  : string.Empty;
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
    private static (Grid Row, TextBlock TextBlock) MakeChatLine(
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
        var tb = new TextBlock
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
    private static void HyperlinkifyTextBlock(TextBlock tb)
    {
        var fullText = tb.Text;
        var matches  = UrlRegex.Matches(fullText);
        if (matches.Count == 0) return; // no URLs ï¿½ leave as plain text

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
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
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
        var initials = handle.Length > 0 ? handle[0].ToString().ToUpper() : "?";
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

    private static void PlayErrorSound()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "error.mp3");
        if (!File.Exists(path)) return;
        var player = new MediaPlayer();
        player.MediaEnded += (_, _) => player.Close();
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

    public void ApplyLicenseBanner() { }

    private void BuyNowButton_Click(object sender, RoutedEventArgs e) { }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _cts?.Cancel();
        _client?.Dispose();
        _listener?.Dispose();
        base.OnClosed(e);
    }
}
