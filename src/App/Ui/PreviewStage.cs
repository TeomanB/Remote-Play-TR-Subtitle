using System.Drawing.Drawing2D;
using AltyaziCevirici.Core;

namespace AltyaziCevirici.Ui;

/// <summary>
/// Live preview with the two editable boxes. Box rectangles are absolute screen pixels; the stage maps them onto
/// the captured frame of <see cref="Source"/>. The selected box is drawn and hit-tested on top; a locked box
/// cannot move and lets clicks fall through to the box under it (its tag still selects it).
/// </summary>
internal sealed class PreviewStage : Control
{
    public const int Orig = 0, Trans = 1, Goal = 2;
    public const int ExtraCapture = 100, ExtraOverlay = 200;
    private static readonly string[] Names = { "Orijinal altyazı", "Çeviri", "Görev" };

    private readonly Rectangle[] _box = new Rectangle[3];
    private readonly bool[] _locked = new bool[3];
    private readonly List<PreviewExtra> _extras = new();
    private Bitmap? _frame;
    private Rectangle _source = new(0, 0, 1920, 1080);
    private int _selected = Trans;
    private AppSettings _look = new();
    private string _sample = "";

    private int _dragBox = -1;
    private int _dragPart = -2; // -1 body, 0..7 handles
    private bool _live;
    private Point _grab;
    private Rectangle _grabRect;

    /// <summary>Selection changed (click, tag, or code).</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>A box moved or resized (live, while dragging).</summary>
    public event EventHandler? BoxesChanged;
    /// <summary>Drag finished: persist now.</summary>
    public event EventHandler? BoxesCommitted;

    public PreviewStage()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Card;
        TabStop = true;
    }

    public Rectangle Source => _source;
    public int Selected => _selected;
    public Rectangle Box(int i) => Get(i);
    public int ExtraCount => _extras.Count;
    public PreviewExtra Extra(int i) => _extras[i];

    public void SetExtras(IReadOnlyList<PreviewExtra> extras)
    {
        _extras.Clear();
        foreach (var e in extras) _extras.Add(e.Copy());
        if (ExtraIndex(_selected) >= _extras.Count) _selected = Trans;
        Invalidate();
    }
    /// <summary>Box rectangle in this control's client pixels, including where the handles sit.</summary>
    public Rectangle BoxClient(int i)
    {
        var f = ToClient(_box[i]);
        var r = Rectangle.Round(f);
        r.Inflate(10, 14);
        return r;
    }
    public bool IsLocked(int i) => i >= 0 && i < _locked.Length && _locked[i];
    public bool HasFrame => _frame != null;

    public void SetFrame(Bitmap? frame, Rectangle source)
    {
        var old = _frame;
        _frame = frame;
        if (source.Width > 0 && source.Height > 0) _source = source;
        old?.Dispose();
        Invalidate();
    }

    public void SetSource(Rectangle source)
    {
        if (source.Width <= 0 || source.Height <= 0 || source == _source) return;
        _source = source;
        Invalidate();
    }

    public void SetBox(int i, Rectangle r)
    {
        Put(i, r);
        Invalidate();
    }

    public void SetLook(AppSettings look, string sample)
    {
        _look = look;
        _sample = sample;
        Invalidate();
    }

    public void Select(int i)
    {
        if (_selected == i) return;
        _selected = i;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLocked(int i, bool locked)
    {
        if (i < 0 || i >= _locked.Length) return;
        _locked[i] = locked;
        Invalidate();
    }

    public static int MinWidth(int i) => i == Orig || (i >= ExtraCapture && i < ExtraOverlay) ? 40 : SubtitleLayout.MinWidth;
    public static int MinHeight(int i) => i == Orig || (i >= ExtraCapture && i < ExtraOverlay) ? 16 : 24;

    public static int ExtraIndexOf(int i) => i >= ExtraOverlay ? i - ExtraOverlay : i >= ExtraCapture ? i - ExtraCapture : -1;
    private static int ExtraIndex(int i) => ExtraIndexOf(i);

    private Rectangle Get(int i)
    {
        int extra = ExtraIndex(i);
        if (extra >= 0 && extra < _extras.Count) return i >= ExtraOverlay ? _extras[extra].Overlay : _extras[extra].Capture;
        return i >= 0 && i < _box.Length ? _box[i] : Rectangle.Empty;
    }

    private void Put(int i, Rectangle r)
    {
        int extra = ExtraIndex(i);
        if (extra >= 0 && extra < _extras.Count)
        {
            if (i >= ExtraOverlay) _extras[extra].Overlay = r;
            else _extras[extra].Capture = r;
            return;
        }
        if (i >= 0 && i < _box.Length) _box[i] = r;
    }

    // ---------------- mapping ----------------

    /// <summary>Where the source frame sits inside the control (letterboxed, aspect kept).</summary>
    private RectangleF Map()
    {
        if (_source.Width <= 0 || _source.Height <= 0) return ClientRectangle;
        float s = Math.Min(Width / (float)_source.Width, Height / (float)_source.Height);
        float w = _source.Width * s, h = _source.Height * s;
        return new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
    }

    private float Zoom => Map().Width / Math.Max(1f, _source.Width);

    private RectangleF ToClient(Rectangle r)
    {
        var m = Map();
        float s = m.Width / Math.Max(1f, _source.Width);
        return new RectangleF(m.X + (r.X - _source.X) * s, m.Y + (r.Y - _source.Y) * s, r.Width * s, r.Height * s);
    }

    private string LabelOf(int i)
    {
        int extra = ExtraIndex(i);
        if (extra >= 0 && extra < _extras.Count)
            return i >= ExtraOverlay ? _extras[extra].Name + " · çeviri" : _extras[extra].Name;
        return i >= 0 && i < Names.Length ? Names[i] : "";
    }

    private Color ColorOf(int i)
    {
        int extra = ExtraIndex(i);
        if (extra >= 0 && extra < _extras.Count && AppSettings.TryParseColor(_extras[extra].ColorHex, out var rgb))
            return Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
        return i == Goal ? Theme.Hex(0x7A4FBF) : i == Orig ? Theme.BoxOrig : Theme.BoxTrans;
    }

    private RectangleF TagRect(int i)
    {
        var b = ToClient(Get(i));
        string text = LabelOf(i);
        int w = Theme.Measure(text, Theme.Tag).Width + Theme.D(14) + (IsLocked(i) ? Theme.D(14) : 0);
        float h = Theme.D(19);
        float y = b.Y - h - Theme.D(5);
        if (y < Theme.D(2)) y = b.Bottom + Theme.D(5);
        return new RectangleF(b.X - 1, y, w, h);
    }

    private static PointF[] HandlePoints(RectangleF b) => new PointF[]
    {
        new(b.Left, b.Top), new(b.Left + b.Width / 2, b.Top), new(b.Right, b.Top), new(b.Right, b.Top + b.Height / 2),
        new(b.Right, b.Bottom), new(b.Left + b.Width / 2, b.Bottom), new(b.Left, b.Bottom), new(b.Left, b.Top + b.Height / 2),
    };

    private static readonly Cursor[] HandleCursors =
    {
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
    };

    /// <summary>Hit test, top box first. Part: -3 tag, -1 body, 0..7 handles.</summary>
    private IEnumerable<int> HitOrder()
    {
        yield return _selected;
        for (int i = 0; i < _extras.Count; i++) yield return ExtraOverlay + i;
        for (int i = 0; i < _extras.Count; i++) yield return ExtraCapture + i;
        yield return Goal;
        yield return Trans;
        yield return Orig;
    }

    private (int Box, int Part) Hit(Point p)
    {
        var seen = new HashSet<int>();
        foreach (int i in HitOrder())
        {
            if (!seen.Add(i) || Get(i).IsEmpty) continue;
            if (TagRect(i).Contains(p)) return (i, -3);
        }
        seen.Clear();
        foreach (int i in HitOrder())
        {
            if (!seen.Add(i) || Get(i).IsEmpty || IsLocked(i)) continue;
            var b = ToClient(Get(i));
            if (i == _selected)
            {
                var pts = HandlePoints(b);
                float reach = Theme.D(8);
                for (int h = 0; h < pts.Length; h++)
                    if (Math.Abs(p.X - pts[h].X) <= reach && Math.Abs(p.Y - pts[h].Y) <= reach) return (i, h);
            }
            if (b.Contains(p)) return (i, -1);
        }
        return (-1, -2);
    }

    // ---------------- input ----------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        var (box, part) = Hit(e.Location);
        if (box < 0)
        {
            // Nothing there yet: draw the OCR strip directly (first run without a region).
            if (Get(Orig).IsEmpty && Map().Contains(e.Location))
            {
                var p = ToSource(e.Location);
                Put(Orig, new Rectangle(p.X, p.Y, 1, 1));
                Select(Orig);
                Begin(Orig, 4, e.Location, live: true);
            }
            return;
        }
        Select(box);
        if (part == -3 || _locked[box]) return;
        Begin(box, part, e.Location, live: false);
    }

    private void Begin(int box, int part, Point at, bool live)
    {
        _dragBox = box;
        _dragPart = part;
        _grab = at;
        _grabRect = Get(box);
        _live = live;
        Capture = true;
    }

    private Point ToSource(Point client)
    {
        var m = Map();
        float s = _source.Width / Math.Max(1f, m.Width);
        return new Point(_source.X + (int)Math.Round((client.X - m.X) * s), _source.Y + (int)Math.Round((client.Y - m.Y) * s));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragBox < 0)
        {
            var (box, part) = Hit(e.Location);
            Cursor = box < 0 ? Cursors.Default : part == -3 ? Cursors.Hand : part == -1 ? Cursors.SizeAll : HandleCursors[part];
            return;
        }
        if (!_live)
        {
            // A click selects; it only moves once the pointer travelled a few pixels.
            if (Math.Abs(e.X - _grab.X) < Theme.D(4) && Math.Abs(e.Y - _grab.Y) < Theme.D(4)) return;
            _live = true;
        }
        float s = _source.Width / Math.Max(1f, Map().Width);
        int dx = (int)Math.Round((e.X - _grab.X) * s), dy = (int)Math.Round((e.Y - _grab.Y) * s);
        Put(_dragBox, Apply(_grabRect, _dragPart, dx, dy, _dragBox));
        Invalidate();
        BoxesChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragBox < 0) return;
        bool moved = _live;
        _dragBox = -1;
        _live = false;
        Capture = false;
        if (moved) BoxesCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_dragBox >= 0 && !Capture)
        {
            bool moved = _live;
            _dragBox = -1;
            _live = false;
            if (moved) BoxesCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step = e.Shift ? 10 : 1;
        int dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
        int dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
        if ((dx == 0 && dy == 0) || IsLocked(_selected) || Get(_selected).IsEmpty) return;
        Put(_selected, Apply(Get(_selected), -1, dx, dy, _selected));
        Invalidate();
        BoxesChanged?.Invoke(this, EventArgs.Empty);
        BoxesCommitted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>Move (part -1) or resize from a handle, kept inside the source and above the minimum size.</summary>
    public Rectangle Apply(Rectangle r, int part, int dx, int dy, int box)
    {
        var src = _source;
        int minW = Math.Min(MinWidth(box), src.Width), minH = Math.Min(MinHeight(box), src.Height);
        int l = r.Left, t = r.Top, rr = r.Right, b = r.Bottom;
        if (part == -1)
        {
            int w = r.Width, h = r.Height;
            int x = Math.Clamp(l + dx, src.Left, Math.Max(src.Left, src.Right - w));
            int y = Math.Clamp(t + dy, src.Top, Math.Max(src.Top, src.Bottom - h));
            return new Rectangle(x, y, w, h);
        }
        bool west = part is 0 or 6 or 7, east = part is 2 or 3 or 4, north = part is 0 or 1 or 2, south = part is 4 or 5 or 6;
        if (west) l = Math.Clamp(l + dx, src.Left, rr - minW);
        if (east) rr = Math.Clamp(rr + dx, l + minW, src.Right);
        if (north) t = Math.Clamp(t + dy, src.Top, b - minH);
        if (south) b = Math.Clamp(b + dy, t + minH, src.Bottom);
        return Rectangle.FromLTRB(l, t, rr, b);
    }

    // ---------------- painting ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.HighQuality(g);
        var full = new RectangleF(0, 0, Width, Height);
        using var clip = Theme.Round(full, Theme.D(8));
        var state = g.Save();
        g.SetClip(clip);
        using (var stage = new SolidBrush(Theme.Hex(0x0E1117))) g.FillRectangle(stage, full);
        var m = Map();
        if (_frame != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(_frame, m);
        }
        else Placeholder.Draw(g, m);

        for (int i = 0; i < _extras.Count; i++)
        {
            if (ExtraCapture + i == _selected || ExtraOverlay + i == _selected) continue;
            DrawBox(g, ExtraCapture + i);
            DrawBox(g, ExtraOverlay + i);
        }
        for (int i = 0; i < _box.Length; i++) if (i != _selected) DrawBox(g, i);
        DrawBox(g, _selected);
        for (int i = 0; i < _extras.Count; i++)
        {
            if (ExtraCapture + i == _selected || ExtraOverlay + i == _selected) continue;
            DrawTag(g, ExtraCapture + i);
            DrawTag(g, ExtraOverlay + i);
        }
        for (int i = 0; i < _box.Length; i++) if (i != _selected) DrawTag(g, i);
        DrawTag(g, _selected);
        g.Restore(state);
    }

    private void DrawTag(Graphics g, int i)
    {
        if (Get(i).IsEmpty) return;
        bool sel = i == _selected, locked = IsLocked(i);
        var color = ColorOf(i);
        float fade = !sel && locked ? 0.75f : 1f;
        var tag = TagRect(i);
        var tagFill = sel ? color : Theme.Mix(color, Theme.Stage, 0.3f);
        Theme.FillRound(g, tag, Theme.D(6), Theme.Alpha(tagFill, (int)(255 * fade)));
        int tx = (int)tag.X + Theme.D(7);
        if (locked)
        {
            Theme.Glyph(g, Ic.Lock, Theme.Icon12, new Rectangle(tx - Theme.D(1), (int)tag.Y, Theme.D(13), (int)tag.Height), Theme.TagInk);
            tx += Theme.D(14);
        }
        Theme.Text(g, LabelOf(i), Theme.Tag, new Rectangle(tx, (int)tag.Y, (int)tag.Right - tx, (int)tag.Height), Theme.TagInk);
    }

    private void DrawBox(Graphics g, int i)
    {
        if (Get(i).IsEmpty) return;
        var b = ToClient(Get(i));
        bool sel = i == _selected;
        bool locked = IsLocked(i);
        var color = ColorOf(i);
        float fade = !sel && locked ? 0.75f : 1f;
        var line = Theme.Alpha(color, (int)(255 * fade));

        if (i == Trans) DrawSample(g, b, _sample, null, null);
        else if (i == Goal) DrawSample(g, b, "Phoenix'i yeniden boya.", "#EDE4FF", "#7A4FBF");
        else if (i >= ExtraOverlay)
        {
            var extra = _extras[i - ExtraOverlay];
            DrawSample(g, b, extra.Sample, "#FFF1F4", extra.ColorHex);
        }

        float radius = Theme.D(7);
        if (sel)
        {
            using var glow = Theme.Round(RectangleF.Inflate(b, Theme.D(2), Theme.D(2)), radius + Theme.D(2));
            using var gp = new Pen(Theme.Alpha(color, 82), Theme.D(4));
            g.DrawPath(gp, glow);
        }
        using (var path = Theme.Round(b, radius))
        using (var pen = new Pen(line, sel ? Theme.D(2) : Theme.D(1.5f)))
        {
            if (!sel && !locked) pen.DashPattern = new[] { 4f, 3f };
            g.DrawPath(pen, path);
        }

        if (!sel || locked) return;
        float hs = Theme.D(11);
        using var hb = new SolidBrush(Color.White);
        using var hp = new Pen(color, Theme.D(2));
        foreach (var p in HandlePoints(b))
        {
            var r = new RectangleF(p.X - hs / 2, p.Y - hs / 2, hs, hs);
            using var hpath = Theme.Round(r, Theme.D(3));
            g.FillPath(hb, hpath);
            g.DrawPath(hp, hpath);
        }
    }

    /// <summary>Sample Turkish line with the overlay's font, color, outline, and background opacity, scaled to the preview.</summary>
    private void DrawSample(Graphics g, RectangleF b, string? text, string? textColor, string? outlineColor)
    {
        float k = Zoom;
        int alpha = (int)Math.Round(Math.Clamp(_look.ArkaPlanOpakligi, 0, 100) * 2.55);
        if (alpha > 0) Theme.FillRound(g, b, Theme.D(6), Color.FromArgb(alpha, 10, 10, 14));
        if (string.IsNullOrWhiteSpace(text)) return;

        string family = SubtitleFonts.Resolve(_look.YaziTipi);
        float em = Math.Max(4f, _look.YaziBoyutu * k);
        using var font = new Font(family, em, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        float padX = 18 * k;
        var layout = new RectangleF(b.X + padX, b.Y, Math.Max(10, b.Width - 2 * padX), b.Height);
        var size = g.MeasureString(text, font, (int)layout.Width, fmt);
        layout.Y = b.Y + Math.Max(0, (b.Height - size.Height) / 2f);
        layout.Height = Math.Max(size.Height + 2, b.Height);

        var state = g.Save();
        g.SetClip(RectangleF.Inflate(b, 2, 2), CombineMode.Intersect);
        using var path = new GraphicsPath();
        path.AddString(text, font.FontFamily, (int)FontStyle.Bold, em, layout, fmt);
        float outline = Math.Clamp(_look.DisCizgi, 0, 16) * k;
        if (outline > 0.2f)
        {
            var oc = AppSettings.TryParseColor(outlineColor ?? _look.DisCizgiRengi, out var c2) ? Color.FromArgb(255, c2.R, c2.G, c2.B) : Color.Black;
            using var pen = new Pen(oc, outline) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        var fc = AppSettings.TryParseColor(textColor ?? _look.YaziRengi, out var c) ? Color.FromArgb(255, c.R, c.G, c.B) : Color.White;
        using (var fill = new SolidBrush(fc)) g.FillPath(fill, path);
        g.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _frame?.Dispose(); _frame = null; }
        base.Dispose(disposing);
    }
}

internal sealed class PreviewExtra
{
    public string Id = "";
    public string Name = "";
    public string ColorHex = "#E8456B";
    public bool Enabled = true;
    public Rectangle Capture;
    public Rectangle Overlay;
    public string Sample = "Türbine bakmak için R3'e bas.";

    public PreviewExtra Copy() => (PreviewExtra)MemberwiseClone();
}

/// <summary>Game-like scene shown when no screen frame is available yet.</summary>
internal static class Placeholder
{
    public static void Draw(Graphics g, RectangleF r)
    {
        if (r.Width < 4 || r.Height < 4) return;
        var state = g.Save();
        g.SetClip(r, CombineMode.Intersect);
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 1600f, r.Height / 900f);
        using (var sky = new LinearGradientBrush(new RectangleF(0, 0, 1600, 600), Color.Black, Color.Black, 90f))
        {
            sky.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Theme.Hex(0x27286B), Theme.Hex(0x9A3F8F), Theme.Hex(0xFF8A5B), Theme.Hex(0xFFD28C) },
                Positions = new[] { 0f, 0.45f, 0.8f, 1f },
            };
            g.FillRectangle(sky, 0, 0, 1600, 600);
        }
        using (var glow = new GraphicsPath())
        {
            glow.AddEllipse(800, 260, 520, 520);
            using var pb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(200, 255, 246, 208), SurroundColors = new[] { Color.FromArgb(0, 255, 179, 107) } };
            g.FillPath(pb, glow);
        }
        using (var sun = new SolidBrush(Theme.Hex(0xFFF1C2))) g.FillEllipse(sun, 982, 442, 156, 156);
        int[][] far = { new[] { 60, 430, 70, 130 }, new[] { 150, 390, 54, 170 }, new[] { 330, 410, 90, 150 }, new[] { 560, 360, 60, 200 }, new[] { 640, 440, 110, 120 }, new[] { 860, 400, 70, 160 }, new[] { 1180, 420, 90, 140 }, new[] { 1380, 380, 64, 180 }, new[] { 1470, 450, 100, 110 } };
        using (var b = new SolidBrush(Color.FromArgb(190, 0x6A, 0x42, 0x9C))) foreach (var a in far) g.FillRectangle(b, a[0], a[1], a[2], a[3]);
        int[][] near = { new[] { 0, 470, 90, 90 }, new[] { 100, 440, 60, 120 }, new[] { 220, 330, 78, 230 }, new[] { 310, 480, 120, 80 }, new[] { 450, 420, 84, 140 }, new[] { 700, 490, 140, 70 }, new[] { 940, 460, 60, 100 }, new[] { 1250, 350, 86, 210 }, new[] { 1350, 470, 130, 90 }, new[] { 1500, 420, 100, 140 } };
        using (var b = new SolidBrush(Theme.Hex(0x2A1B4D))) foreach (var a in near) g.FillRectangle(b, a[0], a[1], a[2], a[3]);
        using (var win = new SolidBrush(Color.FromArgb(200, 255, 217, 138)))
            foreach (var (x, y) in new[] { (236, 360), (262, 400), (236, 450), (470, 446), (500, 486), (1270, 380), (1300, 430), (1270, 490), (1530, 450), (118, 470) })
                g.FillRectangle(win, x, y, 8, 10);
        using (var ground = new SolidBrush(Theme.Hex(0x231A3A))) g.FillRectangle(ground, 0, 560, 1600, 340);
        using (var road = new LinearGradientBrush(new RectangleF(0, 560, 1600, 340), Theme.Hex(0x57407A), Theme.Hex(0x1E1830), 90f))
            g.FillPolygon(road, new PointF[] { new(700, 560), new(900, 560), new(1560, 900), new(40, 900) });
        using (var lane = new SolidBrush(Color.FromArgb(215, 255, 231, 168)))
        {
            g.FillPolygon(lane, new PointF[] { new(796, 578), new(804, 578), new(806, 606), new(794, 606) });
            g.FillPolygon(lane, new PointF[] { new(792, 636), new(808, 636), new(812, 694), new(788, 694) });
            g.FillPolygon(lane, new PointF[] { new(784, 744), new(816, 744), new(826, 868), new(774, 868) });
        }
        using (var car = new SolidBrush(Theme.Hex(0xE8456B))) g.FillRectangle(car, 905, 622, 164, 50);
        using (var roof = new SolidBrush(Theme.Hex(0xB8324F))) g.FillPolygon(roof, new PointF[] { new(935, 626), new(955, 598), new(1019, 598), new(1039, 626) });
        using (var lights = new SolidBrush(Theme.Hex(0xFFE08A))) { g.FillRectangle(lights, 913, 636, 34, 10); g.FillRectangle(lights, 1027, 636, 34, 10); }
        g.Restore(state);
    }
}

/// <summary>Subtitle font choices. Missing families fall back to Segoe UI (GDI+ would silently pick another font).</summary>
internal static class SubtitleFonts
{
    public static readonly string[] Choices = { "Segoe UI", "Segoe UI Variable Display", "Bahnschrift", "Arial", "Verdana", "Tahoma", "Trebuchet MS", "Georgia", "Comic Sans MS" };

    public static string Resolve(string? family) =>
        !string.IsNullOrWhiteSpace(family) && Theme.FontInstalled(family) ? family : "Segoe UI";

    public static IEnumerable<string> Available() => Choices.Where(Theme.FontInstalled);
}
