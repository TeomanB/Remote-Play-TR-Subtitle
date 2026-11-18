namespace AltyaziCevirici.Core;

/// <summary>Screen-space intersection of an overlay box with a captured frame, including negative monitor origins.</summary>
public static class KatmanMaske
{
    public static EkranDikdortgen Kesisim(EkranDikdortgen a, EkranDikdortgen b)
    {
        int left = Math.Max(a.X, b.X);
        int top = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.X + a.Width, b.X + b.Width);
        int bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        if (right <= left || bottom <= top) return default;
        return new EkranDikdortgen(left, top, right - left, bottom - top);
    }

    /// <summary>Pixels inside a frame bitmap whose top-left is <paramref name="frameX"/>, <paramref name="frameY"/>.</summary>
    public static EkranDikdortgen Yerel(EkranDikdortgen overlay, int frameX, int frameY, int frameW, int frameH)
    {
        var hit = Kesisim(overlay, new EkranDikdortgen(frameX, frameY, frameW, frameH));
        if (hit.Width <= 0 || hit.Height <= 0) return default;
        return new EkranDikdortgen(hit.X - frameX, hit.Y - frameY, hit.Width, hit.Height);
    }
}
