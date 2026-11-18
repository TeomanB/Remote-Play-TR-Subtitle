using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using AltyaziCevirici.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace AltyaziCevirici;

/// <summary>
/// Captures the region, skips unchanged frames, crops to the bright-text box, preprocesses and runs
/// Windows OCR. All bitmaps/buffers are reused. Must be called from one loop at a time (not re-entrant).
/// </summary>
internal sealed class OcrCapture : IDisposable
{
    private const int SampleW = 160, SampleH = 32;
    private const double UnchangedDiff = 0.8;

    private readonly OcrEngine _engine;
    private readonly int _maxDim;
    private readonly byte[] _sampleA = new byte[SampleW * SampleH];
    private readonly byte[] _sampleB = new byte[SampleW * SampleH];
    private readonly ImageAttributes _clampAttr = new();
    private byte[] _cur, _prev;
    private bool _hasPrev;
    private Bitmap? _raw;
    private Graphics? _rawG;
    private Bitmap? _scaled;
    private Graphics? _scaledG;
    private byte[] _gray = Array.Empty<byte>();
    private string _prevText = string.Empty;
    private bool _prevFilter;
    private int _prevThreshold;

    public string LanguageTag { get; }
    public bool IsEnglish { get; }
    public bool Matches(string sourceLang) =>
        LanguageTag.StartsWith((string.IsNullOrWhiteSpace(sourceLang) ? "en" : sourceLang).Split('-')[0], StringComparison.OrdinalIgnoreCase);

    private OcrCapture(OcrEngine engine)
    {
        _engine = engine;
        _maxDim = (int)Math.Max(256, OcrEngine.MaxImageDimension);
        _cur = _sampleA;
        _prev = _sampleB;
        _clampAttr.SetWrapMode(WrapMode.TileFlipXY); // no dark ghost border when scaling a crop
        LanguageTag = engine.RecognizerLanguage?.LanguageTag ?? "?";
        IsEnglish = LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates the engine for the subtitle language (default English); null if Windows has no OCR language at all.</summary>
    public static OcrCapture? Create(out bool englishMissing, string sourceLang = "en")
    {
        englishMissing = false;
        OcrEngine? engine = null;
        var lang2 = string.IsNullOrWhiteSpace(sourceLang) ? "en" : sourceLang.Split('-')[0].ToLowerInvariant();
        string[] tags = lang2 == "en" ? new[] { "en-US", "en-GB", "en" } : new[] { sourceLang, lang2 };
        foreach (var tag in tags)
        {
            try
            {
                var lang = new Language(tag);
                if (OcrEngine.IsLanguageSupported(lang)) engine = OcrEngine.TryCreateFromLanguage(lang);
            }
            catch (Exception ex) { ErrorLog.Write(ex, "ocr-create"); }
            if (engine != null) break;
        }
        try
        {
            if (engine == null)
            {
                var en = OcrEngine.AvailableRecognizerLanguages
                    .FirstOrDefault(l => l.LanguageTag.StartsWith(lang2, StringComparison.OrdinalIgnoreCase));
                if (en != null) engine = OcrEngine.TryCreateFromLanguage(en);
            }
            if (engine == null)
            {
                englishMissing = true;
                engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
        }
        catch (Exception ex) { ErrorLog.Write(ex, "ocr-create"); }
        return engine == null ? null : new OcrCapture(engine);
    }

    private enum Prep { Unchanged, Empty, Ready }

    /// <summary>One frame. Returns normalized text ("" if no subtitle).</summary>
    public async Task<string> ReadAsync(Rectangle region, bool whiteFilter, int threshold)
    {
        var prep = Prepare(region, whiteFilter, threshold, out int w, out int h);
        if (prep == Prep.Unchanged) return _prevText;
        if (prep == Prep.Empty) { _prevText = string.Empty; return _prevText; }

        using var sbmp = new SoftwareBitmap(BitmapPixelFormat.Gray8, w, h);
        sbmp.CopyFromBuffer(_gray.AsBuffer(0, w * h));
        var result = await _engine.RecognizeAsync(sbmp);
        var parts = new List<string>(result.Lines.Count);
        foreach (var l in result.Lines) parts.Add(l.Text);
        _prevText = TextUtil.Normalize(string.Join(' ', parts));
        return _prevText;
    }

    /// <summary>Synchronous part: capture, unchanged-frame skip, crop, scale, Gray8 into <see cref="_gray"/>.</summary>
    private Prep Prepare(Rectangle region, bool whiteFilter, int threshold, out int w, out int h)
    {
        w = h = 0;
        EnsureRaw(region.Size);
        _rawG!.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);

        if (whiteFilter != _prevFilter || threshold != _prevThreshold) _hasPrev = false;
        _prevFilter = whiteFilter;
        _prevThreshold = threshold;

        PixelRect crop = new(0, 0, region.Width, region.Height);
        var data = _raw!.LockBits(new Rectangle(Point.Empty, region.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var span = Span(data);
            ImageOps.Sample(span, data.Width, data.Height, data.Stride, _cur, SampleW, SampleH);
            if (_hasPrev && ImageOps.MeanAbsDiff(_cur, _prev) < UnchangedDiff) return Prep.Unchanged; // no OCR
            (_cur, _prev) = (_prev, _cur);
            _hasPrev = true;

            if (whiteFilter && !ImageOps.TryFindTextBox(span, data.Width, data.Height, data.Stride, threshold, out crop))
                return Prep.Empty; // no bright text at all: skip OCR
        }
        finally { _raw.UnlockBits(data); }

        double scale = Math.Min(2.0, (double)_maxDim / Math.Max(crop.Width, crop.Height));
        w = Math.Max(4, ((int)(crop.Width * scale)) & ~3); // multiple of 4: Gray8 stride == width
        h = Math.Max(4, (int)(crop.Height * scale));
        w = Math.Min(w, _maxDim & ~3);
        h = Math.Min(h, _maxDim);
        EnsureScaled(w, h, region.Size);
        _scaledG!.DrawImage(_raw, new Rectangle(0, 0, w, h), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, _clampAttr);

        if (_gray.Length < w * h) _gray = new byte[w * h];
        var sd = _scaled!.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var span = Span(sd);
            if (whiteFilter) ImageOps.ThresholdWhite(span, w, h, sd.Stride, _gray, threshold);
            else ImageOps.GrayStretch(span, w, h, sd.Stride, _gray);
        }
        finally { _scaled.UnlockBits(sd); }
        return Prep.Ready;
    }

    private static unsafe ReadOnlySpan<byte> Span(BitmapData d) => new((void*)d.Scan0, d.Stride * d.Height);

    private void EnsureRaw(Size size)
    {
        if (_raw != null && _raw.Size == size) return;
        _rawG?.Dispose();
        _raw?.Dispose();
        _raw = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        _rawG = Graphics.FromImage(_raw);
        _hasPrev = false;
    }

    private void EnsureScaled(int w, int h, Size region)
    {
        if (_scaled != null && _scaled.Width >= w && _scaled.Height >= h) return;
        _scaledG?.Dispose();
        _scaled?.Dispose();
        int bw = Math.Max(w, Math.Min(region.Width * 2, _maxDim));
        int bh = Math.Max(h, Math.Min(region.Height * 2, _maxDim));
        _scaled = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
        _scaledG = Graphics.FromImage(_scaled);
        _scaledG.InterpolationMode = InterpolationMode.HighQualityBicubic;
        _scaledG.PixelOffsetMode = PixelOffsetMode.HighQuality;
        _scaledG.CompositingMode = CompositingMode.SourceCopy;
    }

    public void Dispose()
    {
        _rawG?.Dispose();
        _raw?.Dispose();
        _scaledG?.Dispose();
        _scaled?.Dispose();
        _clampAttr.Dispose();
    }
}
