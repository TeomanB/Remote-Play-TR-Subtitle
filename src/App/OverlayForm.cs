using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using AltyaziCevirici.Core;

namespace AltyaziCevirici;

/// <summary>Click-through, topmost, per-pixel-alpha overlay that shows the Turkish line.</summary>
internal sealed class OverlayForm : Form
{
    private Rectangle _region;
    private OverlayPlacement _placement;
    private float _fontSize;
    private bool _shown;
    private Font? _font;
    private float _fontFor;
    private readonly StringFormat _fmt;
    private string? _lastText;
    private bool _lastFallback;
    private Rectangle _lastRegion;
    private Color _textColor = Color.White;
    private int _bgAlpha = 165;
    private int _offsetY;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Altyazı Çevirici Katmanı";
        _fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.Word,
        };
        _fmt.FormatFlags &= ~StringFormatFlags.NoWrap;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                          | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Keep the overlay out of screen captures so we never OCR our own text (Win10 2004+).
        try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); }
        catch (Exception ex) { Core.ErrorLog.Write(ex, "affinity"); }
    }

    public void Configure(Rectangle region, OverlayPlacement placement, float fontSize)
    {
        _region = region;
        _placement = placement;
        _fontSize = fontSize;
        _lastText = null; // force redraw with new layout
    }

    /// <summary>Applies all appearance settings; redraws the visible text right away.</summary>
    public void Configure(Rectangle region, AppSettings s)
    {
        Configure(region, s.OverlayKonumu, s.YaziBoyutu);
        _textColor = AppSettings.TryParseColor(s.YaziRengi, out var c) ? Color.FromArgb(255, c.R, c.G, c.B) : Color.White;
        _bgAlpha = (int)Math.Round(Math.Clamp(s.ArkaPlanOpakligi, 0, 100) * 2.55);
        _offsetY = s.DikeyKaydirma;
    }

    /// <summary>Re-renders the current text with the new layout (live preview from the settings window).</summary>
    public void Refresh(string? text, bool isFallback)
    {
        if (_shown && text != null) ShowText(text, isFallback);
    }

    public string? CurrentText => _shown ? _lastText : null;
    public bool CurrentIsFallback => _lastFallback;

    public void ShowText(string text, bool isFallback = false)
    {
        if (!IsHandleCreated) CreateHandle();
        if (_region.Width <= 0) return;
        if (_shown && text == _lastText && isFallback == _lastFallback && _region == _lastRegion) return; // nothing changed
        _lastText = text;
        _lastFallback = isFallback;
        _lastRegion = _region;
        if (_font == null || _fontFor != _fontSize)
        {
            _font?.Dispose();
            _font = new Font("Segoe UI", _fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            _fontFor = _fontSize;
        }
        var font = _font;
        var fmt = _fmt;

        var screen = Screen.FromRectangle(_region).Bounds;
        int width = Math.Clamp(Math.Max(_region.Width, 500), 200, screen.Width);
        const int padX = 18, padY = 10;


        SizeF measured;
        using (var tmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(tmp))
            measured = g.MeasureString(text, font, width - 2 * padX, fmt);
        int height = (int)Math.Ceiling(measured.Height) + 2 * padY + 6;
        int boxWidth = Math.Min(width, (int)Math.Ceiling(measured.Width) + 2 * padX + 12);

        int x = _region.Left + (_region.Width - boxWidth) / 2;
        int y = _placement switch
        {
            OverlayPlacement.Alt => _region.Bottom + 6,
            OverlayPlacement.Uzerinde => _region.Top + (_region.Height - height) / 2,
            _ => _region.Top - height - 6,
        } + _offsetY;
        // Keep on screen: flip side if there is no room.
        if (y < screen.Top) y = _offsetY == 0 ? _region.Bottom + 6 : screen.Top;
        if (y + height > screen.Bottom) y = _offsetY == 0 ? Math.Max(screen.Top, _region.Top - height - 6) : screen.Bottom - height;
        x = Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - boxWidth));

        using var bmp = new Bitmap(boxWidth, height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            if (_bgAlpha > 0)
            {
                using var bg = RoundedRect(new Rectangle(0, 0, boxWidth - 1, height - 1), 10);
                using var brush = new SolidBrush(Color.FromArgb(isFallback ? _bgAlpha * 3 / 4 : _bgAlpha, 10, 10, 14));
                g.FillPath(brush, bg);
            }

            using var path = new GraphicsPath();
            var layout = new RectangleF(padX, padY + 2, boxWidth - 2 * padX, height - 2 * padY);
            float emSize = g.DpiY * font.SizeInPoints / 72f;
            path.AddString(text, font.FontFamily, (int)font.Style, emSize, layout, fmt);
            using (var outline = new Pen(Color.FromArgb(255, 0, 0, 0), Math.Max(3f, _fontSize / 7f)) { LineJoin = LineJoin.Round })
                g.DrawPath(outline, path);
            using (var fill = new SolidBrush(isFallback ? Color.FromArgb(255, 200, 200, 200) : _textColor))
                g.FillPath(fill, path);
        }
        ApplyBitmap(bmp, x, y);
        if (!_shown)
        {
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            _shown = true;
        }
        else
        {
            // Re-assert topmost in case the game/Remote Play window grabbed it.
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    public void HideText()
    {
        _lastText = null;
        if (!_shown || !IsHandleCreated) return;
        Native.ShowWindow(Handle, Native.SW_HIDE);
        _shown = false;
    }

    private void ApplyBitmap(Bitmap bmp, int x, int y)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = Native.SelectObject(memDc, hBmp);
            var size = new Native.SIZE(bmp.Width, bmp.Height);
            var src = new Native.POINT(0, 0);
            var dst = new Native.POINT(x, y);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            _ = Native.ReleaseDC(IntPtr.Zero, screenDc);
            if (hBmp != IntPtr.Zero)
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBmp);
            }
            Native.DeleteDC(memDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font?.Dispose();
            _fmt.Dispose();
        }
        base.Dispose(disposing);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
