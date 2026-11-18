using AltyaziCevirici.Core;
using Xunit;

public class OturumSoakTests
{
    [Fact]
    public void RestartedDisplayNumbersAreShownOnlyAfterTheGateResets()
    {
        var gate = new GosterKapisi();
        Assert.True(gate.Kabul(1));
        Assert.True(gate.Kabul(4));
        Assert.False(gate.Kabul(1));
        Assert.False(gate.Kabul(4));
        gate.Sifirla();
        Assert.True(gate.Kabul(1));
    }

    [Fact]
    public void TwelveSimulatedMinutesShowsEveryLine()
    {
        var shown = new List<DisplayUpdate>();
        var backend = new EchoBackend();
        var proc = new TranslationProcessor(backend, new ProcessorOptions());
        var clock = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);
        using var sched = new TranslationScheduler(proc, new TranslationCache(2000), u => shown.Add(u), () => clock, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        var pipe = new SubtitlePipeline(new SubtitleTracker(0.85, TimeSpan.FromSeconds(2)), sched);

        var script = BuildScript(out double totalSeconds);
        Assert.True(totalSeconds >= 600);

        var frame = TimeSpan.FromSeconds(1.0 / 15.0);
        for (var elapsed = TimeSpan.Zero; elapsed.TotalSeconds <= totalSeconds; elapsed += frame)
        {
            clock = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc) + elapsed;
            string ocr = ActiveLine(script, elapsed.TotalSeconds);
            pipe.OnFrame(ocr, clock);
        }

        var shows = shown.Where(u => u.Kind == DisplayKind.Show && !u.IsFallback && u.Shown != null).Select(u => u.Shown!).ToList();
        foreach (var line in script.Select(s => s.Text).Distinct())
        {
            Assert.Contains(shows, s => s.Contains(line, StringComparison.Ordinal));
        }

        int repeats = script.Count(s => s.Text == script[0].Text);
        Assert.True(repeats >= 2);
        int shownFirst = shows.Count(s => s.Contains(script[0].Text, StringComparison.Ordinal));
        Assert.True(shownFirst >= repeats, "a repeated line after a gap was not shown again (" + shownFirst + "/" + repeats + ")");

        Assert.Contains(shown, u => u.Kind == DisplayKind.Hide);
        Assert.Contains(shown, u => u.Kind == DisplayKind.Show && u.LineKind == "objective" && (u.Shown?.Contains("Respray the Phoenix") ?? false));
        Assert.Contains(shown, u => u.Kind == DisplayKind.Show && u.LineKind != "objective" && (u.Shown?.Contains("we're late") ?? false));
    }

    private static List<(double Start, double End, string Text)> BuildScript(out double total)
    {
        var lines = new List<(double Start, double End, string Text)>();
        double t = 0;
        const int count = 320;
        for (int i = 0; i < count; i++)
        {
            string text = i % 11 == 0
                ? "Respray the Phoenix"
                : i % 5 == 0
                    ? "Get in the car, we're late."
                    : i % 3 == 0
                        ? "Follow that car now please"
                        : "Spoken line " + i + " stays on the screen.";
            if (i == count - 1) text = lines[0].Text;
            lines.Add((t, t + 1.4, text));
            t += 1.4;
            if (i % 4 == 0) t += 2.6;
        }
        total = t;
        return lines;
    }

    private static string ActiveLine(List<(double Start, double End, string Text)> script, double second)
    {
        foreach (var line in script)
            if (second >= line.Start && second < line.End) return line.Text;
        return "";
    }

    private sealed class EchoBackend : ILanguageBackend
    {
        public Task<TranslateOutcome> TranslateAsync(string text, string source, string target, CancellationToken ct)
            => Task.FromResult(new TranslateOutcome(TranslateStatus.Ok, text));
    }
}
