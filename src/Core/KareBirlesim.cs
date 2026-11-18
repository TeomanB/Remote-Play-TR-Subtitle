namespace AltyaziCevirici.Core;

/// <summary>One screen rectangle that covers every capture area, so a frame is copied once.</summary>
public static class KareBirlesim
{
    public static EkranDikdortgen Birles(IReadOnlyList<EkranDikdortgen> alanlar)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var a in alanlar)
        {
            if (a.Width <= 0 || a.Height <= 0) continue;
            left = Math.Min(left, a.X);
            top = Math.Min(top, a.Y);
            right = Math.Max(right, a.X + a.Width);
            bottom = Math.Max(bottom, a.Y + a.Height);
        }
        if (right <= left || bottom <= top) return default;
        return new EkranDikdortgen(left, top, right - left, bottom - top);
    }
}
