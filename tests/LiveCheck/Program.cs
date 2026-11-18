using System.Diagnostics;
using AltyaziCevirici.Core;

// Live check against the real endpoint (few requests, mind the rate limit).
// "dotnet run -- raw" also prints Google's own (unprocessed) output for comparison.
bool raw = args.Contains("raw");
using var t = new GoogleTranslator();
var sw = Stopwatch.StartNew();
bool warm = await t.WarmUpAsync();
Console.WriteLine($"warm-up: {(warm ? "ok" : "FAILED")} {sw.ElapsedMilliseconds} ms, HTTP/{t.LastHttpVersion}, client={t.LastClient}");
var settings = new AppSettings();
settings.Clamp();
settings.Sozluk!.Add(new SozlukGirdisi("Saint Denis"));
var proc = new TranslationProcessor(t, settings.ToProcessorOptions());
int fails = 0;
string[] lines =
{
    "Get in the car, we're late.",
    "What the fuck are you doing?",
    "Where the fuck is Jason?",
    "This is fucking crazy.",
    "Shut the fuck up!",
    "Fuck, we're late.",
    "Where is my money, you motherfucker?",
    "Holy shit.",
    "What the f***?",
    "Meet me at Port Gellhorn.",
    "We are going to Vice City.",
    "Damn it, Lucia, we need to go to Saint Denis.",
};
foreach (var line in lines)
{
    if (raw)
    {
        var r0 = await t.TranslateAsync(line, CancellationToken.None);
        Console.WriteLine($"     google | {line} => {r0.Text} [{r0.Status}]");
        await Task.Delay(1500);
    }
    sw.Restart();
    var r = await proc.TranslateAsync(line, CancellationToken.None);
    Console.WriteLine($"{sw.ElapsedMilliseconds,4} ms | {r.Status} | {line} => {r.Text} [{t.LastClient}]");
    if (r.Status != TranslateStatus.Ok) fails++;
    await Task.Delay(1500);
}

// Through the real scheduler: first sight + cache + display update.
var done = new TaskCompletionSource<DisplayUpdate>();
var cache = new TranslationCache();
using var sched = new TranslationScheduler(proc, cache, u => { if (u.Kind == DisplayKind.Show && !u.IsFallback) done.TrySetResult(u); });
sw.Restart();
sched.Submit("Keep your head down and follow my lead.");
var shown = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"{sw.ElapsedMilliseconds,4} ms | scheduler → display: {shown.Shown}");

using var cts = new CancellationTokenSource(); cts.Cancel();
var c = await t.TranslateAsync("cancel me", cts.Token);
Console.WriteLine($"pre-cancelled request → {c.Status}");
return fails == 0 && c.Status == TranslateStatus.Cancelled ? 0 : 1;
