namespace AltyaziCevirici.Core;

/// <summary>Drops OCR that is our own Turkish line, still on screen or shown in the last few seconds.</summary>
public sealed class YankiKalkan
{
    private readonly TimeSpan _hatirla;
    private readonly List<(string Key, DateTime At)> _satirlar = new();
    private readonly object _lock = new();

    public YankiKalkan(TimeSpan? hatirla = null) => _hatirla = hatirla ?? TimeSpan.FromSeconds(5);

    public void Not(string? turkish, DateTime now)
    {
        if (!TextUtil.LooksLikeText(turkish)) return;
        var key = TextUtil.Key(turkish);
        if (key.Length == 0) return;
        lock (_lock)
        {
            Sil(now);
            _satirlar.Add((key, now));
        }
    }

    public bool Yanki(string? ocr, DateTime now)
    {
        if (!TextUtil.LooksLikeText(ocr)) return false;
        lock (_lock)
        {
            Sil(now);
            foreach (var (key, _) in _satirlar)
                if (TextUtil.Similarity(ocr, key) >= 0.85) return true;
        }
        return false;
    }

    private void Sil(DateTime now) => _satirlar.RemoveAll(s => now - s.At > _hatirla);
}
