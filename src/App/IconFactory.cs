using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AltyaziCevirici;

/// <summary>
/// Tray and window icons are painted from the embedded PNG. System.Drawing.Icon on the
/// multi-size .ico was still showing the old red "TR" mark on the owner's taskbar.
/// </summary>
internal static class IconFactory
{
    public static Icon Window() => Make(48, status: false, running: false);

    public static Icon Tray(bool running) => Make(32, status: true, running);

    private static Icon Make(int size, bool status, bool running)
    {
        using var src = LoadPng();
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, size, size));
            if (status)
            {
                int dot = Math.Max(6, size / 4);
                int margin = Math.Max(1, size / 16);
                var rect = new Rectangle(size - dot - margin, size - dot - margin, dot, dot);
                using var fill = new SolidBrush(running ? Color.FromArgb(255, 48, 196, 96) : Color.FromArgb(255, 150, 156, 166));
                using var ring = new Pen(Color.FromArgb(230, 16, 18, 22), 1.5f);
                g.FillEllipse(fill, rect);
                g.DrawEllipse(ring, rect);
            }
        }
        IntPtr handle = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    private static Bitmap LoadPng()
    {
        using var resource = typeof(IconFactory).Assembly.GetManifestResourceStream("AltyaziCevirici.icon.png")
            ?? throw new InvalidOperationException("icon.png");
        using var raw = new Bitmap(resource);
        return new Bitmap(raw);
    }
}
