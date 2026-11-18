namespace AltyaziCevirici.Ui;

/// <summary>Large preview. Both boxes move by drag and resize from all 8 handles.</summary>
public sealed class PreviewSurface : Canvas
{
    public const string Sample = OnizlemeMetni.Ornek;

    private EkranDikdortgen _screen = new(0, 0, 1920, 1080);
    private EkranDikdortgen _ocr = new(360, 900, 1200, 90);
    private EkranDikdortgen _caption = new(460, 790, 1000, 84);
    private ImageSource? _frame;
    private AppSettings _look = new();
    private bool _snap;
    private BoxPart _drag = BoxPart.None;
    private int _which;
    private EkranDikdortgen _grab;
    private Point _mouse;

    public event Action? Edited;

    public EkranDikdortgen ScreenBox => _screen;
    public EkranDikdortgen Ocr => _ocr;
    public EkranDikdortgen Caption => _caption;
    public bool SnapEnabled { get => _snap; set { _snap = value; InvalidateVisual(); } }

    public PreviewSurface()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(8, 10, 14));
        Cursor = Cursors.Arrow;
    }

    public void SetScene(EkranDikdortgen screen, EkranDikdortgen ocr, EkranDikdortgen caption, AppSettings look)
    {
        _screen = screen.Width > 0 ? screen : _screen;
        _ocr = ocr;
        _caption = caption;
        _look = look;
        InvalidateVisual();
    }

    public void SetFrame(ImageSource? frame) { _frame = frame; InvalidateVisual(); }

    public void GrabLive()
    {
        if (_screen.Width < 20 || _screen.Height < 20) return;
        try
        {
            int maxW = 1280;
            int w = Math.Min(maxW, _screen.Width);
            int h = Math.Max(1, _screen.Height * w / _screen.Width);
            using var full = new System.Drawing.Bitmap(_screen.Width, _screen.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(full))
                g.CopyFromScreen(_screen.X, _screen.Y, 0, 0, new System.Drawing.Size(_screen.Width, _screen.Height));
            var small = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(small))
                g.DrawImage(full, new System.Drawing.Rectangle(0, 0, w, h));
            SetFrame(BitmapSourceFrom(small));
            small.Dispose();
        }
        catch (Exception)
        {
            /* display is switching */
        }
    }

    private static BitmapSource BitmapSourceFrom(System.Drawing.Bitmap bmp)
    {
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgr24, null, data.Scan0, data.Stride * bmp.Height, data.Stride);
            src.Freeze();
            return src;
        }
        finally { bmp.UnlockBits(data); }
    }

    private Rect Map()
    {
        if (_screen.Width <= 0 || ActualWidth <= 1 || ActualHeight <= 1) return new Rect(0, 0, ActualWidth, ActualHeight);
        double scale = Math.Min(ActualWidth / _screen.Width, ActualHeight / _screen.Height);
        double w = _screen.Width * scale;
        double h = _screen.Height * scale;
        return new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
    }

    private Rect ToView(EkranDikdortgen box)
    {
        var map = Map();
        double sx = map.Width / _screen.Width;
        double sy = map.Height / _screen.Height;
        return new Rect(map.X + (box.X - _screen.X) * sx, map.Y + (box.Y - _screen.Y) * sy, Math.Max(12, box.Width * sx), Math.Max(12, box.Height * sy));
    }

    private (int X, int Y) ToScreen(Point view)
    {
        var map = Map();
        double sx = _screen.Width / Math.Max(1, map.Width);
        double sy = _screen.Height / Math.Max(1, map.Height);
        return (_screen.X + (int)((view.X - map.X) * sx), _screen.Y + (int)((view.Y - map.Y) * sy));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var map = Map();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 10, 14)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_frame != null) dc.DrawImage(_frame, map);
        DrawBox(dc, _ocr, Color.FromRgb(255, 176, 46), "Orijinal altyazı", handles: false);
        DrawBox(dc, _caption, Color.FromRgb(61, 220, 180), "Çeviri", handles: false);
        if (_caption.Width > 20) DrawSample(dc, ToView(_caption));
        DrawHandles(dc, _ocr, Color.FromRgb(255, 176, 46));
        DrawHandles(dc, _caption, Color.FromRgb(61, 220, 180));
    }

    private void DrawSample(DrawingContext dc, Rect box)
    {
        double size = Math.Clamp(_look.YaziBoyutu * (box.Width / 640.0), 12, 36);
        var type = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        byte alpha = (byte)Math.Clamp(_look.ArkaPlanOpakligi * 2.55, 0, 255);
        if (alpha > 0)
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(alpha, 8, 10, 14)), null, box, 8, 8);
        dc.PushClip(new RectangleGeometry(box));
        var color = ColorOf(_look.YaziRengi);
        while (size > 11)
        {
            var trial = new FormattedText(Sample, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, type, size, new SolidColorBrush(color), 1.0)
            {
                MaxTextWidth = Math.Max(20, box.Width - 16),
                TextAlignment = TextAlignment.Center,
            };
            if (trial.Height <= box.Height - 28) { dc.DrawText(trial, new Point(box.X + 8, box.Y + 22)); break; }
            size -= 2;
        }
        dc.Pop();
    }

    private static Color ColorOf(string hex)
    {
        if (AppSettings.TryParseColor(hex, out var rgb)) return Color.FromRgb(rgb.R, rgb.G, rgb.B);
        return Colors.White;
    }

    private void DrawBox(DrawingContext dc, EkranDikdortgen box, Color color, string label, bool handles)
    {
        if (box.Width < 8 || box.Height < 8) return;
        var view = ToView(box);
        var brush = new SolidColorBrush(Color.FromArgb(48, color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(color), 2);
        dc.DrawRectangle(brush, pen, view);
        if (handles) DrawHandles(dc, box, color);
        var type = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, type, 12, new SolidColorBrush(color), 1.0);
        dc.DrawText(text, new Point(view.X + 10, view.Y + 4));
    }

    private void DrawHandles(DrawingContext dc, EkranDikdortgen box, Color color)
    {
        if (box.Width < 8 || box.Height < 8) return;
        foreach (var handle in HandlePoints(ToView(box)))
        {
            var r = new Rect(handle.X - 7, handle.Y - 7, 14, 14);
            dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(color), 2), r);
        }
    }

    private static Point[] HandlePoints(Rect box) => new[]
    {
        box.TopLeft, new Point(box.Left + box.Width / 2, box.Top), box.TopRight,
        new Point(box.Right, box.Top + box.Height / 2), box.BottomRight,
        new Point(box.Left + box.Width / 2, box.Bottom), box.BottomLeft,
        new Point(box.Left, box.Top + box.Height / 2),
    };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var view = e.GetPosition(this);
        var (sx, sy) = ToScreen(view);
        var map = Map();
        int reach = (int)Math.Max(10, 16 / Math.Max(0.05, map.Width / _screen.Width));
        var capHit = BoxEdit.Hit(_caption, sx, sy, reach);
        var ocrHit = BoxEdit.Hit(_ocr, sx, sy, reach);
        if (capHit != BoxPart.None) { _which = 2; _drag = capHit; _grab = _caption; }
        else if (ocrHit != BoxPart.None) { _which = 1; _drag = ocrHit; _grab = _ocr; }
        else return;
        _mouse = view;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == BoxPart.None) return;
        var view = e.GetPosition(this);
        var map = Map();
        double sx = _screen.Width / Math.Max(1, map.Width);
        double sy = _screen.Height / Math.Max(1, map.Height);
        int dx = (int)((view.X - _mouse.X) * sx);
        int dy = (int)((view.Y - _mouse.Y) * sy);
        var next = BoxEdit.Drag(_grab, _drag, dx, dy);
        if (_drag == BoxPart.Body) next = BoxEdit.Snap(next, _screen, 12, _snap);
        if (_which == 1) _ocr = next; else _caption = next;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag == BoxPart.None) return;
        _drag = BoxPart.None;
        ReleaseMouseCapture();
        Edited?.Invoke();
    }
}
