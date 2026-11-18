namespace AltyaziCevirici.Core;

public enum BoxPart { None, Body, Nw, N, Ne, E, Se, S, Sw, W }

/// <summary>
/// Moves and resizes a screen rectangle from all 8 handles. This is the math the preview uses,
/// so a handle drag cannot silently no-op the way the 1.4 WinForms canvas did.
/// </summary>
public static class BoxEdit
{
    public const int MinSize = 32;

    public static BoxPart Hit(EkranDikdortgen box, int px, int py, int handle)
    {
        if (box.Width < 4 || box.Height < 4) return BoxPart.None;
        int h = Math.Max(6, handle);
        bool nearL = Math.Abs(px - box.X) <= h;
        bool nearR = Math.Abs(px - box.Right) <= h;
        bool nearT = Math.Abs(py - box.Y) <= h;
        bool nearB = Math.Abs(py - box.Bottom) <= h;
        bool inX = px >= box.X - h && px <= box.Right + h;
        bool inY = py >= box.Y - h && py <= box.Bottom + h;
        if (nearL && nearT && inX && inY) return BoxPart.Nw;
        if (nearR && nearT && inX && inY) return BoxPart.Ne;
        if (nearR && nearB && inX && inY) return BoxPart.Se;
        if (nearL && nearB && inX && inY) return BoxPart.Sw;
        if (nearT && inX) return BoxPart.N;
        if (nearB && inX) return BoxPart.S;
        if (nearL && inY) return BoxPart.W;
        if (nearR && inY) return BoxPart.E;
        if (px >= box.X && px <= box.Right && py >= box.Y && py <= box.Bottom) return BoxPart.Body;
        return BoxPart.None;
    }

    public static EkranDikdortgen Drag(EkranDikdortgen start, BoxPart part, int dx, int dy, int minSize = MinSize)
    {
        int x = start.X, y = start.Y, w = start.Width, h = start.Height;
        switch (part)
        {
            case BoxPart.Body:
                x += dx; y += dy;
                break;
            case BoxPart.Nw:
                x += dx; y += dy; w -= dx; h -= dy;
                break;
            case BoxPart.N:
                y += dy; h -= dy;
                break;
            case BoxPart.Ne:
                y += dy; w += dx; h -= dy;
                break;
            case BoxPart.E:
                w += dx;
                break;
            case BoxPart.Se:
                w += dx; h += dy;
                break;
            case BoxPart.S:
                h += dy;
                break;
            case BoxPart.Sw:
                x += dx; w -= dx; h += dy;
                break;
            case BoxPart.W:
                x += dx; w -= dx;
                break;
        }
        if (w < minSize) { if (part is BoxPart.Nw or BoxPart.W or BoxPart.Sw) x -= minSize - w; w = minSize; }
        if (h < minSize) { if (part is BoxPart.Nw or BoxPart.N or BoxPart.Ne) y -= minSize - h; h = minSize; }
        return new EkranDikdortgen(x, y, w, h);
    }

    /// <summary>Optional snap of the box edges to the screen edges and center. Off by default in the UI.</summary>
    public static EkranDikdortgen Snap(EkranDikdortgen box, EkranDikdortgen screen, int snapPx, bool enabled)
    {
        if (!enabled || snapPx <= 0) return box;
        int x = box.X, y = box.Y;
        int cx = screen.X + (screen.Width - box.Width) / 2;
        int cy = screen.Y + (screen.Height - box.Height) / 2;
        if (Math.Abs(x - screen.X) <= snapPx) x = screen.X;
        else if (Math.Abs(box.Right - screen.Right) <= snapPx) x = screen.Right - box.Width;
        else if (Math.Abs(x - cx) <= snapPx) x = cx;
        if (Math.Abs(y - screen.Y) <= snapPx) y = screen.Y;
        else if (Math.Abs(box.Bottom - screen.Bottom) <= snapPx) y = screen.Bottom - box.Height;
        else if (Math.Abs(y - cy) <= snapPx) y = cy;
        return new EkranDikdortgen(x, y, box.Width, box.Height);
    }
}
