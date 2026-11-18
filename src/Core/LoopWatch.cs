namespace AltyaziCevirici.Core;

/// <summary>Decides when the capture loop should be torn down and started again.</summary>
public static class LoopWatch
{
    public static readonly TimeSpan Stall = TimeSpan.FromSeconds(8);

    public static bool ShouldRestart(DateTime lastBeat, DateTime now, bool running, bool paused) =>
        running && !paused && now - lastBeat >= Stall;
}

/// <summary>One in-flight operation. A second enter is dropped until <see cref="Release"/>.</summary>
public sealed class TekUcus
{
    private int _held;

    public bool Busy => Volatile.Read(ref _held) != 0;
    public bool TryEnter() => Interlocked.CompareExchange(ref _held, 1, 0) == 0;
    public void Release() => Volatile.Write(ref _held, 0);
}
