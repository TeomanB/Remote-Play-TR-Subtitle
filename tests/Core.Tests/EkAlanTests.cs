using AltyaziCevirici.Core;
using Xunit;

public class EkAlanTests
{
    [Fact]
    public void ExistingSettingsStayTheMainStrip()
    {
        var s = new AppSettings { RegionX = 10, RegionY = 20, RegionWidth = 400, RegionHeight = 80, KutuGenislik = 500 };
        s.Clamp();
        Assert.Empty(s.EkAlanlar!);
        Assert.Equal(400, s.RegionWidth);
        Assert.Equal(500, s.KutuGenislik);
    }

    [Fact]
    public void AtMostThreeExtrasAndBadColorIsReplaced()
    {
        var s = new AppSettings
        {
            EkAlanlar = new List<EkAlan>
            {
                new() { Id = "a", Ad = "İpucu", Renk = "nope", RegionWidth = 100, RegionHeight = 40 },
                new() { Id = "a", Ad = "Diğer" },
                new() { Id = "c", Ad = new string('x', 40) },
                new() { Id = "d", Ad = "fazla" },
            },
        };
        s.Clamp();
        Assert.Equal(3, s.EkAlanlar!.Count);
        Assert.Equal(EkAlan.Palet[0], s.EkAlanlar[0].Renk);
        Assert.Equal("ek2", s.EkAlanlar[1].Id);
        Assert.Equal(24, s.EkAlanlar[2].Ad.Length);
    }

    [Fact]
    public void UnionCoversBothCornersAndSkipsEmpty()
    {
        var u = KareBirlesim.Birles(new EkranDikdortgen[]
        {
            new(10, 20, 30, 40),
            new(0, 0, 0, 10),
            new(100, 200, 5, 6),
        });
        Assert.Equal(new EkranDikdortgen(10, 20, 95, 186), u);
    }
}
