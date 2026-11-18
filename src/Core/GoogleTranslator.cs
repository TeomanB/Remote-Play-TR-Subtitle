using System.Net;

namespace AltyaziCevirici.Core;

/// <summary>Free Google translate endpoint client: warm pooled HTTP/2 connection, short timeouts, status mapping.</summary>
public sealed class GoogleTranslator : ITranslateBackend, ILanguageBackend, IDisposable
{
    private readonly HttpClient _http;

    public GoogleTranslator(TimeSpan? timeout = null, HttpMessageHandler? handler = null)
    {
        handler ??= new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(15),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false, // a redirect means the "sorry" (rate limit) page
            ConnectTimeout = TimeSpan.FromSeconds(3),
        };
        _http = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(3),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
    }

    /// <summary>HTTP version negotiated by the last successful response (diagnostics).</summary>
    public Version? LastHttpVersion { get; private set; }

    /// <summary>Opens the TLS connection early so the first real subtitle is fast. Never throws.</summary>
    public async Task<bool> WarmUpAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await TranslateAsync("hi", ct).ConfigureAwait(false);
            return r.Status == TranslateStatus.Ok;
        }
        catch (Exception) { return false; }
    }

    public Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct) => TranslateAsync(text, "en", "tr", ct);

    private DateTime _mainBlockedUntil = DateTime.MinValue;

    /// <summary>Client id of the last successful request (diagnostics).</summary>
    public string? LastClient { get; private set; }

    public async Task<TranslateOutcome> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        bool useBackupFirst = DateTime.UtcNow < _mainBlockedUntil;
        var first = await SendAsync(text, source, target, useBackupFirst ? GoogleTranslateParser.BackupClient : GoogleTranslateParser.MainClient, ct).ConfigureAwait(false);
        if (first.Status != TranslateStatus.RateLimited || ct.IsCancellationRequested) return first;
        // Rate limited: try the other client id once before the scheduler backs off.
        if (!useBackupFirst) _mainBlockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        var second = await SendAsync(text, source, target, useBackupFirst ? GoogleTranslateParser.MainClient : GoogleTranslateParser.BackupClient, ct).ConfigureAwait(false);
        if (second.Status == TranslateStatus.Ok && useBackupFirst) _mainBlockedUntil = DateTime.MinValue;
        return second.Status == TranslateStatus.Ok ? second : first;
    }

    private async Task<TranslateOutcome> SendAsync(string text, string source, string target, string client, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(GoogleTranslateParser.BuildUrl(text, source, target, client), HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests || resp.StatusCode == HttpStatusCode.ServiceUnavailable
                || (resp.StatusCode == HttpStatusCode.Found && resp.Headers.Location?.AbsolutePath.Contains("sorry", StringComparison.OrdinalIgnoreCase) == true))
            {
                TimeSpan? ra = resp.Headers.RetryAfter?.Delta;
                if (ra == null && resp.Headers.RetryAfter?.Date is { } d) ra = d - DateTimeOffset.UtcNow;
                return new TranslateOutcome(TranslateStatus.RateLimited, RetryAfter: ra);
            }
            if (!resp.IsSuccessStatusCode) return new TranslateOutcome(TranslateStatus.Failed);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            LastHttpVersion = resp.Version;
            var parsed = GoogleTranslateParser.Parse(json);
            if (parsed != null) LastClient = client;
            return parsed == null ? new TranslateOutcome(TranslateStatus.Failed) : new TranslateOutcome(TranslateStatus.Ok, parsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new TranslateOutcome(TranslateStatus.Cancelled);
        }
        catch (Exception)
        {
            // Timeout (TaskCanceledException without our token), DNS, socket, TLS...
            return new TranslateOutcome(TranslateStatus.Failed);
        }
    }

    public void Dispose() => _http.Dispose();
}
