using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AltyaziCevirici;

/// <summary>
/// Window and tray icons from the embedded BMP .ico (GDI cannot load a PNG-compressed icon,
/// which is why the tray kept showing the old red TR mark).
/// </summary>
internal static class IconFactory
{
    private static readonly List<MemoryStream> Kept = new();

    public static Icon Window() => Load(new Size(48, 48));

    public static Icon Tray(bool running)
    {
        using var icon = Load(new Size(32, 32));
        using var bmp = new Bitmap(icon.ToBitmap());
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int dot = Math.Max(6, bmp.Width / 4);
            int margin = Math.Max(1, bmp.Width / 16);
            var rect = new Rectangle(bmp.Width - dot - margin, bmp.Height - dot - margin, dot, dot);
            using var fill = new SolidBrush(running ? Color.FromArgb(255, 48, 196, 96) : Color.FromArgb(255, 150, 156, 166));
            using var ring = new Pen(Color.FromArgb(230, 16, 18, 22), 1.5f);
            g.FillEllipse(fill, rect);
            g.DrawEllipse(ring, rect);
        }
        IntPtr handle = bmp.GetHicon();
        var created = (Icon)Icon.FromHandle(handle).Clone();
        Native.DestroyIcon(handle);
        return created;
    }

    private static Icon Load(Size size)
    {
        using var resource = typeof(IconFactory).Assembly.GetManifestResourceStream("AltyaziCevirici.app.ico")
            ?? throw new InvalidOperationException("app.ico");
        var copy = new MemoryStream();
        resource.CopyTo(copy);
        copy.Position = 0;
        Kept.Add(copy);
        return new Icon(copy, size);
    }
}
