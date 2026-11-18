using System.Net;
using System.Text;
using System.Text.Json;

namespace AltyaziCevirici.Core;

public static class MyMemoryParser
{
    public static (string? Text, bool Quota) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool quota = root.TryGetProperty("quotaFinished", out var q) && q.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("responseStatus", out var st) && st.TryGetInt32(out int code) && code is 403 or 429)
                quota = true;
            string? text = null;
            if (root.TryGetProperty("responseData", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("translatedText", out var t)
                && t.ValueKind == JsonValueKind.String)
                text = t.GetString();
            if (text != null && text.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                quota = true;
            if (quota || string.IsNullOrWhiteSpace(text)) return (null, quota);
            return (text.Trim(), false);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }
}

/// <summary>MyMemory GET fallback. Implements <see cref="ILanguageBackend"/> so the profanity pipeline stays in front.</summary>
public sealed class MyMemoryCeviri : ILanguageBackend, IDisposable
{
    private readonly HttpClient _http;

    public MyMemoryCeviri(HttpMessageHandler? handler = null)
    {
        _http = handler == null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(4) }
            : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
    }

    public async Task<TranslateOutcome> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text)
            + "&langpair=" + Uri.EscapeDataString(source) + "|" + Uri.EscapeDataString(target);
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var json = Encoding.UTF8.GetString(bytes);
            if (resp.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                return new TranslateOutcome(TranslateStatus.RateLimited);
            var (translated, quota) = MyMemoryParser.Parse(json);
            if (quota) return new TranslateOutcome(TranslateStatus.RateLimited);
            return string.IsNullOrEmpty(translated)
                ? new TranslateOutcome(TranslateStatus.Failed)
                : new TranslateOutcome(TranslateStatus.Ok, translated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new TranslateOutcome(TranslateStatus.Cancelled);
        }
        catch (Exception)
        {
            return new TranslateOutcome(TranslateStatus.Failed);
        }
    }

    public void Dispose() => _http.Dispose();
}
