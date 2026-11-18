namespace AltyaziCevirici.Core;

/// <summary>Drops OCR that is already Turkish, or too broken to be an English subtitle.</summary>
public static class OcrGate
{
    private static readonly string[] TurkishWords =
    {
        "ve", "bir", "bu", "su", "şu", "icin", "için", "ile", "ama", "cok", "çok", "gibi", "degil", "değil",
        "var", "yok", "sonra", "olan", "diye", "kadar", "daha", "gore", "göre", "icin", "mi", "mı", "mu", "mü",
        "ceviri", "çeviri", "hazir", "hazır", "kurulum", "surum", "sürüm", "pencere", "altyazi", "altyazı",
    };

    public static bool Kabul(string? text, string? hedefDil = "tr")
    {
        if (!TextUtil.LooksLikeText(text)) return false;
        var s = text!.Trim();
        if (Cop(s) || MuhtemelenTurkce(s)) return false;
        if (hedefDil != null && hedefDil.StartsWith("tr", StringComparison.OrdinalIgnoreCase) && s.Contains("Son çeviri", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public static bool Cop(string s)
    {
        int weird = 0, n = 0;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            n++;
            if (char.IsLetterOrDigit(c)) continue;
            if (c is '.' or ',' or '!' or '?' or '\'' or '"' or '-' or ':' or ';') continue;
            weird++;
        }
        if (n == 0) return true;
        return weird * 5 > n; // more than 20% junk symbols
    }

    public static bool MuhtemelenTurkce(string s)
    {
        foreach (char c in s)
        {
            if (c is 'ğ' or 'Ğ' or 'ş' or 'Ş' or 'ö' or 'Ö' or 'ü' or 'Ü' or 'ç' or 'Ç' or 'ı' or 'İ')
                return true;
        }
        int hits = 0, words = 0;
        foreach (var raw in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = raw.Trim('.', ',', '!', '?', '"', '\'', ':', ';', '(', ')').ToLowerInvariant();
            if (w.Length == 0) continue;
            words++;
            if (TurkishWords.Contains(w)) hits++;
        }
        return hits >= 2 || (hits >= 1 && words <= 5);
    }
}
