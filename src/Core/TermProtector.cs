using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AltyaziCevirici.Core;

/// <summary>One user glossary row: English term → Turkish output (empty = keep the English term as is).</summary>
public sealed class SozlukGirdisi
{
    public string Ingilizce { get; set; } = string.Empty;
    public string Turkce { get; set; } = string.Empty;
    /// <summary>When true, only the exact capitalisation matches. Lowercase stays a normal word.</summary>
    public bool CaseSensitive { get; set; }

    public SozlukGirdisi() { }
    public SozlukGirdisi(string en, string tr = "") { Ingilizce = en; Turkce = tr; }
}

/// <summary>
/// Protects glossary terms and proper nouns from the translator: each term is swapped for a pseudo-name
/// placeholder ("Zelvar") that the translator keeps and inflects ("Zelvar'da"); afterwards the placeholder is
/// replaced by the wanted output with the Turkish suffix re-inflected for it ("Saint Denis'te").
/// </summary>
public sealed class TermProtector
{
    /// <summary>Pseudo-names: consonant-final, last vowel 'a', no 'i' (avoids I/İ casing issues), not Turkish words.</summary>
    public static readonly string[] Tokens =
        { "Zelvar", "Dumkar", "Velzar", "Morkar", "Tevrar", "Kuzbar", "Gorvar", "Pelzar", "Zorkar", "Bervar", "Durzar", "Galvar" };

    private static readonly Regex[] TokenRx = Tokens
        .Select(t => new Regex($@"(?<![\p{{L}}])(?i:{t})(?:\s?['’]\s?(?<s>\p{{Ll}}+)|(?<s>\p{{Ll}}+))?", RegexOptions.CultureInvariant | RegexOptions.Compiled))
        .ToArray();

    public sealed record Term(string Token, string Output, bool IsName);

    public sealed record Masked(string Text, IReadOnlyList<Term> Terms)
    {
        public bool StartsWithTerm => Terms.Count > 0 && Text.TrimStart().StartsWith(Terms[0].Token, StringComparison.Ordinal);
    }

    private readonly List<(Regex Rx, string Exact, string Output, bool CaseSensitive, int Words)> _glossary = new();
    private readonly bool _properNouns;

    public TermProtector(IEnumerable<SozlukGirdisi>? glossary, bool properNouns)
    {
        _properNouns = properNouns;
        if (glossary == null) return;
        foreach (var g in glossary.Where(g => !string.IsNullOrWhiteSpace(g.Ingilizce))
                     .OrderByDescending(g => g.Ingilizce.Trim().Length))
        {
            var en = g.Ingilizce.Trim();
            var tr = (g.Turkce ?? string.Empty).Trim();
            var output = tr.Length == 0 ? en : tr;
            AddPattern(Fold(en).Text, output, g.CaseSensitive);
            // OCR often reads the dollar sign in "PTT Youngin$" as S.
            if (en.Contains('$')) AddPattern(Fold(en.Replace("$", "S", StringComparison.Ordinal)).Text, output, g.CaseSensitive);
        }
    }

    private void AddPattern(string foldedTerm, string output, bool caseSensitive)
    {
        var parts = foldedTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pattern = string.Join(@"\s+", parts.Select(Regex.Escape));
        var rx = new Regex($@"(?<![\p{{L}}\p{{N}}]){pattern}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _glossary.Add((rx, foldedTerm, output, caseSensitive, parts.Length));
    }

    /// <summary>Strips accents so "Andrés" and "Andres" compare equal. Index map points back at the original string.</summary>
    private static (string Text, int[] Map) Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            foreach (var c in text[i].ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c);
                map.Add(i);
            }
        }
        return (sb.ToString(), map.ToArray());
    }

    public bool IsEmpty => _glossary.Count == 0 && !_properNouns;

    public Masked Protect(string text, bool startsMidSentence = false)
    {
        var spans = new List<(int Start, int Len, string Output, bool AmbiguousSingle)>();
        var (folded, map) = Fold(text);
        bool allCaps = IsAllCaps(text);
        foreach (var (rx, exact, output, caseSensitive, words) in _glossary)
            foreach (Match m in rx.Matches(folded))
            {
                if (m.Length == 0 || m.Index >= map.Length) continue;
                int start = map[m.Index];
                int end = map[m.Index + m.Length - 1] + 1;
                if (caseSensitive)
                {
                    // Ambiguous single words ("Cheetah", "Keys") only when capitalised exactly.
                    // An all-caps line has no casing, so those singles are left for translation.
                    if (allCaps && words == 1) continue;
                    var slice = Fold(text[start..end]).Text;
                    if (!allCaps && !slice.Equals(exact, StringComparison.Ordinal)) continue;
                }
                if (!Overlaps(spans, start, end - start))
                    spans.Add((start, end - start, output, caseSensitive && words == 1));
            }
        if (_properNouns)
            foreach (var (s, l) in ProperNouns(text, startsMidSentence))
                if (!Overlaps(spans, s, l)) spans.Add((s, l, text.Substring(s, l), false));
        // A capitalised ambiguous word at the start of a sentence is protected only when a name follows it.
        spans.RemoveAll(s => s.AmbiguousSingle
            && AtSentenceStart(text, s.Start, startsMidSentence)
            && !spans.Any(other => other.Start == NextWord(text, s.Start + s.Len)));

        if (spans.Count == 0) return new Masked(text, Array.Empty<Term>());
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var sb = new StringBuilder();
        var terms = new List<Term>();
        int pos = 0;
        foreach (var (start, len, output, _) in spans)
        {
            if (terms.Count >= Tokens.Length) break;
            var tok = Tokens[terms.Count];
            sb.Append(text, pos, start - pos).Append(tok);
            pos = start + len;
            terms.Add(new Term(tok, output, output.Length > 0 && !char.IsLower(output[0])));
        }
        sb.Append(text, pos, text.Length - pos);
        return new Masked(sb.ToString(), terms);
    }

    private static bool Overlaps(List<(int Start, int Len, string Output, bool AmbiguousSingle)> spans, int s, int l) =>
        spans.Any(x => s < x.Start + x.Len && x.Start < s + l);

    private static int NextWord(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static bool AtSentenceStart(string text, int index, bool startsMidSentence)
    {
        int k = index - 1;
        while (k >= 0 && (char.IsWhiteSpace(text[k]) || text[k] is '"' or '\'' or '(' or '“' or '«')) k--;
        if (k < 0) return !startsMidSentence;
        return text[k] is '.' or '!' or '?' or ':' or ';' or '…' or '—' or '-';
    }

    private static bool IsAllCaps(string text)
    {
        int letters = 0, upper = 0;
        foreach (char c in text)
            if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) upper++; }
        return letters > 0 && upper > letters * 0.6;
    }

    /// <summary>Puts the real terms back. Returns null when the translator dropped or mangled a placeholder.</summary>
    public static string? Restore(string translated, Masked masked)
    {
        var result = translated;
        foreach (var t in masked.Terms)
        {
            var rx = TokenRx[Array.IndexOf(Tokens, t.Token)];
            bool found = false;
            result = rx.Replace(result, m =>
            {
                found = true;
                var suffix = m.Groups["s"].Success ? m.Groups["s"].Value : string.Empty;
                if (suffix.Length == 0) return t.Output;
                var inf = TurkishSuffix.Inflect(t.Output, suffix);
                return t.IsName ? t.Output + "'" + inf : t.Output + inf;
            });
            if (!found) return null;
        }
        return result;
    }

    // ---------------- proper-noun heuristic ----------------

    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "I", "I'm", "I'll", "I've", "I'd", "God", "Mom", "Mum", "Dad", "Mommy", "Daddy", "Sir", "Ma'am", "Madam",
        "Mr", "Mrs", "Ms", "Dr", "Miss", "Mister", "OK", "Okay", "Christ", "Jesus", "Lord", "Hey", "Oh", "No", "Yes",
        "Yeah", "Hell", "Heaven", "Police", "Cops", "Cop", "Boss", "Bro", "Man", "Dude", "Baby", "Honey", "Officer",
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
        "Christmas", "Easter", "English", "American", "Americans", "Mexican", "Mexicans", "Spanish", "Russian", "Russians",
        "Chinese", "Japanese", "German", "Germans", "French", "British", "Italian", "Cuban", "Cubans", "Irish", "Indian", "Indians",
        "Black", "White", "Latino", "Christian", "Muslim", "Jewish", "Internet", "TV", "FBI", "CIA", "DEA", "USA", "US", "UK",
        "Uncle", "Aunt", "Grandma", "Grandpa", "Brother", "Sister", "Father", "Mother", "Captain", "Sergeant", "Detective", "Agent",
    };

    private static readonly HashSet<string> Honorifics = new(StringComparer.OrdinalIgnoreCase) { "Mr", "Mrs", "Ms", "Dr", "St", "Jr", "Sr", "Mt" };

    private static readonly Regex Word = new(@"[A-Za-z][A-Za-z'’\-]*", RegexOptions.CultureInvariant);

    /// <summary>Capitalized words that are not at a sentence start (merged into multi-word names), e.g. "Port Gellhorn".</summary>
    public static IEnumerable<(int Start, int Len)> ProperNouns(string text, bool startsMidSentence = false)
    {
        if (IsAllCaps(text)) yield break; // ALL-CAPS subtitles: no casing information

        var words = Word.Matches(text).Cast<Match>().ToList();
        var flags = new bool[words.Count];
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            var core = StripPossessive(w.Value);
            if (core.Length < 2 || !char.IsUpper(core[0]) || !core.Skip(1).Any(char.IsLower)) continue;
            if (NotNames.Contains(core) || NotNames.Contains(w.Value)) continue;
            if (IsSentenceStart(text, words, i, startsMidSentence)) continue;
            flags[i] = true; // candidate; common words survive only inside a multi-word name
        }
        for (int i = 0; i < words.Count; i++)
        {
            if (!flags[i]) continue;
            int j = i;
            // merge "Port Gellhorn", "Shore Court" (single spaces, previous word not possessive)
            while (j + 1 < words.Count && flags[j + 1] && OnlySpaceBetween(text, words[j], words[j + 1])
                   && StripPossessive(words[j].Value) == words[j].Value) j++;
            if (j == i && EnglishWords.IsCommon(StripPossessive(words[i].Value))) { flags[i] = false; continue; }
            int start = words[i].Index;
            var lastCore = StripPossessive(words[j].Value);
            int end = words[j].Index + lastCore.Length;
            yield return (start, end - start);
            i = j;
        }
    }

    private static string StripPossessive(string w)
    {
        if (w.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || w.EndsWith("’s", StringComparison.OrdinalIgnoreCase)) return w[..^2];
        return w.TrimEnd('\'', '’', '-');
    }

    private static bool OnlySpaceBetween(string text, Match a, Match b)
    {
        int s = a.Index + a.Length;
        return b.Index - s == 1 && text[s] == ' ';
    }

    private static bool IsSentenceStart(string text, List<Match> words, int i, bool startsMidSentence)
    {
        int k = words[i].Index - 1;
        while (k >= 0 && (char.IsWhiteSpace(text[k]) || text[k] == '"' || text[k] == '\'' || text[k] == '(' || text[k] == '“' || text[k] == '«')) k--;
        if (k < 0) return !startsMidSentence;
        char c = text[k];
        if (c == '.' && i > 0 && Honorifics.Contains(words[i - 1].Value)) return false;
        return c is '.' or '!' or '?' or ':' or ';' or '-' or '…' or '—';
    }
}
