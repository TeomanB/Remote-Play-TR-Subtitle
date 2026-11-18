using AltyaziCevirici.Core;
using Xunit;

public class PaylasimVeYedekTests
{
    [Fact]
    public void OverlayIntersectionUsesScreenPixelsIncludingNegativeMonitors()
    {
        var ocr = new EkranDikdortgen(516, 1230, 1536, 132);
        var ceviri = new EkranDikdortgen(652, 1191, 1235, 115);
        var gorev = new EkranDikdortgen(718, 1323, 1075, 71);
        var hit = KatmanMaske.Kesisim(ocr, ceviri);
        Assert.Equal(new EkranDikdortgen(652, 1230, 1235, 76), hit);
        var goal = KatmanMaske.Kesisim(ocr, gorev);
        Assert.Equal(new EkranDikdortgen(718, 1323, 1075, 39), goal);

        var local = KatmanMaske.Yerel(new EkranDikdortgen(-100, 100, 200, 40), -1920, 0, 1920, 1080);
        Assert.Equal(new EkranDikdortgen(1820, 100, 100, 40), local);
        Assert.Equal(0, KatmanMaske.Yerel(new EkranDikdortgen(3000, 0, 10, 10), 0, 0, 100, 100).Width);
    }

    [Fact]
    public void FillBlackOnlyCoversTheOverlayIntersection()
    {
        int w = 4, h = 2, stride = w * 4;
        var px = new byte[stride * h];
        for (int i = 0; i < px.Length; i++) px[i] = 255;
        ImageOps.FillBlack(px, stride, 1, 0, 2, 1, w, h);
        Assert.Equal(0, px[4]);
        Assert.Equal(0, px[8]);
        Assert.Equal(255, px[0]);
        Assert.Equal(255, px[12]);
        Assert.Equal(255, px[stride]);
    }

    [Fact]
    public void EchoDropsRecentTurkishAndExpires()
    {
        var t = new DateTime(2026, 11, 18, 21, 0, 0, DateTimeKind.Utc);
        var k = new YankiKalkan();
        k.Not("Arabaya bin.", t);
        Assert.True(k.Yanki("arabaya bin", t.AddSeconds(1)));
        Assert.False(k.Yanki("Get in the car", t.AddSeconds(1)));
        Assert.False(k.Yanki("arabaya bin", t.AddSeconds(6)));
    }

    [Fact]
    public async Task GoogleBlockUsesMyMemoryAndDoublesTheCooldown()
    {
        var clock = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
        int google = 0, memory = 0;
        var g = new ScriptBackend(_ => { google++; return Task.FromResult(new TranslateOutcome(TranslateStatus.RateLimited)); });
        var m = new ScriptBackend(_ => { memory++; return Task.FromResult(new TranslateOutcome(TranslateStatus.Ok, "Merhaba")); });
        var zincir = new CeviriZinciri(g, m, () => clock);

        var first = await zincir.TranslateAsync("Hello", "en", "tr", CancellationToken.None);
        Assert.Equal("Merhaba", first.Text);
        Assert.Equal(1, google);
        Assert.Equal(1, memory);
        Assert.Equal(TimeSpan.FromMinutes(2), zincir.GoogleBekleme);

        await zincir.TranslateAsync("Again", "en", "tr", CancellationToken.None);
        Assert.Equal(1, google);
        Assert.Equal(2, memory);

        clock = clock.AddMinutes(2);
        await zincir.TranslateAsync("Third", "en", "tr", CancellationToken.None);
        Assert.Equal(2, google);
        Assert.Equal(TimeSpan.FromMinutes(4), zincir.GoogleBekleme);
    }

    [Fact]
    public async Task MyMemoryQuotaAlsoRestsAndEnglishIsNotReturned()
    {
        var clock = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
        int memory = 0;
        var g = new ScriptBackend(_ => Task.FromResult(new TranslateOutcome(TranslateStatus.RateLimited)));
        var m = new ScriptBackend(_ => { memory++; return Task.FromResult(new TranslateOutcome(TranslateStatus.RateLimited)); });
        var zincir = new CeviriZinciri(g, m, () => clock);
        var failed = await zincir.TranslateAsync("Hello", "en", "tr", CancellationToken.None);
        Assert.Equal(TranslateStatus.Failed, failed.Status);
        Assert.NotEqual("Hello", failed.Text);
        await zincir.TranslateAsync("Hello", "en", "tr", CancellationToken.None);
        Assert.Equal(1, memory);
    }

    [Fact]
    public void MyMemoryParserReadsUtf8TextAndQuotaWarning()
    {
        var (text, quota) = MyMemoryParser.Parse("""{"responseStatus":200,"responseData":{"translatedText":"Arabaya bin."}}""");
        Assert.False(quota);
        Assert.Equal("Arabaya bin.", text);
        var blocked = MyMemoryParser.Parse("""{"responseStatus":200,"quotaFinished":true,"responseData":{"translatedText":"MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY"}}""");
        Assert.True(blocked.Quota);
        Assert.Null(blocked.Text);
    }

    private sealed class ScriptBackend(Func<string, Task<TranslateOutcome>> next) : ILanguageBackend
    {
        public Task<TranslateOutcome> TranslateAsync(string text, string source, string target, CancellationToken ct) => next(text);
    }
}
