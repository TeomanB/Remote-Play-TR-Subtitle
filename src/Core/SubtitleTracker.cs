namespace AltyaziCevirici.Core;

public enum TrackAction
{
    /// <summary>Nothing changed that needs work.</summary>
    None,
    /// <summary>Translate <see cref="TrackResult.Text"/> now (first sight, growth, correction or new line).</summary>
    Translate,
    /// <summary>The shown line is still on screen (identical, jitter or partial reading).</summary>
    Keep,
    /// <summary>No subtitle for the configured time: hide.</summary>
    Hide,
}

/// <param name="IsUpdate">True when the new text is the same line refined (growth/correction): update in place.</param>
public readonly record struct TrackResult(TrackAction Action, string? Text = null, bool IsUpdate = false);

/// <summary>
/// Low-latency change detection: translates on first sight, re-translates immediately when a line
/// grows, ignores jitter/partial regressions, accepts an OCR correction once it is read twice in a row,
/// and hides after a quiet period. Not thread-safe: feed from a single loop.
/// </summary>
public sealed class SubtitleTracker
{
    private readonly double _threshold;
    private readonly TimeSpan _hideAfter;
    private string? _current;
    private string? _currentKey;
    private string? _correction;
    private DateTime _lastSeen;

    public SubtitleTracker(double similarityThreshold = 0.85, TimeSpan? hideAfter = null)
    {
        _threshold = similarityThreshold;
        _hideAfter = hideAfter ?? TimeSpan.FromSeconds(2);
    }

    public string? Current => _current;

    public void Reset()
    {
        _current = null;
        _currentKey = null;
        _correction = null;
    }

    public TrackResult Feed(string? text, DateTime now)
    {
        if (!TextUtil.LooksLikeText(text))
        {
            _correction = null;
            if (_current != null && now - _lastSeen >= _hideAfter)
            {
                Reset();
                return new TrackResult(TrackAction.Hide);
            }
            return new TrackResult(TrackAction.None);
        }

        var t = text!;
        var key = TextUtil.Key(t);
        if (_current == null)
            return Set(t, key, now, isUpdate: false);

        _lastSeen = now;
        if (key == _currentKey)
        {
            _correction = null;
            return new TrackResult(TrackAction.Keep);
        }
        if (TextUtil.IsGrowth(_currentKey, key, _threshold))
            return Set(t, key, now, isUpdate: true);
        if (TextUtil.IsGrowth(key, _currentKey, _threshold))
        {
            // Partial/regressed reading of the shown line (OCR dropped the tail): keep showing the fuller one.
            _correction = null;
            return new TrackResult(TrackAction.Keep);
        }
        if (TextUtil.Similarity(key, _currentKey) >= _threshold)
        {
            // Jitter or a correction. Accept it only when the same new reading repeats (avoids ping-pong spam).
            if (_correction != null && TextUtil.Key(_correction) == key)
                return Set(t, key, now, isUpdate: true);
            _correction = t;
            return new TrackResult(TrackAction.Keep);
        }
        return Set(t, key, now, isUpdate: false);
    }

    private TrackResult Set(string text, string key, DateTime now, bool isUpdate)
    {
        _current = text;
        _currentKey = key;
        _correction = null;
        _lastSeen = now;
        return new TrackResult(TrackAction.Translate, text, isUpdate);
    }
}
