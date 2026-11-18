using AltyaziCevirici.Core;
using Xunit;

/// <summary>
/// Verification for the 1.4 bug: the owner could not resize either subtitle box.
/// Every handle must change the matching edge, the body must move the box, and size cannot collapse.
/// </summary>
public class BoxEditTests
{
    private static readonly EkranDikdortgen Start = new(100, 200, 400, 80);

    [Theory]
    [InlineData(BoxPart.E, 30, 0, 100, 200, 430, 80)]
    [InlineData(BoxPart.W, -20, 0, 80, 200, 420, 80)]
    [InlineData(BoxPart.S, 0, 15, 100, 200, 400, 95)]
    [InlineData(BoxPart.N, 0, -10, 100, 190, 400, 90)]
    [InlineData(BoxPart.Se, 12, 8, 100, 200, 412, 88)]
    [InlineData(BoxPart.Sw, -12, 8, 88, 200, 412, 88)]
    [InlineData(BoxPart.Ne, 12, -8, 100, 192, 412, 88)]
    [InlineData(BoxPart.Nw, -12, -8, 88, 192, 412, 88)]
    [InlineData(BoxPart.Body, 25, -40, 125, 160, 400, 80)]
    public void AllEightHandlesAndBodyResize(BoxPart part, int dx, int dy, int x, int y, int w, int h)
    {
        var next = BoxEdit.Drag(Start, part, dx, dy);
        Assert.Equal(new EkranDikdortgen(x, y, w, h), next);
    }

    [Fact]
    public void HandlesAreHitBeforeTheBodyAndMinSizeHolds()
    {
        Assert.Equal(BoxPart.Nw, BoxEdit.Hit(Start, Start.X, Start.Y, 8));
        Assert.Equal(BoxPart.E, BoxEdit.Hit(Start, Start.Right, Start.Y + Start.Height / 2, 8));
        Assert.Equal(BoxPart.S, BoxEdit.Hit(Start, Start.X + 40, Start.Bottom, 8));
        Assert.Equal(BoxPart.Body, BoxEdit.Hit(Start, Start.X + 40, Start.Y + 40, 8));
        Assert.Equal(BoxPart.None, BoxEdit.Hit(Start, 0, 0, 8));

        var shrunk = BoxEdit.Drag(Start, BoxPart.W, 500, 0);
        Assert.Equal(BoxEdit.MinSize, shrunk.Width);
        Assert.True(shrunk.Right <= Start.Right);
    }

    [Fact]
    public void SnapIsOptional()
    {
        var screen = new EkranDikdortgen(0, 0, 1920, 1080);
        var near = new EkranDikdortgen(4, 100, 400, 80);
        Assert.Equal(near, BoxEdit.Snap(near, screen, 8, enabled: false));
        Assert.Equal(0, BoxEdit.Snap(near, screen, 8, enabled: true).X);
    }
}
