using System.Drawing.Drawing2D;

namespace AltyaziCevirici;

internal static class UiTheme
{
    public static readonly Color Bg = Color.FromArgb(18, 20, 24);
    public static readonly Color Card = Color.FromArgb(30, 33, 40);
    public static readonly Color CardEdge = Color.FromArgb(54, 60, 72);
    public static readonly Color Text = Color.FromArgb(232, 234, 238);
    public static readonly Color Muted = Color.FromArgb(154, 160, 172);
    public static readonly Color Accent = Color.FromArgb(62, 214, 176);
    public static readonly Color AccentInk = Color.FromArgb(12, 28, 24);
    public static readonly Color Danger = Color.FromArgb(232, 84, 74);
    public static readonly Color Ocr = Color.FromArgb(255, 186, 64);
    public static readonly Color Caption = Color.FromArgb(62, 214, 176);

    public static Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        foreach (var name in new[] { "Segoe UI Variable", "Segoe UI" })
        {
            try { return new Font(name, size, style, GraphicsUnit.Point); }
            catch (ArgumentException) { /* family missing on this machine */ }
        }
        return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
    }
}

internal sealed class FlatButton : Control
{
    private bool _hover, _down;
    public bool Primary { get; set; }
    public bool Danger { get; set; }

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Height = 36;
        Width = 120;
        Font = UiTheme.Font(10f, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = !Enabled ? Color.FromArgb(48, 52, 60)
            : Danger ? (_down ? Color.FromArgb(190, 60, 52) : _hover ? Color.FromArgb(214, 96, 86) : UiTheme.Danger)
            : Primary ? (_down ? Color.FromArgb(40, 170, 140) : _hover ? Color.FromArgb(90, 230, 196) : UiTheme.Accent)
            : (_down ? Color.FromArgb(48, 54, 64) : _hover ? Color.FromArgb(46, 52, 62) : UiTheme.Card);
        Color ink = Primary && Enabled ? UiTheme.AccentInk : UiTheme.Text;
        using var path = Round(rect, 8);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        if (!Primary && !Danger)
        {
            using var pen = new Pen(UiTheme.CardEdge);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, rect, Enabled ? ink : UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    internal static GraphicsPath Round(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

internal sealed class ToggleSwitch : Control
{
    private bool _on;
    public bool On
    {
        get => _on;
        set { if (_on == value) return; _on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(44, 24);
        Cursor = Cursors.Hand;
    }

    protected override void OnClick(EventArgs e) { On = !On; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 2, Width - 1, Height - 5);
        using var path = FlatButton.Round(track, track.Height / 2);
        using var brush = new SolidBrush(_on ? UiTheme.Accent : Color.FromArgb(58, 64, 74));
        g.FillPath(brush, path);
        int d = Height - 8;
        int x = _on ? Width - d - 4 : 4;
        using var knob = new SolidBrush(_on ? UiTheme.AccentInk : Color.White);
        g.FillEllipse(knob, x, 4, d, d);
    }
}

internal sealed class FlatSlider : Control
{
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    private int _value;
    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? ValueChanged;
    private bool _drag;

    public FlatSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Height = 28;
        Width = 220;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; SetFrom(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFrom(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }

    private void SetFrom(int x)
    {
        float t = Math.Clamp((x - 8f) / Math.Max(1, Width - 16), 0, 1);
        Value = Minimum + (int)Math.Round(t * (Maximum - Minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int y = Height / 2;
        float t = Maximum == Minimum ? 0 : (_value - Minimum) / (float)(Maximum - Minimum);
        int x = 8 + (int)(t * (Width - 16));
        using var track = new Pen(Color.FromArgb(58, 64, 74), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new Pen(UiTheme.Accent, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(track, 8, y, Width - 8, y);
        g.DrawLine(fill, 8, y, x, y);
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, x - 7, y - 7, 14, 14);
    }
}
