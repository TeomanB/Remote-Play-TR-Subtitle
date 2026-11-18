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
    private readonly ImageAttributes _clampAttr = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private Bitmap? _raw;
    private Graphics? _rawG;
    private Bitmap? _scaled;
    private Graphics? _scaledG;
    private byte[] _gray = Array.Empty<byte>();
    private Task<string>? _recognize;
    private DateTime _busySince;

    private sealed class Slot
    {
        public byte[] Cur = new byte[SampleW * SampleH];
        public byte[] Prev = new byte[SampleW * SampleH];
        public bool Has;
        public string Text = "";
        public bool Filter;
        public int Threshold;
    }

    /// <summary>True while a Windows OCR call has not returned. The capture loop skips new calls until it does.</summary>
    public bool InFlight => _recognize is { IsCompleted: false };
    public bool BusyTooLong(TimeSpan limit) => InFlight && DateTime.UtcNow - _busySince > limit;
    public Task WhenQuiet() => _recognize ?? Task.CompletedTask;

    public string LanguageTag { get; }
    public bool IsEnglish { get; }
    public bool Matches(string sourceLang) =>
        LanguageTag.StartsWith((string.IsNullOrWhiteSpace(sourceLang) ? "en" : sourceLang).Split('-')[0], StringComparison.OrdinalIgnoreCase);

    private OcrCapture(OcrEngine engine)
    {
        _engine = engine;
        _maxDim = (int)Math.Max(256, OcrEngine.MaxImageDimension);
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
        var all = await ReadFrameAsync(new[] { ("", region) }, whiteFilter, threshold).ConfigureAwait(false);
        return all.Length == 0 ? "" : all[0];
    }

    /// <summary>
    /// Copies the union of the areas once, then OCRs each. An area whose pixels did not change
    /// returns its previous text and does not call the engine.
    /// </summary>
    public async Task<string[]> ReadFrameAsync(IReadOnlyList<(string Slot, Rectangle Region)> areas, bool whiteFilter, int threshold, IReadOnlyList<EkranDikdortgen>? maskeler = null)
    {
        var texts = new string[areas.Count];
        if (InFlight)
        {
            for (int i = 0; i < areas.Count; i++) texts[i] = SlotFor(areas[i].Slot).Text;
            return texts;
        }
        var live = new List<int>();
        for (int i = 0; i < areas.Count; i++)
            if (areas[i].Region.Width >= 8 && areas[i].Region.Height >= 8) live.Add(i);
        if (live.Count == 0) return texts;

        var union = KareBirlesim.Birles(live.Select(i =>
        {
            var r = areas[i].Region;
            return new EkranDikdortgen(r.X, r.Y, r.Width, r.Height);
        }).ToArray());
        var frame = new Rectangle(union.X, union.Y, union.Width, union.Height);
        var ready = new List<(int Index, PixelRect Crop)>();
        try
        {
            EnsureRaw(frame.Size);
            _rawG!.CopyFromScreen(frame.Left, frame.Top, 0, 0, frame.Size, CopyPixelOperation.SourceCopy);
            Mask(frame, maskeler);
            Collect(areas, live, frame, whiteFilter, threshold, texts, ready);

            foreach (var (index, crop) in ready)
            {
                if (InFlight) { texts[index] = SlotFor(areas[index].Slot).Text; continue; }
                var region = areas[index].Region;
                int sx = region.X - frame.X + crop.X;
                int sy = region.Y - frame.Y + crop.Y;
                texts[index] = await Recognize(areas[index].Slot, new Rectangle(sx, sy, crop.Width, crop.Height), region.Size, whiteFilter, threshold).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "ocr-frame");
            _rawG?.Dispose();
            _raw?.Dispose();
            _raw = null;
            _rawG = null;
        }
        return texts;
    }

    private void Mask(Rectangle frame, IReadOnlyList<EkranDikdortgen>? maskeler)
    {
        if (maskeler == null || maskeler.Count == 0 || _raw == null) return;
        var data = _raw.LockBits(new Rectangle(Point.Empty, frame.Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var span = Writable(data);
            foreach (var box in maskeler)
            {
                var local = KatmanMaske.Yerel(box, frame.X, frame.Y, frame.Width, frame.Height);
                if (local.Width <= 0 || local.Height <= 0) continue;
                ImageOps.FillBlack(span, data.Stride, local.X, local.Y, local.Width, local.Height, frame.Width, frame.Height);
                TanilamaLog.Yaz("maske", local.X + "," + local.Y + " " + local.Width + "x" + local.Height);
            }
        }
        finally { _raw.UnlockBits(data); }
    }

    private Slot SlotFor(string slot)
    {
        if (!_slots.TryGetValue(slot, out var state)) _slots[slot] = state = new Slot();
        return state;
    }

    private void Collect(IReadOnlyList<(string Slot, Rectangle Region)> areas, List<int> live, Rectangle frame, bool whiteFilter, int threshold, string[] texts, List<(int Index, PixelRect Crop)> ready)
    {
        var data = _raw!.LockBits(new Rectangle(Point.Empty, frame.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var span = Span(data);
            foreach (int i in live)
            {
                var region = areas[i].Region;
                var local = new Rectangle(region.X - frame.X, region.Y - frame.Y, region.Width, region.Height);
                var state = SlotFor(areas[i].Slot);
                var prep = Inspect(state, span, data.Stride, local, whiteFilter, threshold, out var crop);
                if (prep == Prep.Unchanged) texts[i] = state.Text;
                else if (prep == Prep.Empty) { state.Text = ""; texts[i] = ""; }
                else ready.Add((i, crop));
            }
        }
        finally { _raw.UnlockBits(data); }
    }

    private static Prep Inspect(Slot state, ReadOnlySpan<byte> span, int stride, Rectangle local, bool whiteFilter, int threshold, out PixelRect crop)
    {
        crop = new PixelRect(0, 0, local.Width, local.Height);
        if (whiteFilter != state.Filter || threshold != state.Threshold) state.Has = false;
        state.Filter = whiteFilter;
        state.Threshold = threshold;
        ImageOps.SampleRect(span, stride, local.X, local.Y, local.Width, local.Height, state.Cur, SampleW, SampleH);
        if (state.Has && ImageOps.MeanAbsDiff(state.Cur, state.Prev) < UnchangedDiff) return Prep.Unchanged;
        (state.Cur, state.Prev) = (state.Prev, state.Cur);
        state.Has = true;
        if (whiteFilter && !ImageOps.TryFindTextBoxRect(span, stride, local.X, local.Y, local.Width, local.Height, threshold, out crop))
            return Prep.Empty;
        return Prep.Ready;
    }

    private async Task<string> Recognize(string slot, Rectangle src, Size region, bool whiteFilter, int threshold)
    {
        var state = SlotFor(slot);
        double scale = Math.Min(2.0, (double)_maxDim / Math.Max(src.Width, src.Height));
        int w = Math.Max(4, ((int)(src.Width * scale)) & ~3);
        int h = Math.Max(4, (int)(src.Height * scale));
        w = Math.Min(w, _maxDim & ~3);
        h = Math.Min(h, _maxDim);
        Raster(src, w, h, region, whiteFilter, threshold);

        var sbmp = new SoftwareBitmap(BitmapPixelFormat.Gray8, w, h);
        var handed = false;
        try
        {
            sbmp.CopyFromBuffer(_gray.AsBuffer(0, w * h));
            var call = _engine.RecognizeAsync(sbmp).AsTask();
            _busySince = DateTime.UtcNow;
            _recognize = call.ContinueWith(t =>
            {
                try
                {
                    if (t.Status != TaskStatus.RanToCompletion) return state.Text;
                    var parts = new List<string>(t.Result.Lines.Count);
                    foreach (var line in t.Result.Lines) parts.Add(line.Text);
                    state.Text = TextUtil.Normalize(string.Join(' ', parts));
                    return state.Text;
                }
                finally { sbmp.Dispose(); }
            }, TaskScheduler.Default);
            handed = true;
            var done = await Task.WhenAny(_recognize, Task.Delay(TimeSpan.FromSeconds(4))).ConfigureAwait(false);
            if (done != _recognize)
            {
                ErrorLog.Write(new TimeoutException("Windows OCR did not return"), "ocr-timeout");
                return state.Text;
            }
            return await _recognize.ConfigureAwait(false);
        }
        catch
        {
            if (!handed) sbmp.Dispose();
            throw;
        }
    }

    private void Raster(Rectangle src, int w, int h, Size region, bool whiteFilter, int threshold)
    {
        EnsureScaled(w, h, region);
        _scaledG!.DrawImage(_raw!, new Rectangle(0, 0, w, h), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, _clampAttr);
        if (_gray.Length < w * h) _gray = new byte[w * h];
        var sd = _scaled!.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = Span(sd);
            if (whiteFilter) ImageOps.ThresholdWhite(pixels, w, h, sd.Stride, _gray, threshold);
            else ImageOps.GrayStretch(pixels, w, h, sd.Stride, _gray);
        }
        finally { _scaled.UnlockBits(sd); }
    }

    private static unsafe ReadOnlySpan<byte> Span(BitmapData d) => new((void*)d.Scan0, d.Stride * d.Height);
    private static unsafe Span<byte> Writable(BitmapData d) => new((void*)d.Scan0, d.Stride * d.Height);

    private void EnsureRaw(Size size)
    {
        if (_raw != null && _raw.Size == size) return;
        _rawG?.Dispose();
        _raw?.Dispose();
        _raw = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        _rawG = Graphics.FromImage(_raw);
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
