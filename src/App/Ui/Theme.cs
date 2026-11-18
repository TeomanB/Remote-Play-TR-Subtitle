using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AltyaziCevirici.Ui;

/// <summary>Light theme from docs/design/claude-a: colors, pixel fonts, and drawing helpers.</summary>
internal static class Theme
{
    public static float S { get; private set; } = 1f;

    /// <summary>Design pixels (96 dpi) to device pixels.</summary>
    public static int D(float v) => (int)Math.Round(v * S);

    public static readonly Color Acc = Hex(0x14B8B2);
    public static readonly Color AccD = Hex(0x0A8480);
    public static readonly Color AccDD = Hex(0x086E6B);
    public static readonly Color AccPressed = Hex(0x075E5B);
    public static readonly Color AccT = Hex(0xE4F7F6);
    public static readonly Color AccT2 = Hex(0xC8EEEC);
    public static readonly Color AccInk = Hex(0x0B4F4D);
    public static readonly Color Bg = Hex(0xF3F5F7);
    public static readonly Color Card = Color.White;
    public static readonly Color Subtle = Hex(0xF6F8FA);
    public static readonly Color Pressed = Hex(0xEEF1F4);
    public static readonly Color SegBg = Hex(0xE9EDF1);
    public static readonly Color Chip = Hex(0xEDF0F3);
    public static readonly Color Line = Hex(0xEBEDEF);
    public static readonly Color Line2 = Hex(0xD6DADE);
    public static readonly Color Tx = Hex(0x17212B);
    public static readonly Color Tx2 = Hex(0x566371);
    public static readonly Color Tx3 = Hex(0x8793A0);
    public static readonly Color Ok = Hex(0x15803D);
    public static readonly Color OkT = Hex(0xE7F6EC);
    public static readonly Color OkDot = Hex(0x16A34A);
    public static readonly Color Warn = Hex(0xB45309);
    public static readonly Color WarnT = Hex(0xFDF1DC);
    public static readonly Color Red = Hex(0xB42318);
    public static readonly Color RedLine = Hex(0xEFC4BF);
    public static readonly Color RedT = Hex(0xFEF3F2);
    public static readonly Color CloseHover = Hex(0xC42B1C);
    public static readonly Color KeyOrig = Hex(0xF5A524);
    public static readonly Color BoxOrig = Hex(0xFFB020);
    public static readonly Color BoxTrans = Hex(0x35E0D8);
    public static readonly Color TagInk = Hex(0x0E1F29);
    public static readonly Color Stage = Hex(0x1B1530);
    public static readonly Color Neutral = Hex(0xA6B0BA);
    public static readonly Color ToggleOff = Hex(0x8A96A3);
    public static readonly Color Scroll = Hex(0xC5CDD5);

    public static Font Body { get; private set; } = null!;
    public static Font BodySemi { get; private set; } = null!;
    public static Font Small { get; private set; } = null!;
    public static Font SmallSemi { get; private set; } = null!;
    public static Font Caption { get; private set; } = null!;
    public static Font Nav { get; private set; } = null!;
    public static Font NavSemi { get; private set; } = null!;
    public static Font H1 { get; private set; } = null!;
    public static Font H2 { get; private set; } = null!;
    public static Font Brand { get; private set; } = null!;
    public static Font Big { get; private set; } = null!;
    public static Font ButtonLg { get; private set; } = null!;
    public static Font Title { get; private set; } = null!;
    public static Font Tag { get; private set; } = null!;
    public static Font Icon12 { get; private set; } = null!;
    public static Font Icon16 { get; private set; } = null!;
    public static Font Icon20 { get; private set; } = null!;

    /// <summary>Raised after <see cref="SetScale"/> rebuilt the fonts.</summary>
    public static event EventHandler? Changed;

    static Theme() => Build();

    public static void SetScale(float scale)
    {
        scale = Math.Clamp(scale, 0.75f, 4f);
        if (Math.Abs(scale - S) < 0.001f && Body != null) return;
        S = scale;
        Build();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void Build()
    {
        string[] text = { "Segoe UI Variable Text", "Segoe UI" };
        string[] textSemi = { "Segoe UI Variable Text Semibold", "Segoe UI Variable Text Semibol", "Segoe UI Semibold" };
        string[] displaySemi = { "Segoe UI Variable Display Semib", "Segoe UI Variable Display Semibold", "Segoe UI Semibold" };
        string[] icons = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };
        Body = Make(text, 13.5f);
        BodySemi = Make(textSemi, 13.5f);
        Small = Make(text, 12f);
        SmallSemi = Make(textSemi, 12.5f);
        Caption = Make(textSemi, 11.5f);
        Nav = Make(text, 13.5f);
        NavSemi = Make(textSemi, 13.5f);
        H1 = Make(displaySemi, 22f);
        H2 = Make(displaySemi, 16f);
        Brand = Make(displaySemi, 14f);
        Big = Make(textSemi, 15f);
        ButtonLg = Make(textSemi, 15f);
        Title = Make(text, 12.5f);
        Tag = Make(textSemi, 11f);
        Icon12 = Make(icons, 12f);
        Icon16 = Make(icons, 16f);
        Icon20 = Make(icons, 20f);
    }

    /// <summary>First installed family. GDI+ silently substitutes missing names, so the result is checked.</summary>
    private static Font Make(string[] families, float px)
    {
        foreach (var name in families)
        {
            var f = new Font(name, Math.Max(1f, px * S), FontStyle.Regular, GraphicsUnit.Pixel);
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
        }
        return new Font("Segoe UI", Math.Max(1f, px * S), FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static readonly Dictionary<string, bool> Installed = new(StringComparer.OrdinalIgnoreCase);

    public static bool FontInstalled(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return false;
        if (Installed.TryGetValue(family, out bool known)) return known;
        using var f = new Font(family, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        bool ok = string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase);
        Installed[family] = ok;
        return ok;
    }

    public static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(a.A + (b.A - a.A) * t),
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static Color Alpha(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);

    public static void HighQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
    {
        using var path = Round(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c, float width = 1f)
    {
        var inset = RectangleF.Inflate(r, -width / 2f, -width / 2f);
        using var path = Round(inset, Math.Max(0, radius - width / 2f));
        using var pen = new Pen(c, width);
        g.DrawPath(pen, path);
    }

    /// <summary>Soft card shadow: stacked translucent rounded rectangles, wider and fainter outward.</summary>
    public static void Shadow(Graphics g, RectangleF r, float radius, float spread, int strength = 14)
    {
        int steps = Math.Max(3, (int)Math.Round(spread));
        for (int i = steps; i >= 1; i--)
        {
            float t = i / (float)steps;
            var rr = RectangleF.Inflate(r, i * 0.9f, i * 0.9f);
            rr.Offset(0, D(1.5f) * (1 - t) + D(1));
            int a = (int)Math.Round(strength * (1 - t) * (1 - t) + 1);
            FillRound(g, rr, radius + i, Color.FromArgb(a, 15, 30, 45));
        }
    }

    public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
    public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
    public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
    public const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    public static void Text(Graphics g, string? s, Font f, Rectangle r, Color c, TextFormatFlags flags = Left) =>
        TextRenderer.DrawText(g, s ?? "", f, r, c, flags);

    public static Size Measure(string? s, Font f, TextFormatFlags flags = Left) =>
        TextRenderer.MeasureText(s ?? "", f, new Size(int.MaxValue, int.MaxValue), flags);

    public static int WrapHeight(string? s, Font f, int width) =>
        TextRenderer.MeasureText(s ?? "", f, new Size(Math.Max(1, width), int.MaxValue), Wrap).Height;

    public static void Glyph(Graphics g, string glyph, Font iconFont, Rectangle r, Color c) =>
        TextRenderer.DrawText(g, glyph, iconFont, r, c, Center);
}

/// <summary>Segoe Fluent Icons / MDL2 code points.</summary>
internal static class Ic
{
    public const string Crop = "";
    public const string Font = "";
    public const string Book = "";
    public const string Sliders = "";
    public const string Play = "";
    public const string Stop = "";
    public const string Refresh = "";
    public const string Sync = "";
    public const string Globe = "";
    public const string Power = "";
    public const string Bolt = "";
    public const string Info = "";
    public const string Reset = "";
    public const string Search = "";
    public const string Add = "";
    public const string Arrow = "";
    public const string Check = "";
    public const string Lock = "";
    public const string Unlock = "";
    public const string Close = "";
    public const string Down = "";
    public const string Chevron = "";
    public const string Back = "";
    public const string Delete = "";
    public const string Download = "";
    public const string Upload = "";
    public const string Keyboard = "";
    public const string Filter = "";
    public const string Screen = "";
    public const string Select = "";
    public const string Warning = "";
    public const string Min = "";
    public const string Max = "";
    public const string Restore = "";
    public const string ChromeClose = "";
}

/// <summary>The turquoise console mascot (vector port of the design's 64×64 symbol).</summary>
internal static class Mascot
{
    public static void Draw(Graphics g, RectangleF r)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 64f, r.Height / 64f);
        var dark = Theme.Hex(0x0A8480);
        using (var arm = new Pen(dark, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawBezier(arm, 12f, 34f, 8f, 35.33f, 6f, 38.67f, 6f, 42f);
            g.DrawBezier(arm, 52f, 34f, 56f, 35.33f, 58f, 38.67f, 58f, 42f);
        }
        Theme.FillRound(g, new RectangleF(20, 52, 7, 8), 3, dark);
        Theme.FillRound(g, new RectangleF(37, 52, 7, 8), 3, dark);
        using (var body = Theme.Round(new RectangleF(11, 6, 42, 49), 10))
        {
            using var shade = new SolidBrush(Theme.Hex(0x0FA39D));
            g.FillPath(shade, body);
        }
        Theme.FillRound(g, new RectangleF(11, 6, 42, 45), 10, Theme.Hex(0x18BDB6));
        Theme.FillRound(g, new RectangleF(16, 11, 32, 22), 5, Theme.Hex(0xDDFBF7));
        var ink = Theme.Hex(0x0B4F4D);
        using (var eye = new SolidBrush(ink))
        {
            g.FillEllipse(eye, 24f, 17.5f, 4f, 4f);
            g.FillEllipse(eye, 36f, 17.5f, 4f, 4f);
        }
        using (var smile = new Pen(ink, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawBezier(smile, 28f, 23.5f, 30.67f, 25.83f, 33.33f, 25.83f, 36f, 23.5f);
        Theme.FillRound(g, new RectangleF(22, 28, 20, 2.2f), 1.1f, Color.FromArgb(190, 0x18, 0xBD, 0xB6));
        using (var pad = new GraphicsPath())
        {
            pad.AddPolygon(new PointF[]
            {
                new(21, 40.5f), new(24, 40.5f), new(24, 37.5f), new(27, 37.5f), new(27, 40.5f), new(30, 40.5f),
                new(30, 43.5f), new(27, 43.5f), new(27, 46.5f), new(24, 46.5f), new(24, 43.5f), new(21, 43.5f),
            });
            using var b = new SolidBrush(Theme.Hex(0x086E6B));
            g.FillPath(b, pad);
        }
        using (var y = new SolidBrush(Theme.Hex(0xFFD166))) g.FillEllipse(y, 36.4f, 37.4f, 5.2f, 5.2f);
        using (var red = new SolidBrush(Theme.Hex(0xFF7A7A))) g.FillEllipse(red, 42.4f, 41.9f, 5.2f, 5.2f);
        g.Restore(state);
    }
}
