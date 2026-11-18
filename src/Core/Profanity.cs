using System.Text;
using System.Text.RegularExpressions;

namespace AltyaziCevirici.Core;

/// <summary>
/// English → Turkish profanity handling. Google's free endpoint does not mask swear words, but its model
/// silently drops intensifiers ("What the fuck are you doing?" → "Ne yapıyorsun sen?") and softens
/// exclamations ("Holy shit" → "Lanet olsun"). This turns them into natural Turkish deterministically:
/// leading exclamations/insults come from a dictionary, intensifiers are removed before translation and
/// re-added as the Turkish particle "lan"/"be", and trailing insults ("..., you asshole.") are appended.
/// </summary>
public static class Profanity
{
    public enum Strength { None = 0, Mild = 1, Strong = 2 }

    public sealed record Plan(
        IReadOnlyList<(string Tr, string Sep)> Prefixes,
        string Body,
        IReadOnlyList<(int Sentence, Strength Strength)> Particles,
        string? Vocative,
        string VocativePunct);

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // ---- censored words from the game ("f***", "sh*t", "a**hole") ----
    private static readonly string[] Known =
    {
        "motherfucker", "motherfucking", "fucking", "fuckin", "fucker", "fucked", "fuck", "shitty", "shit",
        "bullshit", "bitch", "bitches", "asshole", "bastard", "dickhead", "dick", "damn", "goddamn", "cunt", "piss",
        "hell", "crap", "prick",
    };
    private static readonly System.Buffers.SearchValues<char> CensorChars = System.Buffers.SearchValues.Create("*#@$%");
    private static readonly Regex Censored = new(@"\b[A-Za-z]*[A-Za-z][*#@$%]+[A-Za-z*#@$%]*|\b[*#@$%]+[A-Za-z]+", RegexOptions.CultureInvariant);

    /// <summary>Restores asterisk-censored swear words: "f***ing" → "fucking". Unknown patterns are left as is.</summary>
    public static string Uncensor(string text)
    {
        if (text.AsSpan().IndexOfAny(CensorChars) < 0) return text;
        return Censored.Replace(text, m =>
        {
            var tok = m.Value;
            if (!tok.Any(char.IsLetter)) return tok;
            foreach (var w in Known)
            {
                if (w.Length != tok.Length) continue;
                bool ok = true;
                for (int i = 0; i < w.Length && ok; i++)
                {
                    char c = tok[i];
                    if (char.IsLetter(c)) ok = char.ToLowerInvariant(c) == w[i];
                }
                if (ok) return MatchCase(tok, w);
            }
            return tok;
        });
    }

    private static string MatchCase(string like, string word)
    {
        if (like.Count(char.IsLetter) > 1 && like.Where(char.IsLetter).All(char.IsUpper)) return word.ToUpperInvariant();
        if (char.IsUpper(like[0])) return char.ToUpperInvariant(word[0]) + word[1..];
        return word;
    }

    // ---- exclamations and insults that make up a whole clause ----
    private static readonly (string Pattern, string Tr)[] Interjections =
    {
        ("shut the fuck up", "Kapa çeneni lan"),
        ("what the fuck", "Ne oluyor lan"),
        ("what the fucking hell", "Ne oluyor lan"),
        ("what the hell", "Ne oluyor be"),
        ("holy fucking shit", "Hassiktir"),
        ("holy shit|oh shit|oh fuck|fuck me|holy fuck", "Hassiktir"),
        ("fuck you|fuck off|go fuck yourself|screw you|fuck you too", "Siktir git"),
        ("fuck this|fuck that|fuck it|fuck this shit", "Siktir et"),
        ("fuck yeah|fuck yes|hell yeah|hell yes", "Aynen lan"),
        ("piss off", "Defol git"),
        ("bullshit", "Hadi lan oradan"),
        ("fuck|shit|fuck fuck|fuck fuck fuck|fucking hell|shit shit|aw shit|ah shit|ah fuck|aw fuck", "Siktir"),
        ("damn|damn it|dammit|goddamn|goddamn it|goddammit|god damn it|god dammit|bloody hell|oh damn", "Kahretsin"),
        ("you son of a bitch|you motherfucker|you fucking bastard", "Seni orospu çocuğu"),
        ("son of a bitch|motherfucker|motherfuckers|sons of bitches", "Orospu çocuğu"),
        ("you asshole|you fucking asshole", "Seni göt herif"),
        ("asshole|assholes", "Göt herif"),
        ("you bitch|you fucking bitch", "Seni kaltak"),
        ("bitch", "Kaltak"),
        ("you bastard", "Seni piç"),
        ("bastard|bastards", "Piç"),
        ("you dickhead|you dick", "Seni yarrak kafalı"),
        ("dickhead", "Yarrak kafalı"),
        ("you prick", "Seni hıyar"),
        ("prick", "Hıyar"),
        ("you cunt", "Seni amcık"),
        ("cunt", "Amcık"),
    };

    private static readonly Regex Leading;
    private static readonly Dictionary<string, string> InterjectionMap = new(StringComparer.OrdinalIgnoreCase);

    // ---- insults hanging off the end of a sentence: "..., you asshole." ----
    private static readonly (string Pattern, string Tr)[] Insults =
    {
        ("son of a bitch|motherfucker", "orospu çocuğu"),
        ("asshole", "göt herif"),
        ("bitch", "kaltak"),
        ("bastard", "piç"),
        ("dickhead|dick", "yarrak kafalı"),
        ("prick", "hıyar"),
        ("cunt", "amcık"),
    };
    private static readonly Regex Trailing;
    private static readonly Dictionary<string, string> InsultMap = new(StringComparer.OrdinalIgnoreCase);

    // ---- intensifiers the translator drops ----
    private static readonly (Regex Rx, string Repl, Strength S)[] Intensifiers =
    {
        (new Regex(@",?\s*\bfor\s+fuck'?s\s+sake\b,?", Opt), "", Strength.Strong),
        (new Regex(@"\b([a-z]+)\s+as\s+fuck\b", Opt), "really $1", Strength.Strong),
        (new Regex(@"\b([a-z]+)\s+as\s+hell\b", Opt), "really $1", Strength.Mild),
        (new Regex(@"\s*\bthe\s+(?:fucking\s+hell|fuck|fuckin'?|fucking)\b", Opt), "", Strength.Strong),
        (new Regex(@"\s*\bthe\s+(?:hell|heck)\b", Opt), "", Strength.Mild),
        (new Regex(@"\b(?:motherfucking|fucking|fuckin'?|effing)\s+(?=[a-z])", Opt), "", Strength.Strong),
        (new Regex(@"\b(?:freaking|friggin'?|frickin'?|fricking)\s+(?=[a-z])", Opt), "", Strength.Mild),
    };

    static Profanity()
    {
        var alts = new List<string>();
        foreach (var (p, tr) in Interjections)
            foreach (var a in p.Split('|'))
            {
                InterjectionMap.TryAdd(a, tr);
                alts.Add(a);
            }
        alts.Sort((x, y) => y.Length.CompareTo(x.Length));
        var alt = string.Join("|", alts.Select(a => Regex.Escape(a).Replace(@"\ ", @"[\s,]+")));
        Leading = new Regex($@"^\s*(?:(?:oh|ah|aw|aww),?\s+)??(?<k>{alt})(?<sep>\s*[,.!?…]+\s*|\s*$)", Opt);

        var ins = new List<string>();
        foreach (var (p, tr) in Insults)
            foreach (var a in p.Split('|')) { InsultMap.TryAdd(a, tr); ins.Add(a); }
        ins.Sort((x, y) => y.Length.CompareTo(x.Length));
        Trailing = new Regex($@",\s*(?<you>you\s+)?(?:fucking\s+|little\s+|stupid\s+|dumb\s+)?(?<k>{string.Join("|", ins.Select(Regex.Escape))})\s*(?<p>[.!?…]*)\s*$", Opt);
    }

    private static string Canon(string k) => Regex.Replace(k.ToLowerInvariant(), @"[\s,]+", " ");

    /// <summary>Splits the English line into dictionary-translated parts and the body that still needs the translator.</summary>
    public static Plan Analyze(string text)
    {
        var prefixes = new List<(string, string)>();
        var rest = text.Trim();
        for (int guard = 0; guard < 6 && rest.Length > 0; guard++)
        {
            var m = Leading.Match(rest);
            if (!m.Success || !InterjectionMap.TryGetValue(Canon(m.Groups["k"].Value), out var tr)) break;
            prefixes.Add((tr, m.Groups["sep"].Value.Trim()));
            rest = rest[m.Length..].Trim();
        }

        string? voc = null;
        string vocP = "";
        var tm = rest.Length > 0 ? Trailing.Match(rest) : Match.Empty;
        if (tm.Success && tm.Index > 0 && InsultMap.TryGetValue(tm.Groups["k"].Value, out var insult))
        {
            voc = (tm.Groups["you"].Success ? "seni " : "") + insult;
            vocP = tm.Groups["p"].Value;
            rest = rest[..tm.Index].Trim() + vocP;
        }

        var particles = new List<(int, Strength)>();
        foreach (var (rx, repl, s) in Intensifiers)
        {
            var body = rest;
            rest = rx.Replace(body, m =>
            {
                particles.Add((SentenceIndex(body, m.Index), s));
                return m.Result(repl);
            });
        }
        rest = Tidy(rest);
        return new Plan(prefixes, rest, particles, voc, vocP);
    }

    private static readonly Regex SentenceEnd = new(@"[.!?…]+(?=\s+\S)", RegexOptions.CultureInvariant);

    private static int SentenceIndex(string s, int pos) => SentenceEnd.Matches(s[..Math.Min(pos, s.Length)]).Count;

    private static string Tidy(string s)
    {
        s = Regex.Replace(s, @"\s{2,}", " ");
        s = Regex.Replace(s, @"\s+([,.!?…])", "$1");
        s = Regex.Replace(s, @",\s*([.!?…])", "$1");
        s = Regex.Replace(s, @"^[\s,]+", "");
        return s.Trim();
    }

    public static bool HasLetters(string s) => s.Any(char.IsLetter);

    /// <summary>Puts the Turkish parts back around the translated body.</summary>
    public static string Compose(Plan plan, string? translatedBody, int sourceSentences, bool bodyStartsWithName = false)
    {
        var body = translatedBody?.Trim() ?? string.Empty;
        if (body.Length > 0 && plan.Particles.Count > 0) body = AddParticles(body, plan.Particles, sourceSentences);

        if (plan.Vocative != null && body.Length > 0)
        {
            var mm = Regex.Match(body, @"[.!?…]+$");
            string punct = plan.VocativePunct.Length > 0 ? plan.VocativePunct : (mm.Success ? mm.Value : ".");
            body = body.TrimEnd('.', '!', '?', '…', ' ') + ", " + plan.Vocative + punct;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < plan.Prefixes.Count; i++)
        {
            var (tr, sep) = plan.Prefixes[i];
            bool last = i == plan.Prefixes.Count - 1;
            if (sb.Length > 0 && sb[^1] == ' ' && sb.Length >= 2 && sb[^2] == ',') tr = LowerFirst(tr);
            sb.Append(tr);
            if (last && body.Length == 0) { sb.Append(sep.Length > 0 && sep != "," ? sep : "!"); break; }
            sb.Append(sep.Length == 0 ? "," : sep).Append(' ');
        }
        if (body.Length > 0)
        {
            if (sb.Length >= 2 && sb[^2] == ',' && !bodyStartsWithName && body.Length > 1 && char.IsUpper(body[0])) body = LowerFirst(body);
            sb.Append(body);
        }
        return sb.ToString().Trim();
    }

    private static string LowerFirst(string s) =>
        s.Length == 0 ? s : char.ToLower(s[0], TurkishSuffix.Tr) + s[1..];

    private static readonly Regex TrSentence = new(@"(?<=[.!?…])\s+", RegexOptions.CultureInvariant);

    private static string AddParticles(string body, IReadOnlyList<(int Sentence, Strength Strength)> particles, int sourceSentences)
    {
        var parts = TrSentence.Split(body).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return body;
        var want = new Strength[parts.Count];
        foreach (var (idx, s) in particles)
        {
            int t = parts.Count == sourceSentences ? Math.Clamp(idx, 0, parts.Count - 1) : parts.Count - 1;
            if (s > want[t]) want[t] = s;
        }
        for (int i = 0; i < parts.Count; i++)
        {
            if (want[i] == Strength.None) continue;
            var word = want[i] == Strength.Strong ? "lan" : "be";
            var m = Regex.Match(parts[i], @"^(?<b>.*?)(?<p>[.!?…""»)]*)$", RegexOptions.Singleline);
            var b = m.Groups["b"].Value.TrimEnd();
            if (Regex.IsMatch(b, @"\b(lan|be|ulan)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
            parts[i] = b + " " + word + m.Groups["p"].Value;
        }
        return string.Join(" ", parts);
    }

    /// <summary>Number of sentences in an English text (for mapping particles).</summary>
    public static int CountSentences(string s) => string.IsNullOrWhiteSpace(s) ? 0 : SentenceEnd.Matches(s).Count + 1;
}
