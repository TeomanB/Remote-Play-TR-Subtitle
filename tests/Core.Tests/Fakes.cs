using AltyaziCevirici.Core;

/// <summary>Manually controlled backend: each call parks until the test completes it.</summary>
public sealed class ControlledBackend : ITranslateBackend
{
    public sealed class Call
    {
        public required string Text { get; init; }
        public required CancellationToken Token { get; init; }
        public required TaskCompletionSource<TranslateOutcome> Tcs { get; init; }
        public bool Cancelled => Tcs.Task.IsCanceled;
        public void Ok(string? tr = null) => Inline(() => Tcs.TrySetResult(new TranslateOutcome(TranslateStatus.Ok, tr ?? "TR:" + Text)));
        public void RateLimited(TimeSpan? ra = null) => Inline(() => Tcs.TrySetResult(new TranslateOutcome(TranslateStatus.RateLimited, RetryAfter: ra)));
        public void Fail() => Inline(() => Tcs.TrySetResult(new TranslateOutcome(TranslateStatus.Failed)));
        public void Throw() => Inline(() => Tcs.TrySetException(new InvalidOperationException("boom")));

        /// <summary>Complete without xUnit's SynchronizationContext so the scheduler's continuation runs inline (deterministic).</summary>
        private static void Inline(Action a)
        {
            var old = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try { a(); } finally { SynchronizationContext.SetSynchronizationContext(old); }
        }
    }

    public List<Call> Calls { get; } = new();
    public int Active;
    public int MaxActive;

    public Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<TranslateOutcome>();
        var call = new Call { Text = text, Token = ct, Tcs = tcs };
        Calls.Add(call);
        Active++;
        MaxActive = Math.Max(MaxActive, Active);
        ct.Register(() => tcs.TrySetCanceled(ct));
        tcs.Task.ContinueWith(_ => Active--, TaskContinuationOptions.ExecuteSynchronously);
        return tcs.Task;
    }

    public Call Last => Calls[^1];
}

public sealed class FakeClock
{
    public DateTime Now { get; set; } = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    public void Advance(double ms) => Now = Now.AddMilliseconds(ms);
}

/// <summary>Backend with configurable latency (real time), honoring cancellation.</summary>
public sealed class DelayBackend(int latencyMs, Func<string, TranslateOutcome>? result = null) : ITranslateBackend
{
    private int _active;
    public int MaxActive;
    public int Calls;

    public async Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        int a = Interlocked.Increment(ref _active);
        lock (this) MaxActive = Math.Max(MaxActive, a);
        try
        {
            await Task.Delay(latencyMs, ct).ConfigureAwait(false);
            return result?.Invoke(text) ?? new TranslateOutcome(TranslateStatus.Ok, "TR(" + text + ")");
        }
        finally { Interlocked.Decrement(ref _active); }
    }
}

public sealed class DisplayRecorder
{
    private readonly object _lock = new();
    public List<DisplayUpdate> Updates { get; } = new();
    public void On(DisplayUpdate u) { lock (_lock) Updates.Add(u); }
    public DisplayUpdate? LastShow { get { lock (_lock) return Updates.LastOrDefault(u => u.Kind == DisplayKind.Show); } }
    public DisplayUpdate? Last { get { lock (_lock) return Updates.Count == 0 ? null : Updates[^1]; } }
    public int Count { get { lock (_lock) return Updates.Count; } }
}
