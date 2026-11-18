using AltyaziCevirici.Core;
using Xunit;

public class ImageOpsTests
{
    private static byte[] Image(int w, int h, int stride, (byte b, byte g, byte r) bg)
    {
        var a = new byte[stride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { int p = y * stride + x * 4; a[p] = bg.b; a[p + 1] = bg.g; a[p + 2] = bg.r; a[p + 3] = 255; }
        return a;
    }

    private static void Fill(byte[] a, int stride, int x0, int y0, int x1, int y1, (byte b, byte g, byte r) c)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) { int p = y * stride + x * 4; a[p] = c.b; a[p + 1] = c.g; a[p + 2] = c.r; }
    }

    [Fact]
    public void FindsWhiteTextBoxWithPaddingAndStride()
    {
        int w = 300, h = 80, stride = w * 4 + 16; // padded stride
        var img = Image(w, h, stride, (40, 60, 30));
        Fill(img, stride, 100, 30, 199, 49, (250, 250, 250));
        Assert.True(ImageOps.TryFindTextBox(img, w, h, stride, 170, out var box, pad: 8));
        Assert.Equal(new PixelRect(92, 22, 116, 36), box);
    }

    [Fact]
    public void NoTextNoBox()
    {
        int w = 200, h = 50, stride = w * 4;
        var img = Image(w, h, stride, (20, 20, 20));
        Assert.False(ImageOps.TryFindTextBox(img, w, h, stride, 170, out _));
        // bright but saturated (e.g. orange sky) is not "white text"
        Fill(img, stride, 0, 0, 199, 49, (30, 160, 255));
        Assert.False(ImageOps.TryFindTextBox(img, w, h, stride, 170, out _));
        // a few stray bright pixels below minPixels
        Fill(img, stride, 5, 5, 7, 7, (255, 255, 255));
        Assert.False(ImageOps.TryFindTextBox(img, w, h, stride, 170, out _, minPixels: 12));
    }

    [Fact]
    public void BoxClampedToImage()
    {
        int w = 50, h = 20, stride = w * 4;
        var img = Image(w, h, stride, (0, 0, 0));
        Fill(img, stride, 0, 0, 49, 19, (255, 255, 255));
        Assert.True(ImageOps.TryFindTextBox(img, w, h, stride, 170, out var box));
        Assert.Equal(new PixelRect(0, 0, 50, 20), box);
    }

    [Fact]
    public void ThresholdWhiteMakesBlackTextOnWhite()
    {
        int w = 4, h = 1, stride = 16;
        var img = Image(w, h, stride, (0, 0, 0));
        Fill(img, stride, 1, 0, 1, 0, (255, 255, 255));
        Fill(img, stride, 2, 0, 2, 0, (255, 255, 100)); // not white
        var g = new byte[4];
        ImageOps.ThresholdWhite(img, w, h, stride, g, 170);
        Assert.Equal(new byte[] { 255, 0, 255, 255 }, g);
    }

    [Fact]
    public void GrayStretchUsesFullRange()
    {
        int w = 3, h = 1, stride = 12;
        var img = Image(w, h, stride, (100, 100, 100));
        Fill(img, stride, 2, 0, 2, 0, (200, 200, 200));
        var g = new byte[3];
        ImageOps.GrayStretch(img, w, h, stride, g);
        Assert.Equal(0, g[0]);
        Assert.Equal(255, g[2]);
    }

    [Fact]
    public void SampleAndDiffDetectChange()
    {
        int w = 640, h = 100, stride = w * 4;
        var a = Image(w, h, stride, (30, 30, 30));
        var b = Image(w, h, stride, (30, 30, 30));
        var sa = new byte[160 * 32]; var sb = new byte[160 * 32];
        ImageOps.Sample(a, w, h, stride, sa, 160, 32);
        ImageOps.Sample(b, w, h, stride, sb, 160, 32);
        Assert.Equal(0, ImageOps.MeanAbsDiff(sa, sb));
        Fill(b, stride, 100, 30, 500, 60, (255, 255, 255)); // a subtitle appears
        ImageOps.Sample(b, w, h, stride, sb, 160, 32);
        Assert.True(ImageOps.MeanAbsDiff(sa, sb) > 5);
        Assert.Equal(double.MaxValue, ImageOps.MeanAbsDiff(sa, new byte[3]));
    }
}
