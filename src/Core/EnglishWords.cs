namespace AltyaziCevirici.Core;

/// <summary>Common English words. A capitalised one of these is not a name unless it sits in a multi-word name.</summary>
public static class EnglishWords
{
    private static readonly HashSet<string> Words = Load();

    public static bool IsCommon(string word) =>
        Words.Contains(word.Trim().ToLowerInvariant());

    private static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var stream = typeof(EnglishWords).Assembly.GetManifestResourceStream("AltyaziCevirici.Core.english-common.txt");
        if (stream == null) return set;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var w = line.Trim().ToLowerInvariant();
            if (w.Length > 0) set.Add(w);
        }
        return set;
    }
}
