using AltyaziCevirici.Core;

namespace AltyaziCevirici;

/// <summary>On-screen drag and width handles for the subtitle box. Kaydet writes the box back.</summary>
internal sealed class PlacementEditorForm : Form
{
    private readonly AppSettings _settings;
    private readonly EkranDikdortgen _region;
    private readonly string _sample;
    private readonly Rectangle _virtual;
    private Rectangle _box;
    private bool _drag;
    private bool _left;
    private bool _right;
    private Point _grab;
    private Rectangle _grabBox;
    private readonly CheckBox _follow;
    private readonly Button _save;
    private readonly Button _cancel;

    public PlacementEditorForm(AppSettings settings, Rectangle region, string sample)
    {
        _settings = settings;
        _region = new EkranDikdortgen(region.X, region.Y, region.Width, region.Height);
        _sample = sample;
        _virtual = SystemInformation.VirtualScreen;
        var screen = MonitorOf(settings.MonitorAdi, region);
        int height = 88;
        int width = SubtitleLayout.ChooseWidth(settings, _region, To(screen));
        var placed = settings.KonumAyarlandi
            ? SubtitleLayout.Resolve(settings, _region, To(screen), width, height)
            : SubtitleLayout.Legacy(settings.OverlayKonumu, _region, To(screen), Math.Min(width, 720), height, settings.DikeyKaydirma);
        _box = new Rectangle(placed.X, placed.Y, placed.Width, placed.Height);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtual;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Color.Black;
        Cursor = Cursors.SizeAll;
        Text = "Altyazı konumunu düzenle";

        var bar = new Panel { Height = 52, Width = 640, BackColor = DarkTheme.Surface };
        bar.Location = new Point(screen.X - _virtual.X + 24, screen.Y - _virtual.Y + 24);
        var title = new Label
        {
            Text = "Altyazı konumunu düzenle  —  kutuyu sürükle, kenarlardan genişlet",
            AutoSize = true, ForeColor = DarkTheme.Text, Location = new Point(12, 16),
        };
        _follow = new CheckBox
        {
            Text = "Bölgeyi takip et",
            Checked = settings.BolgeyiTakipEt,
            AutoSize = true,
            ForeColor = DarkTheme.Text,
            Location = new Point(12, 28),
            Visible = false,
        };
        _save = new Button { Text = "Kaydet", AutoSize = true };
        _cancel = new Button { Text = "Vazgeç", AutoSize = true };
        _save.Click += (_, _) => SaveAndClose();
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Width = 280, Padding = new Padding(8),
        };
        row.Controls.Add(_cancel);
        row.Controls.Add(_save);
        _follow.Location = new Point(12, 14);
        _follow.Visible = true;
        bar.Controls.Add(row);
        bar.Controls.Add(_follow);
        bar.Controls.Add(title);
        title.Location = new Point(12, 4);
        _follow.Location = new Point(12, 26);
        Controls.Add(bar);
        DarkTheme.Apply(bar);
        _save.BackColor = DarkTheme.Accent;
        _save.ForeColor = Color.White;
    }

    private void SaveAndClose()
    {
        var box = ToRect(_box);
        var screen = To(Screen.FromPoint(new Point(_box.X + _box.Width / 2, _box.Y + _box.Height / 2)).Bounds);
        box = SubtitleLayout.Snap(box, screen, 0);
        SubtitleLayout.Remember(_settings, box, _region, _follow.Checked);
        _settings.MonitorAdi = Screen.FromPoint(new Point(box.X + box.Width / 2, box.Y + box.Height / 2)).DeviceName;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        else if (e.KeyCode == Keys.Enter) SaveAndClose();
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        if (hit == 0) { base.OnMouseDown(e); return; }
        _drag = true;
        _left = hit == 1;
        _right = hit == 2;
        _grab = e.Location;
        _grabBox = _box;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_drag)
        {
            Cursor = Hit(e.Location) switch { 1 or 2 => Cursors.SizeWE, 3 => Cursors.SizeAll, _ => Cursors.Default };
            return;
        }
        int dx = (e.X + _virtual.X) - (_grab.X + _virtual.X);
        int dy = (e.Y + _virtual.Y) - (_grab.Y + _virtual.Y);
        var next = _grabBox;
        if (_right)
            next.Width = Math.Max(SubtitleLayout.MinWidth, _grabBox.Width + dx);
        else if (_left)
        {
            int w = Math.Max(SubtitleLayout.MinWidth, _grabBox.Width - dx);
            next.X = _grabBox.Right - w;
            next.Width = w;
        }
        else
        {
            next.X = _grabBox.X + dx;
            next.Y = _grabBox.Y + dy;
        }
        var screen = To(Screen.FromPoint(new Point(next.X + next.Width / 2, next.Y + next.Height / 2)).Bounds);
        var snapped = SubtitleLayout.Snap(ToRect(next), screen);
        _box = new Rectangle(snapped.X, snapped.Y, snapped.Width, snapped.Height);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _drag = _left = _right = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    /// <summary>0 miss, 1 left handle, 2 right handle, 3 body.</summary>
    private int Hit(Point client)
    {
        var b = ClientBox();
        var left = new Rectangle(b.Left - 6, b.Top, 14, b.Height);
        var right = new Rectangle(b.Right - 8, b.Top, 14, b.Height);
        if (left.Contains(client)) return 1;
        if (right.Contains(client)) return 2;
        if (b.Contains(client)) return 3;
        return 0;
    }

    private Rectangle ClientBox() => new(_box.X - _virtual.X, _box.Y - _virtual.Y, _box.Width, _box.Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(150, 8, 8, 10));
        var b = ClientBox();
        int alpha = (int)Math.Round(Math.Clamp(_settings.ArkaPlanOpakligi, 0, 100) * 2.55);
        using var bg = new SolidBrush(Color.FromArgb(Math.Max(alpha, 40), 16, 16, 20));
        e.Graphics.FillRectangle(bg, b);
        using var pen = new Pen(DarkTheme.Accent, 2);
        e.Graphics.DrawRectangle(pen, b);
        using var handle = new SolidBrush(DarkTheme.Accent);
        e.Graphics.FillRectangle(handle, b.Left - 4, b.Top + b.Height / 2 - 14, 8, 28);
        e.Graphics.FillRectangle(handle, b.Right - 4, b.Top + b.Height / 2 - 14, 8, 28);
        using var font = new Font("Segoe UI", Math.Clamp(_settings.YaziBoyutu, 10f, 72f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(AppSettings.TryParseColor(_settings.YaziRengi, out var c) ? Color.FromArgb(c.R, c.G, c.B) : Color.White);
        var layout = new RectangleF(b.X + 16, b.Y + 12, Math.Max(20, b.Width - 32), Math.Max(20, b.Height - 24));
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        e.Graphics.DrawString(_sample, font, text, layout, fmt);
    }

    private static EkranDikdortgen To(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
    private static EkranDikdortgen ToRect(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    private static Rectangle MonitorOf(string name, Rectangle region)
    {
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var sc in Screen.AllScreens)
                if (string.Equals(sc.DeviceName, name, StringComparison.OrdinalIgnoreCase))
                    return sc.Bounds;
        }
        var probe = region.Width > 0 ? region : new Rectangle(0, 0, 1, 1);
        return Screen.FromRectangle(probe).Bounds;
    }
}
