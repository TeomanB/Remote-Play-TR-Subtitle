using AltyaziCevirici.Core;
using Xunit;

public class SubtitleLayoutTests
{
    private static readonly EkranDikdortgen Screen = new(0, 0, 1920, 1080);
    private static readonly EkranDikdortgen Region = new(400, 800, 1000, 120);

    [Fact]
    public void LegacyStaysAboveTheRegion()
    {
        var box = SubtitleLayout.Legacy(OverlayPlacement.Ust, Region, Screen, 600, 80, 0);
        Assert.Equal(Region.X + (Region.Width - 600) / 2, box.X);
        Assert.Equal(Region.Y - 80 - 6, box.Y);
    }

    [Fact]
    public void OldSettingsFileKeepsLegacyAnchor()
    {
        var s = new AppSettings { OverlayKonumu = OverlayPlacement.Alt, KonumAyarlandi = false };
        var box = SubtitleLayout.Resolve(s, Region, Screen, 600, 80);
        Assert.Equal(Region.Bottom + 6, box.Y);
    }

    [Fact]
    public void SnapCatchesCenterAndEdges()
    {
        var almostCenter = new EkranDikdortgen(660, 500, 600, 80); // center x is (1920-600)/2 = 660
        var snapped = SubtitleLayout.Snap(almostCenter, Screen);
        Assert.Equal(660, snapped.X);

        var nearRight = new EkranDikdortgen(1920 - 600 - 10, 100, 600, 80);
        Assert.Equal(1920 - 600, SubtitleLayout.Snap(nearRight, Screen).X);

        var nearBottom = new EkranDikdortgen(100, 1080 - 80 - 8, 600, 80);
        Assert.Equal(1080 - 80, SubtitleLayout.Snap(nearBottom, Screen).Y);
    }

    [Fact]
    public void PresetsUseTheChosenMonitor()
    {
        var second = new EkranDikdortgen(1920, 0, 1280, 800);
        var bottom = SubtitleLayout.Preset(AltyaziOnAyar.AltOrta, second, Region, 640, 80);
        Assert.Equal(1920 + (1280 - 640) / 2, bottom.X);
        Assert.Equal(800 - 80 - 40, bottom.Y);

        var top = SubtitleLayout.Preset(AltyaziOnAyar.UstOrta, second, Region, 640, 80);
        Assert.Equal(40, top.Y);
        Assert.True(top.X >= second.X);
    }

    [Fact]
    public void FollowModeKeepsTheOffsetWhenTheRegionMoves()
    {
        var s = new AppSettings();
        SubtitleLayout.ApplyPreset(s, AltyaziOnAyar.BolgeninUstu, Region, Screen, 80, follow: true);
        Assert.True(s.BolgeyiTakipEt);
        var first = SubtitleLayout.Resolve(s, Region, Screen, s.KutuGenislik, 80);
        var moved = new EkranDikdortgen(Region.X + 50, Region.Y + 30, Region.Width, Region.Height);
        var second = SubtitleLayout.Resolve(s, moved, Screen, s.KutuGenislik, 80);
        Assert.Equal(first.X + 50, second.X);
        Assert.Equal(first.Y + 30, second.Y);
    }

    [Fact]
    public void FreeDragDoesNotFollowUntilAsked()
    {
        var s = new AppSettings();
        var box = new EkranDikdortgen(100, 200, 700, 90);
        SubtitleLayout.Remember(s, box, Region, follow: false);
        var moved = new EkranDikdortgen(900, 100, Region.Width, Region.Height);
        var resolved = SubtitleLayout.Resolve(s, moved, Screen, 700, 90);
        Assert.Equal(100, resolved.X);
        Assert.Equal(200, resolved.Y);
        Assert.Equal(700, resolved.Width);
    }

    [Fact]
    public void RegionPresetsSitAgainstTheCaptureStrip()
    {
        var above = SubtitleLayout.Preset(AltyaziOnAyar.BolgeninUstu, Screen, Region, 800, 70);
        Assert.Equal(Region.Y - 70 - 6, above.Y);
        var below = SubtitleLayout.Preset(AltyaziOnAyar.BolgeninAlti, Screen, Region, 800, 70);
        Assert.Equal(Region.Bottom + 6, below.Y);
    }
}
