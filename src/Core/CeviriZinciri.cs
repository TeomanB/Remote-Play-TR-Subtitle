namespace AltyaziCevirici.Core;

/// <summary>
/// Google first. On 429, 403, or a captcha block, Google rests (2 minutes, doubling up to 30)
/// and MyMemory translates instead. A quota error rests MyMemory too.
/// </summary>
public sealed class CeviriZinciri : ILanguageBackend, IDisposable
{
    public static readonly TimeSpan GoogleBaslangic = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan GoogleTavan = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan YedekDinlenme = TimeSpan.FromMinutes(2);

    private readonly ILanguageBackend _google;
    private readonly ILanguageBackend _yedek;
    private readonly Func<DateTime> _clock;
    private TimeSpan _googleBekleme = TimeSpan.Zero;

    public CeviriZinciri(ILanguageBackend google, ILanguageBackend yedek, Func<DateTime>? clock = null)
    {
        _google = google;
        _yedek = yedek;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public DateTime GoogleAcilma { get; private set; } = DateTime.MinValue;
    public DateTime YedekAcilma { get; private set; } = DateTime.MinValue;
    public TimeSpan GoogleBekleme => _googleBekleme;

    public async Task<TranslateOutcome> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var now = _clock();
        if (now >= GoogleAcilma)
        {
            var g = await _google.TranslateAsync(text, source, target, ct).ConfigureAwait(false);
            if (g.Status == TranslateStatus.Ok)
            {
                _googleBekleme = TimeSpan.Zero;
                return g;
            }
            if (g.Status == TranslateStatus.Cancelled) return g;
            if (g.Status == TranslateStatus.RateLimited) DinlendirGoogle(now);
        }
        else TanilamaLog.Yaz("ceviri yok", "google dinleniyor");

        if (now >= YedekAcilma)
        {
            var y = await _yedek.TranslateAsync(text, source, target, ct).ConfigureAwait(false);
            if (y.Status == TranslateStatus.Ok) return y;
            if (y.Status == TranslateStatus.Cancelled) return y;
            if (y.Status == TranslateStatus.RateLimited)
            {
                YedekAcilma = now + YedekDinlenme;
                TanilamaLog.Yaz("ceviri yok", "mymemory kota");
            }
            else TanilamaLog.Yaz("ceviri yok", "mymemory " + y.Status);
            return new TranslateOutcome(TranslateStatus.Failed);
        }

        TanilamaLog.Yaz("ceviri yok", "yedek dinleniyor");
        return new TranslateOutcome(TranslateStatus.Failed);
    }

    private void DinlendirGoogle(DateTime now)
    {
        _googleBekleme = _googleBekleme == TimeSpan.Zero
            ? GoogleBaslangic
            : TimeSpan.FromTicks(Math.Min(_googleBekleme.Ticks * 2, GoogleTavan.Ticks));
        GoogleAcilma = now + _googleBekleme;
        TanilamaLog.Yaz("ceviri yok", "google " + (int)_googleBekleme.TotalMinutes + " dk");
    }

    public void Dispose()
    {
        (_google as IDisposable)?.Dispose();
        (_yedek as IDisposable)?.Dispose();
    }
}
