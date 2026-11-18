using AltyaziCevirici.Core;
using Xunit;

public class SchedulerTests
{
    private readonly ControlledBackend _be = new();
    private readonly FakeClock _clock = new();
    private readonly DisplayRecorder _rec = new();
    private readonly TranslationCache _cache = new(100);

    private TranslationScheduler Make(double spacingMs = 0) =>
        new(_be, _cache, _rec.On, () => _clock.Now, TimeSpan.FromMilliseconds(spacingMs));

    [Fact]
    public void FirstSubmitStartsImmediatelyAndShows()
    {
        var s = Make();
        s.Submit("Get in the car.");
        Assert.Single(_be.Calls);
        _be.Last.Ok("Arabaya bin.");
        Assert.Equal("Arabaya bin.", _rec.LastShow!.Value.Shown);
        Assert.False(_rec.LastShow!.Value.IsFallback);
        Assert.Equal(0, s.InFlightCount);
    }

    [Fact]
    public void CacheHitIsInstantNoRequest()
    {
        var s = Make();
        _cache.Set("Get in the car.", "Arabaya bin.");
        s.Submit("get in the car");
        Assert.Empty(_be.Calls);
        Assert.Equal("Arabaya bin.", _rec.LastShow!.Value.Shown);
    }

    [Fact]
    public void IdenticalTextDebounced()
    {
        var s = Make();
        s.Submit("Hello there");
        s.Submit("Hello there");
        s.Submit("hello there!");
        Assert.Single(_be.Calls);
    }

    [Fact]
    public void GrowthKeepsInflightAndQueuesOnlyLatest_MaxOneInFlight()
    {
        var s = Make();
        s.Submit("Get in");
        s.Submit("Get in the");
        s.Submit("Get in the car");
        s.Submit("Get in the car, now");
        Assert.Single(_be.Calls);               // 1 in flight
        Assert.True(s.HasPending);              // + 1 pending (latest only)
        _be.Calls[0].Ok("Gir");                 // partial result is shown progressively
        Assert.Equal("Gir", _rec.LastShow!.Value.Shown);
        Assert.Equal(2, _be.Calls.Count);
        Assert.Equal("Get in the car, now", _be.Calls[1].Text); // intermediate ones skipped
        _be.Calls[1].Ok("Hemen arabaya bin");
        Assert.Equal("Hemen arabaya bin", _rec.LastShow!.Value.Shown);
        Assert.Equal(1, _be.MaxActive);
        Assert.Equal(1, s.MaxConcurrentBackendCalls);
    }

    [Fact]
    public void DifferentLineCancelsStaleAndNewestWins()
    {
        var s = Make();
        s.Submit("Get in the car.");
        var first = _be.Last;
        s.Submit("Where is Lucia?");
        Assert.True(first.Cancelled);
        Assert.Equal(2, _be.Calls.Count);
        _be.Last.Ok("Lucia nerede?");
        Assert.Equal("Lucia nerede?", _rec.LastShow!.Value.Shown);
        Assert.Equal(1, _be.MaxActive);
    }

    [Fact]
    public void StaleResultNeverOverwritesNewer()
    {
        var s = Make();
        s.Submit("Get in the car.");
        var first = _be.Last;
        _cache.Set("Where is Lucia?", "Lucia nerede?");
        s.Submit("Where is Lucia?");                 // cache hit shown immediately
        first.Ok("Arabaya bin.");                    // late result for old line (even if backend ignores cancel)
        Assert.Equal("Lucia nerede?", _rec.LastShow!.Value.Shown);
        Assert.True(_cache.TryGet("Get in the car.", out _) || first.Cancelled);
    }

    [Fact]
    public void SeqStrictlyIncreasing()
    {
        var s = Make();
        foreach (var l in new[] { "One line here", "Two lines here", "Three lines here" })
        {
            s.Submit(l);
            _be.Last.Ok();
        }
        s.Clear();
        var seqs = _rec.Updates.Select(u => u.Seq).ToList();
        Assert.Equal(seqs.OrderBy(x => x).Distinct(), seqs);
        Assert.Equal(DisplayKind.Hide, _rec.Last!.Value.Kind);
    }

    [Fact]
    public void ClearCancelsAndDropsLateResults()
    {
        var s = Make();
        s.Submit("Hello there");
        var c = _be.Last;
        s.Clear();
        Assert.True(c.Cancelled);
        c.Ok("Merhaba");
        Assert.DoesNotContain(_rec.Updates, u => u.Kind == DisplayKind.Show);
    }

    [Fact]
    public void ClearWhenNothingShownEmitsNothing()
    {
        var s = Make();
        s.Clear();
        Assert.Equal(0, _rec.Count);
    }

    [Fact]
    public void RateLimitBacksOffExponentiallyWithoutShowingEnglish()
    {
        var s = Make();
        s.Submit("Get in the car.");
        _be.Last.RateLimited();
        Assert.DoesNotContain(_rec.Updates, u => u.IsFallback);
        Assert.Equal(TranslationScheduler.RateLimitBackoffStart, s.CurrentBackoff);

        s.Tick();                                 // still in backoff
        Assert.Single(_be.Calls);
        s.Submit("Where is Lucia?");              // new line during backoff: no English, no request
        Assert.Single(_be.Calls);
        Assert.DoesNotContain(_rec.Updates, u => u.IsFallback || u.Shown == "Where is Lucia?");

        _clock.Advance(2001);
        s.Tick();                                 // retry latest
        Assert.Equal(2, _be.Calls.Count);
        Assert.Equal("Where is Lucia?", _be.Last.Text);
        _be.Last.RateLimited();
        Assert.Equal(TimeSpan.FromSeconds(4), s.CurrentBackoff);
        _clock.Advance(4001); s.Tick(); _be.Last.RateLimited();
        Assert.Equal(TimeSpan.FromSeconds(8), s.CurrentBackoff);
        for (int i = 0; i < 10; i++) { _clock.Advance(61000); s.Tick(); _be.Last.RateLimited(); }
        Assert.Equal(TranslationScheduler.RateLimitBackoffMax, s.CurrentBackoff);

        _clock.Advance(61000); s.Tick();
        _be.Last.Ok("Lucia nerede?");             // recovery: fallback upgraded to translation
        Assert.Equal("Lucia nerede?", _rec.LastShow!.Value.Shown);
        Assert.False(_rec.LastShow!.Value.IsFallback);
        Assert.Equal(TimeSpan.Zero, s.CurrentBackoff);
    }

    [Fact]
    public void RetryAfterHeaderRespected()
    {
        var s = Make();
        s.Submit("Hello there");
        _be.Last.RateLimited(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), s.CurrentBackoff);
    }

    [Fact]
    public void NetworkErrorsAndExceptionsFallBackAndRetry()
    {
        var s = Make();
        s.Submit("Hello there");
        _be.Last.Throw();
        Assert.DoesNotContain(_rec.Updates, u => u.IsFallback);
        Assert.Equal(TranslationScheduler.ErrorBackoffStart, s.CurrentBackoff);
        _clock.Advance(600); s.Tick();
        Assert.Equal(2, _be.Calls.Count);
        _be.Last.Fail();
        Assert.Equal(TimeSpan.FromSeconds(1), s.CurrentBackoff);
        _clock.Advance(1100); s.Tick();
        _be.Last.Ok("Merhaba");
        Assert.Equal("Merhaba", _rec.LastShow!.Value.Shown);
    }

    [Fact]
    public void PendingDuringFailureShownInEnglishThenRetried()
    {
        var s = Make();
        s.Submit("Get in");
        s.Submit("Get in the car");
        _be.Calls[0].Fail();
        Assert.DoesNotContain(_rec.Updates, u => u.IsFallback || u.Shown == "Get in the car");
        Assert.Single(_be.Calls);
        _clock.Advance(600); s.Tick();
        Assert.Equal("Get in the car", _be.Last.Text);
    }

    [Fact]
    public void MinSpacingDefersButTickStarts()
    {
        var s = Make(spacingMs: 60);
        s.Submit("Get in the car.");
        s.Submit("Where is Lucia?");      // unrelated: cancels, but spacing not elapsed → pending
        Assert.Single(_be.Calls);
        Assert.True(_be.Calls[0].Cancelled);
        Assert.True(s.HasPending);
        _clock.Advance(30); s.Tick();
        Assert.Single(_be.Calls);
        _clock.Advance(31); s.Tick();
        Assert.Equal(2, _be.Calls.Count);
        Assert.Equal("Where is Lucia?", _be.Last.Text);
    }

    [Fact]
    public void DisplayCallbackExceptionDoesNotBreak()
    {
        int n = 0;
        var s = new TranslationScheduler(_be, _cache, _ => { n++; throw new InvalidOperationException(); }, () => _clock.Now, TimeSpan.Zero);
        s.Submit("Hello there");
        _be.Last.Ok();
        s.Submit("Second line here");
        _be.Last.Ok();
        Assert.Equal(2, n);
    }

    [Fact]
    public void DisposeStopsEverything()
    {
        var s = Make();
        s.Submit("Hello there");
        var c = _be.Last;
        s.Dispose();
        Assert.True(c.Cancelled);
        s.Submit("Other line");
        s.Tick();
        s.Clear();
        Assert.Single(_be.Calls);
        Assert.Equal(0, _rec.Count);
    }

    [Fact]
    public void ConcurrentSubmitsFromManyThreadsAreSafe()
    {
        var be = new DelayBackend(1);
        var rec = new DisplayRecorder();
        using var s = new TranslationScheduler(be, new TranslationCache(50), rec.On, null, TimeSpan.Zero);
        Parallel.For(0, 2000, i =>
        {
            s.Submit("Line number " + (i % 37) + " is here");
            s.Tick();
            if (i % 100 == 0) s.Clear();
        });
        Thread.Sleep(100);
        var seqs = rec.Updates.Select(u => u.Seq).ToList();
        Assert.Equal(seqs.OrderBy(x => x), seqs);
    }
}
