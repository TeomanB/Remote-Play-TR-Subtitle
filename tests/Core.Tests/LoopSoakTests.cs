using System.Diagnostics;
using System.Runtime.InteropServices;
using AltyaziCevirici.Core;
using Xunit;

public class LoopSoakTests
{
    [Fact]
    public void WatchRestartsOnlyWhenTheLoopIsQuiet()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(LoopWatch.ShouldRestart(now, now.AddSeconds(3), running: true, paused: false));
        Assert.False(LoopWatch.ShouldRestart(now, now.AddSeconds(30), running: true, paused: true));
        Assert.False(LoopWatch.ShouldRestart(now, now.AddSeconds(30), running: false, paused: false));
        Assert.True(LoopWatch.ShouldRestart(now, now.AddSeconds(8), running: true, paused: false));
    }

    [Fact]
    public void SingleFlightDropsWhileBusy()
    {
        var gate = new TekUcus();
        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        gate.Release();
        Assert.True(gate.TryEnter());
        gate.Release();
    }

    [Fact]
    public async Task HungTranslationTimesOutAndTheNextLineStillTranslates()
    {
        var hang = new TaskCompletionSource<TranslateOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var backend = new ScriptBackend(_ =>
        {
            int n = Interlocked.Increment(ref calls);
            return n == 1 ? hang.Task : Task.FromResult(new TranslateOutcome(TranslateStatus.Ok, "ikinci"));
        });
        var shown = new List<string>();
        using var sched = new TranslationScheduler(backend, new TranslationCache(), u =>
        {
            if (u.Kind == DisplayKind.Show && u.Shown != null) shown.Add(u.Shown);
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(80));

        sched.Submit("first line of dialogue");
        await Task.Delay(250);
        sched.Tick();
        sched.Submit("second line of dialogue");
        await Task.Delay(200);
        sched.Tick();
        Assert.Contains("ikinci", shown);
    }

    [Fact]
    public async Task TwoMinuteLoopKeepsTranslatingAndMemoryFlat()
    {
        long shows = 0;
        var backend = new ScriptBackend(text => Task.FromResult(new TranslateOutcome(TranslateStatus.Ok, "tr " + text)));
        using var sched = new TranslationScheduler(backend, new TranslationCache(400), u =>
        {
            if (u.Kind == DisplayKind.Show && !u.IsFallback) Interlocked.Increment(ref shows);
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        int gdiBefore = GdiCount();
        long memBefore = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        int i = 0;
        var gate = new TekUcus();
        while (sw.Elapsed < TimeSpan.FromSeconds(120))
        {
            if (gate.TryEnter())
            {
                try { sched.Submit("subtitle line number " + (i % 50)); }
                finally { gate.Release(); }
            }
            sched.Tick();
            i++;
            if ((i & 63) == 0) await Task.Yield();
            else await Task.Delay(12);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memAfter = GC.GetTotalMemory(true);
        int gdiAfter = GdiCount();
        Assert.True(shows > 200, "translations stopped (" + shows + ")");
        Assert.True(memAfter - memBefore < 40L * 1024 * 1024, "managed memory grew by " + (memAfter - memBefore));
        if (gdiBefore >= 0)
            Assert.InRange(gdiAfter - gdiBefore, -10, 15);
    }

    private static int GdiCount()
    {
        if (!OperatingSystem.IsWindows()) return -1;
        return GetGuiResources(GetCurrentProcess(), 0);
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, uint flags);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private sealed class ScriptBackend(Func<string, Task<TranslateOutcome>> next) : ITranslateBackend
    {
        public Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct) => next(text);
    }
}
