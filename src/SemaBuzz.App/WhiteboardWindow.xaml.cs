using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SemaBuzz.Protocol;

namespace SemaBuzz.App;

/// <summary>
/// Shared whiteboard — both peers can draw simultaneously.
/// Each side sees their own strokes on the local canvas layer and the peer's
/// strokes on the peer canvas layer underneath.
/// Stroke points are transmitted as normalized (0–65535) coordinates so the
/// two windows don't need to be the same size.
/// </summary>
public partial class WhiteboardWindow : Window
{
    /// <summary>Fired when the local user draws or clears. MainWindow forwards these to the wire.</summary>
    public event EventHandler<SemaBuzzDrawEvent>? DrawSent;

    // Palette indexed to match SemaBuzzDraw.PaletteCount
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xFF, 0xB3, 0x00),  // 0 amber
        Color.FromRgb(0xE0, 0xE0, 0xE0),  // 1 white
        Color.FromRgb(0xF4, 0x43, 0x36),  // 2 red
        Color.FromRgb(0x4C, 0xAF, 0x50),  // 3 green
        Color.FromRgb(0x21, 0x96, 0xF3),  // 4 blue
        Color.FromRgb(0x61, 0x61, 0x61),  // 5 eraser (matches canvas bg-ish, drawn opaque)
    ];

    private static readonly double[] Sizes = [2.0, 4.0, 8.0];

    private byte      _colorIndex = 0;
    private byte      _sizeIndex  = 1;
    private bool      _drawing;
    private Polyline? _localStroke;   // active local stroke being drawn
    private Polyline? _peerStroke;    // active peer stroke being received

    private Button[] _swatches = [];
    private Button[] _sizeBtns = [];

    public WhiteboardWindow()
    {
        InitializeComponent();
        _swatches = [Swatch0, Swatch1, Swatch2, Swatch3, Swatch4, Swatch5];
        _sizeBtns = [SizeSmall, SizeMed, SizeLarge];
        UpdateSwatchHighlight();
        UpdateSizeHighlight();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SemaBuzzThemeManager.ApplyChrome(this);
        SemaBuzzTheme.HideCloseButton(this);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Render a draw event that arrived from the remote peer.</summary>
    public void ReceiveDraw(SemaBuzzDrawEvent ev)
    {
        switch (ev.Action)
        {
            case SemaBuzzDrawAction.Down:
                _peerStroke = MakePolyline(ev.ColorIndex, ev.SizeIndex);
                AppendNormalized(_peerStroke, ev.X, ev.Y, PeerCanvas);
                PeerCanvas.Children.Add(_peerStroke);
                break;

            case SemaBuzzDrawAction.Move:
                if (_peerStroke != null)
                    AppendNormalized(_peerStroke, ev.X, ev.Y, PeerCanvas);
                break;

            case SemaBuzzDrawAction.Up:
                _peerStroke = null;
                break;

            case SemaBuzzDrawAction.Clear:
                PeerCanvas.Children.Clear();
                break;
        }
    }

    // ── Canvas mouse handlers ────────────────────────────────────────────────

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _drawing = true;
        LocalCanvas.CaptureMouse();
        _localStroke = MakePolyline(_colorIndex, _sizeIndex);
        var pos = e.GetPosition(LocalCanvas);
        AppendClamped(_localStroke, pos);
        LocalCanvas.Children.Add(_localStroke);
        SendEvent(SemaBuzzDrawAction.Down, pos);
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _localStroke == null) return;
        var pos = e.GetPosition(LocalCanvas);
        AppendClamped(_localStroke, pos);
        SendEvent(SemaBuzzDrawAction.Move, pos);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        EndStroke(e.GetPosition(LocalCanvas));
        e.Handled = true;
    }

    private void Canvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_drawing)
            EndStroke(e.GetPosition(LocalCanvas));
    }

    private void EndStroke(Point pos)
    {
        _drawing = false;
        LocalCanvas.ReleaseMouseCapture();
        _localStroke = null;
        SendEvent(SemaBuzzDrawAction.Up, pos);
    }

    // ── Toolbar handlers ─────────────────────────────────────────────────────

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var idx = Array.IndexOf(_swatches, btn);
        if (idx >= 0)
        {
            _colorIndex = (byte)idx;
            UpdateSwatchHighlight();
        }
    }

    private void SizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var idx = Array.IndexOf(_sizeBtns, btn);
        if (idx >= 0)
        {
            _sizeIndex = (byte)idx;
            UpdateSizeHighlight();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        LocalCanvas.Children.Clear();
        PeerCanvas.Children.Clear();
        DrawSent?.Invoke(this, new SemaBuzzDrawEvent { Action = SemaBuzzDrawAction.Clear });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title            = "Save Whiteboard",
            Filter           = "PNG Image|*.png",
            FileName         = $"whiteboard-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            DefaultExt       = ".png",
            AddExtension     = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        // Measure the canvas area in device-independent pixels
        var w = CanvasBorder.ActualWidth;
        var h = CanvasBorder.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Get the current DPI so the bitmap matches screen resolution
        var src   = PresentationSource.FromVisual(this);
        var dpiX  = src != null ? 96.0 * src.CompositionTarget.TransformToDevice.M11 : 96.0;
        var dpiY  = src != null ? 96.0 * src.CompositionTarget.TransformToDevice.M22 : 96.0;

        var pixW = (int)(w * dpiX / 96.0);
        var pixH = (int)(h * dpiY / 96.0);

        var rtb = new RenderTargetBitmap(pixW, pixH, dpiX, dpiY, PixelFormats.Pbgra32);

        // Fill background first (#111214) so the saved PNG isn't transparent
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x11, 0x12, 0x14)),
                             null, new Rect(0, 0, w, h));
        rtb.Render(bg);

        // Render peer strokes then local strokes (same z-order as the window)
        rtb.Render(PeerCanvas);
        rtb.Render(LocalCanvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var stream = System.IO.File.OpenWrite(dlg.FileName);
        encoder.Save(stream);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Polyline MakePolyline(byte colorIdx, byte sizeIdx)
    {
        var color = colorIdx < Palette.Length ? Palette[colorIdx] : Palette[0];
        var thick = sizeIdx  < Sizes.Length   ? Sizes[sizeIdx]    : 2.0;
        return new Polyline
        {
            Stroke             = new SolidColorBrush(color),
            StrokeThickness    = thick,
            StrokeLineJoin     = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        };
    }

    /// <summary>Add a point clamped to the local canvas bounds.</summary>
    private void AppendClamped(Polyline polyline, Point pos)
    {
        var x = Math.Max(0, Math.Min(pos.X, LocalCanvas.ActualWidth));
        var y = Math.Max(0, Math.Min(pos.Y, LocalCanvas.ActualHeight));
        polyline.Points.Add(new Point(x, y));
    }

    /// <summary>Add a normalized (0-65535) peer point scaled to the peer canvas size.</summary>
    private static void AppendNormalized(Polyline polyline, ushort xNorm, ushort yNorm, Canvas canvas)
    {
        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        polyline.Points.Add(new Point(xNorm / 65535.0 * w, yNorm / 65535.0 * h));
    }

    private void SendEvent(SemaBuzzDrawAction action, Point pos)
    {
        var w = LocalCanvas.ActualWidth;
        var h = LocalCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var xNorm = (ushort)(Math.Clamp(pos.X / w, 0.0, 1.0) * 65535);
        var yNorm = (ushort)(Math.Clamp(pos.Y / h, 0.0, 1.0) * 65535);
        DrawSent?.Invoke(this, new SemaBuzzDrawEvent
        {
            Action     = action,
            X          = xNorm,
            Y          = yNorm,
            ColorIndex = _colorIndex,
            SizeIndex  = _sizeIndex,
        });
    }

    private void UpdateSwatchHighlight()
    {
        for (var i = 0; i < _swatches.Length; i++)
        {
            _swatches[i].BorderBrush     = i == _colorIndex
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            _swatches[i].BorderThickness = new Thickness(i == _colorIndex ? 2 : 1);
        }
    }

    private void UpdateSizeHighlight()
    {
        for (var i = 0; i < _sizeBtns.Length; i++)
            _sizeBtns[i].Opacity = i == _sizeIndex ? 1.0 : 0.40;
    }
}
