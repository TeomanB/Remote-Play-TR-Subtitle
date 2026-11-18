using AltyaziCevirici.Core;
using Xunit;

public class YakalamaAlaniTests
{
    [Fact]
    public void LegacySettingsBecomeOneKonusmaArea()
    {
        var s = new AppSettings
        {
            RegionX = 10, RegionY = 20, RegionWidth = 400, RegionHeight = 80,
            KonumAyarlandi = true, KutuX = 30, KutuY = 40, KutuGenislik = 500, KutuYukseklik = 70,
        };
        s.Clamp();
        Assert.Single(s.Alanlar!);
        Assert.Equal("Konuşma", s.Alanlar![0].Ad);
        Assert.Equal(10, s.Alanlar[0].RegionX);
        Assert.Equal(30, s.KutuX);
        Assert.Equal(70, s.KutuYukseklik);
    }

    [Fact]
    public void ExtraAreasStayAndTheFirstStillMirrorsLegacyFields()
    {
        var s = new AppSettings
        {
            Alanlar = new()
            {
                new YakalamaAlani { Id = "konusma", Ad = "Konuşma", RegionX = 5, RegionY = 6, RegionWidth = 200, RegionHeight = 40, KutuGenislik = 300, KutuYukseklik = 60, KonumAyarlandi = true },
                new YakalamaAlani { Id = "gorev", Ad = "Görev", Renk = YakalamaAlani.Palet[1], RegionX = 8, RegionY = 9, RegionWidth = 180, RegionHeight = 36 },
            },
        };
        s.Clamp();
        Assert.Equal(2, s.Alanlar!.Count);
        Assert.Equal(5, s.RegionX);
        Assert.Equal(300, s.KutuGenislik);
    }
}
