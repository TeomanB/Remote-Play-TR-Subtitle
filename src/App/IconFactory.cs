using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AltyaziCevirici;

/// <summary>
/// Window, taskbar, and tray icons, all from the turquoise console artwork embedded in the exe.
/// The tray icon is rendered from the 512 px PNG at the exact small-icon size for the current DPI, so Windows
/// never scales (or substitutes) it. <see cref="RefreshShellIcon"/> asks Explorer to drop its cached copy of
/// the exe icon, which is what kept the old red TR mark on shortcuts and notifications after an update.
/// </summary>
internal static class IconFactory
{
    private static readonly List<MemoryStream> Kept = new();
    private static Bitmap? _art;

    public static Icon Window()
    {
        using var resource = typeof(IconFactory).Assembly.GetManifestResourceStream("AltyaziCevirici.app.ico")
            ?? throw new InvalidOperationException("app.ico");
        var copy = new MemoryStream();
        resource.CopyTo(copy);
        copy.Position = 0;
        Kept.Add(copy);
        return new Icon(copy);
    }

    public static Icon Tray(bool running)
    {
        int size = SmallIconSize();
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(Art(), new Rectangle(0, 0, size, size));
            int dot = Math.Max(5, size * 5 / 16);
            var rect = new Rectangle(size - dot, size - dot, dot - 1, dot - 1);
            using var fill = new SolidBrush(running ? Color.FromArgb(255, 34, 197, 94) : Color.FromArgb(255, 160, 168, 178));
            using var ring = new Pen(Color.White, Math.Max(1f, size / 16f));
            g.FillEllipse(fill, rect);
            g.DrawEllipse(ring, rect);
        }
        IntPtr handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    private static Bitmap Art()
    {
        if (_art != null) return _art;
        using var resource = typeof(IconFactory).Assembly.GetManifestResourceStream("AltyaziCevirici.icon.png")
            ?? throw new InvalidOperationException("icon.png");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        copy.Position = 0;
        using var loaded = new Bitmap(copy);
        _art = new Bitmap(loaded);
        return _art;
    }

    private static int SmallIconSize()
    {
        try
        {
            using var screen = Graphics.FromHwnd(IntPtr.Zero);
            int s = GetSystemMetricsForDpi(49 /* SM_CXSMICON */, (uint)Math.Round(screen.DpiX));
            return Math.Clamp(s, 16, 64);
        }
        catch (EntryPointNotFoundException) { return SystemInformation.SmallIconSize.Width; }
    }

    /// <summary>Tells Explorer the exe changed so shortcuts, taskbar pins, and notifications reload its icon.</summary>
    public static void RefreshShellIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            const int SHCNE_UPDATEITEM = 0x00002000;
            const uint SHCNF_PATHW = 0x0005, SHCNF_FLUSHNOWAIT = 0x3000;
            var path = Marshal.StringToHGlobalUni(exe);
            try { SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path, IntPtr.Zero); }
            finally { Marshal.FreeHGlobal(path); }
        }
        catch (Exception ex) { Core.ErrorLog.Write(ex, "shell-icon"); }
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
