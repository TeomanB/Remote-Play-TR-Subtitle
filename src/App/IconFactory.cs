using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AltyaziCevirici;

/// <summary>Loads the console icon. The tray copy adds a small running/stopped dot.</summary>
internal static class IconFactory
{
    private static readonly List<MemoryStream> Kept = new();

    public static Icon Window() => Open("AltyaziCevirici.app.ico");

    public static Icon Tray(bool running)
    {
        using var png = LoadPng();
        byte[] small = Render(png, 16, running);
        byte[] medium = Render(png, 32, running);
        return OpenBytes(PackIco(stackalloc int[] { 16, 32 }, small, medium));
    }

    private static Icon Open(string name)
    {
        using var resource = typeof(IconFactory).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(name);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return OpenBytes(copy.ToArray());
    }

    private static Bitmap LoadPng()
    {
        using var resource = typeof(IconFactory).Assembly.GetManifestResourceStream("AltyaziCevirici.icon.png")
            ?? throw new InvalidOperationException("icon.png");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        copy.Position = 0;
        return new Bitmap(copy);
    }

    private static Icon OpenBytes(byte[] ico)
    {
        var stream = new MemoryStream(ico);
        Kept.Add(stream);
        return new Icon(stream);
    }

    private static byte[] Render(Bitmap src, int size, bool running)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, size, size));
            int dot = Math.Max(4, size / 4);
            int margin = Math.Max(1, size / 16);
            var rect = new Rectangle(size - dot - margin, size - dot - margin, dot, dot);
            using var fill = new SolidBrush(running ? Color.FromArgb(255, 48, 196, 96) : Color.FromArgb(255, 150, 156, 166));
            using var ring = new Pen(Color.FromArgb(230, 18, 20, 24), Math.Max(1f, size / 16f));
            g.FillEllipse(fill, rect);
            g.DrawEllipse(ring, rect);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] PackIco(ReadOnlySpan<int> sizes, params byte[][] pngs)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)pngs.Length);
        int offset = 6 + 16 * pngs.Length;
        for (int i = 0; i < pngs.Length; i++)
        {
            int side = sizes[i];
            byte dim = side >= 256 ? (byte)0 : (byte)side;
            w.Write(dim);
            w.Write(dim);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
        w.Flush();
        return ms.ToArray();
    }
}
