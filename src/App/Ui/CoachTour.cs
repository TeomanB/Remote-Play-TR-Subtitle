namespace AltyaziCevirici.Ui;

/// <summary>One coach-mark: a client rectangle to spotlight and the callout copy.</summary>
internal readonly record struct TourCue(Rectangle Spot, string Title, string Body, int Page);

/// <summary>
/// Dims the window, rings one area, and shows a small callout. The picture underneath is a snapshot
/// so the highlight can sit above every child without a transparent WinForms hole.
/// </summary>
internal sealed class CoachTour : Control
{
    private readonly UiButton _back = new() { Text = "Geri", Kind = ButtonKind.Secondary };
    private readonly UiButton _next = new() { Text = "İleri", Kind = ButtonKind.Primary };
    private readonly UiButton _skip = new() { Text = "Atla", Kind = ButtonKind.Ghost };

    private Bitmap? _behind;
    private TourCue[] _cues = Array.Empty<TourCue>();
    private int _index;
    private Rectangle _spot;
    private Rectangle _card;
    private string _title = "";
    private string _body = "";

    /// <summary>Raised before the snapshot, so the host can switch pages.</summary>
    public event EventHandler<int>? Preparing;

    public event EventHandler? Closed;

    public CoachTour()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Visible = false;
        TabStop = false;
        foreach (var b in new[] { _back, _next, _skip })
        {
            b.TabStop = true;
            Controls.Add(b);
        }
        _back.Click += (_, _) => Go(_index - 1);
        _next.Click += (_, _) => { if (_index >= _cues.Length - 1) Finish(); else Go(_index + 1); };
        _skip.Click += (_, _) => Finish();
    }

    public bool Running => Visible;

    public void Play(TourCue[] cues)
    {
        _cues = cues;
        _index = 0;
        if (Parent != null) Bounds = Parent.ClientRectangle;
        Visible = true;
        BringToFront();
        Apply();
    }

    public void Go(int index)
    {
        if (_cues.Length == 0) return;
        _index = Math.Clamp(index, 0, _cues.Length - 1);
        Visible = true;
        BringToFront();
        Apply();
    }

    private void Finish()
    {
        Visible = false;
        _behind?.Dispose();
        _behind = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply()
    {
        if (_cues.Length == 0) return;
        Preparing?.Invoke(this, _index);
        var cue = _cues[_index];
        _spot = cue.Spot;
        _title = cue.Title;
        _body = cue.Body;
        _back.Enabled = _index > 0;
        LayoutCard();
        Snapshot();
        Invalidate();
    }

    private void LayoutCard()
    {
        int w = Theme.D(340);
        int pad = Theme.D(16);
        int bodyH = Theme.WrapHeight(_body, Theme.Small, w - 2 * pad);
        int h = pad + Theme.D(18) + Theme.D(6) + Theme.D(22) + Theme.D(4) + bodyH + Theme.D(14) + Theme.D(34) + pad;
        _card = PlaceCard(w, h, _spot);
        int bw = Theme.D(84);
        int y = _card.Bottom - pad - Theme.D(34);
        _skip.SetBounds(_card.X + pad, y, Theme.D(64), Theme.D(34));
        _next.SetBounds(_card.Right - pad - bw, y, bw, Theme.D(34));
        _back.SetBounds(_next.Left - Theme.D(8) - bw, y, bw, Theme.D(34));
    }

    private Rectangle PlaceCard(int w, int h, Rectangle spot)
    {
        int m = Theme.D(16);
        var options = new[]
        {
            new Rectangle(spot.Right + m, spot.Top, w, h),
            new Rectangle(spot.Left, spot.Bottom + m, w, h),
            new Rectangle(spot.Left, spot.Top - h - m, w, h),
            new Rectangle(spot.Left - w - m, spot.Top, w, h),
        };
        foreach (var option in options)
        {
            var clamped = Clamp(option, m);
            if (!clamped.IntersectsWith(Rectangle.Inflate(spot, 4, 4))) return clamped;
        }
        return Clamp(options[1], m);
    }

    private Rectangle Clamp(Rectangle r, int m)
    {
        int x = Math.Clamp(r.X, m, Math.Max(m, Width - r.Width - m));
        int y = Math.Clamp(r.Y, m, Math.Max(m, Height - r.Height - m));
        return new Rectangle(x, y, r.Width, r.Height);
    }

    private void Snapshot()
    {
        if (Parent == null || Parent.ClientSize.Width < 20 || Parent.ClientSize.Height < 20) return;
        Bounds = Parent.ClientRectangle;
        Visible = false;
        var bmp = new Bitmap(Parent.ClientSize.Width, Parent.ClientSize.Height);
        Parent.DrawToBitmap(bmp, new Rectangle(Point.Empty, Parent.ClientSize));
        _behind?.Dispose();
        _behind = bmp;
        Visible = true;
        BringToFront();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        if (_behind != null) g.DrawImage(_behind, Point.Empty);
        else g.Clear(Theme.Bg);
        using (var dim = new SolidBrush(Color.FromArgb(148, 23, 33, 43)))
            g.FillRectangle(dim, ClientRectangle);
        if (_spot.Width > 8 && _spot.Height > 8 && _behind != null)
        {
            var hole = Rectangle.Intersect(Rectangle.Inflate(_spot, Theme.D(6), Theme.D(6)), ClientRectangle);
            g.DrawImage(_behind, hole, hole, GraphicsUnit.Pixel);
            using var ring = new Pen(Theme.Acc, Theme.D(3));
            using var path = Theme.Round(hole, Theme.D(12));
            g.DrawPath(ring, path);
        }
        Theme.FillRound(g, _card, Theme.D(14), Color.White);
        using (var edge = new Pen(Theme.Line2))
        using (var path = Theme.Round(new RectangleF(_card.X + 0.5f, _card.Y + 0.5f, _card.Width - 1f, _card.Height - 1f), Theme.D(14)))
            g.DrawPath(edge, path);
        int pad = Theme.D(16);
        string counter = (_index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " / " + _cues.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Theme.Text(g, counter, Theme.Caption, new Rectangle(_card.X + pad, _card.Y + pad, _card.Width - 2 * pad, Theme.D(16)), Theme.Tx3);
        Theme.Text(g, _title, Theme.H2, new Rectangle(_card.X + pad, _card.Y + pad + Theme.D(18), _card.Width - 2 * pad, Theme.D(24)), Theme.Tx);
        Theme.Text(g, _body, Theme.Small, new Rectangle(_card.X + pad, _card.Y + pad + Theme.D(46), _card.Width - 2 * pad, _card.Height - Theme.D(100)), Theme.Tx2, Theme.Wrap);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _behind?.Dispose();
        base.Dispose(disposing);
    }
}
