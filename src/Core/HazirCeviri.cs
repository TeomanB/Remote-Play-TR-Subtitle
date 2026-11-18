using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AltyaziCevirici.Core;

/// <summary>One local line pack: { "game", "lines": [ { "en", "tr" } ] }.</summary>
public sealed class HazirPaketDosyasi
{
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("lines")] public List<HazirSatir> Lines { get; set; } = new();
}

public sealed class HazirSatir
{
    [JsonPropertyName("en")] public string En { get; set; } = "";
    [JsonPropertyName("tr")] public string Tr { get; set; } = "";
}

/// <summary>Normalized lookup over one game's lines. Exact, fuzzy (>= 0.88), prefix, and a joined previous line.</summary>
public sealed class HazirDizin
{
    public const double Esik = 0.88;
    public string Oyun { get; }
    public int Satir { get; }

    private readonly Dictionary<string, string> _tam = new(StringComparer.Ordinal);
    private readonly Entry[] _entries;
    private readonly Dictionary<int, List<int>> _boy = new();
    private readonly Dictionary<string, List<int>> _onEk = new(StringComparer.Ordinal);

    private readonly record struct Entry(string Norm, string Tr);

    public HazirDizin(string oyun, IEnumerable<(string En, string Tr)> satirlar)
    {
        Oyun = oyun;
        var list = new List<Entry>();
        foreach (var (en, tr) in satirlar)
        {
            var n = Norm(en);
            var t = (tr ?? "").Trim();
            if (n.Length == 0 || t.Length == 0) continue;
            if (_tam.ContainsKey(n)) continue;
            _tam[n] = t;
            int i = list.Count;
            list.Add(new Entry(n, t));
            if (!_boy.TryGetValue(n.Length, out var bucket)) _boy[n.Length] = bucket = new List<int>();
            bucket.Add(i);
            if (n.Length >= 3)
            {
                var key = n[..3];
                if (!_onEk.TryGetValue(key, out var pre)) _onEk[key] = pre = new List<int>();
                pre.Add(i);
            }
        }
        _entries = list.ToArray();
        Satir = _entries.Length;
    }

    /// <summary>Case, punctuation, whitespace, curly quotes, and l/I plus 0/O OCR swaps.</summary>
    public static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var raw in s.Normalize(NormalizationForm.FormKC))
        {
            char c = raw switch
            {
                '\u2019' or '\u2018' or '`' or '\u00B4' => '\'',
                '\u201C' or '\u201D' or '\u201E' => '"',
                '\u2013' or '\u2014' => ' ',
                '\u00A0' => ' ',
                _ => raw,
            };
            c = char.ToLowerInvariant(c);
            if (c is 'i' or 'l' or '|') c = 'l';
            else if (c == '0') c = 'o';
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == '\'') continue;
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }

    public string? Ara(string ocr, string? onceki = null)
    {
        var n = Norm(ocr);
        var hit = Bul(n);
        if (hit != null || string.IsNullOrWhiteSpace(onceki)) return hit;
        var joined = Norm(onceki + " " + ocr);
        if (joined.Length <= n.Length) return null;
        return Bul(joined);
    }

    private string? Bul(string n)
    {
        if (n.Length == 0) return null;
        if (_tam.TryGetValue(n, out var exact)) return exact;
        string? best = null;
        double bestScore = Esik - 0.0001;
        int bestLen = int.MaxValue;
        if (n.Length >= 8)
        {
            int maxDist = (int)Math.Floor((1 - Esik) * n.Length);
            int lo = Math.Max(1, n.Length - maxDist);
            int hi = n.Length + maxDist + 2;
            for (int len = lo; len <= hi; len++)
            {
                if (!_boy.TryGetValue(len, out var ids)) continue;
                foreach (var i in ids) Consider(_entries[i], n, ref best, ref bestScore, ref bestLen);
            }
        }
        if (n.Length >= 12 && _onEk.TryGetValue(n[..3], out var prefixes))
        {
            foreach (var i in prefixes)
            {
                var e = _entries[i];
                if (e.Norm.Length <= n.Length || !e.Norm.StartsWith(n, StringComparison.Ordinal)) continue;
                if (e.Norm.Length < bestLen)
                {
                    bestScore = 1;
                    best = e.Tr;
                    bestLen = e.Norm.Length;
                }
            }
        }
        return best;
    }

    private static void Consider(Entry e, string n, ref string? best, ref double bestScore, ref int bestLen)
    {
        int maxLen = Math.Max(e.Norm.Length, n.Length);
        int limit = (int)Math.Floor((1 - Esik) * maxLen);
        int dist = Distance(e.Norm, n, limit);
        if (dist > limit) return;
        double score = 1.0 - dist / (double)maxLen;
        if (score > bestScore || (Math.Abs(score - bestScore) < 0.0001 && e.Norm.Length < bestLen))
        {
            bestScore = score;
            best = e.Tr;
            bestLen = e.Norm.Length;
        }
    }

    private static int Distance(string a, string b, int limit)
    {
        if (a.Length < b.Length) (a, b) = (b, a);
        if (a.Length - b.Length > limit) return limit + 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                if (cur[j] < rowMin) rowMin = cur[j];
            }
            if (rowMin > limit) return limit + 1;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}

/// <summary>Every *.json pack in the local lines folder, grouped by game name.</summary>
public sealed class HazirKutuphane
{
    public static readonly HazirKutuphane Bos = new(Array.Empty<HazirDizin>(), "");
    public static HazirKutuphane Guncel { get; private set; } = Bos;

    public static HazirKutuphane From(params HazirDizin[] paketler) => new(paketler, "shot");
    public static void Use(HazirKutuphane k) => Guncel = k;

    public IReadOnlyList<HazirDizin> Paketler { get; }
    public string Imza { get; }

    private HazirKutuphane(IReadOnlyList<HazirDizin> paketler, string imza)
    {
        Paketler = paketler;
        Imza = imza;
    }

    public static string Klasor() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AltyaziCevirici", "packs", "lines");

    public static HazirKutuphane Yukle()
    {
        try
        {
            var dir = Klasor();
            Directory.CreateDirectory(dir);
            return Guncel = Oku(dir);
        }
        catch (Exception)
        {
            return Bos;
        }
    }

    public static HazirKutuphane Oku(string folder)
    {
        if (!Directory.Exists(folder)) return Bos;
        var byGame = new Dictionary<string, List<(string En, string Tr)>>(StringComparer.OrdinalIgnoreCase);
        var stamp = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(file);
                stamp.Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append(';');
                var dosya = JsonSerializer.Deserialize<HazirPaketDosyasi>(File.ReadAllText(file));
                if (dosya == null || string.IsNullOrWhiteSpace(dosya.Game)) continue;
                var game = dosya.Game.Trim();
                if (!byGame.TryGetValue(game, out var list)) byGame[game] = list = new();
                foreach (var line in dosya.Lines)
                {
                    if (line == null || string.IsNullOrWhiteSpace(line.En) || string.IsNullOrWhiteSpace(line.Tr)) continue;
                    list.Add((line.En, line.Tr));
                }
            }
            catch (Exception) { /* one bad file does not drop the others */ }
        }
        var paketler = byGame
            .Select(kv => new HazirDizin(kv.Key, kv.Value))
            .Where(d => d.Satir > 0)
            .OrderBy(d => d.Oyun, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new HazirKutuphane(paketler, stamp.ToString());
    }

    public HazirDizin? Sec(string? oyun)
    {
        if (Paketler.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(oyun))
        {
            var hit = Paketler.FirstOrDefault(p => p.Oyun.Equals(oyun, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return Paketler.Count == 1 ? Paketler[0] : null;
    }
}
