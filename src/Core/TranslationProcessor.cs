using System.Text.Json.Serialization;

namespace AltyaziCevirici.Core;

/// <summary>Live options for <see cref="TranslationProcessor"/> (swapped atomically when settings change).</summary>
public sealed record ProcessorOptions(
    string Source = "en",
    string Target = "tr",
    IReadOnlyList<SozlukGirdisi>? Glossary = null,
    bool KeepProperNouns = true,
    [property: JsonIgnore] HazirDizin? Hazir = null,
    bool SadeceHazir = false)
{
    public bool EnglishToTurkish =>
        Source.StartsWith("en", StringComparison.OrdinalIgnoreCase) && Target.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Language-aware raw translator (the Google client).</summary>
public interface ILanguageBackend
{
    Task<TranslateOutcome> TranslateAsync(string text, string source, string target, CancellationToken ct);
}

/// <summary>
/// Decorates the raw translator: uncensors and handles profanity (en→tr, always on), protects glossary terms
/// and proper nouns with placeholders, then restores them. One request per line as before; whole-line
/// exclamations need no request at all. If a placeholder gets lost the line is retried once unprotected.
/// </summary>
public sealed class TranslationProcessor : ITranslateBackend
{
    private readonly ILanguageBackend _inner;
    private volatile ProcessorOptions _options;
    private volatile TermProtector _protector;
    private string? _onceki;

    public TranslationProcessor(ILanguageBackend inner, ProcessorOptions? options = null)
    {
        _inner = inner;
        _options = options ?? new ProcessorOptions();
        _protector = new TermProtector(_options.Glossary, _options.KeepProperNouns);
    }

    public ProcessorOptions Options
    {
        get => _options;
        set
        {
            _protector = new TermProtector(value.Glossary, value.KeepProperNouns);
            _options = value;
        }
    }

    public async Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct)
    {
        var opt = _options;
        var protector = _protector;
        var src = text.Trim();
        if (opt.Target.StartsWith("tr", StringComparison.OrdinalIgnoreCase) && OcrGate.MuhtemelenTurkce(src))
            return new TranslateOutcome(TranslateStatus.Skipped);
        if (opt.Source.StartsWith("en", StringComparison.OrdinalIgnoreCase)) src = Profanity.Uncensor(src);
        if (opt.Hazir is { } hazir)
        {
            var hazirTr = hazir.Ara(src, _onceki);
            if (!string.IsNullOrEmpty(hazirTr))
            {
                _onceki = null;
                return new TranslateOutcome(TranslateStatus.Ok, hazirTr);
            }
            _onceki = src;
            if (opt.SadeceHazir) return new TranslateOutcome(TranslateStatus.Skipped);
        }

        Profanity.Plan plan = opt.EnglishToTurkish
            ? Profanity.Analyze(src)
            : new Profanity.Plan(Array.Empty<(string, string)>(), src, Array.Empty<(int, Profanity.Strength)>(), null, "");

        string body = string.Empty;
        bool startsWithName = false;
        if (Profanity.HasLetters(plan.Body))
        {
            bool midSentence = plan.Prefixes.Count > 0 && plan.Prefixes[^1].Sep == ",";
            var masked = protector.Protect(plan.Body, midSentence);
            var r = await _inner.TranslateAsync(masked.Text, opt.Source, opt.Target, ct).ConfigureAwait(false);
            if (HedefZaten(opt, r.Detected)) return new TranslateOutcome(TranslateStatus.Skipped, Detected: r.Detected);
            if (r.Status != TranslateStatus.Ok || string.IsNullOrEmpty(r.Text)) return r;
            string? restored = masked.Terms.Count == 0 ? r.Text : TermProtector.Restore(r.Text!, masked);
            if (restored == null)
            {
                r = await _inner.TranslateAsync(plan.Body, opt.Source, opt.Target, ct).ConfigureAwait(false);
                if (r.Status != TranslateStatus.Ok || string.IsNullOrEmpty(r.Text)) return r;
                restored = r.Text!;
            }
            else startsWithName = masked.StartsWithTerm && masked.Terms[0].IsName;
            body = restored;
        }
        else if (plan.Prefixes.Count == 0)
        {
            body = plan.Body; // nothing translatable (numbers/punctuation)
        }

        var output = Profanity.Compose(plan, body, Profanity.CountSentences(plan.Body), startsWithName);
        if (output.Length > 0 && char.IsLower(output[0]) && !startsWithName)
            output = char.ToUpper(output[0], TurkishSuffix.Tr) + output[1..];
        return output.Length == 0 ? new TranslateOutcome(TranslateStatus.Failed) : new TranslateOutcome(TranslateStatus.Ok, output);
    }

    private static bool HedefZaten(ProcessorOptions opt, string? detected) =>
        !string.IsNullOrWhiteSpace(detected)
        && detected.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
        && opt.Target.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
}
