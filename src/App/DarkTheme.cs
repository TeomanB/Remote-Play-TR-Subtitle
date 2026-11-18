namespace AltyaziCevirici;

internal static class DarkTheme
{
    public static readonly Color Bg = Color.FromArgb(30, 31, 34);
    public static readonly Color Surface = Color.FromArgb(37, 39, 44);
    public static readonly Color Card = Color.FromArgb(48, 51, 58);
    public static readonly Color Text = Color.FromArgb(235, 236, 240);
    public static readonly Color Muted = Color.FromArgb(154, 160, 170);
    public static readonly Color Accent = Color.FromArgb(56, 132, 244);
    public static readonly Color Good = Color.FromArgb(42, 157, 92);
    public static readonly Color Stop = Color.FromArgb(196, 72, 68);
    public static readonly Color Line = Color.FromArgb(70, 74, 82);

    public static void Apply(Control root)
    {
        Paint(root);
        foreach (Control child in root.Controls) Apply(child);
    }

    private static void Paint(Control c)
    {
        c.ForeColor = Text;
        switch (c)
        {
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Line;
                b.BackColor = Card;
                b.ForeColor = Text;
                b.Cursor = Cursors.Hand;
                break;
            case TextBox or ComboBox or NumericUpDown:
                c.BackColor = Card;
                c.ForeColor = Text;
                break;
            case DataGridView grid:
                grid.BackgroundColor = Surface;
                grid.BorderStyle = BorderStyle.None;
                grid.GridColor = Line;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Card;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Accent;
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.RowHeadersDefaultCellStyle.BackColor = Surface;
                grid.RowHeadersDefaultCellStyle.ForeColor = Text;
                break;
            case TabPage:
                c.BackColor = Surface;
                c.ForeColor = Text;
                break;
            case Panel or FlowLayoutPanel or TableLayoutPanel or TabControl:
                c.BackColor = c is TabControl ? Bg : Surface;
                break;
            default:
                c.BackColor = c is Form ? Bg : Surface;
                break;
        }
    }
}
