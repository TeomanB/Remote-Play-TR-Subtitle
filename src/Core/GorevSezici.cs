namespace AltyaziCevirici.Core;

/// <summary>Short HUD imperatives ("Respray the Phoenix") are not spoken dialogue.</summary>
public static class GorevSezici
{
    private static readonly HashSet<string> Konusma = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "i'm", "im", "you", "you're", "we", "we're", "he", "she", "they", "me", "my", "your",
    };

    public static bool Benzir(string? english)
    {
        if (string.IsNullOrWhiteSpace(english)) return false;
        var t = english.Trim();
        if (t.Length is < 6 or > 48) return false;
        if (t.IndexOfAny(['?', '!', '"', ':', ',', '.']) >= 0) return false;
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 6) return false;
        if (!char.IsUpper(words[0][0])) return false;
        if (words.Any(w => Konusma.Contains(w.Trim('\'', '.')))) return false;
        return true;
    }
}
