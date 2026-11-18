namespace AltyaziCevirici.Ui;

/// <summary>
/// Rounded white card with a soft shadow. The control's bounds include <see cref="Pad"/> around the card for
/// the shadow, so place it with <see cref="Place"/>. Children sit on the white body.
/// </summary>
internal sealed class UiCard : Panel
{
    public static int Pad => Theme.D(7);
    public float Radius { get; set; } = 14;

    /// <summary>Paints card content; the rectangle is the white body in client coordinates.</summary>
    public event Action<Graphics, Rectangle>? PaintBody;

    public UiCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    public Rectangle Body => new(Pad, Pad, Math.Max(1, Width - 2 * Pad), Math.Max(1, Height - 2 * Pad));

    public void Place(Rectangle card) => Bounds = Rectangle.Inflate(card, Pad, Pad);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.HighQuality(g);
        var b = Body;
        float r = Theme.D(Radius);
        Theme.Shadow(g, b, r, Pad - 1, 12);
        Theme.FillRound(g, b, r, Theme.Card);
        Theme.StrokeRound(g, RectangleF.Inflate(b, 0.5f, 0.5f), r, Color.FromArgb(18, 15, 30, 45));
        PaintBody?.Invoke(g, b);
    }
}

/// <summary>
/// Inspector page: child controls plus painted text, tiles, and dividers, all positioned by <see cref="Arrange"/>.
/// </summary>
internal sealed class InspectorPage : Panel
{
    private readonly List<Action<Graphics>> _ops = new();
    private readonly Func<InspectorPage, int, int, int> _body;

    public string Heading { get; }
    public string Lead { get; }

    public InspectorPage(string heading, string lead, Func<InspectorPage, int, int, int> body)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Heading = heading;
        Lead = lead;
        _body = body;
    }

    /// <summary>Lays the page out for <paramref name="width"/> and returns its height.</summary>
    public int Arrange(int width)
    {
        _ops.Clear();
        int y = 0;
        int h1 = Theme.Measure(Heading, Theme.H2).Height;
        AddText(new Rectangle(0, y, width, h1), Heading, Theme.H2, Theme.Tx);
        y += h1 + Theme.D(3);
        y += AddWrap(0, y, width, Lead, Theme.Small, Theme.Tx2);
        y += Theme.D(16);
        y = _body(this, width, y);
        Height = y + Theme.D(4);
        Width = width;
        Invalidate();
        return Height;
    }

    public void AddText(Rectangle r, string? text, Font font, Color color, TextFormatFlags flags = Theme.Left) =>
        _ops.Add(g => Theme.Text(g, text, font, r, color, flags));

    public int AddWrap(int x, int y, int width, string text, Font font, Color color)
    {
        int h = Theme.WrapHeight(text, font, width);
        var r = new Rectangle(x, y, width, h + 2);
        _ops.Add(g => Theme.Text(g, text, font, r, color, Theme.Wrap));
        return h;
    }

    /// <summary>"Label ........ value" row used above sliders and fields.</summary>
    public int AddLabel(int y, int width, string label, string? value = null)
    {
        int h = Theme.D(18);
        AddText(new Rectangle(0, y, width, h), label, Theme.SmallSemi, Theme.Tx2);
        if (value != null) AddText(new Rectangle(0, y, width, h), value, Theme.SmallSemi, Theme.Tx, Theme.Right);
        return y + h + Theme.D(8);
    }

    public int AddSection(int y, int width, string text)
    {
        string label = text.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        int h = Math.Max(Theme.D(16), Theme.Measure(label, Theme.Caption).Height);
        AddText(new Rectangle(0, y, Math.Max(1, width), h), label, Theme.Caption, Theme.Tx3);
        return h;
    }

    public void AddDivider(int y, int width) =>
        _ops.Add(g => { using var p = new Pen(Theme.Line); g.DrawLine(p, 0, y, width, y); });

    public void AddTile(Rectangle r, string glyph, Color bg, Color ink) =>
        _ops.Add(g =>
        {
            Theme.FillRound(g, r, Theme.D(10), bg);
            Theme.Glyph(g, glyph, Theme.Icon16, r, ink);
        });

    public void AddBox(Rectangle r, Color fill, Color? border = null) =>
        _ops.Add(g =>
        {
            Theme.FillRound(g, r, Theme.D(10), fill);
            if (border is { } b) Theme.StrokeRound(g, r, Theme.D(10), b);
        });

    public void AddBadge(Rectangle r, string text, BadgeKind kind, string? glyph = null) =>
        _ops.Add(g => Badge.Draw(g, r, text, kind, glyph));

    public void AddPaint(Action<Graphics> op) => _ops.Add(op);

    /// <summary>Accent hint box with an info glyph. Returns its height.</summary>
    public int AddHint(int y, int width, string text)
    {
        int pad = Theme.D(12), icon = Theme.D(16), gap = Theme.D(10);
        int th = Theme.WrapHeight(text, Theme.Small, width - 2 * pad - icon - gap);
        int h = th + 2 * pad;
        var r = new Rectangle(0, y, width, h);
        _ops.Add(g =>
        {
            Theme.FillRound(g, r, Theme.D(10), Theme.AccT);
            Theme.Glyph(g, Ic.Info, Theme.Icon12, new Rectangle(pad, y + pad, icon, Theme.D(18)), Theme.AccD);
            Theme.Text(g, text, Theme.Small, new Rectangle(pad + icon + gap, y + pad, width - 2 * pad - icon - gap, th + 2), Theme.AccInk, Theme.Wrap);
        });
        return h;
    }

    public static void Put(Control c, int x, int y, int w, int h) => c.SetBounds(x, y, Math.Max(1, w), Math.Max(1, h));

    /// <summary>Inputs draw a focus ring around their frame; their bounds grow by the ring on every side.</summary>
    public static void PutInput(UiInput c, int x, int y, int w, int h) =>
        c.SetBounds(x - UiInput.Ring, y - UiInput.Ring, Math.Max(1, w + 2 * UiInput.Ring), Math.Max(1, h + 2 * UiInput.Ring));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.HighQuality(g);
        foreach (var op in _ops) op(g);
    }
}

/// <summary>Vertical scroller with a thin overlay bar in the right gutter. Hosts one <see cref="InspectorPage"/>.</summary>
internal sealed class ScrollHost : Control
{
    private InspectorPage? _page;
    private int _offset;
    private bool _hover, _drag;
    private int _dragStart, _dragOffset;

    public ScrollHost()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
    }

    public static int Gutter => Theme.D(12);
    public InspectorPage? Page => _page;

    public void Show(InspectorPage page)
    {
        if (_page == page) return;
        if (_page != null) Controls.Remove(_page);
        _page = page;
        _offset = 0;
        Controls.Add(page);
        Relayout();
    }

    public void Relayout()
    {
        if (_page == null) return;
        _page.Arrange(Math.Max(10, Width - Gutter));
        Clamp();
        _page.Location = new Point(0, -_offset);
        Invalidate();
    }

    private int Max => _page == null ? 0 : Math.Max(0, _page.Height - Height);

    private void Clamp() => _offset = Math.Clamp(_offset, 0, Max);

    public void ScrollTo(int offset)
    {
        _offset = offset;
        Clamp();
        if (_page != null) _page.Top = -_offset;
        Invalidate();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(_offset - Math.Sign(e.Delta) * Theme.D(56));
        if (e is HandledMouseEventArgs h) h.Handled = true;
        base.OnMouseWheel(e);
    }

    private Rectangle Thumb()
    {
        if (_page == null || Max <= 0) return Rectangle.Empty;
        int track = Height - Theme.D(8);
        int len = Math.Max(Theme.D(32), (int)(track * (Height / (float)_page.Height)));
        int y = Theme.D(4) + (int)((track - len) * (_offset / (float)Max));
        int w = Theme.D(_hover || _drag ? 6 : 4);
        return new Rectangle(Width - Theme.D(3) - w, y, w, len);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag)
        {
            var t = Thumb();
            int track = Height - Theme.D(8) - t.Height;
            if (track > 0) ScrollTo(_dragOffset + (int)((e.Y - _dragStart) * (Max / (float)track)));
            return;
        }
        bool over = e.X >= Width - Gutter;
        if (over != _hover) { _hover = over; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (!_drag) { _hover = false; Invalidate(); } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var t = Thumb();
        if (t.IsEmpty || e.X < Width - Gutter) return;
        if (t.Contains(new Point(t.X + 1, e.Y))) { _drag = true; _dragStart = e.Y; _dragOffset = _offset; Capture = true; }
        else ScrollTo(_offset + (e.Y < t.Y ? -Height : Height) * 3 / 4);
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; Capture = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.HighQuality(g);
        var t = Thumb();
        if (t.IsEmpty) return;
        Theme.FillRound(g, t, t.Width / 2f, _hover || _drag ? Theme.Tx3 : Theme.Scroll);
    }
}

/// <summary>Sidebar navigation item.</summary>
internal sealed class NavItem : UiControl
{
    private bool _active, _dot;
    public string Glyph { get; set; } = Ic.Crop;
    public bool Active { get => _active; set { _active = value; Invalidate(); } }
    /// <summary>Accent dot (for example: update available).</summary>
    public bool Dot { get => _dot; set { _dot = value; Invalidate(); } }

    public NavItem()
    {
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var r = new RectangleF(Theme.D(2), Theme.D(1), Width - Theme.D(4), Height - Theme.D(3));
        float rad = Theme.D(8);
        if (_active)
        {
            var sh = r; sh.Offset(0, Theme.D(1));
            Theme.FillRound(g, RectangleF.Inflate(sh, 0.5f, 0.5f), rad, Color.FromArgb(16, 15, 30, 45));
            Theme.FillRound(g, r, rad, Theme.Card);
            Theme.FillRound(g, new RectangleF(r.X, r.Y + Theme.D(10), Theme.D(3), r.Height - Theme.D(20)), Theme.D(1.5f), Theme.Acc);
        }
        else if (Hover) Theme.FillRound(g, r, rad, Down ? Color.FromArgb(22, 15, 30, 45) : Color.FromArgb(13, 15, 30, 45));
        if (FocusCue) Theme.StrokeRound(g, r, rad, Theme.Acc, Theme.D(1.5f));
        int x = (int)r.X + Theme.D(12);
        Theme.Glyph(g, Glyph, Theme.Icon16, new Rectangle(x, 0, Theme.D(20), Height), _active ? Theme.AccD : Theme.Tx2);
        x += Theme.D(30);
        Theme.Text(g, Text, _active ? Theme.NavSemi : Theme.Nav, new Rectangle(x, 0, Width - x - Theme.D(20), Height), _active ? Theme.Tx : Theme.Tx2);
        if (_dot)
        {
            float d = Theme.D(8);
            using var b = new SolidBrush(Theme.Acc);
            g.FillEllipse(b, r.Right - Theme.D(16), (Height - d) / 2f, d, d);
        }
    }
}

/// <summary>Header legend pill: "■ Orijinal altyazı | 🔒". Left part selects the box, right part toggles its lock.</summary>
internal sealed class LegendChip : UiControl
{
    private bool _active, _locked;
    private int _hot = -1;
    public Color Key { get; set; } = Theme.KeyOrig;
    public bool Active { get => _active; set { _active = value; Invalidate(); } }
    public bool Locked { get => _locked; set { _locked = value; Invalidate(); } }
    public event EventHandler? SelectClicked;
    public event EventHandler? LockClicked;

    public LegendChip()
    {
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
    }

    private static int LockWidth => Theme.D(32);
    private static int Ring => Theme.D(3);

    public int PreferredWidth => Theme.D(12 + 9 + 7 + 10) + Theme.Measure(Text, Theme.SmallSemi).Width + LockWidth + 2 * Ring;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = e.X >= Width - Ring - LockWidth ? 1 : 0;
        if (h != _hot) { _hot = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hot = -1; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (e.X >= Width - Ring - LockWidth) LockClicked?.Invoke(this, EventArgs.Empty);
            else SelectClicked?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var r = new RectangleF(Ring + 0.5f, Ring + 0.5f, Width - 2 * Ring - 1, Height - 2 * Ring - 1);
        float rad = r.Height / 2;
        if (_active) Theme.FillRound(g, RectangleF.Inflate(r, Ring, Ring), rad + Ring, Theme.AccT2);
        using (var clip = Theme.Round(r, rad))
        {
            var st = g.Save();
            g.SetClip(clip);
            using (var white = new SolidBrush(Theme.Card)) g.FillRectangle(white, r);
            var lockRect = new RectangleF(r.Right - LockWidth, r.Y, LockWidth, r.Height);
            if (_locked)
            {
                using var b = new SolidBrush(Theme.AccT);
                g.FillRectangle(b, lockRect);
            }
            if (_hot >= 0)
            {
                var hotRect = _hot == 1 ? lockRect : new RectangleF(r.X, r.Y, r.Width - LockWidth, r.Height);
                using var hb = new SolidBrush(Color.FromArgb(13, 15, 30, 45));
                g.FillRectangle(hb, hotRect);
            }
            using (var sep = new Pen(Theme.Line))
                g.DrawLine(sep, lockRect.X, r.Y, lockRect.X, r.Bottom);
            g.Restore(st);
        }
        Theme.StrokeRound(g, r, rad, _active ? Theme.AccD : Theme.Line2);
        int x = (int)r.X + Theme.D(12);
        int ks = Theme.D(9);
        Theme.FillRound(g, new RectangleF(x, r.Y + (r.Height - ks) / 2, ks, ks), Theme.D(3), Key);
        x += ks + Theme.D(7);
        Theme.Text(g, Text, Theme.SmallSemi, new Rectangle(x, (int)r.Y, (int)(r.Right - LockWidth) - x, (int)r.Height), _active ? Theme.Tx : Theme.Tx2);
        var lr = new Rectangle((int)(r.Right - LockWidth), (int)r.Y, LockWidth, (int)r.Height);
        Theme.Glyph(g, _locked ? Ic.Lock : Ic.Unlock, Theme.Icon12, lr, _locked ? Theme.AccDD : Theme.Tx3);
    }
}

internal sealed record PackRow(string Id, string Name, string Abbrev, Color Tile, int Count, bool On);

/// <summary>Glossary packs. The switch turns a pack on or off; the rest of the row opens its terms.</summary>
internal sealed class PackList : UiControl
{
    private readonly List<PackRow> _rows = new();
    private int _hot = -1;
    private bool _hotSwitch;
    public event Action<string, bool>? Toggled;
    public event Action<string>? Opened;

    private static int SwitchZone => Theme.D(52);

    public PackList() { Cursor = Cursors.Hand; }

    public static int RowHeight => Math.Max(Theme.D(48),
        Theme.Measure("Ag", Theme.BodySemi).Height + Theme.Measure("Ag", Theme.Small).Height + Theme.D(18));

    public void SetRows(IEnumerable<PackRow> rows)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        Height = Math.Max(1, _rows.Count * RowHeight);
        Invalidate();
    }

    public int Count => _rows.Count;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = e.Y / RowHeight;
        if (h >= _rows.Count) h = -1;
        bool sw = e.X >= Width - SwitchZone;
        if (h != _hot || sw != _hotSwitch) { _hot = h; _hotSwitch = sw; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hot = -1; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = e.Y / RowHeight;
        if (e.Button == MouseButtons.Left && i >= 0 && i < _rows.Count)
        {
            if (e.X >= Width - SwitchZone)
            {
                var row = _rows[i] with { On = !_rows[i].On };
                _rows[i] = row;
                Invalidate();
                Toggled?.Invoke(row.Id, row.On);
            }
            else Opened?.Invoke(_rows[i].Id);
        }
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        int rh = RowHeight;
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            int y = i * rh;
            if (i == _hot && !_hotSwitch) Theme.FillRound(g, new RectangleF(-Theme.D(6), y + Theme.D(2), Width - SwitchZone + Theme.D(4), rh - Theme.D(4)), Theme.D(8), Theme.Subtle);
            int t = Theme.D(32);
            var tile = new Rectangle(0, y + (rh - t) / 2, t, t);
            Theme.FillRound(g, tile, Theme.D(9), row.Tile);
            Theme.Text(g, row.Abbrev, Theme.Tag, tile, Color.White, Theme.Center);
            int x = t + Theme.D(10);
            int sw = Theme.D(40);
            int chev = Theme.D(18);
            int nameH = Theme.Measure("Ag", Theme.BodySemi).Height;
            int subH = Theme.Measure("Ag", Theme.Small).Height;
            int tw = Math.Max(Theme.D(8), Width - x - SwitchZone - chev);
            Theme.Text(g, row.Name, Theme.BodySemi, new Rectangle(x, y + Theme.D(6), tw, nameH), Theme.Tx);
            Theme.Text(g, row.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " terim · göster", Theme.Small, new Rectangle(x, y + Theme.D(6) + nameH, tw, subH), i == _hot && !_hotSwitch ? Theme.AccD : Theme.Tx2);
            Theme.Glyph(g, Ic.Chevron, Theme.Icon12, new Rectangle(Width - SwitchZone - chev, y, chev, rh), i == _hot && !_hotSwitch ? Theme.Tx2 : Theme.Tx3);
            DrawSwitch(g, new RectangleF(Width - sw, y + (rh - Theme.D(22)) / 2f, sw, Theme.D(22)), row.On, i == _hot);
            if (i < _rows.Count - 1)
            {
                using var p = new Pen(Theme.Line);
                g.DrawLine(p, 0, y + rh - 1, Width, y + rh - 1);
            }
        }
    }

    internal static void DrawSwitch(Graphics g, RectangleF track, bool on, bool hover)
    {
        track.Inflate(-0.75f, -0.75f);
        float h = track.Height;
        if (on) Theme.FillRound(g, track, h / 2, hover ? Theme.AccDD : Theme.AccD);
        else
        {
            Theme.FillRound(g, track, h / 2, Theme.Card);
            Theme.StrokeRound(g, track, h / 2, Theme.ToggleOff, Theme.D(1.5f));
        }
        float d = Theme.D(on ? 14 : 13);
        float cx = on ? track.Right - Theme.D(4) - d / 2 : track.X + Theme.D(4) + d / 2;
        using var knob = new SolidBrush(on ? Color.White : Theme.Hex(0x5B6876));
        g.FillEllipse(knob, cx - d / 2, track.Y + (h - d) / 2, d, d);
    }
}

internal sealed record TermRow(string English, string Turkish, string? Pack);

/// <summary>Glossary terms. User rows show a delete button on hover; pack rows show their pack name.</summary>
internal sealed class TermList : UiControl
{
    private readonly List<TermRow> _rows = new();
    private int _hot = -1;
    private bool _hotDelete;
    public event Action<TermRow>? DeleteClicked;
    public string Empty { get; set; } = "";

    public static int RowHeight => Math.Max(Theme.D(36), Theme.Measure("Ag", Theme.Body).Height + Theme.D(14));

    public void SetRows(IEnumerable<TermRow> rows)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        Height = _rows.Count == 0 ? Theme.D(34) : _rows.Count * RowHeight;
        Invalidate();
    }

    private Rectangle DeleteRect(int i) => new(Width - Theme.D(28), i * RowHeight + (RowHeight - Theme.D(26)) / 2, Theme.D(26), Theme.D(26));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = _rows.Count == 0 ? -1 : e.Y / RowHeight;
        if (h >= _rows.Count) h = -1;
        bool del = h >= 0 && _rows[h].Pack == null && DeleteRect(h).Contains(e.Location);
        if (h != _hot || del != _hotDelete) { _hot = h; _hotDelete = del; Invalidate(); }
        Cursor = del ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hot = -1; _hotDelete = false; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = _rows.Count == 0 ? -1 : e.Y / RowHeight;
        if (e.Button == MouseButtons.Left && i >= 0 && i < _rows.Count && _rows[i].Pack == null && DeleteRect(i).Contains(e.Location))
            DeleteClicked?.Invoke(_rows[i]);
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        if (_rows.Count == 0)
        {
            Theme.Text(g, Empty, Theme.Small, new Rectangle(0, 0, Width, Height), Theme.Tx3);
            return;
        }
        int rh = RowHeight;
        int arrow = Theme.D(26);
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            int y = i * rh;
            if (i == _hot) Theme.FillRound(g, new RectangleF(-Theme.D(6), y + Theme.D(2), Width + Theme.D(12), rh - Theme.D(4)), Theme.D(8), Theme.Subtle);
            int packW = row.Pack == null ? 0 : Theme.Measure(row.Pack, Theme.Small).Width + Theme.D(8);
            int right = Math.Max(arrow + Theme.D(16), row.Pack == null ? Width - Theme.D(32) : Width - packW - Theme.D(8));
            int col = Math.Max(Theme.D(8), (right - arrow) / 2);
            Theme.Text(g, row.English, Theme.Body, new Rectangle(0, y, col, rh), Theme.Tx);
            Theme.Glyph(g, Ic.Arrow, Theme.Icon12, new Rectangle(col, y, arrow, rh), Theme.Tx3);
            string tr = string.IsNullOrEmpty(row.Turkish) ? "olduğu gibi" : row.Turkish;
            Theme.Text(g, tr, string.IsNullOrEmpty(row.Turkish) ? Theme.Body : Theme.BodySemi, new Rectangle(col + arrow, y, right - col - arrow, rh), string.IsNullOrEmpty(row.Turkish) ? Theme.Tx3 : Theme.Tx);
            if (row.Pack != null) Theme.Text(g, row.Pack, Theme.Small, new Rectangle(right, y, Width - right, rh), Theme.Tx3, Theme.Right);
            else if (i == _hot)
            {
                var d = DeleteRect(i);
                if (_hotDelete) Theme.FillRound(g, d, Theme.D(6), Theme.RedT);
                Theme.Glyph(g, Ic.Delete, Theme.Icon12, d, _hotDelete ? Theme.Red : Theme.Tx3);
            }
            if (i < _rows.Count - 1)
            {
                using var p = new Pen(Theme.Line);
                g.DrawLine(p, 0, y + rh - 1, Width, y + rh - 1);
            }
        }
    }
}
