namespace AltyaziCevirici.Core;

public enum TranslateStatus { Ok, RateLimited, Failed, Cancelled, Skipped }

public readonly record struct TranslateOutcome(TranslateStatus Status, string? Text = null, TimeSpan? RetryAfter = null, string? Detected = null, string? Kind = null);

public interface ITranslateBackend
{
    Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct);
}

public enum DisplayKind { Show, Hide }

/// <param name="Seq">Strictly increasing display order number; a consumer must ignore updates older than the last applied one.</param>
public readonly record struct DisplayUpdate(DisplayKind Kind, long Seq, string? English = null, string? Shown = null, bool IsFallback = false, string? LineKind = null);

/// <summary>
/// Schedules translations with newest-wins semantics: at most 1 request in flight plus 1 pending (latest),
/// identical-text debounce, cache, minimum spacing between requests, exponential backoff on 429/errors
/// with English fallback, and strictly ordered display updates (no stale overwrite).
/// Display callbacks are invoked under an internal lock and must be quick and non-blocking (e.g. Post to UI).
/// </summary>
public sealed class TranslationScheduler : IDisposable
{
    private sealed class Request
    {
        public required string Text { get; init; }
        public required long Seq { get; init; }
        public required CancellationTokenSource Cts { get; init; }
    }

    private readonly ITranslateBackend _backend;
    private readonly TranslationCache _cache;
    private readonly Action<DisplayUpdate> _onDisplay;
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _minSpacing;
    private readonly TimeSpan _callTimeout;
    private readonly object _lock = new();

    private long _seq;
    private long _displaySeq;
    private long _shownSeq;
    private bool _shownIsFallback;
    private bool _visible;
    private string? _latest;
    private string? _latestKey;
    private long _latestSeq;
    private Request? _inflight;
    private string? _pending;
    private long _pendingSeq;
    private bool _needsRetry;
    private DateTime _lastStart = DateTime.MinValue;
    private DateTime _backoffUntil = DateTime.MinValue;
    private TimeSpan _backoff = TimeSpan.Zero;
    private bool _disposed;

    public static readonly TimeSpan RateLimitBackoffStart = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RateLimitBackoffMax = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ErrorBackoffStart = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan ErrorBackoffMax = TimeSpan.FromSeconds(15);

    public TranslationScheduler(ITranslateBackend backend, TranslationCache cache, Action<DisplayUpdate> onDisplay,
        Func<DateTime>? clock = null, TimeSpan? minSpacing = null, TimeSpan? callTimeout = null)
    {
        _backend = backend;
        _cache = cache;
        _onDisplay = onDisplay;
        _clock = clock ?? (() => DateTime.UtcNow);
        _minSpacing = minSpacing ?? TimeSpan.FromMilliseconds(60);
        _callTimeout = callTimeout ?? TimeSpan.FromSeconds(8);
    }

    // ---- diagnostics for tests ----
    public int InFlightCount { get { lock (_lock) return _inflight == null ? 0 : 1; } }
    public bool HasPending { get { lock (_lock) return _pending != null; } }
    public int RequestsStarted { get; private set; }
    public int MaxConcurrentBackendCalls => _maxConcurrent;
    public TimeSpan CurrentBackoff { get { lock (_lock) return _backoff; } }
    public DateTime BackoffUntil { get { lock (_lock) return _backoffUntil; } }

    private int _concurrent;
    private int _maxConcurrent;

    /// <summary>A new/changed subtitle line (English) to translate and display.</summary>
    public void Submit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lock)
        {
            if (_disposed) return;
            var key = TextUtil.Key(text);
            if (key == _latestKey) return; // debounce identical text (until Clear)
            long seq = ++_seq;
            _latest = text;
            _latestKey = key;
            _latestSeq = seq;
            _needsRetry = false;

            if (_cache.TryGet(text, out var cached, out var cachedKind))
            {
                _pending = null;
                Show(seq, text, cached, fallback: false, cachedKind);
                return;
            }

            var now = _clock();
            if (now < _backoffUntil)
            {
                _pending = null;
                if (_inflight != null && !TextUtil.AreRelated(_inflight.Text, text)) CancelInflight();
                Show(seq, text, text, fallback: true);
                _needsRetry = true;
                return;
            }

            if (_inflight == null)
            {
                if (now - _lastStart >= _minSpacing) Start(text, seq, now);
                else { _pending = text; _pendingSeq = seq; }
            }
            else if (TextUtil.AreRelated(_inflight.Text, text))
            {
                // Same line refined: let the in-flight one finish (progressive display), queue the latest.
                _pending = text;
                _pendingSeq = seq;
            }
            else
            {
                // Completely different line: newest wins.
                CancelInflight();
                if (now - _lastStart >= _minSpacing) { _pending = null; Start(text, seq, now); }
                else { _pending = text; _pendingSeq = seq; }
            }
        }
    }

    /// <summary>Call regularly (every frame): starts spaced/pending requests and retries after backoff.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (_disposed || _inflight != null) return;
            var now = _clock();
            if (now < _backoffUntil || now - _lastStart < _minSpacing) return;
            if (_pending != null)
            {
                var p = _pending; _pending = null;
                Start(p, _pendingSeq, now);
            }
            else if (_needsRetry && _latest != null)
            {
                _needsRetry = false;
                Start(_latest, _latestSeq, now);
            }
        }
    }

    /// <summary>Subtitle disappeared (or pause): hide and drop everything in flight.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_disposed) return;
            CancelInflight();
            _pending = null;
            _needsRetry = false;
            _latest = null;
            _latestKey = null;
            long seq = ++_seq;
            _shownSeq = seq;
            _shownIsFallback = false;
            if (_visible)
            {
                _visible = false;
                SafeDisplay(new DisplayUpdate(DisplayKind.Hide, ++_displaySeq));
            }
        }
    }

    private void Start(string text, long seq, DateTime now)
    {
        var req = new Request { Text = text, Seq = seq, Cts = new CancellationTokenSource() };
        _inflight = req;
        _lastStart = now;
        RequestsStarted++;
        _ = RunAsync(req);
    }

    private void CancelInflight()
    {
        var req = _inflight;
        if (req == null) return;
        _inflight = null; // clear first: a synchronous cancellation callback must not see itself as in flight
        try { req.Cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task RunAsync(Request req)
    {
        TranslateOutcome outcome;
        var started = System.Diagnostics.Stopwatch.StartNew();
        int c = Interlocked.Increment(ref _concurrent);
        InterlockedMax(ref _maxConcurrent, c);
        try
        {
            var work = _backend.TranslateAsync(req.Text, req.Cts.Token);
            if (_callTimeout > TimeSpan.Zero && _callTimeout != Timeout.InfiniteTimeSpan)
            {
                using var timeout = new CancellationTokenSource(_callTimeout);
                var gate = Task.Delay(_callTimeout, timeout.Token);
                var finished = await Task.WhenAny(work, gate).ConfigureAwait(false);
                if (finished == work)
                {
                    timeout.Cancel();
                    _ = gate.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                    outcome = await work.ConfigureAwait(false);
                }
                else
                {
                    _ = work.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                    outcome = new TranslateOutcome(TranslateStatus.Cancelled);
                }
            }
            else outcome = await work.ConfigureAwait(false);
            if (req.Cts.IsCancellationRequested && outcome.Status != TranslateStatus.Ok)
                outcome = new TranslateOutcome(TranslateStatus.Cancelled);
        }
        catch (OperationCanceledException) when (req.Cts.IsCancellationRequested)
        {
            outcome = new TranslateOutcome(TranslateStatus.Cancelled);
        }
        catch (Exception)
        {
            outcome = new TranslateOutcome(TranslateStatus.Failed);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrent);
        }

        TanilamaLog.Yaz("ceviri", outcome.Status + " " + started.ElapsedMilliseconds + "ms kind=" + (outcome.Kind ?? "-") + " «" + req.Text + "»");
        try { Complete(req, outcome); }
        catch (Exception) { /* never let a completion kill anything */ }
        finally { req.Cts.Dispose(); }
    }

    private void Complete(Request req, TranslateOutcome outcome)
    {
        lock (_lock)
        {
            bool stillOurs = _inflight == req;
            if (stillOurs) _inflight = null;
            if (_disposed) return;
            var now = _clock();
            switch (outcome.Status)
            {
                case TranslateStatus.Ok when !string.IsNullOrEmpty(outcome.Text):
                    _cache.Set(req.Text, outcome.Text!, outcome.Kind);
                    _backoff = TimeSpan.Zero;
                    _backoffUntil = DateTime.MinValue;
                    if (IsRelevant(req.Text) &&
                        (req.Seq > _shownSeq || (req.Seq == _shownSeq && _shownIsFallback)))
                        Show(req.Seq, req.Text, outcome.Text!, fallback: false, outcome.Kind);
                    break;
                case TranslateStatus.Skipped:
                    _backoff = TimeSpan.Zero;
                    _backoffUntil = DateTime.MinValue;
                    if (IsRelevant(req.Text) && _visible)
                    {
                        _visible = false;
                        _shownSeq = Math.Max(_shownSeq, req.Seq);
                        SafeDisplay(new DisplayUpdate(DisplayKind.Hide, ++_displaySeq));
                    }
                    break;
                case TranslateStatus.Cancelled:
                    // A replaced in-flight call already decided the next step. A call that timed out
                    // while it was still the current one must be retried, or that line never translates.
                    if (stillOurs && IsRelevant(req.Text))
                    {
                        if (req.Seq > _shownSeq) Show(req.Seq, req.Text, req.Text, fallback: true);
                        _needsRetry = _latest != null;
                    }
                    return;
                default:
                    bool limited = outcome.Status == TranslateStatus.RateLimited;
                    var start = limited ? RateLimitBackoffStart : ErrorBackoffStart;
                    var max = limited ? RateLimitBackoffMax : ErrorBackoffMax;
                    _backoff = _backoff == TimeSpan.Zero ? start : TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, max.Ticks));
                    if (_backoff < start) _backoff = start;
                    if (outcome.RetryAfter is { } ra && ra > _backoff) _backoff = ra > RateLimitBackoffMax ? RateLimitBackoffMax : ra;
                    _backoffUntil = now + _backoff;
                    if (IsRelevant(req.Text))
                    {
                        if (req.Seq > _shownSeq) Show(req.Seq, req.Text, req.Text, fallback: true);
                        _needsRetry = _latest != null;
                    }
                    if (_pending != null)
                    {
                        // Pending text can't go out yet: show it in English, retry later.
                        Show(_pendingSeq, _pending, _pending, fallback: true);
                        _pending = null;
                        _needsRetry = true;
                    }
                    return;
            }

            if (_pending != null && _inflight == null && now >= _backoffUntil && now - _lastStart >= _minSpacing)
            {
                var p = _pending; _pending = null;
                Start(p, _pendingSeq, now);
            }
        }
    }

    private bool IsRelevant(string text) =>
        _latest != null && (TextUtil.Key(text) == _latestKey || TextUtil.AreRelated(text, _latest));

    private void Show(long seq, string english, string shown, bool fallback, string? lineKind = null)
    {
        if (seq < _shownSeq) return;
        _shownSeq = seq;
        _shownIsFallback = fallback;
        _visible = true;
        SafeDisplay(new DisplayUpdate(DisplayKind.Show, ++_displaySeq, english, shown, fallback, lineKind));
    }

    private void SafeDisplay(DisplayUpdate u)
    {
        try { _onDisplay(u); } catch (Exception) { /* display errors must not break scheduling */ }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int cur;
        while ((cur = Volatile.Read(ref target)) < value &&
               Interlocked.CompareExchange(ref target, value, cur) != cur) { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            CancelInflight();
            _pending = null;
        }
    }
}
