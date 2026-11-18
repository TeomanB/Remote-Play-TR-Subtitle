using System.Drawing.Drawing2D;

namespace AltyaziCevirici;

/// <summary>Full-screen frozen screenshot on which the user drags the subtitle rectangle.</summary>
internal sealed class RegionSelectorForm : Form
{
    private readonly Bitmap _shot;
    private readonly Bitmap _dim;
    private readonly Rectangle _virtual;
    private Point _start;
    private Rectangle _sel;
    private bool _dragging;

    /// <summary>Selected region in screen coordinates (valid when DialogResult == OK).</summary>
    public Rectangle SelectedRegion { get; private set; }

    public RegionSelectorForm()
    {
        _virtual = SystemInformation.VirtualScreen;
        _shot = new Bitmap(_virtual.Width, _virtual.Height);
        using (var g = Graphics.FromImage(_shot))
            g.CopyFromScreen(_virtual.Left, _virtual.Top, 0, 0, _virtual.Size);
        _dim = new Bitmap(_shot);
        using (var g = Graphics.FromImage(_dim))
        using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            g.FillRectangle(b, 0, 0, _dim.Width, _dim.Height);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtual;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        Text = "Altyazı bölgesini seç";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _virtual;
        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _start = e.Location;
        _sel = new Rectangle(e.Location, Size.Empty);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var old = _sel;
        _sel = Rectangle.FromLTRB(Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y),
                                  Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));
        var inv = Rectangle.Union(old, _sel);
        inv.Inflate(40, 40);
        Invalidate(inv);
        Invalidate(new Rectangle(0, 0, Width, 120));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        if (_sel.Width < 20 || _sel.Height < 10) { _sel = Rectangle.Empty; Invalidate(); return; }
        SelectedRegion = new Rectangle(_sel.X + _virtual.Left, _sel.Y + _virtual.Top, _sel.Width, _sel.Height);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImage(_dim, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
        if (_sel.Width > 0 && _sel.Height > 0)
        {
            g.DrawImage(_shot, _sel, _sel, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(255, 0, 200, 255), 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, _sel);
        }
        const string msg = "Altyazının çıktığı alanı fareyle sürükleyerek seç  •  Esc / sağ tık: iptal";
        using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
        var size = g.MeasureString(msg, font);
        var box = new RectangleF((Width - size.Width) / 2 - 16, 30, size.Width + 32, size.Height + 16);
        using (var bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20))) g.FillRectangle(bg, box);
        g.DrawString(msg, font, Brushes.White, box.X + 16, box.Y + 8);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _shot.Dispose(); _dim.Dispose(); }
        base.Dispose(disposing);
    }
}
