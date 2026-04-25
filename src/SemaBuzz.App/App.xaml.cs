using System.Threading;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using Application = System.Windows.Application;

namespace SemaBuzz.App;

/// <summary>
/// WPF Application entry point. Manages startup, single-instance enforcement,
/// buzz:// URI registration and forwarding, license checking, and theme initialization.
/// </summary>
public partial class App : Application
{
    /// <summary>Persisted application settings, loaded at startup and saved on change.</summary>
    internal static SemaBuzzSettings Settings { get; private set; } = new();

    private CancellationTokenSource _appExiting = new();

    /// <summary>
    /// Enforces single-instance, registers buzz:// URI handling, loads settings,
    /// applies the saved theme, and kicks off the async license check.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        //  Single-instance: if another SemaBuzz is already running, forward
        //    any buzz:// URI from our command line to it and exit immediately.
        var buzzArg = e.Args.FirstOrDefault(
            a => a.StartsWith("buzz://", StringComparison.OrdinalIgnoreCase));

        if (!SemaBuzzSingleInstance.ClaimPrimary())
        {
            // Always forward something so the primary comes to front;
            // pass the buzz:// URI if we have one, otherwise just a focus signal.
            SemaBuzzSingleInstance.ForwardToPrimary(buzzArg);
            Shutdown();
            return;
        }

        // Register buzz:// URI scheme and start the pipe listener in the background.
        Task.Run(SemaBuzzUriHandler.EnsureRegistered);

        //  Start listening for URIs forwarded by secondary instances.
        SemaBuzzSingleInstance.StartListening(_appExiting.Token);
        SemaBuzzSingleInstance.UriReceived += OnBuzzUriReceived;
        SemaBuzzSingleInstance.FocusRequested += OnFocusRequested;

        // Catch any unhandled exceptions so they are visible rather than a silent exit
        DispatcherUnhandledException += (_, ex) =>
        {
            System.Windows.MessageBox.Show(
                ex.Exception.ToString(),
                "SemaBuzz  Unhandled Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ex.Handled = true;
        };

        // Load persisted settings and apply the saved theme immediately,
        // before the main window is shown, so the first paint uses the right colours.
        Settings = SemaBuzzSettings.Load();
        SemaBuzzThemeManager.Apply(Settings.Theme);

        // Check the Microsoft Store license in the background  don't block startup.
        // The main window's Loaded handler calls ApplyLicenseBanner() which will
        // reflect whatever state is set by the time it runs; if the check finishes
        // after the window is already shown, it calls ApplyLicenseBanner() directly.
        _ = Task.Run(SemaBuzzLicense.CheckAsync).ContinueWith(_ =>
            Dispatcher.InvokeAsync(() =>
            {
                if (!SemaBuzzLicense.IsProUnlocked)
                {
                    var changed = false;
                    if (Settings.Theme != SemaBuzzThemeId.Obsidian
                        && Settings.Theme != SemaBuzzThemeId.Daylight)
                    {
                        Settings.Theme = SemaBuzzThemeId.Obsidian;
                        SemaBuzzThemeManager.Apply(SemaBuzzThemeId.Obsidian);
                        changed = true;
                    }
                    if (Settings.RelayUri != SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri)
                    {
                        Settings.RelayUri = SemaBuzz.Protocol.SemaBuzzRelayPacket.DefaultRelayUri;
                        changed = true;
                    }
                    if (changed) Settings.Save();
                }
                if (MainWindow is MainWindow win)
                    win.ApplyLicenseBanner();
            }));

        // Register for toast notification activation in the background  the COM
        // server registration and Start Menu shortcut creation it performs can take
        // several seconds on first run after a build, so we must not block the UI thread.
        Task.Run(() =>
        {
            try
            {
                ToastNotificationManagerCompat.OnActivated += _ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (MainWindow is { } win)
                        {
                            if (win.WindowState == System.Windows.WindowState.Minimized)
                                win.WindowState = System.Windows.WindowState.Normal;
                            win.Activate();
                        }
                    });
                };
            }
            catch { /* toast activation unavailable  continue without it */ }
        });

        // If we were launched directly by a buzz:// click, handle it after
        // the main window has finished loading.
        if (buzzArg != null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (MainWindow is MainWindow win)
                    win.OpenBuzzUri(buzzArg);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnBuzzUriReceived(string uri)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is MainWindow win)
            {
                if (win.WindowState == System.Windows.WindowState.Minimized)
                    win.WindowState = System.Windows.WindowState.Normal;
                win.Activate();
                win.OpenBuzzUri(uri);
            }
        });
    }

    private void OnFocusRequested()
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is { } win)
            {
                if (win.WindowState == System.Windows.WindowState.Minimized)
                    win.WindowState = System.Windows.WindowState.Normal;
                win.Activate();
            }
        });
    }

    /// <summary>Cancels background tasks and cleans up on application exit.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _appExiting.Cancel();
        // NOTE: Do NOT call ToastNotificationManagerCompat.Uninstall() here.
        // Uninstall() removes the COM server registration and Start Menu shortcut
        // on every normal exit, which breaks toast activation on the next launch.
        // Uninstall() should only be called from an actual uninstaller.
        base.OnExit(e);
    }
}

