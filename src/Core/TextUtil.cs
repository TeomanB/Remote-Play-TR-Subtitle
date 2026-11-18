using System.Text;

namespace AltyaziCevirici.Core;

/// <summary>Pure text helpers: OCR cleanup, comparison keys and fuzzy similarity.</summary>
public static class TextUtil
{
    /// <summary>Cleans raw OCR output into a display/translation-ready single line.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var sb = new StringBuilder(raw.Length);
        bool lastSpace = true;
        foreach (char c0 in raw)
        {
            char c = c0 switch
            {
                '\u2018' or '\u2019' or '`' or '´' => '\'',
                '\u201C' or '\u201D' => '"',
                '|' => 'I',
                '\u2014' or '\u2013' => '-',
                _ => c0
            };
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                if (!lastSpace) { sb.Append(' '); lastSpace = true; }
                continue;
            }
            sb.Append(c);
            lastSpace = false;
        }
        var s = sb.ToString().Trim();
        // Remove stray OCR punctuation garbage at both ends (keep sentence punctuation at end).
        s = s.TrimStart('-', '_', '~', '.', ',', ':', ';', '*', '=', '+', '^', '°', '•', ' ');
        s = TrimEndKeepCensor(s);
        // Fix spaces before punctuation produced by OCR: "Hey , man ." -> "Hey, man."
        s = s.Replace(" ,", ",").Replace(" .", ".").Replace(" ?", "?").Replace(" !", "!").Replace(" '", "'");
        return s;
    }

    /// <summary>Trims trailing OCR garbage but keeps asterisks of a censored word at the end ("Oh, f***").</summary>
    private static string TrimEndKeepCensor(string s)
    {
        int end = s.Length;
        while (end > 0)
        {
            char c = s[end - 1];
            if (c == '*')
            {
                int k = end - 1;
                while (k > 0 && s[k - 1] == '*') k--;
                if (k > 0 && char.IsLetter(s[k - 1])) break; // "f***"
                end--;
                continue;
            }
            if (c is '_' or '~' or '=' or '+' or '^' or '°' or '•' or '-' or ' ') { end--; continue; }
            break;
        }
        return s[..end];
    }

    /// <summary>Comparison key: lowercase letters/digits separated by single spaces.</summary>
    public static string Key(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        bool lastSpace = true;
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// True when the text plausibly is a subtitle and not OCR noise from a busy background.
    /// Requires real word-like tokens; rejects symbol/digit soup that would spam the translator.
    /// </summary>
    public static bool LooksLikeText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = Profanity.Uncensor(s); // "F***!" is a real line
        int letters = 0, total = 0;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            total++;
            if (char.IsLetter(c)) letters++;
        }
        if (letters < 2 || letters < total * 0.6) return false;

        int good = 0, bad = 0;
        foreach (var raw in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tok = raw.Trim('.', ',', '!', '?', '"', '\'', ':', ';', '(', ')', '-', '…');
            if (tok.Length == 0) continue;
            int l = 0, other = 0;
            foreach (char c in tok)
            {
                if (char.IsLetter(c)) l++;
                else if (c != '\'' && c != '-' && !(c == '*' && l > 0)) other++; // "f***ing" is a censored word
            }
            if (l >= 1 && other == 0) good++;
            else if (l == 0 && tok.All(char.IsDigit)) { /* numbers are neutral */ }
            else bad++;
        }
        if (good == 0) return false;
        // Very short readings ("lI", "rn") are usually noise; real short lines start with a capital ("No.", "OK").
        if (letters < 3 && !char.IsUpper(s.TrimStart()[0])) return false;
        return bad <= good / 2;
    }

    /// <summary>True when <paramref name="longer"/> is <paramref name="shorter"/> with text appended (a growing partial line), tolerant to OCR jitter.</summary>
    public static bool IsGrowth(string? shorter, string? longer, double threshold = 0.85)
    {
        var a = Key(shorter);
        var b = Key(longer);
        if (a.Length == 0 || b.Length <= a.Length) return false;
        if (b.StartsWith(a, StringComparison.Ordinal)) return true;
        if (a.Length < 4) return false;
        return SimilarityKeys(a, b[..a.Length]) >= threshold;
    }

    /// <summary>Same subtitle line (jitter, correction, growth or partial) vs. a different line.</summary>
    public static bool AreRelated(string? a, string? b, double threshold = 0.85) =>
        IsGrowth(a, b, threshold) || IsGrowth(b, a, threshold) || Similarity(a, b) >= threshold;

    /// <summary>Fuzzy similarity 0..1 based on Levenshtein distance of the comparison keys.</summary>
    public static double Similarity(string? a, string? b)
    {
        return SimilarityKeys(Key(a), Key(b));
    }

    private static double SimilarityKeys(string x, string y)
    {
        if (x.Length == 0 && y.Length == 0) return 1.0;
        if (x.Length == 0 || y.Length == 0) return 0.0;
        if (x == y) return 1.0;
        int dist = Levenshtein(x, y);
        return 1.0 - (double)dist / Math.Max(x.Length, y.Length);
    }

    public static int Levenshtein(string a, string b)
    {
        if (a.Length < b.Length) (a, b) = (b, a);
        Span<int> prev = b.Length + 1 <= 512 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
        Span<int> cur = b.Length + 1 <= 512 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            char ca = a[i - 1];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = ca == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            var t = prev; prev = cur; cur = t;
        }
        return prev[b.Length];
    }
}
