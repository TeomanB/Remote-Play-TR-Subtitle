namespace AltyaziCevirici.Core;

/// <summary>Glue: per-frame OCR text → tracker → scheduler → ordered display updates.</summary>
public sealed class SubtitlePipeline : IDisposable
{
    private readonly SubtitleTracker _tracker;
    private readonly TranslationScheduler _scheduler;

    public SubtitlePipeline(SubtitleTracker tracker, TranslationScheduler scheduler)
    {
        _tracker = tracker;
        _scheduler = scheduler;
    }

    public TranslationScheduler Scheduler => _scheduler;
    public SubtitleTracker Tracker => _tracker;

    /// <summary>Feed one OCR frame (normalized text, may be empty). Never throws.</summary>
    public TrackAction OnFrame(string? ocrText, DateTime now)
    {
        try
        {
            var r = _tracker.Feed(ocrText, now);
            switch (r.Action)
            {
                case TrackAction.Translate: _scheduler.Submit(r.Text!); break;
                case TrackAction.Hide: _scheduler.Clear(); break;
            }
            _scheduler.Tick();
            return r.Action;
        }
        catch (Exception)
        {
            return TrackAction.None;
        }
    }

    public void Reset()
    {
        _tracker.Reset();
        _scheduler.Clear();
    }

    public void Dispose() => _scheduler.Dispose();
}
