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
    /// Enforces single-instance, loads settings,
    /// applies the saved theme, and kicks off the async license check.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SemaBuzzSingleInstance.ClaimPrimary())
        {
            SemaBuzzSingleInstance.ForwardToPrimary(null);
            Shutdown();
            return;
        }

        //  Start listening for focus requests from secondary instances.
        SemaBuzzSingleInstance.StartListening(_appExiting.Token);
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

