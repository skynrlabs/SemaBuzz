using System.Net;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SemaBuzz.App;

public partial class SemaBuzzConnectRequestDialog : Window
{
    public bool Accepted { get; private set; }

    private readonly DispatcherTimer _timer;
    private int _secondsLeft = 30;
    private MediaPlayer? _ring;

    public SemaBuzzConnectRequestDialog(IPEndPoint remote)
    {
        InitializeComponent();
        FromLabel.Text = $"Peer at  {remote}  wants to open a wire.";
        UpdateCountdown();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) =>
        {
            _timer.Start();
            StartRing();
        };
        Closed += (_, _) => StopRing();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SemaBuzzThemeManager.ApplyChrome(this);
        SemaBuzzTheme.HideCloseButton(this);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _secondsLeft--;
        UpdateCountdown();
        if (_secondsLeft <= 0)
        {
            _timer.Stop();
            StopRing();
            Accepted     = false;
            DialogResult = false;
        }
    }

    private void UpdateCountdown()
        => CountdownLabel.Text = $"Auto-declining in {_secondsLeft}s...";

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        StopRing();
        Accepted     = true;
        DialogResult = true;
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        StopRing();
        Accepted     = false;
        DialogResult = false;
    }

    private void StartRing()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "request.mp3");
        if (!System.IO.File.Exists(path)) return;
        _ring = new MediaPlayer();
        _ring.MediaEnded += (_, _) => { _ring.Position = TimeSpan.Zero; _ring.Play(); };
        _ring.Open(new Uri(path));
        _ring.Play();
    }

    private void StopRing()
    {
        if (_ring == null) return;
        _ring.Stop();
        _ring.Close();
        _ring = null;
    }
}
