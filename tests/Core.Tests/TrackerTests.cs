using AltyaziCevirici.Core;
using Xunit;

public class TrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime F(int frame) => T0.AddMilliseconds(frame * 66);

    [Fact]
    public void TranslatesOnFirstSight()
    {
        var t = new SubtitleTracker();
        var r = t.Feed("Get in the car.", F(0));
        Assert.Equal(TrackAction.Translate, r.Action);
        Assert.Equal("Get in the car.", r.Text);
        Assert.False(r.IsUpdate);
    }

    [Fact]
    public void IdenticalRepeatsAreDebounced()
    {
        var t = new SubtitleTracker();
        t.Feed("Get in the car.", F(0));
        for (int i = 1; i < 30; i++) Assert.Equal(TrackAction.Keep, t.Feed("Get in the car", F(i)).Action);
    }

    [Fact]
    public void GrowingPartialLineRetranslatesInPlace()
    {
        var t = new SubtitleTracker();
        Assert.Equal(TrackAction.Translate, t.Feed("Get in", F(0)).Action);
        var r = t.Feed("Get in the car", F(1));
        Assert.Equal(TrackAction.Translate, r.Action);
        Assert.True(r.IsUpdate);
        r = t.Feed("Get in the car, we're late!", F(2));
        Assert.Equal(TrackAction.Translate, r.Action);
        Assert.True(r.IsUpdate);
        Assert.Equal("Get in the car, we're late!", t.Current);
    }

    [Fact]
    public void PartialRegressionKeepsFullerLine()
    {
        var t = new SubtitleTracker();
        t.Feed("Get in the car, we're late!", F(0));
        Assert.Equal(TrackAction.Keep, t.Feed("Get in the car", F(1)).Action);
        Assert.Equal("Get in the car, we're late!", t.Current);
    }

    [Fact]
    public void JitterPingPongDoesNotRetranslate()
    {
        var t = new SubtitleTracker();
        t.Feed("Where have you been all night?", F(0));
        for (int i = 1; i < 40; i++)
        {
            var reading = i % 2 == 0 ? "Where have you been all night?" : "Wnere have you been all niqht?";
            Assert.Equal(TrackAction.Keep, t.Feed(reading, F(i)).Action);
        }
    }

    [Fact]
    public void ConsistentCorrectionAcceptedOnSecondRead()
    {
        var t = new SubtitleTracker();
        t.Feed("Wnere have you been all niqht?", F(0));
        Assert.Equal(TrackAction.Keep, t.Feed("Where have you been all night?", F(1)).Action);
        var r = t.Feed("Where have you been all night?", F(2));
        Assert.Equal(TrackAction.Translate, r.Action);
        Assert.True(r.IsUpdate);
    }

    [Fact]
    public void DifferentLineTranslatesImmediately()
    {
        var t = new SubtitleTracker();
        t.Feed("Get in the car.", F(0));
        var r = t.Feed("Where is Lucia?", F(1));
        Assert.Equal(TrackAction.Translate, r.Action);
        Assert.False(r.IsUpdate);
    }

    [Fact]
    public void EmptyFramesAutoHideAfterTimeout()
    {
        var t = new SubtitleTracker(hideAfter: TimeSpan.FromSeconds(2));
        t.Feed("Hello there", F(0));
        for (int i = 1; i < 30; i++) Assert.Equal(TrackAction.None, t.Feed("", F(i)).Action); // < 2 s
        Assert.Equal(TrackAction.Hide, t.Feed("", F(31)).Action);                             // 2.05 s
        Assert.Equal(TrackAction.None, t.Feed("", F(32)).Action);                             // hide only once
        Assert.Null(t.Current);
        Assert.Equal(TrackAction.Translate, t.Feed("Hello there", F(40)).Action);             // reappears → shows again
    }

    [Fact]
    public void BriefOcrDropoutDoesNotHide()
    {
        var t = new SubtitleTracker();
        t.Feed("Hello there", F(0));
        Assert.Equal(TrackAction.None, t.Feed("", F(1)).Action);
        Assert.Equal(TrackAction.None, t.Feed("|.", F(2)).Action);
        Assert.Equal(TrackAction.Keep, t.Feed("Hello there", F(3)).Action);
    }

    [Fact]
    public void NoiseNeverTranslates()
    {
        var t = new SubtitleTracker();
        foreach (var n in new[] { "", "|", "#@! 12%%", "lI", "1234", "  " })
            Assert.Equal(TrackAction.None, t.Feed(n, F(0)).Action);
    }

    [Fact]
    public void ResetForgetsLine()
    {
        var t = new SubtitleTracker();
        t.Feed("Hello there", F(0));
        t.Reset();
        Assert.Equal(TrackAction.Translate, t.Feed("Hello there", F(1)).Action);
    }
}
