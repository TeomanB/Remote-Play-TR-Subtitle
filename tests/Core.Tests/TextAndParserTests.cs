using AltyaziCevirici.Core;
using Xunit;

public class TextUtilTests
{
    [Theory]
    [InlineData("  Hey ,  man .\n Get  in the car! ", "Hey, man. Get in the car!")]
    [InlineData("I don\u2019t know |t", "I don't know It")]
    [InlineData("-- Where are you going? __", "Where are you going?")]
    [InlineData("\t\r\n", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize(string? input, string expected) => Assert.Equal(expected, TextUtil.Normalize(input));

    [Fact]
    public void KeyIgnoresCaseAndPunctuation() => Assert.Equal("hey man get in", TextUtil.Key("Hey, MAN... get-in!"));

    [Fact]
    public void KeyKeepsTurkishAndAccents() => Assert.Equal("çok güzel", TextUtil.Key("Çok GÜZEL!"));

    [Fact]
    public void Similarity()
    {
        Assert.Equal(1.0, TextUtil.Similarity("Get in the car.", "get in the car"));
        Assert.True(TextUtil.Similarity("Get in the car, now!", "Get in tne car, n0w!") >= 0.85);
        Assert.True(TextUtil.Similarity("Get in the car, now!", "We need to talk about Lucia.") < 0.5);
        Assert.Equal(0.0, TextUtil.Similarity("", "abc"));
        Assert.Equal(1.0, TextUtil.Similarity("", null));
        Assert.Equal(3, TextUtil.Levenshtein("kitten", "sitting"));
        Assert.Equal(0, TextUtil.Levenshtein("", ""));
    }

    [Fact]
    public void LevenshteinLongStringsNoStackOverflow()
    {
        var a = new string('a', 3000);
        var b = new string('a', 2990) + new string('b', 10);
        Assert.Equal(10, TextUtil.Levenshtein(a, b));
    }

    [Theory]
    [InlineData("Hello there", true)]
    [InlineData("No.", true)]
    [InlineData("OK", true)]
    [InlineData("Get in the car, Lucia!", true)]
    [InlineData("I've got 2 minutes.", true)]
    [InlineData("|.,-_ 1", false)]
    [InlineData("a", false)]
    [InlineData("lI", false)]
    [InlineData("  ", false)]
    [InlineData("#@! 12%% &*", false)]
    [InlineData("x7$ q9# ii", false)]
    [InlineData("1234 5678", false)]
    public void LooksLikeText(string s, bool expected) => Assert.Equal(expected, TextUtil.LooksLikeText(s));

    [Theory]
    [InlineData("Get in", "Get in the car", true)]
    [InlineData("Get in tne", "Get in the car now", true)]
    [InlineData("Get in the car", "Get in", false)]
    [InlineData("Get in the car", "Where is Lucia?", false)]
    [InlineData("", "Hello", false)]
    [InlineData("Hey", "Hey you", true)]
    public void IsGrowth(string a, string b, bool expected) => Assert.Equal(expected, TextUtil.IsGrowth(a, b));

    [Fact]
    public void AreRelated()
    {
        Assert.True(TextUtil.AreRelated("Get in the car", "Get in"));
        Assert.True(TextUtil.AreRelated("Get in the car", "Get in tne car"));
        Assert.False(TextUtil.AreRelated("Get in the car", "Where is Lucia?"));
    }
}

public class ParserTests
{
    [Fact]
    public void ParsesMultiSegment()
    {
        const string json = "[[[\"Merhaba, nasılsın? \",\"Hello, how are you? \",null,null,10],[\"Arabaya bin.\",\"Get in the car.\",null,null,10]],null,\"en\",null,null,null,null,[]]";
        Assert.Equal("Merhaba, nasılsın? Arabaya bin.", GoogleTranslateParser.Parse(json));
    }

    [Fact]
    public void ParsesEscapesAndUnicode()
    {
        const string json = "[[[\"\\\"Dur!\\\" dedi \\u00e7ocuk\",\"\\\"Stop!\\\" said the kid\",null,null,3]],null,\"en\"]";
        Assert.Equal("\"Dur!\" dedi çocuk", GoogleTranslateParser.Parse(json));
    }

    [Fact]
    public void SkipsNonStringSegments()
    {
        const string json = "[[[null,\"x\"],[\"Tamam\",\"OK\"],[],[5]],null,\"en\"]";
        Assert.Equal("Tamam", GoogleTranslateParser.Parse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("<html>429 Too Many Requests</html>")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[[]]")]
    [InlineData("[[[\"   \",\"x\"]]]")]
    [InlineData("[[[\"truncated")]
    public void BadPayloadsReturnNull(string? json) => Assert.Null(GoogleTranslateParser.Parse(json));

    [Fact]
    public void UrlEscapes()
    {
        var url = GoogleTranslateParser.BuildUrl("Hey & you? #1 çok");
        Assert.StartsWith("https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=tr&dt=t&q=", url);
        Assert.Contains("q=Hey%20%26%20you%3F%20%231%20%C3%A7ok", url);
    }
}

public class CacheTests
{
    [Fact]
    public void CacheByKeyAndEvictsLru()
    {
        var c = new TranslationCache(2);
        c.Set("Hello!", "Merhaba!");
        Assert.True(c.TryGet("hello", out var v));
        Assert.Equal("Merhaba!", v);
        c.Set("a b", "1");
        Assert.True(c.TryGet("HELLO", out _)); // touch → most recent
        c.Set("c d", "2");                      // evicts "a b"
        Assert.False(c.TryGet("a b", out _));
        Assert.True(c.TryGet("hello", out _));
        Assert.Equal(2, c.Count);
    }

    [Fact]
    public void OverwriteAndIgnoreEmptyKey()
    {
        var c = new TranslationCache(10);
        c.Set("Hi", "1");
        c.Set("hi!", "2");
        Assert.Equal(1, c.Count);
        Assert.True(c.TryGet("Hi", out var v));
        Assert.Equal("2", v);
        c.Set("...", "x");
        Assert.Equal(1, c.Count);
    }

    [Fact]
    public void ThreadSafe()
    {
        var c = new TranslationCache(100);
        Parallel.For(0, 20000, i =>
        {
            c.Set("line " + (i % 300), "t" + i);
            c.TryGet("line " + ((i * 7) % 300), out _);
        });
        Assert.Equal(100, c.Count);
    }
}

public class SettingsTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "altyazi_test_" + Guid.NewGuid() + ".json");

    [Fact]
    public void RoundTripAndClamp()
    {
        var p = Tmp();
        try
        {
            var s = new AppSettings { RegionX = 10, RegionY = 900, RegionWidth = 1200, RegionHeight = 120, Fps = 99, OverlayKonumu = OverlayPlacement.Alt };
            s.Save(p);
            var l = AppSettings.Load(p);
            Assert.Equal(30, l.Fps);
            Assert.Equal(OverlayPlacement.Alt, l.OverlayKonumu);
            Assert.True(l.HasRegion);
            Assert.Equal(AppSettings.CurrentVersion, l.Surum);
            Assert.Contains("\"Alt\"", File.ReadAllText(p));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void DefaultsAre15Fps() => Assert.Equal(15, AppSettings.Load(Tmp()).Fps);

    [Fact]
    public void V1FileMigratesTo15Fps()
    {
        var p = Tmp();
        try
        {
            File.WriteAllText(p, "{\"RegionX\":0,\"RegionY\":800,\"RegionWidth\":1000,\"RegionHeight\":100,\"Fps\":6,\"SabitKare\":2,\"OverlayKonumu\":\"Ust\"}");
            var l = AppSettings.Load(p);
            Assert.Equal(15, l.Fps);
            Assert.True(l.HasRegion);
        }
        finally { File.Delete(p); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("{\"Fps\":\"fast\"}")]
    public void CorruptFileGivesDefaults(string content)
    {
        var p = Tmp();
        try
        {
            File.WriteAllText(p, content);
            var l = AppSettings.Load(p);
            Assert.Equal(15, l.Fps);
            Assert.False(l.HasRegion);
        }
        finally { File.Delete(p); }
    }
}

public class ErrorLogTests
{
    [Fact]
    public void WritesCapsAndNeverThrows()
    {
        var p = Path.Combine(Path.GetTempPath(), "altyazi_err_" + Guid.NewGuid() + ".log");
        var old = ErrorLog.Path;
        try
        {
            ErrorLog.Path = p;
            ErrorLog.MaxBytes = 2000;
            for (int i = 0; i < 200; i++) ErrorLog.Write(new InvalidOperationException("err " + i), "test");
            Assert.True(File.Exists(p));
            Assert.True(new FileInfo(p).Length < 4000);
            Assert.Contains("InvalidOperationException", File.ReadAllText(p));
            // duplicate suppression
            ErrorLog.Write(new IOException("same"), "dup");
            var len = new FileInfo(p).Length;
            ErrorLog.Write(new IOException("same"), "dup");
            Assert.Equal(len, new FileInfo(p).Length);
            // invalid path must not throw
            ErrorLog.Path = "/nonexistent_dir_xyz/a/b/c.log";
            ErrorLog.Write(new Exception("x"), "bad");
        }
        finally
        {
            ErrorLog.Path = old;
            ErrorLog.MaxBytes = 256 * 1024;
            File.Delete(p); File.Delete(p + ".1");
        }
    }
}
