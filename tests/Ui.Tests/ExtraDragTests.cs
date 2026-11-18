using AltyaziCevirici.Ui;
using Xunit;

public class ExtraDragTests
{
    [Fact]
    public void MouseDragMovesExtraCaptureAndOverlay()
    {
        using var stage = new PreviewStage { Size = new Size(640, 360) };
        stage.SetSource(new Rectangle(0, 0, 1000, 800));
        var extra = new PreviewExtra
        {
            Id = "ipucu",
            Name = "İpucu",
            ColorHex = "#E8456B",
            Enabled = true,
            Capture = new Rectangle(30, 32, 260, 48),
            Overlay = new Rectangle(30, 128, 420, 96),
        };
        stage.SetExtras(new[] { extra });

        var capture = stage.Box(PreviewStage.ExtraCapture);
        Assert.True(stage.BeginDrag(PreviewStage.ExtraCapture, -1));
        stage.ContinueDrag(40, 25);
        var movedCapture = stage.Box(PreviewStage.ExtraCapture);
        Assert.Equal(capture.X + 40, movedCapture.X);
        Assert.Equal(capture.Y + 25, movedCapture.Y);
        Assert.Equal(capture.Size, movedCapture.Size);

        var overlay = stage.Box(PreviewStage.ExtraOverlay);
        Assert.True(stage.BeginDrag(PreviewStage.ExtraOverlay, -1));
        stage.ContinueDrag(-20, 30);
        var movedOverlay = stage.Box(PreviewStage.ExtraOverlay);
        Assert.Equal(overlay.X - 20, movedOverlay.X);
        Assert.Equal(overlay.Y + 30, movedOverlay.Y);
        Assert.Equal(stage.Box(PreviewStage.ExtraCapture), movedCapture);
    }
}
