using System.Diagnostics;
using AltyaziCevirici.Core;
using Xunit;
using Xunit.Abstractions;

public class SoakTests(ITestOutputHelper output)
{
    private sealed class RandomBackend(int seed) : ITranslateBackend
    {
        private readonly Random _rnd = new(seed);
        public int Calls;
        public Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct)
        {
            Calls++;
            double r = _rnd.NextDouble();
            if (r < 0.01) return Task.FromResult(new TranslateOutcome(TranslateStatus.RateLimited));
            if (r < 0.02) return Task.FromResult(new TranslateOutcome(TranslateStatus.Failed));
            if (r < 0.025) throw new HttpRequestException("socket");
            if (r < 0.03) return Task.FromException<TranslateOutcome>(new TaskCanceledException("timeout"));
            if (r < 0.10) return new TaskCompletionSource<TranslateOutcome>().Task; // hangs until cancelled... never completes
            return Task.FromResult(new TranslateOutcome(TranslateStatus.Ok, "TR " + text));
        }
    }

    [Fact]
    public void HundredThousandFramesNoExceptionsNoGrowth()
    {
        var rnd = new Random(7);
        var clock = new FakeClock();
        var backend = new RandomBackend(9);
        long shows = 0, hides = 0, lastSeq = 0;
        bool orderOk = true;
        var cache = new TranslationCache(500);
        using var pipeline = new SubtitlePipeline(
            new SubtitleTracker(0.85, TimeSpan.FromSeconds(2)),
            new TranslationScheduler(backend, cache, u =>
            {
                if (u.Seq <= lastSeq) orderOk = false;
                lastSeq = u.Seq;
                if (u.Kind == DisplayKind.Show) shows++; else hides++;
            }, () => clock.Now));

        string RandomLine()
        {
            int words = 2 + rnd.Next(10);
            var w = new string[words];
            for (int i = 0; i < words; i++)
            {
                var len = 2 + rnd.Next(7);
                var c = new char[len];
                for (int k = 0; k < len; k++) c[k] = (char)('a' + rnd.Next(26));
                if (i == 0) c[0] = char.ToUpperInvariant(c[0]);
                w[i] = new string(c);
            }
            return string.Join(' ', w) + ".";
        }

        var pool = Enumerable.Range(0, 3000).Select(_ => RandomLine()).ToArray();
        var sw = Stopwatch.StartNew();
        long memAt10k = 0;
        string line = pool[0];
        int remaining = 0;
        for (int f = 0; f < 100_000; f++)
        {
            if (remaining-- <= 0)
            {
                remaining = rnd.Next(60);
                line = rnd.NextDouble() < 0.25 ? "" : pool[rnd.Next(pool.Length)];
            }
            string text = line;
            double r = rnd.NextDouble();
            if (line.Length > 0 && r < 0.1) text = line[..Math.Max(1, line.Length / 2)];        // partial
            else if (line.Length > 0 && r < 0.2) text = line.Replace('e', 'c');                // jitter
            else if (r < 0.23) text = "#@! %% |";                                              // noise
            var action = pipeline.OnFrame(text, clock.Now);
            Assert.True(pipeline.Scheduler.InFlightCount <= 1);
            clock.Advance(66);
            if (f == 10_000) memAt10k = Mem();
        }
        sw.Stop();
        long memEnd = Mem();
        output.WriteLine($"100k frames in {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds * 1000 / 100_000:F1} µs/frame); " +
                         $"backend calls {backend.Calls}, shows {shows}, hides {hides}, cache {cache.Count}; " +
                         $"heap after 10k {memAt10k / 1024} KB, after 100k {memEnd / 1024} KB");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "altyazi_soak.txt"),
            $"{sw.ElapsedMilliseconds}|{backend.Calls}|{shows}|{hides}|{memAt10k / 1024}|{memEnd / 1024}");
        Assert.True(orderOk, "display order");
        Assert.True(shows > 1000 && hides > 100);
        Assert.True(cache.Count <= 500);
        Assert.True(memEnd - memAt10k < 4 * 1024 * 1024, $"heap grew {(memEnd - memAt10k) / 1024} KB");
    }

    private static long Mem()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return GC.GetTotalMemory(true);
    }
}
