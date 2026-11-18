namespace AltyaziCevirici.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Allocation-free pixel helpers on 32bpp BGRA buffers (pure, testable on any OS).</summary>
public static class ImageOps
{
    /// <summary>Downsamples luminance on a sx×sy grid into <paramref name="dst"/> (length ≥ sx*sy).</summary>
    public static void Sample(ReadOnlySpan<byte> bgra, int width, int height, int stride, Span<byte> dst, int sx, int sy)
    {
        for (int j = 0; j < sy; j++)
        {
            int y = Math.Min(height - 1, (int)((j + 0.5) * height / sy));
            int rowOff = y * stride;
            for (int i = 0; i < sx; i++)
            {
                int x = Math.Min(width - 1, (int)((i + 0.5) * width / sx));
                int p = rowOff + x * 4;
                dst[j * sx + i] = (byte)((bgra[p + 2] * 77 + bgra[p + 1] * 150 + bgra[p] * 29) >> 8);
            }
        }
    }

    public static double MeanAbsDiff(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length || a.Length == 0) return double.MaxValue;
        long diff = 0;
        for (int i = 0; i < a.Length; i++) diff += Math.Abs(a[i] - b[i]);
        return diff / (double)a.Length;
    }

    /// <summary>
    /// Finds the bounding box of bright, low-saturation (white) pixels — subtitle text — with padding.
    /// Returns false when there are too few bright pixels (no subtitle → OCR can be skipped).
    /// </summary>
    public static bool TryFindTextBox(ReadOnlySpan<byte> bgra, int width, int height, int stride, int threshold,
        out PixelRect box, int pad = 8, int minPixels = 12)
    {
        box = default;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
        for (int y = 0; y < height; y++)
        {
            int rowOff = y * stride;
            int rowCount = 0;
            for (int x = 0; x < width; x++)
            {
                int p = rowOff + x * 4;
                byte b = bgra[p], g = bgra[p + 1], r = bgra[p + 2];
                if (b >= threshold && g >= threshold && r >= threshold)
                {
                    rowCount++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
            }
            if (rowCount > 0)
            {
                count += rowCount;
                if (y < minY) minY = y;
                maxY = y;
            }
        }
        if (count < minPixels || maxX < 0) return false;
        int x0 = Math.Max(0, minX - pad), y0 = Math.Max(0, minY - pad);
        int x1 = Math.Min(width - 1, maxX + pad), y1 = Math.Min(height - 1, maxY + pad);
        box = new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        return true;
    }

    /// <summary>White-text threshold → Gray8 (text black on white), which Windows OCR reads best.</summary>
    public static void ThresholdWhite(ReadOnlySpan<byte> bgra, int width, int height, int stride, Span<byte> gray, int threshold)
    {
        for (int y = 0; y < height; y++)
        {
            int rowOff = y * stride, o = y * width;
            for (int x = 0; x < width; x++)
            {
                int p = rowOff + x * 4;
                int mn = Math.Min(bgra[p + 2], Math.Min(bgra[p + 1], bgra[p]));
                gray[o + x] = mn >= threshold ? (byte)0 : (byte)255;
            }
        }
    }

    /// <summary>Luminance + contrast stretch → Gray8.</summary>
    public static void GrayStretch(ReadOnlySpan<byte> bgra, int width, int height, int stride, Span<byte> gray)
    {
        int min = 255, max = 0, n = width * height;
        for (int y = 0; y < height; y++)
        {
            int rowOff = y * stride, o = y * width;
            for (int x = 0; x < width; x++)
            {
                int p = rowOff + x * 4;
                int v = (bgra[p + 2] * 77 + bgra[p + 1] * 150 + bgra[p] * 29) >> 8;
                if (v < min) min = v;
                if (v > max) max = v;
                gray[o + x] = (byte)v;
            }
        }
        int range = max - min;
        if (range <= 10 || (min == 0 && max == 255)) return;
        for (int i = 0; i < n; i++) gray[i] = (byte)((gray[i] - min) * 255 / range);
    }
}
