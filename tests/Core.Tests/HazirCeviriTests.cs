using AltyaziCevirici.Core;
using Xunit;

public class HazirCeviriTests
{
    private static HazirDizin Pack() => new("GTA 6", new (string, string)[]
    {
        ("Get in the car, we're late.", "Arabaya bin, geç kaldık."),
        ("Meet me at Shore Court.", "Shore Court'ta buluşalım."),
        ("The code is IO.", "Kod IO."),
    });

    [Fact]
    public void NormalizesCasePunctuationQuotesAndOcrSwaps()
    {
        var d = Pack();
        Assert.Equal("Arabaya bin, geç kaldık.", d.Ara("  GET in the car... we're late! "));
        Assert.Equal("Arabaya bin, geç kaldık.", d.Ara("Get in the car, we\u2019re late."));
        Assert.Equal("Kod IO.", d.Ara("The code is l0."));
    }

    [Fact]
    public void FuzzyAndPrefixAndSplitLines()
    {
        var d = Pack();
        Assert.Equal("Shore Court'ta buluşalım.", d.Ara("Meet me at Shore Cort."));
        Assert.Null(d.Ara("hello there friend"));
        Assert.Equal("Arabaya bin, geç kaldık.", d.Ara("Get in the car, we're"));
        Assert.Equal("Arabaya bin, geç kaldık.", d.Ara("we're late.", "Get in the car,"));
    }

    [Fact]
    public async Task PackOnlyNeverCallsGoogleOnAMiss()
    {
        var backend = new CountingBackend();
        var proc = new TranslationProcessor(backend, new ProcessorOptions(Hazir: Pack(), SadeceHazir: true));
        var miss = await proc.TranslateAsync("nothing like the pack", CancellationToken.None);
        Assert.Equal(TranslateStatus.Skipped, miss.Status);
        Assert.Equal(0, backend.Calls);
        var hit = await proc.TranslateAsync("get in the car, we're late", CancellationToken.None);
        Assert.Equal(TranslateStatus.Ok, hit.Status);
        Assert.Equal("Arabaya bin, geç kaldık.", hit.Text);
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task LiveModeTranslatesAMiss()
    {
        var backend = new CountingBackend();
        var proc = new TranslationProcessor(backend, new ProcessorOptions(Hazir: Pack(), SadeceHazir: false));
        var miss = await proc.TranslateAsync("hello there", CancellationToken.None);
        Assert.Equal(TranslateStatus.Ok, miss.Status);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public void ReadsATinyPackFromDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hazir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "gta.json"), """
                { "game": "GTA 6", "lines": [ { "en": "Hello there.", "tr": "Selam." } ] }
                """);
            var lib = HazirKutuphane.Oku(dir);
            Assert.Equal("Selam.", lib.Sec("GTA 6")!.Ara("hello there"));
            Assert.Equal("GTA 6", lib.Sec("")!.Oyun);
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class CountingBackend : ILanguageBackend
    {
        public int Calls;
        public Task<TranslateOutcome> TranslateAsync(string text, string source, string target, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new TranslateOutcome(TranslateStatus.Ok, "TR"));
        }
    }
}
