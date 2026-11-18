using AltyaziCevirici.Core;

namespace AltyaziCevirici.Ui;

/// <summary>Loaded local line packs. A click selects the game the ready-translation toggle applies to.</summary>
internal sealed class LinePackList : UiControl
{
    private HazirDizin[] _rows = Array.Empty<HazirDizin>();
    private string _selected = "";

    public event EventHandler<string>? Chosen;

    public int Count => _rows.Length;

    public void Set(IReadOnlyList<HazirDizin> rows, string selected)
    {
        _rows = rows.ToArray();
        _selected = selected ?? "";
        Height = Math.Max(Theme.D(4), _rows.Length * Theme.D(34));
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int row = e.Y / Math.Max(1, Theme.D(34));
        if (row >= 0 && row < _rows.Length) Chosen?.Invoke(this, _rows[row].Oyun);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        int h = Theme.D(34);
        for (int i = 0; i < _rows.Length; i++)
        {
            var r = new Rectangle(0, i * h, Width, h - Theme.D(4));
            bool on = _rows[i].Oyun.Equals(_selected, StringComparison.OrdinalIgnoreCase);
            Theme.FillRound(g, r, Theme.D(8), on ? Theme.AccT : Theme.Subtle);
            Theme.Text(g, _rows[i].Oyun, Theme.BodySemi, new Rectangle(r.X + Theme.D(10), r.Y, r.Width - Theme.D(90), r.Height), Theme.Tx);
            Theme.Text(g, _rows[i].Satir.ToString(System.Globalization.CultureInfo.InvariantCulture) + " satır", Theme.Small,
                new Rectangle(r.X, r.Y, r.Width - Theme.D(10), r.Height), Theme.Tx2, Theme.Right);
        }
    }
}
