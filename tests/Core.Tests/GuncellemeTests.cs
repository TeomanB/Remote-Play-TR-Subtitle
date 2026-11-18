using AltyaziCevirici.Core;
using Xunit;

public class GuncellemeTests
{
    [Theory]
    [InlineData("v1.3.0", "1.2.0", true)]
    [InlineData("v1.3.0", "1.3.0", false)]
    [InlineData("V1.3.1", "1.3.0", true)]
    [InlineData("v1.10.0", "1.9.0", true)]
    [InlineData("v1.2.9", "1.3.0", false)]
    [InlineData("1.4", "1.3.0", true)]
    [InlineData("latest", "1.3.0", false)]
    [InlineData("", "1.3.0", false)]
    [InlineData("v1.3.0-beta", "1.2.0", false)]
    public void ComparesReleaseTags(string tag, string current, bool newer)
    {
        Assert.Equal(newer, SurumKarsilastirma.IsNewer(tag, Version.Parse(current)));
    }

    [Fact]
    public void ReadsSetupAssetAndIgnoresTheZip()
    {
        const string json = """
            {
              "tag_name": "v1.4.0",
              "html_url": "https://github.com/TeomanB/Remote-Play-TR-Subtitle/releases/tag/v1.4.0",
              "assets": [
                {"name": "AltyaziCevirici-Portable-1.4.0.zip", "browser_download_url": "https://github.com/TeomanB/Remote-Play-TR-Subtitle/releases/download/v1.4.0/portable.zip"},
                {"name": "AltyaziCevirici-Setup-1.4.0.exe", "browser_download_url": "https://github.com/TeomanB/Remote-Play-TR-Subtitle/releases/download/v1.4.0/AltyaziCevirici-Setup-1.4.0.exe"}
              ]
            }
            """;
        var info = GuncellemeOkuyucu.Oku(json);
        Assert.NotNull(info);
        Assert.Equal("v1.4.0", info.Tag);
        Assert.Equal("AltyaziCevirici-Setup-1.4.0.exe", info.KurulumAdi);
        Assert.EndsWith("AltyaziCevirici-Setup-1.4.0.exe", info.KurulumUrl);
        Assert.True(GuncellemeOkuyucu.GuvenliKurulumAdresi(info.KurulumUrl));
        Assert.True(GuncellemeOkuyucu.BannerGoster(new Version(1, 3, 0), info, ""));
        Assert.False(GuncellemeOkuyucu.BannerGoster(new Version(1, 3, 0), info, "v1.4.0"));
        Assert.False(GuncellemeOkuyucu.BannerGoster(new Version(1, 4, 0), info, ""));
    }

    [Fact]
    public void RejectsUnsafeInstallerUrlsAndBrokenJson()
    {
        Assert.False(GuncellemeOkuyucu.GuvenliKurulumAdresi("http://github.com/x.exe"));
        Assert.False(GuncellemeOkuyucu.GuvenliKurulumAdresi("https://example.com/AltyaziCevirici-Setup-1.4.0.exe"));
        Assert.Null(GuncellemeOkuyucu.Oku("not json"));
        Assert.Null(GuncellemeOkuyucu.Oku("{\"tag_name\":\"nope\"}"));
        var pageOnly = GuncellemeOkuyucu.Oku("{\"tag_name\":\"v2.0.0\",\"html_url\":\"https://github.com/TeomanB/Remote-Play-TR-Subtitle/releases/tag/v2.0.0\",\"assets\":[]}");
        Assert.NotNull(pageOnly);
        Assert.Null(pageOnly.KurulumUrl);
    }
}
