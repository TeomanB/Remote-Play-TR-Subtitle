using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AltyaziCevirici;

internal static class IconFactory
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(255, 200, 16, 46));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var f = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("TR", f, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        }
        IntPtr h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        return icon;
    }
}
