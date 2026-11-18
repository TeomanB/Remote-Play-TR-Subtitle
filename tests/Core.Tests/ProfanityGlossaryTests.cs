using AltyaziCevirici.Core;
using Xunit;

/// <summary>Fake Google: returns canned translations; records what was sent.</summary>
public sealed class DictBackend : ILanguageBackend
{
    private readonly Dictionary<string, string> _map;
    public List<string> Sent { get; } = new();
    public DictBackend(Dictionary<string, string> map) => _map = map;
    public Task<TranslateOutcome> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        Sent.Add(text);
        return Task.FromResult(_map.TryGetValue(text, out var t)
            ? new TranslateOutcome(TranslateStatus.Ok, t)
            : new TranslateOutcome(TranslateStatus.Ok, "TR(" + text + ")"));
    }
}

public class ProfanityTests
{
    private static async Task<(string? Out, DictBackend B)> Run(string en, Dictionary<string, string>? map = null, ProcessorOptions? opt = null)
    {
        var b = new DictBackend(map ?? new());
        var p = new TranslationProcessor(b, opt ?? new ProcessorOptions(Glossary: Array.Empty<SozlukGirdisi>()));
        var r = await p.TranslateAsync(en, CancellationToken.None);
        Assert.Equal(TranslateStatus.Ok, r.Status);
        return (r.Text, b);
    }

    [Theory]
    [InlineData("f***", "fuck")]
    [InlineData("What the f***ing hell?", "What the fucking hell?")]
    [InlineData("Sh*t, he's dead.", "Shit, he's dead.")]
    [InlineData("You a**hole!", "You asshole!")]
    [InlineData("F***!", "Fuck!")]
    [InlineData("Price is 5*3", "Price is 5*3")]
    [InlineData("No stars here", "No stars here")]
    public void Uncensor(string input, string expected) => Assert.Equal(expected, Profanity.Uncensor(input));

    [Fact]
    public async Task IntensifierIsStrippedAndReAddedAsLan()
    {
        // Real Google output for the stripped line; the original came back as "Ne yapıyorsun sen?" (swear dropped).
        var (o, b) = await Run("What the fuck are you doing?", new() { ["What are you doing?"] = "Ne yapıyorsun?" });
        Assert.Equal("What are you doing?", Assert.Single(b.Sent));
        Assert.Equal("Ne yapıyorsun lan?", o);
    }

    [Fact]
    public async Task FuckingAdverbAndShutUp()
    {
        var (o, _) = await Run("This is fucking crazy.", new() { ["This is crazy."] = "Bu çok çılgınca." });
        Assert.Equal("Bu çok çılgınca lan.", o);
        var (o2, b2) = await Run("Shut the fuck up!");
        Assert.Equal("Kapa çeneni lan!", o2);
        Assert.Empty(b2.Sent); // dictionary line: no network request at all
    }

    [Fact]
    public async Task MildHellGetsBe()
    {
        var (o, _) = await Run("Where the hell have you been?", new() { ["Where have you been?"] = "Neredeydin?" });
        Assert.Equal("Neredeydin be?", o);
    }

    [Theory]
    [InlineData("Fuck!", "Siktir!")]
    [InlineData("Holy shit.", "Hassiktir.")]
    [InlineData("Oh, shit!", "Hassiktir!")]
    [InlineData("Fuck you!", "Siktir git!")]
    [InlineData("You son of a bitch!", "Seni orospu çocuğu!")]
    [InlineData("Motherfucker...", "Orospu çocuğu...")]
    [InlineData("Damn it!", "Kahretsin!")]
    [InlineData("Fuck", "Siktir!")]
    [InlineData("F***!", "Siktir!")]
    public async Task WholeLineExclamations(string en, string tr)
    {
        var (o, b) = await Run(en);
        Assert.Equal(tr, o);
        Assert.Empty(b.Sent);
    }

    [Fact]
    public async Task LeadingExclamationPlusBody()
    {
        var (o, b) = await Run("Fuck, we're late.", new() { ["we're late."] = "Geç kaldık." });
        Assert.Equal("we're late.", Assert.Single(b.Sent));
        Assert.Equal("Siktir, geç kaldık.", o);
    }

    [Fact]
    public async Task NameRightAfterExclamationKeepsCapital()
    {
        var (o, b) = await Run("Fuck off, Brian, I'm busy.", new() { ["Zelvar, I'm busy."] = "Zelvar, meşgulüm." });
        Assert.Equal("Zelvar, I'm busy.", Assert.Single(b.Sent));
        Assert.Equal("Siktir git, Brian, meşgulüm.", o);
    }

    [Fact]
    public async Task ExclamationAndTrailingInsult()
    {
        var (o, b) = await Run("Fuck you, asshole.");
        Assert.Empty(b.Sent);
        Assert.Equal("Siktir git, göt herif.", o);
        var (o2, _) = await Run("Where is my money, you motherfucker?", new() { ["Where is my money?"] = "Param nerede?" });
        Assert.Equal("Param nerede, seni orospu çocuğu?", o2);
    }

    [Fact]
    public async Task TwoSentencesParticleGoesToTheRightOne()
    {
        var (o, _) = await Run("Get in the car. Where the fuck is Jason?",
            new() { ["Get in the car. Where is Zelvar?"] = "Arabaya bin. Zelvar nerede?" });
        Assert.Equal("Arabaya bin. Jason nerede lan?", o);
    }

    [Fact]
    public async Task CleanLinesUnchanged()
    {
        var (o, b) = await Run("Get in the car, we're late.", new() { ["Get in the car, we're late."] = "Arabaya bin, geç kaldık." });
        Assert.Equal("Arabaya bin, geç kaldık.", o);
        Assert.Single(b.Sent);
    }

    [Fact]
    public async Task OtherLanguagePairsSkipTurkishProfanityRules()
    {
        var (o, b) = await Run("What the fuck?", opt: new ProcessorOptions("en", "de", Array.Empty<SozlukGirdisi>()));
        Assert.Equal("What the fuck?", Assert.Single(b.Sent));
        Assert.Equal("TR(What the fuck?)", o);
    }
}

public class GlossaryTests
{
    [Theory]
    [InlineData("Saint Denis", "a", "e")]
    [InlineData("Saint Denis", "da", "te")]
    [InlineData("Vice City", "a", "ye")]
    [InlineData("Vice City", "ın", "nin")]
    [InlineData("Leonida", "dan", "dan")]
    [InlineData("Leonida", "ı", "yı")]
    [InlineData("Port Gellhorn", "da", "da")]
    [InlineData("Jason", "la", "la")]
    [InlineData("Lucia", "la", "yla")]
    [InlineData("Lucia", "ın", "nın")]
    [InlineData("Grassrivers", "a", "e")]
    [InlineData("Mount Kalaga", "dakiler", "dakiler")]
    [InlineData("Brooklyn", "ı", "i")]
    [InlineData("Novigrad", "ta", "da")]
    public void SuffixInflection(string word, string zelvarSuffix, string expected) =>
        Assert.Equal(expected, TurkishSuffix.Inflect(word, zelvarSuffix));

    [Fact]
    public void ProtectAndRestoreWithSuffix()
    {
        var p = new TermProtector(new[] { new SozlukGirdisi("Saint Denis"), new SozlukGirdisi("Vice City", "Vice City") }, properNouns: false);
        var m = p.Protect("We need to go to saint denis, then Vice City.");
        Assert.Equal("We need to go to Zelvar, then Dumkar.", m.Text);
        var r = TermProtector.Restore("Zelvar'a, sonra Dumkar'a gitmemiz lazım.", m);
        Assert.Equal("Saint Denis'e, sonra Vice City'ye gitmemiz lazım.", r);
    }

    [Fact]
    public void GlossaryTurkishOutputAndCommonNouns()
    {
        var p = new TermProtector(new[] { new SozlukGirdisi("Blackwater", "Karasu"), new SozlukGirdisi("Pinkertons", "dedektifler") }, properNouns: false);
        var m = p.Protect("The Pinkertons are in Blackwater.");
        var r = TermProtector.Restore("Zelvar Dumkar'da.", m);
        Assert.Equal("dedektifler Karasu'da.", r);
        // common noun (lowercase output): suffix joins without apostrophe
        var m2 = p.Protect("I saw the Pinkertons.");
        Assert.Equal("dedektifleri gördüm", TermProtector.Restore("Zelvar'ı gördüm", m2));
    }

    [Fact]
    public void LostPlaceholderReturnsNull()
    {
        var p = new TermProtector(new[] { new SozlukGirdisi("Leonida") }, false);
        var m = p.Protect("Welcome to Leonida.");
        Assert.Null(TermProtector.Restore("Hoş geldiniz.", m));
    }

    [Theory]
    [InlineData("We need to go to Port Gellhorn now.", "Port Gellhorn")]
    [InlineData("Tell Lucia's brother to wait.", "Lucia")]
    [InlineData("I told you, Jason, I'm done.", "Jason")]
    [InlineData("Mr. Smith is here.", "Smith")]
    public void ProperNounHeuristicFinds(string text, string expected)
    {
        var found = TermProtector.ProperNouns(text).Select(s => text.Substring(s.Start, s.Len)).ToList();
        Assert.Contains(expected, found);
    }

    [Theory]
    [InlineData("Where are you going?")]
    [InlineData("Lucia is here.")]             // sentence start: left to the translator
    [InlineData("I said OK, Mom. On Monday.")]  // stop words
    [InlineData("WE NEED TO GO TO VICE CITY")]  // all caps: no casing info
    [InlineData("Run. Now!")]
    public void ProperNounHeuristicIgnores(string text) => Assert.Empty(TermProtector.ProperNouns(text));

    [Fact]
    public async Task ProcessorKeepsNamesAndFixesGoogleSuffix()
    {
        var b = new DictBackend(new() { ["We are going to Zelvar."] = "Zelvar'a gidiyoruz." });
        var p = new TranslationProcessor(b, new ProcessorOptions(Glossary: new[] { new SozlukGirdisi("Vice City") }));
        var r = await p.TranslateAsync("We are going to Vice City.", CancellationToken.None);
        Assert.Equal("Vice City'ye gidiyoruz.", r.Text); // Google itself writes "Vice City'e"
    }

    [Fact]
    public async Task ProperNounNotTranslatedLiterally()
    {
        var b = new DictBackend(new() { ["Meet me at Zelvar."] = "Benimle Zelvar'da buluş." });
        var p = new TranslationProcessor(b, new ProcessorOptions(Glossary: Array.Empty<SozlukGirdisi>()));
        var r = await p.TranslateAsync("Meet me at Port Gellhorn.", CancellationToken.None);
        Assert.Equal("Benimle Port Gellhorn'da buluş.", r.Text); // Google alone: "Gellhorn Limanı'nda"
    }

    [Fact]
    public async Task FallsBackUnprotectedWhenPlaceholderLost()
    {
        var b = new DictBackend(new() { ["Meet me at Zelvar."] = "Benimle orada buluş.", ["Meet me at Port Gellhorn."] = "Gellhorn Limanı'nda buluş." });
        var p = new TranslationProcessor(b, new ProcessorOptions(Glossary: Array.Empty<SozlukGirdisi>()));
        var r = await p.TranslateAsync("Meet me at Port Gellhorn.", CancellationToken.None);
        Assert.Equal("Gellhorn Limanı'nda buluş.", r.Text);
        Assert.Equal(2, b.Sent.Count);
    }

    [Fact]
    public async Task OptionsSwapLive()
    {
        var b = new DictBackend(new() { ["Go to Zelvar."] = "Zelvar'a git." });
        var p = new TranslationProcessor(b, new ProcessorOptions(Glossary: Array.Empty<SozlukGirdisi>(), KeepProperNouns: false));
        p.Options = new ProcessorOptions(Glossary: new[] { new SozlukGirdisi("blackwater", "Karasu") }, KeepProperNouns: false);
        var r = await p.TranslateAsync("Go to Blackwater.", CancellationToken.None);
        Assert.Equal("Karasu'ya git.", r.Text);
    }
}

public class SettingsV12Tests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "altyazi-" + Guid.NewGuid().ToString("N"), "ayarlar.json");

    [Fact]
    public void V11FileLoadsWithNewDefaultsAndKeepsValues()
    {
        var p = Tmp();
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "{\"RegionX\":5,\"RegionY\":800,\"RegionWidth\":1000,\"RegionHeight\":100,\"Surum\":2,\"Fps\":20,\"BeyazYaziFiltresi\":false,\"BeyazEsik\":180,\"OverlayKonumu\":\"Alt\",\"YaziBoyutu\":32,\"GizlemeMs\":2500,\"BenzerlikEsigi\":0.8}");
        var s = AppSettings.Load(p);
        Assert.Equal(20, s.Fps); // v1.1 fps kept
        Assert.Equal(OverlayPlacement.Alt, s.OverlayKonumu);
        Assert.Equal(32f, s.YaziBoyutu);
        Assert.False(s.BeyazYaziFiltresi);
        Assert.Equal("en", s.KaynakDil);
        Assert.Equal("tr", s.HedefDil);
        Assert.Equal("#FFFFFF", s.YaziRengi);
        Assert.Equal(65, s.ArkaPlanOpakligi);
        Assert.Empty(s.Sozluk!); // user list starts empty; built-in packs are separate
        Assert.Contains("genel", s.AcikPaketler!);
        Assert.DoesNotContain("gta6", s.AcikPaketler!);
        Assert.Equal(AppSettings.CurrentVersion, s.Surum);
    }

    [Fact]
    public void GlossaryRoundTripsWithTurkishCharacters()
    {
        var p = Tmp();
        var s = new AppSettings { Sozluk = new() { new("Blackwater", "Karasu Çiğdem"), new("  ", "x") }, YaziRengi = "#FFD700", ArkaPlanOpakligi = 150 };
        s.Save(p);
        Assert.Contains("Çiğdem", File.ReadAllText(p));
        var l = AppSettings.Load(p);
        Assert.Single(l.Sozluk!);
        Assert.Equal("Karasu Çiğdem", l.Sozluk![0].Turkce);
        Assert.Equal("#FFD700", l.YaziRengi);
        Assert.Equal(100, l.ArkaPlanOpakligi);
        Assert.DoesNotContain("Kufur", File.ReadAllText(p), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BadValuesAreRepaired()
    {
        var p = Tmp();
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "{\"YaziRengi\":\"mavi\",\"KaynakDil\":\"en; drop\",\"Sozluk\":[]}");
        var s = AppSettings.Load(p);
        Assert.Equal("#FFFFFF", s.YaziRengi);
        Assert.Equal("en", s.KaynakDil);
        Assert.Empty(s.Sozluk!); // user emptied the glossary: stays empty
    }

    [Fact]
    public void CensoredWordsSurviveOcrCleanup()
    {
        Assert.Equal("Oh, f***", TextUtil.Normalize("Oh, f*** "));
        Assert.Equal("Stop", TextUtil.Normalize("Stop **"));
        Assert.True(TextUtil.LooksLikeText("F***!"));
        Assert.True(TextUtil.LooksLikeText("What the f***ing hell?"));
    }
}

public class ProcessorSpeedTests
{
    [Fact]
    public async Task LocalProcessingAddsWellUnderAMillisecond()
    {
        var b = new DictBackend(new());
        var s = new AppSettings(); s.Clamp();
        var p = new TranslationProcessor(b, s.ToProcessorOptions());
        string[] lines = { "What the fuck are you doing, Jason?", "Meet me at Port Gellhorn.", "Get in the car, we're late.", "Fuck you, asshole.", "Tell Lucia's brother to meet Mr. Smith in Vice City." };
        for (int i = 0; i < 50; i++) await p.TranslateAsync(lines[i % lines.Length], default); // warm up / JIT
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int n = 2000;
        for (int i = 0; i < n; i++) await p.TranslateAsync(lines[i % lines.Length] + " " + i, default);
        double perLine = sw.Elapsed.TotalMilliseconds / n;
        Assert.True(perLine < 1.0, $"{perLine:F3} ms per line");
    }
}
