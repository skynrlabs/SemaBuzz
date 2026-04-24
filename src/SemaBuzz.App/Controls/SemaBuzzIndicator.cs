using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using UserControl = System.Windows.Controls.UserControl;

namespace SemaBuzz.App.Controls;

/// <summary>
/// The SemaBuzz signature visual: a vibrating polyline filament that reacts
/// to incoming packet intensity. Higher intensity  wider oscillation.
/// Decays back to flatline when the wire goes silent.
/// </summary>
public sealed class SemaBuzzIndicator : UserControl
{
    private readonly Polyline _filament;
    private readonly DispatcherTimer _renderTimer;

    private double _currentAmplitude;
    private double _phase;
    private DateTime _burstUntil = DateTime.MinValue;

    private const double DecayRate    = 0.92;
    private const double MaxAmplitude = 22.0;
    private const int    WavePoints   = 64;

    /// <summary>
    /// Intensity multiplier (0.5 = calm, 1.0 = default, 2.0 = aggressive).
    /// Applied in Pulse() so the filament reacts more or less to each packet.
    /// </summary>
    public double Sensitivity { get; set; } = 1.0;

    /// <summary>Filament animation style (free = Flicker; PRO = Pulse / Wave).</summary>
    public IndicatorStyleId IndicatorStyle { get; set; } = IndicatorStyleId.Flicker;

    public static readonly DependencyProperty FilamentBrushProperty =
        DependencyProperty.Register(nameof(FilamentBrush), typeof(Brush), typeof(SemaBuzzIndicator),
            new PropertyMetadata(
                new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
                OnFilamentBrushChanged));

    public Brush FilamentBrush
    {
        get => (Brush)GetValue(FilamentBrushProperty);
        set => SetValue(FilamentBrushProperty, value);
    }

    private static void OnFilamentBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SemaBuzzIndicator indicator)
            indicator._filament.Stroke = (Brush)e.NewValue;
    }

    public SemaBuzzIndicator()
    {
        var canvas = new Canvas { Background = Brushes.Transparent };

        _filament = new Polyline
        {
            Stroke          = FilamentBrush,
            StrokeThickness = 1.5,
            StrokeLineJoin  = PenLineJoin.Round,
        };

        canvas.Children.Add(_filament);
        Content = canvas;

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }

    /// <summary>
    /// Energize the filament with an incoming packet intensity (0–255).
    /// Sensitivity scales how aggressively the amplitude responds.
    /// Must be called on the UI thread.
    /// </summary>
    public void Pulse(byte intensity)
    {
        _currentAmplitude = Math.Min((intensity / 255.0) * MaxAmplitude * Sensitivity, MaxAmplitude);
    }

    /// <summary>
    /// Spike the filament to maximum amplitude and sustain it for 600 ms
    /// used for the Buzz feature. Must be called on the UI thread.
    /// </summary>
    public void MaxBurst()
    {
        _currentAmplitude = MaxAmplitude;
        _burstUntil       = DateTime.UtcNow.AddMilliseconds(600);
    }

    /// <summary>Instantly flatten the filament (wire dead state).</summary>
    public void Flatline()
    {
        _currentAmplitude = 0;
        _burstUntil       = DateTime.MinValue;
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        // Phase advances at different speeds per style
        _phase += IndicatorStyle switch
        {
            IndicatorStyleId.Pulse => 0.28, // faster  crisp heartbeat feel
            IndicatorStyleId.Wave  => 0.10, // slower  rolling ocean wave
            _                      => 0.18, // Flicker default
        };

        // During a burst, hold amplitude at max; outside burst, decay normally
        if (DateTime.UtcNow < _burstUntil)
            _currentAmplitude = MaxAmplitude;
        else
            _currentAmplitude *= DecayRate;

        double width;
        if (ActualWidth > 4)
            width = ActualWidth;
        else
            width = 300;
        double height;
        if (ActualHeight > 4)
            height = ActualHeight;
        else
            height = 40;
        var midY   = height / 2.0;

        var points = new PointCollection(WavePoints);
        for (var i = 0; i < WavePoints; i++)
        {
            var x = (i / (double)(WavePoints - 1)) * width;
            var y = IndicatorStyle switch
            {
                // Pulse: single clean harmonic  one full period across the width
                IndicatorStyleId.Pulse =>
                    midY + _currentAmplitude * Math.Sin(_phase + i * Math.PI * 2.0 / WavePoints),

                // Wave: slow wide rolling sine  wide arcs, no harmonics
                IndicatorStyleId.Wave =>
                    midY + _currentAmplitude * Math.Sin(_phase + i * Math.PI * 1.2 / WavePoints),

                // Flicker (default): two overlapping harmonics  chaotic, high-frequency
                _ =>
                    midY
                    + _currentAmplitude * Math.Sin(_phase + i * 0.32)
                    + (_currentAmplitude * 0.35) * Math.Sin(_phase * 1.9 + i * 0.65),
            };
            points.Add(new Point(x, y));
        }

        _filament.Points = points;
    }
}
