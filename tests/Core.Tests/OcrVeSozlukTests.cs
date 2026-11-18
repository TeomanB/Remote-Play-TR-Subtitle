using AltyaziCevirici.Core;
using Xunit;

public class OcrGateTests
{
    [Theory]
    [InlineData("Get in the car, we're late.", true)]
    [InlineData("What the fuck are you doing?", true)]
    [InlineData("Son çeviri: sürükle bırak", false)]
    [InlineData("Kurulum dosyasını hazırlıyor", false)]
    [InlineData("a•b•c ??? ###", false)]
    [InlineData("ok", false)]
    public void AcceptsEnglishSubtitlesAndDropsTurkishOrJunk(string text, bool ok)
    {
        Assert.Equal(ok, OcrGate.Kabul(text, "tr"));
    }
}

public class DetectedLanguageTests
{
    [Fact]
    public void ReadsDetectedSourceFromGtxPayload()
    {
        const string json = "[[[\"Merhaba\",\"Hello\",null,null,1]],null,\"en\"]";
        Assert.Equal("en", GoogleTranslateParser.DetectedLanguage(json));
        Assert.Equal("tr", GoogleTranslateParser.DetectedLanguage("[[[\"x\",\"y\"]],null,\"tr\"]"));
        Assert.Null(GoogleTranslateParser.DetectedLanguage("[[[\"x\",\"y\"]]]"));
    }
}

public class SozlukPaketTests
{
    [Fact]
    public void DefaultPacksExcludeUnverifiedGta6Terms()
    {
        SozlukKutuphanesi.Gta6Json = "{\"id\":\"gta6\",\"ad\":\"GTA 6\",\"entries\":[]}";
        var s = new AppSettings();
        s.Clamp();
        var terms = SozlukKutuphanesi.Etkin(s).Select(g => g.Ingilizce).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("got it", terms);
        Assert.DoesNotContain("Vice City", terms);
        Assert.DoesNotContain("Leonida", terms);
        Assert.DoesNotContain("Jason", terms);
    }

    [Fact]
    public void GtaPackLoadsOnlyFromJson()
    {
        const string json = """
            {"id":"gta6","ad":"GTA 6","entries":[
              {"term":"Example Town","type":"protect","tr":"","category":"yer","sources":["manual"]}
            ]}
            """;
        var dosya = SozlukKutuphanesi.OkuGta(json);
        Assert.NotNull(dosya);
        Assert.Single(dosya.Entries);
        Assert.Equal("Example Town", dosya.Entries[0].Term);
        Assert.Equal("", dosya.Entries[0].ToGirdi().Turkce);

        SozlukKutuphanesi.Gta6Json = json;
        var s = new AppSettings();
        s.Clamp();
        s.AcikPaketler!.Add("gta6");
        Assert.Contains(SozlukKutuphanesi.Etkin(s), g => g.Ingilizce == "Example Town");
    }

    [Fact]
    public void AccentAndDollarOcrAliasMatch()
    {
        var names = new TermProtector(new[] { new SozlukGirdisi("Andrés de León"), new SozlukGirdisi("PTT Youngin$") }, false);
        Assert.Contains("Zelvar", names.Protect("Meet Andres de Leon tonight.").Text);
        Assert.Contains("Zelvar", names.Protect("Andrés de León is here.").Text);
        Assert.Contains("Zelvar", names.Protect("PTT Youngin$ run this block.").Text);
        Assert.Contains("Zelvar", names.Protect("PTT YoungínS run this block.").Text);
        Assert.DoesNotContain("Zelvar", names.Protect("PTT Younginz run this block.").Text);
    }

    [Fact]
    public void ShippedGtaPackIsTheVerifiedList()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "packs", "gta6.json"));
        var pack = SozlukKutuphanesi.OkuGta(json);
        Assert.Equal(111, pack!.Entries.Count);
        Assert.All(pack.Entries, e => Assert.Equal("protect", e.Type));
        foreach (var term in new[] { "Vice City", "Leonida", "Leonida Keys", "Port Gellhorn", "Grassrivers", "Ambrosia", "Mount Kalaga", "Jason", "Lucia" })
            Assert.Contains(pack.Entries, e => e.Term == term);
        foreach (var banned in new[] { "Starfish Island", "Valentina", "Vice Beach", "Lickety Split" })
            Assert.DoesNotContain(pack.Entries, e => e.Term == banned);
        foreach (var ambiguous in new[] { "The Chamber", "Keys", "Vapid", "Cheetah", "Trick", "MW", "Hawk", "Crest", "Mudslide" })
            Assert.Contains(pack.Entries, e => e.Term == ambiguous && e.CaseSensitive);
        Assert.Contains(pack.Entries, e => e.Term == "Mount Kalaga National Park" && e.Type == "protect" && string.IsNullOrEmpty(e.Tr));
        Assert.Contains(pack.Entries, e => e.Term == "Leonida Penitentiary" && e.Type == "protect" && string.IsNullOrEmpty(e.Tr));
    }

    [Fact]
    public async Task CaseAndProperNounRules()
    {
        var glossary = new[]
        {
            new SozlukGirdisi("Cheetah") { CaseSensitive = true },
            new SozlukGirdisi("The Chamber") { CaseSensitive = true },
            new SozlukGirdisi("Keys") { CaseSensitive = true },
            new SozlukGirdisi("Leonida Keys"),
        };
        var b = new DictBackend(new());
        var p = new TranslationProcessor(b, new ProcessorOptions(Glossary: glossary));

        var lower = await p.TranslateAsync("The cheetah ran.", CancellationToken.None);
        Assert.DoesNotContain("Zelvar", b.Sent[0]);
        Assert.Contains("cheetah", lower.Text, StringComparison.OrdinalIgnoreCase);

        b.Sent.Clear();
        var kept = await p.TranslateAsync("Get in the Cheetah.", CancellationToken.None);
        Assert.Contains("Zelvar", b.Sent[0]);
        Assert.Contains("Cheetah", kept.Text);
        Assert.DoesNotContain("Zelvar", kept.Text);

        b.Sent.Clear();
        var start = await p.TranslateAsync("Cheetah ran fast.", CancellationToken.None);
        Assert.DoesNotContain("Zelvar", b.Sent[^1]);
        Assert.Contains("Cheetah", start.Text);

        b.Sent.Clear();
        var caps = await p.TranslateAsync("GET IN THE CHEETAH.", CancellationToken.None);
        Assert.DoesNotContain("Zelvar", b.Sent[^1]);

        b.Sent.Clear();
        var multi = await p.TranslateAsync("MEET AT LEONIDA KEYS.", CancellationToken.None);
        Assert.Contains("Zelvar", b.Sent[^1]);
        Assert.Contains("Leonida Keys", multi.Text);

        b.Sent.Clear();
        var chamber = await p.TranslateAsync("The Chamber is open.", CancellationToken.None);
        Assert.Contains("Zelvar", b.Sent[^1]);
        Assert.Contains("The Chamber", chamber.Text);

        var names = new TranslationProcessor(new DictBackend(new()), new ProcessorOptions(Glossary: Array.Empty<SozlukGirdisi>()));
        var shore = await names.TranslateAsync("Meet me at Shore Court.", CancellationToken.None);
        Assert.Contains("Shore Court", shore.Text);
    }

    [Fact]
    public void UserEntryOverridesPackAndRoundTripsJson()
    {
        SozlukKutuphanesi.Gta6Json = "{\"entries\":[]}";
        var s = new AppSettings { Sozluk = new() { new("got it", "Tamamdır") } };
        s.Clamp();
        var hit = SozlukKutuphanesi.Etkin(s).Single(g => g.Ingilizce.Equals("got it", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Tamamdır", hit.Turkce);
        var back = SozlukKutuphanesi.IceAktar(SozlukKutuphanesi.DisaAktar(s.Sozluk));
        Assert.Equal("Tamamdır", back[0].Turkce);
    }
}
