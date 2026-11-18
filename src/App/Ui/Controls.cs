using System.Drawing.Drawing2D;

namespace AltyaziCevirici.Ui;

/// <summary>Owner-drawn control with hover / pressed tracking. Paints its own background (BackColor = surface).</summary>
internal class UiControl : Control
{
    protected bool Hover { get; private set; }
    protected bool Down { get; private set; }

    public UiControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Card;
        ForeColor = Theme.Tx;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected Graphics Begin(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.HighQuality(g);
        return g;
    }

    protected bool FocusCue => Focused && ShowFocusCues;
}

internal enum ButtonKind { Secondary, Primary, Ghost, Danger }

internal sealed class UiButton : UiControl
{
    private ButtonKind _kind;
    private string? _glyph;
    private bool _large;

    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }
    public string? Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }
    public bool Large { get => _large; set { _large = value; Invalidate(); } }
    /// <summary>Glyph drawn on the right of the text (chevrons).</summary>
    public bool GlyphAfter { get; set; }
    public bool AlignLeft { get; set; }

    public UiButton()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
        base.OnKeyUp(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (CanFocus && TabStop) Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float radius = Theme.D(_large ? 10 : 8);
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Color fill, border, ink;
        switch (_kind)
        {
            case ButtonKind.Primary:
                fill = Down ? Theme.AccPressed : Hover ? Theme.AccDD : Theme.AccD;
                border = fill;
                ink = Color.White;
                break;
            case ButtonKind.Danger:
                fill = Down ? Theme.Mix(Theme.RedT, Theme.RedLine, 0.4f) : Hover ? Theme.RedT : Theme.Card;
                border = Theme.RedLine;
                ink = Theme.Red;
                break;
            case ButtonKind.Ghost:
                fill = Down ? Color.FromArgb(26, 15, 30, 45) : Hover ? Color.FromArgb(15, 15, 30, 45) : Color.Transparent;
                border = Color.Transparent;
                ink = Theme.Tx2;
                break;
            default:
                fill = Down ? Theme.Pressed : Hover ? Theme.Subtle : Theme.Card;
                border = Theme.Line2;
                ink = Theme.Tx;
                break;
        }
        if (!Enabled)
        {
            fill = _kind == ButtonKind.Ghost ? Color.Transparent : Theme.Mix(fill, Theme.Bg, 0.6f);
            border = Theme.Mix(border, Theme.Bg, 0.5f);
            ink = Theme.Mix(ink, Theme.Bg, 0.55f);
        }
        if (_kind == ButtonKind.Primary && Enabled && !Down)
        {
            var sh = r; sh.Offset(0, Theme.D(1));
            Theme.FillRound(g, sh, radius, Color.FromArgb(60, 8, 110, 107));
        }
        if (fill.A > 0) Theme.FillRound(g, r, radius, fill);
        if (border.A > 0 && _kind != ButtonKind.Primary) Theme.StrokeRound(g, r, radius, border);
        if (FocusCue) Theme.StrokeRound(g, RectangleF.Inflate(r, -1, -1), radius - 1, Theme.Acc, Theme.D(2));

        var font = _large ? Theme.ButtonLg : Theme.SmallSemi;
        var iconFont = _large ? Theme.Icon16 : Theme.Icon12;
        bool hasText = !string.IsNullOrEmpty(Text);
        bool hasGlyph = !string.IsNullOrEmpty(_glyph);
        int gap = hasText && hasGlyph ? Theme.D(8) : 0;
        int gw = hasGlyph ? Theme.D(_large ? 16 : 14) : 0;
        int tw = hasText ? Theme.Measure(Text, font).Width : 0;
        int pad = Theme.D(14);
        int total = gw + gap + tw;
        int x = AlignLeft ? pad : (Width - total) / 2;
        int maxText = Math.Max(0, Width - 2 * pad - gw - gap);
        if (tw > maxText) { tw = maxText; x = pad; }
        if (GlyphAfter)
        {
            if (hasText) Theme.Text(g, Text, font, new Rectangle(x, 0, tw, Height), ink);
            if (hasGlyph) Theme.Glyph(g, _glyph!, iconFont, new Rectangle(AlignLeft ? Width - pad - gw : x + tw + gap, 0, gw, Height), ink);
            return;
        }
        if (hasGlyph) Theme.Glyph(g, _glyph!, iconFont, new Rectangle(x, 0, gw, Height), ink);
        if (hasText) Theme.Text(g, Text, font, new Rectangle(x + gw + gap, 0, tw + 2, Height), ink);
    }
}

/// <summary>Title bar button (46 × 40, red close hover).</summary>
internal sealed class TitleButton : UiControl
{
    public string Glyph { get; set; } = Ic.Min;
    public bool IsClose { get; set; }

    public TitleButton()
    {
        BackColor = Theme.Bg;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color bg = IsClose && Hover ? (Down ? Theme.Hex(0xA52A1E) : Theme.CloseHover)
            : Hover ? (Down ? Theme.Mix(BackColor, Color.Black, 0.1f) : Theme.Mix(BackColor, Color.Black, 0.06f)) : BackColor;
        g.Clear(bg);
        var ink = IsClose && Hover ? Color.White : Theme.Tx2;
        Theme.Glyph(g, Glyph, Theme.Icon12, ClientRectangle, ink);
    }
}

internal sealed class UiToggle : UiControl
{
    private bool _on;
    public bool Checked
    {
        get => _on;
        set { if (_on == value) return; _on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CheckedChanged;

    public UiToggle()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Size = new Size(Theme.D(40), Theme.D(22));
    }

    /// <summary>Changes the state without raising <see cref="CheckedChanged"/>.</summary>
    public void SetSilently(bool on) { _on = on; Invalidate(); }

    protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float h = Theme.D(22), w = Theme.D(40);
        var track = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
        track.Inflate(-0.75f, -0.75f);
        var on = Enabled ? (Hover ? Theme.AccDD : Theme.AccD) : Theme.Neutral;
        if (_on) Theme.FillRound(g, track, h / 2, on);
        else
        {
            Theme.FillRound(g, track, h / 2, Hover ? Theme.Subtle : Theme.Card);
            Theme.StrokeRound(g, track, h / 2, Enabled ? Theme.ToggleOff : Theme.Line2, Theme.D(1.5f));
        }
        float d = Theme.D(_on || Down ? 14 : 13);
        if (Down) d += Theme.D(2);
        float cy = track.Y + track.Height / 2;
        float cx = _on ? track.Right - Theme.D(4) - d / 2 : track.X + Theme.D(4) + d / 2;
        using var knob = new SolidBrush(_on ? Color.White : Enabled ? Theme.Hex(0x5B6876) : Theme.Neutral);
        g.FillEllipse(knob, cx - d / 2, cy - d / 2, d, d);
        if (FocusCue) Theme.StrokeRound(g, RectangleF.Inflate(track, Theme.D(2), Theme.D(2)), h / 2 + 2, Theme.Acc, Theme.D(1.5f));
    }
}

internal sealed class UiSlider : UiControl
{
    private int _min, _max = 100, _value;
    private bool _drag;

    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Invalidate(); } }
    public int Step { get; set; } = 1;
    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    /// <summary>Live while dragging.</summary>
    public event EventHandler? ValueChanged;
    /// <summary>Mouse released or key pressed: persist here.</summary>
    public event EventHandler? ValueCommitted;

    public UiSlider()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = Theme.D(22);
    }

    public void SetSilently(int v) { _value = Math.Clamp(v, _min, _max); Invalidate(); }

    private static float Pad => Theme.D(10);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _drag = true;
        Capture = true;
        SetFrom(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFrom(e.X); base.OnMouseMove(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drag) { _drag = false; Capture = false; ValueCommitted?.Invoke(this, EventArgs.Empty); }
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int delta = e.KeyCode switch { Keys.Left or Keys.Down => -Step, Keys.Right or Keys.Up => Step, _ => 0 };
        if (delta != 0) { Value += delta; ValueCommitted?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    private void SetFrom(int x)
    {
        float t = Math.Clamp((x - Pad) / Math.Max(1f, Width - 2 * Pad), 0f, 1f);
        int raw = _min + (int)Math.Round(t * (_max - _min));
        int step = Math.Max(1, Step);
        Value = _min + (int)Math.Round((raw - _min) / (double)step) * step;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float cy = Height / 2f;
        float t = _max == _min ? 0 : (_value - _min) / (float)(_max - _min);
        float x0 = Pad, x1 = Width - Pad;
        float x = x0 + t * (x1 - x0);
        float th = Theme.D(4);
        Theme.FillRound(g, new RectangleF(x0, cy - th / 2, x1 - x0, th), th / 2, Theme.Hex(0xDCE2E7));
        if (x > x0) Theme.FillRound(g, new RectangleF(x0, cy - th / 2, x - x0, th), th / 2, Enabled ? Theme.AccD : Theme.Neutral);
        float outer = Theme.D(20);
        using (var shadow = new SolidBrush(Color.FromArgb(40, 15, 30, 45)))
            g.FillEllipse(shadow, x - outer / 2, cy - outer / 2 + Theme.D(1), outer, outer);
        using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, x - outer / 2, cy - outer / 2, outer, outer);
        using (var ring = new Pen(Theme.Line2, 1f)) g.DrawEllipse(ring, x - outer / 2, cy - outer / 2, outer, outer);
        float inner = Theme.D(Hover || _drag ? 13 : 11);
        if (_drag) inner = Theme.D(10);
        using var dot = new SolidBrush(Enabled ? Theme.AccD : Theme.Neutral);
        g.FillEllipse(dot, x - inner / 2, cy - inner / 2, inner, inner);
        if (FocusCue)
        {
            using var focus = new Pen(Theme.Acc, Theme.D(1.5f));
            g.DrawEllipse(focus, x - outer / 2 - 2, cy - outer / 2 - 2, outer + 4, outer + 4);
        }
    }
}

/// <summary>Segmented control. Items may carry a colored key square.</summary>
internal sealed class UiSegmented : UiControl
{
    private readonly List<(string Text, Color Key)> _items = new();
    private int _selected;
    private int _hot = -1;

    public int SelectedIndex
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? SelectedIndexChanged;

    public UiSegmented()
    {
        Cursor = Cursors.Hand;
        Height = Theme.D(34);
    }

    public void Add(string text, Color key) { _items.Add((text, key)); Invalidate(); }
    public void SetSilently(int index) { _selected = index; Invalidate(); }

    private RectangleF Cell(int i)
    {
        float p = Theme.D(3);
        float w = (Width - 2 * p - (_items.Count - 1) * Theme.D(2)) / Math.Max(1, _items.Count);
        return new RectangleF(p + i * (w + Theme.D(2)), p, w, Height - 2 * p);
    }

    private int HitIndex(Point pt)
    {
        for (int i = 0; i < _items.Count; i++) if (Cell(i).Contains(pt)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitIndex(e.Location);
        if (h != _hot) { _hot = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hot = -1; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int h = HitIndex(e.Location);
        if (h >= 0) SelectedIndex = h;
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        Theme.FillRound(g, new RectangleF(0, 0, Width, Height), Theme.D(9), Theme.SegBg);
        for (int i = 0; i < _items.Count; i++)
        {
            var c = Cell(i);
            bool sel = i == _selected;
            if (sel)
            {
                var sh = c; sh.Offset(0, Theme.D(1));
                Theme.FillRound(g, sh, Theme.D(7), Color.FromArgb(30, 15, 30, 45));
                Theme.FillRound(g, c, Theme.D(7), Color.White);
            }
            else if (i == _hot) Theme.FillRound(g, c, Theme.D(7), Color.FromArgb(70, 255, 255, 255));
            var (text, key) = _items[i];
            int tw = Theme.Measure(text, Theme.SmallSemi).Width;
            int ks = key.A > 0 ? Theme.D(9) : 0;
            int gap = ks > 0 ? Theme.D(6) : 0;
            int x = (int)(c.X + (c.Width - tw - ks - gap) / 2);
            if (ks > 0) Theme.FillRound(g, new RectangleF(x, c.Y + (c.Height - ks) / 2, ks, ks), Theme.D(3), key);
            Theme.Text(g, text, Theme.SmallSemi, new Rectangle(x + ks + gap, (int)c.Y, tw + 2, (int)c.Height), sel ? Theme.Tx : Theme.Tx2);
        }
    }
}

/// <summary>Round color swatch. <see cref="Picker"/> draws the rainbow "custom color" swatch.</summary>
internal sealed class UiSwatch : UiControl
{
    private bool _selected;
    public Color Swatch { get; set; } = Color.White;
    public bool Picker { get; set; }
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public UiSwatch()
    {
        Cursor = Cursors.Hand;
        Size = new Size(Theme.D(36), Theme.D(36));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float d = Theme.D(28);
        var r = new RectangleF((Width - d) / 2f, (Height - d) / 2f, d, d);
        if (_selected)
        {
            var ring = RectangleF.Inflate(r, Theme.D(4), Theme.D(4));
            using var b = new SolidBrush(Theme.AccD);
            g.FillEllipse(b, ring);
            var gapR = RectangleF.Inflate(r, Theme.D(2), Theme.D(2));
            using var w = new SolidBrush(BackColor);
            g.FillEllipse(w, gapR);
        }
        if (Picker)
        {
            Color[] hues = { Theme.Hex(0xFF4433), Theme.Hex(0xFFCC33), Theme.Hex(0x33CC66), Theme.Hex(0x33CCFF), Theme.Hex(0x6633FF), Theme.Hex(0xFF33CC) };
            for (int i = 0; i < hues.Length; i++)
            {
                using var b = new SolidBrush(hues[i]);
                g.FillPie(b, r.X, r.Y, r.Width, r.Height, -90 + i * 60, 61);
            }
        }
        else
        {
            using var b = new SolidBrush(Swatch);
            g.FillEllipse(b, r);
        }
        using var edge = new Pen(Hover ? Theme.Tx3 : Theme.Line2, 1f);
        g.DrawEllipse(edge, r);
    }
}

/// <summary>Text field: custom frame + borderless TextBox, optional prefix cell, search glyph, and suffix.</summary>
internal sealed class UiInput : UiControl
{
    private readonly TextBox _box = new() { BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Tx };
    private string? _prefix, _suffix, _glyph;

    public static int Ring => Theme.D(3);

    public UiInput()
    {
        Controls.Add(_box);
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => { Invalidate(); Committed?.Invoke(this, EventArgs.Empty); };
        _box.TextChanged += (_, _) => { if (!_silent) ValueChanged?.Invoke(this, EventArgs.Empty); };
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Committed?.Invoke(this, EventArgs.Empty); EnterPressed?.Invoke(this, EventArgs.Empty); }
            if (Numeric && e.KeyCode is Keys.Up or Keys.Down) { e.SuppressKeyPress = true; Nudge(e.KeyCode == Keys.Up ? (e.Shift ? 10 : 1) : (e.Shift ? -10 : -1)); }
        };
        _box.KeyPress += (_, e) => { if (Numeric && !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '-') e.Handled = true; };
        Cursor = Cursors.IBeam;
        ApplyFont();
    }

    private bool _silent;

    public TextBox Box => _box;
    public bool Numeric { get; set; }
    public string? Prefix { get => _prefix; set { _prefix = value; LayoutBox(); Invalidate(); } }
    public string? Suffix { get => _suffix; set { _suffix = value; LayoutBox(); Invalidate(); } }
    public string? Glyph { get => _glyph; set { _glyph = value; LayoutBox(); Invalidate(); } }
    public string Placeholder { get => _box.PlaceholderText; set => _box.PlaceholderText = value; }
    public string Value { get => _box.Text; set => SetText(value); }

    public event EventHandler? ValueChanged;
    public event EventHandler? Committed;
    public event EventHandler? EnterPressed;

    public void SetText(string text)
    {
        if (_box.Text == text) return;
        _silent = true;
        _box.Text = text;
        _silent = false;
    }

    public void ApplyFont()
    {
        _box.Font = Theme.Body;
        LayoutBox();
    }

    private void Nudge(int by)
    {
        if (!int.TryParse(_box.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v)) v = 0;
        _box.Text = (v + by).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _box.SelectionStart = _box.Text.Length;
    }

    protected override void OnClick(EventArgs e) { _box.Focus(); base.OnClick(e); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutBox(); }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _box.BackColor = Enabled ? Theme.Card : Theme.Bg;
    }

    private Rectangle Frame => new(Ring, Ring, Math.Max(1, Width - 2 * Ring), Math.Max(1, Height - 2 * Ring));
    private int PrefixWidth => string.IsNullOrEmpty(_prefix) ? 0 : Theme.D(30);
    private int GlyphWidth => string.IsNullOrEmpty(_glyph) ? 0 : Theme.D(30);
    private int SuffixWidth => string.IsNullOrEmpty(_suffix) ? 0 : Theme.Measure(_suffix, Theme.Small).Width + Theme.D(10);

    private void LayoutBox()
    {
        var f = Frame;
        int left = f.X + (PrefixWidth > 0 ? PrefixWidth + Theme.D(8) : GlyphWidth > 0 ? GlyphWidth : Theme.D(10));
        int right = f.Right - (SuffixWidth > 0 ? SuffixWidth : Theme.D(10));
        int h = _box.PreferredHeight;
        _box.SetBounds(left, f.Y + (f.Height - h) / 2 + 1, Math.Max(10, right - left), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var f = Frame;
        float r = Theme.D(8);
        bool focus = _box.Focused;
        if (focus) Theme.FillRound(g, RectangleF.Inflate(f, Ring, Ring), r + Ring, Theme.AccT2);
        Theme.FillRound(g, f, r, Enabled ? Theme.Card : Theme.Bg);
        if (PrefixWidth > 0)
        {
            var cell = new Rectangle(f.X, f.Y, PrefixWidth, f.Height);
            using (var clip = Theme.Round(f, r))
            {
                var st = g.Save();
                g.SetClip(clip);
                using var b = new SolidBrush(Enabled ? Theme.Hex(0xFAFBFC) : Theme.Bg);
                g.FillRectangle(b, cell);
                g.Restore(st);
            }
            using var sep = new Pen(Theme.Line);
            g.DrawLine(sep, cell.Right, f.Y + 1, cell.Right, f.Bottom - 1);
            Theme.Text(g, _prefix, Theme.SmallSemi, cell, Theme.Tx3, Theme.Center);
        }
        if (GlyphWidth > 0)
            Theme.Glyph(g, _glyph!, Theme.Icon12, new Rectangle(f.X + Theme.D(2), f.Y, GlyphWidth, f.Height), Theme.Tx3);
        if (SuffixWidth > 0)
            Theme.Text(g, _suffix, Theme.Small, new Rectangle(f.Right - SuffixWidth, f.Y, SuffixWidth - Theme.D(8), f.Height), Theme.Tx3, Theme.Right);
        Theme.StrokeRound(g, f, r, focus ? Theme.AccD : Enabled ? Theme.Line2 : Theme.Line);
    }
}

/// <summary>Select box: a button that opens a styled drop-down menu.</summary>
internal sealed class UiDropdown : UiControl
{
    private readonly ContextMenuStrip _menu = new() { ShowImageMargin = false, ShowCheckMargin = true };
    private readonly List<(string Text, object? Tag)> _items = new();
    private int _selected = -1;

    public event EventHandler? Opening;
    public event EventHandler? SelectedChanged;
    public string? Glyph { get; set; }
    public string EmptyText { get; set; } = "Seç";

    public UiDropdown()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        _menu.Renderer = new MenuRenderer();
        _menu.Closed += (_, _) => Invalidate();
        Menus.Style(_menu);
    }

    public int SelectedIndex => _selected;
    public object? SelectedTag => _selected >= 0 && _selected < _items.Count ? _items[_selected].Tag : null;
    public string SelectedText => _selected >= 0 && _selected < _items.Count ? _items[_selected].Text : EmptyText;
    public IReadOnlyList<(string Text, object? Tag)> Items => _items;

    public void SetItems(IEnumerable<(string Text, object? Tag)> items, int selected)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = Math.Clamp(selected, -1, _items.Count - 1);
        Invalidate();
    }

    public void SelectSilently(int index) { _selected = Math.Clamp(index, -1, _items.Count - 1); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space or Keys.Down) Open(); base.OnKeyDown(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { Focus(); Open(); }
    }

    private void Open()
    {
        Opening?.Invoke(this, EventArgs.Empty);
        _menu.Items.Clear();
        _menu.Font = Theme.Body;
        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(_items[i].Text) { Checked = i == _selected, Padding = new Padding(0, Theme.D(5), Theme.D(8), Theme.D(5)) };
            item.Click += (_, _) =>
            {
                _selected = index;
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            };
            _menu.Items.Add(item);
        }
        if (_items.Count == 0) _menu.Items.Add(new ToolStripMenuItem("Seçenek yok") { Enabled = false });
        _menu.MinimumSize = new Size(Width, 0);
        _menu.Show(this, new Point(0, Height + Theme.D(4)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        float rad = Theme.D(8);
        bool open = _menu.Visible;
        Theme.FillRound(g, r, rad, Down || open ? Theme.Subtle : Hover ? Theme.Hex(0xFAFBFC) : Theme.Card);
        Theme.StrokeRound(g, r, rad, open || Focused ? Theme.AccD : Hover ? Theme.Tx3 : Theme.Line2);
        int x = Theme.D(10);
        if (!string.IsNullOrEmpty(Glyph))
        {
            Theme.Glyph(g, Glyph!, Theme.Icon12, new Rectangle(x, 0, Theme.D(16), Height), Theme.Tx2);
            x += Theme.D(24);
        }
        int chevron = Theme.D(28);
        Theme.Text(g, SelectedText, Theme.Body, new Rectangle(x, 0, Width - x - chevron, Height), _selected >= 0 ? Theme.Tx : Theme.Tx3);
        Theme.Glyph(g, Ic.Down, Theme.Icon12, new Rectangle(Width - chevron, 0, Theme.D(20), Height), Theme.Tx2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Small rounded status pill.</summary>
internal enum BadgeKind { Neutral, Ok, Warn }

internal static class Badge
{
    public static Size Measure(string text, string? glyph)
    {
        int w = Theme.Measure(text, Theme.Caption).Width + Theme.D(18) + (glyph != null ? Theme.D(16) : 0);
        return new Size(w, Theme.D(22));
    }

    public static void Draw(Graphics g, Rectangle r, string text, BadgeKind kind, string? glyph = null)
    {
        var (bg, fg) = kind switch
        {
            BadgeKind.Ok => (Theme.OkT, Theme.Ok),
            BadgeKind.Warn => (Theme.WarnT, Theme.Warn),
            _ => (Theme.Chip, Theme.Tx2),
        };
        Theme.FillRound(g, r, r.Height / 2f, bg);
        int x = r.X + Theme.D(9);
        if (glyph != null)
        {
            Theme.Glyph(g, glyph, Theme.Icon12, new Rectangle(x - Theme.D(2), r.Y, Theme.D(14), r.Height), fg);
            x += Theme.D(16);
        }
        Theme.Text(g, text, Theme.Caption, new Rectangle(x, r.Y, r.Right - x - Theme.D(7), r.Height), fg);
    }
}

internal static class Menus
{
    public static void Style(ToolStripDropDown menu)
    {
        menu.Renderer = new MenuRenderer();
        menu.BackColor = Theme.Card;
        menu.ForeColor = Theme.Tx;
        menu.Padding = new Padding(Theme.D(4));
        menu.Opened += (_, _) => Dwm.RoundCorners(menu.Handle, small: true);
    }
}

/// <summary>Light, rounded-feel menu renderer for drop-downs and the tray menu.</summary>
internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    public MenuRenderer() : base(new Colors()) { RoundedEdges = false; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.Card);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(Theme.Line2);
        e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        Theme.HighQuality(g);
        var r = new RectangleF(Theme.D(2), 1, e.Item.Width - Theme.D(4), e.Item.Height - 2);
        Theme.FillRound(g, r, Theme.D(6), Theme.AccT);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        Theme.Glyph(e.Graphics, Ic.Check, Theme.Icon12, new Rectangle(r.X, 0, r.Width, e.Item.Height), Theme.AccD);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.Tx3 : Theme.Tx;
        e.TextFont = e.Item is ToolStripMenuItem { Checked: true } || ReferenceEquals(e.Item.Font, Theme.BodySemi) ? Theme.BodySemi : Theme.Body;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Theme.Line);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(p, Theme.D(8), y, e.Item.Width - Theme.D(8), y);
    }

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color MenuBorder => Theme.Line2;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.AccT;
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color ImageMarginGradientBegin => Theme.Card;
        public override Color ImageMarginGradientMiddle => Theme.Card;
        public override Color ImageMarginGradientEnd => Theme.Card;
        public override Color SeparatorDark => Theme.Line;
        public override Color SeparatorLight => Theme.Card;
    }
}

internal static class Dwm
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Windows 11 rounded corners (no-op on Windows 10).</summary>
    public static void RoundCorners(IntPtr hwnd, bool small)
    {
        try
        {
            int pref = small ? 3 : 2; // DWMWCP_ROUNDSMALL / DWMWCP_ROUND
            _ = DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
        }
        catch (DllNotFoundException) { /* older Windows */ }
        catch (EntryPointNotFoundException) { /* older Windows */ }
    }
}
