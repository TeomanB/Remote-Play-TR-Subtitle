using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AltyaziCevirici.Core;

namespace AltyaziCevirici;

/// <summary>Monitor preview. The user draws the OCR strip and the translation box on it.</summary>
internal sealed class PreviewCanvas : Control
{
    public const string Ornek = "Bu bir örnek altyazıdır, ayarları buradan görebilirsin.";

    private Bitmap? _frame;
    private Rectangle _screen = new(0, 0, 1920, 1080);
    private Rectangle _ocr;
    private Rectangle _caption;
    private bool _showSample = true;
    private AppSettings _look = new();
    private int _drag; // 0 none, 1 ocr, 2 caption
    private int _handle; // -1 body, 0-7 handles
    private Point _grab;
    private Rectangle _grabRect;

    public event EventHandler? RegionsChanged;

    public PreviewCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        Dock = DockStyle.Fill;
    }

    public void SetFrame(Bitmap? frame, Rectangle screen)
    {
        var old = _frame;
        _frame = frame;
        _screen = screen.Width > 0 ? screen : _screen;
        old?.Dispose();
        Invalidate();
    }

    public void SetRegions(Rectangle ocr, Rectangle caption, AppSettings look, bool showSample)
    {
        _ocr = ocr;
        _caption = caption;
        _look = look;
        _showSample = showSample;
        Invalidate();
    }

    public Rectangle OcrRegion => _ocr;
    public Rectangle CaptionRegion => _caption;

    private RectangleF Map()
    {
        if (_screen.Width <= 0 || _screen.Height <= 0) return new RectangleF(0, 0, Width, Height);
        float scale = Math.Min(Width / (float)_screen.Width, Height / (float)_screen.Height);
        float w = _screen.Width * scale;
        float h = _screen.Height * scale;
        return new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
    }

    private Rectangle ToClient(Rectangle screenRect)
    {
        var map = Map();
        float sx = map.Width / _screen.Width;
        float sy = map.Height / _screen.Height;
        return new Rectangle(
            (int)(map.X + (screenRect.X - _screen.X) * sx),
            (int)(map.Y + (screenRect.Y - _screen.Y) * sy),
            Math.Max(8, (int)(screenRect.Width * sx)),
            Math.Max(8, (int)(screenRect.Height * sy)));
    }

    private Point ToScreen(Point client)
    {
        var map = Map();
        float sx = _screen.Width / Math.Max(1f, map.Width);
        float sy = _screen.Height / Math.Max(1f, map.Height);
        return new Point(
            _screen.X + (int)((client.X - map.X) * sx),
            _screen.Y + (int)((client.Y - map.Y) * sy));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(10, 12, 16));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var map = Map();
        if (_frame != null) g.DrawImage(_frame, map);
        else
        {
            using var hint = new SolidBrush(UiTheme.Muted);
            g.DrawString("Ekran görüntüsü burada görünecek.\nOrijinal ve çeviri kutularını bu tuvalin üzerine çiz.",
                UiTheme.Font(11f), hint, new RectangleF(20, 20, Width - 40, 80));
        }
        DrawBox(g, _ocr, UiTheme.Ocr, "Orijinal altyazı bölgesi");
        DrawBox(g, _caption, UiTheme.Caption, "Çeviri altyazı konumu");
        if (_showSample && _caption.Width > 20)
            DrawSample(g, ToClient(_caption));
    }

    private void DrawSample(Graphics g, Rectangle box)
    {
        float size = Math.Clamp(_look.YaziBoyutu * (box.Width / 640f), 10f, 42f);
        using var font = UiTheme.Font(size, FontStyle.Bold);
        int alpha = (int)Math.Round(Math.Clamp(_look.ArkaPlanOpakligi, 0, 100) * 2.55);
        if (alpha > 0)
        {
            using var bg = new SolidBrush(Color.FromArgb(alpha, 10, 12, 16));
            g.FillRectangle(bg, box);
        }
        using var brush = new SolidBrush(AppSettings.TryParseColor(_look.YaziRengi, out var c) ? Color.FromArgb(c.R, c.G, c.B) : Color.White);
        var layout = new RectangleF(box.X + 8, box.Y + 6, Math.Max(10, box.Width - 16), Math.Max(10, box.Height - 12));
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(Ornek, font, brush, layout, format);
    }

    private void DrawBox(Graphics g, Rectangle screen, Color color, string label)
    {
        if (screen.Width < 8 || screen.Height < 8) return;
        var box = ToClient(screen);
        using var fill = new SolidBrush(Color.FromArgb(40, color));
        using var pen = new Pen(color, 2);
        g.FillRectangle(fill, box);
        g.DrawRectangle(pen, box);
        foreach (var h in Handles(box))
        {
            using var hb = new SolidBrush(color);
            g.FillRectangle(hb, h);
        }
        using var text = new SolidBrush(color);
        g.DrawString(label, UiTheme.Font(8.5f, FontStyle.Bold), text, box.X + 6, Math.Max(2, box.Y - 16));
    }

    private static Rectangle[] Handles(Rectangle box)
    {
        const int s = 8;
        int l = box.Left - s / 2, r = box.Right - s / 2, t = box.Top - s / 2, b = box.Bottom - s / 2, cx = box.Left + box.Width / 2 - s / 2, cy = box.Top + box.Height / 2 - s / 2;
        return new[]
        {
            new Rectangle(l, t, s, s), new Rectangle(cx, t, s, s), new Rectangle(r, t, s, s),
            new Rectangle(r, cy, s, s), new Rectangle(r, b, s, s), new Rectangle(cx, b, s, s),
            new Rectangle(l, b, s, s), new Rectangle(l, cy, s, s),
        };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
        var map = Map();
        if (!map.Contains(e.Location)) { base.OnMouseDown(e); return; }
        if (TryHandle(_caption, e.Location, out _handle)) { _drag = 2; _grab = e.Location; _grabRect = _caption; }
        else if (TryHandle(_ocr, e.Location, out _handle)) { _drag = 1; _grab = e.Location; _grabRect = _ocr; }
        else if (ToClient(_caption).Contains(e.Location) && _caption.Width > 0) { _drag = 2; _handle = -1; _grab = e.Location; _grabRect = _caption; }
        else if (ToClient(_ocr).Contains(e.Location) && _ocr.Width > 0) { _drag = 1; _handle = -1; _grab = e.Location; _grabRect = _ocr; }
        else
        {
            _drag = 1;
            _handle = 4;
            var p = ToScreen(e.Location);
            _ocr = _grabRect = new Rectangle(p.X, p.Y, 4, 4);
            _grab = e.Location;
        }
        Capture = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == 0) { base.OnMouseMove(e); return; }
        var map = Map();
        float sx = _screen.Width / Math.Max(1f, map.Width);
        float sy = _screen.Height / Math.Max(1f, map.Height);
        int dx = (int)((e.X - _grab.X) * sx);
        int dy = (int)((e.Y - _grab.Y) * sy);
        var next = _handle < 0 ? new Rectangle(_grabRect.X + dx, _grabRect.Y + dy, _grabRect.Width, _grabRect.Height) : ResizeRect(_grabRect, _handle, dx, dy);
        if (next.Width < 20) next.Width = 20;
        if (next.Height < 16) next.Height = 16;
        if (_drag == 1) _ocr = next; else _caption = next;
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drag != 0)
        {
            _drag = 0;
            Capture = false;
            RegionsChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseUp(e);
    }

    private static Rectangle ResizeRect(Rectangle r, int handle, int dx, int dy)
    {
        int x = r.X, y = r.Y, w = r.Width, h = r.Height;
        if (handle is 0 or 6 or 7) { x += dx; w -= dx; }
        if (handle is 2 or 3 or 4) w += dx;
        if (handle is 0 or 1 or 2) { y += dy; h -= dy; }
        if (handle is 4 or 5 or 6) h += dy;
        return new Rectangle(x, y, w, h);
    }

    private bool TryHandle(Rectangle screen, Point client, out int handle)
    {
        handle = -1;
        if (screen.Width < 8) return false;
        var handles = Handles(ToClient(screen));
        for (int i = 0; i < handles.Length; i++)
        {
            var h = handles[i];
            h.Inflate(4, 4);
            if (h.Contains(client)) { handle = i; return true; }
        }
        return false;
    }
}
