using System.Net;
using System.Windows;
using System.Windows.Threading;

namespace SemaBuzz.App;

public partial class SemaBuzzConnectRequestDialog : Window
{
    public bool Accepted { get; private set; }

    private readonly DispatcherTimer _timer;
    private int _secondsLeft = 30;

    public SemaBuzzConnectRequestDialog(IPEndPoint remote)
    {
        InitializeComponent();
        FromLabel.Text = $"Peer at  {remote}  wants to open a wire.";
        UpdateCountdown();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => _timer.Start();
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
            Accepted     = false;
            DialogResult = false;
        }
    }

    private void UpdateCountdown()
        => CountdownLabel.Text = $"Auto-declining in {_secondsLeft}s...";

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Accepted     = true;
        DialogResult = true;
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Accepted     = false;
        DialogResult = false;
    }
}
