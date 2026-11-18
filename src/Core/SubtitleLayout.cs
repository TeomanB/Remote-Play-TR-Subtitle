namespace AltyaziCevirici.Core;

/// <summary>Screen rectangle in pixels. Independent of WinForms so placement can be tested anywhere.</summary>
public readonly record struct EkranDikdortgen(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public enum AltyaziOnAyar { Serbest, AltOrta, UstOrta, BolgeninAlti, BolgeninUstu }

/// <summary>
/// Where the Turkish subtitle box sits. Until the user picks a preset or drags the box,
/// <see cref="Resolve"/> repeats the v1.2 anchor (above, below, or over the capture region).
/// </summary>
public static class SubtitleLayout
{
    public const int SnapPx = 16;
    /// <summary>Smallest saved subtitle box. 1.4 forced 200px, so a short drag could not stick.</summary>
    public const int MinWidth = BoxEdit.MinSize;

    public static int MaxWidth(int regionWidth, int screenWidth) =>
        Math.Clamp(Math.Max(regionWidth, 500), MinWidth, Math.Max(MinWidth, screenWidth));

    public static int ChooseWidth(AppSettings s, EkranDikdortgen region, EkranDikdortgen screen)
    {
        if (s.KonumAyarlandi && s.KutuGenislik >= MinWidth)
            return Math.Clamp(s.KutuGenislik, MinWidth, Math.Max(MinWidth, screen.Width));
        return MaxWidth(region.Width, screen.Width);
    }

    public static EkranDikdortgen ClampTo(EkranDikdortgen box, EkranDikdortgen screen)
    {
        int w = Math.Clamp(box.Width, 1, Math.Max(1, screen.Width));
        int h = Math.Clamp(box.Height, 1, Math.Max(1, screen.Height));
        int x = Math.Clamp(box.X, screen.X, Math.Max(screen.X, screen.Right - w));
        int y = Math.Clamp(box.Y, screen.Y, Math.Max(screen.Y, screen.Bottom - h));
        return new EkranDikdortgen(x, y, w, h);
    }

    /// <summary>Pulls a box to the screen center or edges when it is already within <paramref name="snapPx"/>.</summary>
    public static EkranDikdortgen Snap(EkranDikdortgen box, EkranDikdortgen screen, int snapPx = SnapPx)
    {
        int x = box.X;
        int y = box.Y;
        int cx = screen.X + (screen.Width - box.Width) / 2;
        int cy = screen.Y + (screen.Height - box.Height) / 2;
        if (Math.Abs(x - screen.X) <= snapPx) x = screen.X;
        else if (Math.Abs(box.Right - screen.Right) <= snapPx) x = screen.Right - box.Width;
        else if (Math.Abs(x - cx) <= snapPx) x = cx;

        if (Math.Abs(y - screen.Y) <= snapPx) y = screen.Y;
        else if (Math.Abs(box.Bottom - screen.Bottom) <= snapPx) y = screen.Bottom - box.Height;
        else if (Math.Abs(y - cy) <= snapPx) y = cy;
        return ClampTo(new EkranDikdortgen(x, y, box.Width, box.Height), screen);
    }

    public static EkranDikdortgen Preset(AltyaziOnAyar preset, EkranDikdortgen screen, EkranDikdortgen region, int width, int height)
    {
        width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, screen.Width));
        height = Math.Max(1, height);
        int centered = screen.X + (screen.Width - width) / 2;
        int onRegion = region.IsEmpty ? centered : region.X + (region.Width - width) / 2;
        EkranDikdortgen box = preset switch
        {
            AltyaziOnAyar.UstOrta => new(centered, screen.Y + 40, width, height),
            AltyaziOnAyar.AltOrta => new(centered, screen.Bottom - height - 40, width, height),
            AltyaziOnAyar.BolgeninAlti => new(onRegion, region.IsEmpty ? screen.Bottom - height - 40 : region.Bottom + 6, width, height),
            AltyaziOnAyar.BolgeninUstu => new(onRegion, region.IsEmpty ? screen.Y + 40 : region.Y - height - 6, width, height),
            _ => new(centered, screen.Y + 40, width, height),
        };
        return ClampTo(box, screen);
    }

    public static EkranDikdortgen Legacy(OverlayPlacement placement, EkranDikdortgen region, EkranDikdortgen screen, int boxWidth, int height, int offsetY)
    {
        int x = region.X + (region.Width - boxWidth) / 2;
        int y = placement switch
        {
            OverlayPlacement.Alt => region.Bottom + 6,
            OverlayPlacement.Uzerinde => region.Y + (region.Height - height) / 2,
            _ => region.Y - height - 6,
        } + offsetY;
        if (offsetY == 0)
        {
            if (y < screen.Y) y = region.Bottom + 6;
            if (y + height > screen.Bottom) y = Math.Max(screen.Y, region.Y - height - 6);
        }
        return ClampTo(new EkranDikdortgen(x, y, boxWidth, height), screen);
    }

    public static EkranDikdortgen Resolve(AppSettings s, EkranDikdortgen region, EkranDikdortgen screen, int boxWidth, int height)
    {
        if (!s.KonumAyarlandi)
            return Legacy(s.OverlayKonumu, region, screen, boxWidth, height, s.DikeyKaydirma);
        int w = s.KutuGenislik >= MinWidth ? Math.Clamp(s.KutuGenislik, MinWidth, Math.Max(MinWidth, screen.Width)) : boxWidth;
        int x = s.BolgeyiTakipEt && !region.IsEmpty ? region.X + s.BagilX : s.KutuX;
        int y = s.BolgeyiTakipEt && !region.IsEmpty ? region.Y + s.BagilY : s.KutuY;
        return ClampTo(new EkranDikdortgen(x, y, w, height), screen);
    }

    public static void Remember(AppSettings s, EkranDikdortgen box, EkranDikdortgen region, bool follow)
    {
        s.KonumAyarlandi = true;
        s.OnAyar = AltyaziOnAyar.Serbest;
        s.BolgeyiTakipEt = follow && !region.IsEmpty;
        s.KutuX = box.X;
        s.KutuY = box.Y;
        s.KutuGenislik = Math.Max(MinWidth, box.Width);
        s.BagilX = region.IsEmpty ? 0 : box.X - region.X;
        s.BagilY = region.IsEmpty ? 0 : box.Y - region.Y;
        s.DikeyKaydirma = 0;
    }

    public static void ApplyPreset(AppSettings s, AltyaziOnAyar preset, EkranDikdortgen region, EkranDikdortgen screen, int height, bool follow)
    {
        int width = s.KutuGenislik >= MinWidth ? s.KutuGenislik : MaxWidth(region.Width, screen.Width);
        var box = Preset(preset, screen, region, width, height);
        bool anchor = follow || preset is AltyaziOnAyar.BolgeninAlti or AltyaziOnAyar.BolgeninUstu;
        Remember(s, box, region, anchor);
        s.OnAyar = preset;
        s.BolgeyiTakipEt = anchor && !region.IsEmpty;
    }
}
