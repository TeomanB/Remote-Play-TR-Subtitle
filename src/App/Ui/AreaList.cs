using AltyaziCevirici.Core;

namespace AltyaziCevirici.Ui;

internal sealed class AreaList : UiControl
{
    public readonly record struct Row(string Name, string ColorHex, bool On);

    private Row[] _rows = Array.Empty<Row>();
    private int _selected;

    public event EventHandler<int>? Chosen;
    public event EventHandler<(int Index, bool On)>? Toggled;

    public int Count => _rows.Length;
    public static int RowHeight => Math.Max(Theme.D(36), Theme.Measure("Ag", Theme.BodySemi).Height + Theme.D(16));

    public void Set(IReadOnlyList<Row> rows, int selected)
    {
        _rows = rows.ToArray();
        _selected = selected;
        Height = Math.Max(Theme.D(4), Math.Max(1, _rows.Length) * RowHeight);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int row = e.Y / Math.Max(1, RowHeight);
        if (row < 0 || row >= _rows.Length) return;
        if (e.X >= Width - Theme.D(52)) Toggled?.Invoke(this, (row, !_rows[row].On));
        else Chosen?.Invoke(this, row);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        int h = RowHeight;
        for (int i = 0; i < _rows.Length; i++)
        {
            var r = new Rectangle(0, i * h, Width, h - Theme.D(4));
            Theme.FillRound(g, r, Theme.D(8), i == _selected ? Theme.AccT : Theme.Subtle);
            var dot = new Rectangle(r.X + Theme.D(8), r.Y + (r.Height - Theme.D(14)) / 2, Theme.D(14), Theme.D(14));
            var color = AppSettings.TryParseColor(_rows[i].ColorHex, out var rgb) ? Color.FromArgb(255, rgb.R, rgb.G, rgb.B) : Theme.KeyOrig;
            Theme.FillRound(g, dot, Theme.D(7), color);
            Theme.Text(g, _rows[i].Name, Theme.BodySemi, new Rectangle(dot.Right + Theme.D(8), r.Y, Math.Max(Theme.D(20), r.Width - Theme.D(80)), r.Height), Theme.Tx);
            Theme.Text(g, _rows[i].On ? "açık" : "kapalı", Theme.Small, new Rectangle(r.Right - Theme.D(56), r.Y, Theme.D(48), r.Height), _rows[i].On ? Theme.AccD : Theme.Tx3, Theme.Right);
        }
    }
}
