using System.Globalization;
using System.Text;

namespace AltyaziCevirici.Core;

/// <summary>
/// Re-inflects Turkish case suffixes. Placeholders are pseudo-names ending in a voiced consonant with last
/// vowel 'a' (e.g. "Zelvar"), so the translator writes suffixes for that shape ("'a", "'ın", "'da", "'la").
/// This rewrites such a suffix for the real word: buffer letters, d/t hardening and vowel harmony.
/// </summary>
public static class TurkishSuffix
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private const string Vowels = "aeıioöuüâîûAEIİOÖUÜÂÎÛ";
    private const string Voiceless = "çfhkpsştÇFHKPSŞT";

    public static bool IsVowel(char c) => Vowels.Contains(c, StringComparison.Ordinal);

    /// <summary>Last vowel used for harmony (English spelling approximated; a final 'y' reads as 'i').</summary>
    public static char LastVowel(string word)
    {
        var w = word.TrimEnd();
        if (w.Length > 0 && (w[^1] == 'y' || w[^1] == 'Y') && w.Length > 1 && !IsVowel(w[^2])) return 'i';
        for (int i = w.Length - 1; i >= 0; i--)
        {
            char c = char.ToLower(w[i], Tr);
            if (c == 'y' && i > 0 && !IsVowel(w[i - 1]) && (i == w.Length - 1 || !IsVowel(w[i + 1]))) return 'i'; // "Brooklyn"
            if (c == 'â') return 'a';
            if (c == 'î') return 'i';
            if (c == 'û') return 'u';
            if ("aeıioöuü".Contains(c, StringComparison.Ordinal)) return c;
        }
        return 'a';
    }

    /// <summary>True when the word (as spelled) ends in a vowel sound.</summary>
    public static bool EndsWithVowel(string word)
    {
        var w = word.TrimEnd();
        if (w.Length == 0) return false;
        char c = w[^1];
        if (IsVowel(c)) return true;
        return (c == 'y' || c == 'Y') && w.Length > 1 && !IsVowel(w[^2]);
    }

    private static bool EndsVoiceless(string word)
    {
        var w = word.TrimEnd();
        return w.Length > 0 && Voiceless.Contains(w[^1], StringComparison.Ordinal);
    }

    private static bool Front(char v) => "eiöü".Contains(v, StringComparison.Ordinal);
    private static bool Round(char v) => "oöuü".Contains(v, StringComparison.Ordinal);

    /// <summary>
    /// Rewrites <paramref name="suffix"/> (written for a consonant-final, back-'a' stem) for <paramref name="word"/>.
    /// Suffix letters only, no apostrophe. Unknown shapes are harmonized vowel by vowel.
    /// </summary>
    public static string Inflect(string word, string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return string.Empty;
        var s = suffix.ToLower(Tr);
        bool vowelEnd = EndsWithVowel(word);
        if (vowelEnd && s.Length > 0 && IsVowel(s[0]))
        {
            // Buffer letter: genitive -ın → -nın, possessive is rare for names; everything else takes y.
            bool genitive = s.StartsWith("ın", StringComparison.Ordinal) && (s.Length == 2 || !IsVowel(s[2]));
            s = (genitive ? "n" : "y") + s;
        }
        else if (vowelEnd && (s == "la" || s == "le" || s.StartsWith("lay", StringComparison.Ordinal) || s.StartsWith("ley", StringComparison.Ordinal)))
        {
            s = "y" + s; // instrumental: Lucia'yla
        }
        var sb = new StringBuilder(s.Length);
        char prev = LastVowel(word);
        bool first = true;
        bool hard = EndsVoiceless(word);
        foreach (char c in s)
        {
            char o = c;
            if (first && (c == 'd' || c == 't')) o = hard ? 't' : 'd';
            else if (first && (c == 'c' || c == 'ç')) o = hard ? 'ç' : 'c';
            if (c == 'i' && sb.Length >= 3 && sb[^1] == 'k' && (sb[^2] == 'a' || sb[^2] == 'e') && (sb[^3] == 'd' || sb[^3] == 't'))
            {
                o = 'i'; prev = 'i'; // "-ki" never harmonizes: Kalaga'dakiler
            }
            else if (c == 'a' || c == 'e') { o = Front(prev) ? 'e' : 'a'; prev = o; }
            else if (c == 'ı' || c == 'i' || c == 'u' || c == 'ü')
            {
                bool f = Front(prev), r = Round(prev);
                o = f ? (r ? 'ü' : 'i') : (r ? 'u' : 'ı');
                prev = o;
            }
            else if (IsVowel(c)) prev = c;
            sb.Append(o);
            first = false;
        }
        return sb.ToString();
    }
}
