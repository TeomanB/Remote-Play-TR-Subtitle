using AltyaziCevirici.Core;

namespace AltyaziCevirici;

internal sealed class TrayApp : ApplicationContext, ISettingsHost
{
    private const string AppName = "Altyazı Çevirici";

    private readonly string _settingsPath;
    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly HotkeyWindow _hotkeys;
    private readonly OverlayForm _overlay;
    private readonly GoogleTranslator _translator = new();
    private readonly TranslationProcessor _processor;
    private SettingsForm? _settingsForm;
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 3500 };
    public event EventHandler? StateChanged;
    private readonly TranslationCache _cache = new(4000);
    private readonly System.Threading.Timer _warmTimer;
    private SubtitlePipeline? _pipeline;
    private long _lastAppliedSeq;
    private DateTime _lastNetUse = DateTime.UtcNow;
    private readonly SynchronizationContext _ui;
    private readonly Icon _icon;

    private OcrCapture? _ocr;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private volatile bool _paused;
    private volatile bool _resetRequested;
    private bool _selecting;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settingsPath = AppPaths.SettingsFile;
        _settings = AppSettings.Load(_settingsPath);

        _overlay = new OverlayForm();
        _ = _overlay.Handle; // create handle on UI thread

        _processor = new TranslationProcessor(_translator, _settings.ToProcessorOptions());
        _overlay.Configure(Region, _settings);
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); if (_previewing) { _previewing = false; _overlay.HideText(); } };

        _pauseItem = new ToolStripMenuItem("Duraklat (F9)", null, (_, _) => SafeRun(TogglePause, "pause"));
        var settingsItem = new ToolStripMenuItem("Ayarlar…", null, (_, _) => SafeRun(OpenSettings, "settings")) { Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold) };

        var menu = new ContextMenuStrip();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripMenuItem("Altyazı bölgesini seç (F8)", null, (_, _) => SafeRun(SelectRegion, "select")));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Çıkış (F10)", null, (_, _) => SafeRun(Quit, "quit")));

        _icon = IconFactory.Create();
        _tray = new NotifyIcon { Icon = _icon, Text = AppName, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => SafeRun(OpenSettings, "settings");

        _hotkeys = new HotkeyWindow();
        var failed = new List<string>();
        if (!_hotkeys.Register(Keys.F8, () => SafeRun(SelectRegion, "select"))) failed.Add("F8");
        if (!_hotkeys.Register(Keys.F9, () => SafeRun(TogglePause, "pause"))) failed.Add("F9");
        if (!_hotkeys.Register(Keys.F10, () => SafeRun(Quit, "quit"))) failed.Add("F10");
        if (failed.Count > 0)
            Balloon($"{string.Join(", ", failed)} tuşu başka bir program tarafından kullanılıyor. Simgeye sağ tıklayarak menüyü kullanabilirsin.", ToolTipIcon.Warning);

        _ = WarmAsync();
        // Keep the TLS connection warm while idle (HTTP/2 PINGs do most of it; this covers HTTP/1.1 / GOAWAY).
        _warmTimer = new System.Threading.Timer(_ => OnWarmTimer(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        _ui.Post(_ => SafeRun(Start, "start"), null);
    }

    private void Start()
    {
        _ocr = OcrCapture.Create(out bool englishMissing, _settings.KaynakDil);
        if (_ocr == null)
        {
            MessageBox.Show(EnglishOcrHelp(noOcrAtAll: true), AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Quit();
            return;
        }
        if (englishMissing || !_ocr.Matches(_settings.KaynakDil))
            MessageBox.Show(EnglishOcrHelp(noOcrAtAll: false), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        if (!_settings.HasRegion)
        {
            Balloon("Altyazının çıktığı alanı seç. F8: bölge seç, F9: duraklat, F10: çıkış. Ayarlar: simgeye çift tıkla.", ToolTipIcon.Info);
            SelectRegion();
        }
        else
        {
            Balloon("Çalışıyor. F8: bölge seç, F9: duraklat, F10: çıkış. Ayarlar: simgeye çift tıkla.", ToolTipIcon.Info);
            StartLoop();
        }
    }

    private static string EnglishOcrHelp(bool noOcrAtAll) =>
        (noOcrAtAll
            ? "Windows'ta hiç metin tanıma (OCR) dili bulunamadı, program çalışamıyor.\n\n"
            : "Windows'ta İngilizce metin tanıma (OCR) paketi yüklü değil. Şimdilik sistem dilinle okunacak ama İngilizce altyazılar hatalı okunabilir.\n\n") +
        "İngilizce OCR paketini eklemek için:\n" +
        "1) Ayarlar > Zaman ve dil > Dil ve bölge > Dil ekle\n" +
        "2) \"English (United States)\" seç. \"Görüntülenen dil olarak ayarla\" işaretini kaldır;\n" +
        "   \"Temel yazma\" / \"Optik karakter tanıma\" seçili kalsın, Yükle'ye bas.\n" +
        "3) Kurulum bitince Altyazı Çevirici'yi yeniden aç.\n\n" +
        "Alternatif (Yönetici PowerShell):\n" +
        "Add-WindowsCapability -Online -Name \"Language.OCR~~~en-US~0.0.1.0\"";

    private Rectangle Region => new(_settings.RegionX, _settings.RegionY, _settings.RegionWidth, _settings.RegionHeight);

    private void SaveSettings()
    {
        try { _settings.Save(_settingsPath); }
        catch (Exception ex) { ErrorLog.Write(ex, "settings"); Balloon("Ayarlar kaydedilemedi.", ToolTipIcon.Warning); }
    }

    // ---------------- capture loop ----------------

    private SubtitlePipeline CreatePipeline()
    {
        var tracker = new SubtitleTracker(_settings.BenzerlikEsigi, TimeSpan.FromMilliseconds(_settings.GizlemeMs));
        var scheduler = new TranslationScheduler(new TrackingBackend(this), _cache, OnDisplay);
        return new SubtitlePipeline(tracker, scheduler);
    }

    /// <summary>Wraps the translator to remember the last network use (for idle warm-up).</summary>
    private sealed class TrackingBackend(TrayApp app) : ITranslateBackend
    {
        public Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct)
        {
            app._lastNetUse = DateTime.UtcNow;
            return app._processor.TranslateAsync(text, ct);
        }
    }

    private void StartLoop()
    {
        StopLoop();
        if (_ocr == null || !_settings.HasRegion) return;
        _overlay.Configure(Region, _settings);
        _pipeline?.Dispose();
        _pipeline = CreatePipeline();
        var cts = _loopCts = new CancellationTokenSource();
        var region = Region;
        var pipeline = _pipeline;
        _loopTask = Task.Run(() => LoopAsync(region, pipeline, cts.Token));
    }

    private void StopLoop()
    {
        try
        {
            _loopCts?.Cancel();
            _loopTask?.Wait(1500);
        }
        catch (Exception) { /* loop ends with cancellation */ }
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
        _pipeline?.Reset();
    }

    /// <summary>
    /// Sequential capture loop: one frame at a time (never overlapping, never queued). If a frame takes
    /// longer than the interval, the next one starts immediately; missed ticks are skipped.
    /// </summary>
    private async Task LoopAsync(Rectangle region, SubtitlePipeline pipeline, CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / _settings.Fps);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int errors = 0;
        while (!ct.IsCancellationRequested)
        {
            var t0 = sw.Elapsed;
            try
            {
                if (_resetRequested) { _resetRequested = false; pipeline.Reset(); }
                if (!_paused && _ocr != null)
                {
                    string text = await _ocr.ReadAsync(region, _settings.BeyazYaziFiltresi, _settings.BeyazEsik).ConfigureAwait(false);
                    pipeline.OnFrame(text, DateTime.UtcNow);
                }
                errors = 0;
            }
            catch (Exception ex)
            {
                // Transient capture failures (UAC prompt, lock screen, resolution change): log once, back off.
                errors++;
                ErrorLog.Write(ex, "frame");
            }

            var wait = errors > 3 ? TimeSpan.FromSeconds(1) : interval - (sw.Elapsed - t0);
            try
            {
                if (wait > TimeSpan.FromMilliseconds(1)) await Task.Delay(wait, ct).ConfigureAwait(false);
                else await Task.Yield();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Called by the scheduler (under its lock): only marshal to the UI thread here.</summary>
    private void OnDisplay(DisplayUpdate u) => _ui.Post(ApplyDisplay, u);

    private void ApplyDisplay(object? state)
    {
        try
        {
            if (state is not DisplayUpdate u || u.Seq <= _lastAppliedSeq) return; // never apply stale updates
            _lastAppliedSeq = u.Seq;
            if (_previewing) { _previewing = false; _previewTimer.Stop(); }
            if (u.Kind == DisplayKind.Hide || _paused || _selecting) { _overlay.HideText(); return; }
            _overlay.ShowText(u.Shown ?? string.Empty, u.IsFallback);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "overlay"); }
    }

    private async Task WarmAsync()
    {
        try
        {
            _lastNetUse = DateTime.UtcNow;
            await _translator.WarmUpAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "warm"); }
    }

    private void OnWarmTimer()
    {
        try
        {
            // HTTP/2 keep-alive PINGs (every 30 s) hold the connection; refresh with a real request only
            // after 4 idle minutes so we never add load that could trigger throttling.
            if (_paused || DateTime.UtcNow - _lastNetUse < TimeSpan.FromMinutes(4)) return;
            _ = WarmAsync();
        }
        catch (Exception ex) { ErrorLog.Write(ex, "warm-timer"); }
    }

    private static void SafeRun(Action a, string where)
    {
        try { a(); } catch (Exception ex) { ErrorLog.Write(ex, where); }
    }

    // ---------------- actions ----------------

    private void SelectRegion()
    {
        if (_selecting) return;
        _selecting = true;
        bool wasRunning = _loopTask != null;
        StopLoop();
        _overlay.HideText();
        try
        {
            using var f = new RegionSelectorForm();
            if (f.ShowDialog() == DialogResult.OK)
            {
                var r = f.SelectedRegion;
                _settings.RegionX = r.X;
                _settings.RegionY = r.Y;
                _settings.RegionWidth = r.Width;
                _settings.RegionHeight = r.Height;
                SaveSettings();
                StateChanged?.Invoke(this, EventArgs.Empty);
                Balloon("Bölge kaydedildi. Çeviri başladı.", ToolTipIcon.Info);
                StartLoop();
            }
            else if (wasRunning || _settings.HasRegion)
            {
                StartLoop();
            }
            else
            {
                Balloon("Bölge seçilmedi. F8 ile ya da simgeye çift tıklayarak seçebilirsin.", ToolTipIcon.Info);
            }
        }
        finally { _selecting = false; }
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _pauseItem.Text = _paused ? "Devam et (F9)" : "Duraklat (F9)";
        _tray.Text = _paused ? AppName + " (duraklatıldı)" : AppName;
        if (_paused)
        {
            _overlay.HideText();
            _resetRequested = true;
        }
        Balloon(_paused ? "Duraklatıldı. Devam için F9." : "Devam ediyor.", ToolTipIcon.None);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- settings window ----------------

    private bool _previewing;

    public AppSettings Current => _settings;
    public bool IsPaused => _paused;

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(this);
        _settingsForm.FormClosed += (_, _) => { _settingsForm?.Dispose(); _settingsForm = null; };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    public void Apply(AppSettings e, bool persist)
    {
        var oldOptions = System.Text.Json.JsonSerializer.Serialize(_settings.ToProcessorOptions());
        string oldSource = _settings.KaynakDil;

        _settings.KaynakDil = e.KaynakDil;
        _settings.HedefDil = e.HedefDil;
        _settings.OzelIsimleriKoru = e.OzelIsimleriKoru;
        _settings.BeyazYaziFiltresi = e.BeyazYaziFiltresi;
        _settings.YaziBoyutu = e.YaziBoyutu;
        _settings.YaziRengi = e.YaziRengi;
        _settings.ArkaPlanOpakligi = e.ArkaPlanOpakligi;
        _settings.OverlayKonumu = e.OverlayKonumu;
        _settings.DikeyKaydirma = e.DikeyKaydirma;
        _settings.Sozluk = e.Sozluk?.Select(g => new SozlukGirdisi(g.Ingilizce, g.Turkce)).ToList() ?? new List<SozlukGirdisi>();
        _settings.Clamp();

        // Appearance: redraw the visible line right away.
        var text = _overlay.CurrentText;
        bool fb = _overlay.CurrentIsFallback;
        _overlay.Configure(Region, _settings);
        _overlay.Refresh(text, fb);

        // Translation rules: new options, drop cached translations, re-translate the current line.
        var opts = _settings.ToProcessorOptions();
        if (System.Text.Json.JsonSerializer.Serialize(opts) != oldOptions)
        {
            _processor.Options = opts;
            _cache.Clear();
            _resetRequested = true;
        }

        // Subtitle language: the OCR engine must change too.
        if (!string.Equals(oldSource, _settings.KaynakDil, StringComparison.OrdinalIgnoreCase) && _ocr != null)
        {
            bool wasRunning = _loopTask != null;
            StopLoop();
            var old = _ocr;
            var fresh = OcrCapture.Create(out _, _settings.KaynakDil);
            if (fresh != null) { _ocr = fresh; old.Dispose(); }
            if (!_ocr.Matches(_settings.KaynakDil))
                Balloon("Bu dil için Windows metin tanıma (OCR) paketi yüklü değil; mevcut dille okunacak.", ToolTipIcon.Warning);
            if (wasRunning || _settings.HasRegion) StartLoop();
        }

        if (persist) SaveSettings();
    }

    public void ShowPreview()
    {
        if (!_settings.HasRegion) { Balloon("Önce altyazı bölgesini seç (F8).", ToolTipIcon.Info); return; }
        _previewing = true;
        _overlay.Configure(Region, _settings);
        _overlay.ShowText("Örnek çeviri: Siktir, Port Gellhorn'a geç kaldık!", false);
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    void ISettingsHost.SelectRegion() => SafeRun(SelectRegion, "select");
    void ISettingsHost.TogglePause() => SafeRun(TogglePause, "pause");

    private void Balloon(string text, ToolTipIcon icon)
    {
        try { _tray?.ShowBalloonTip(2500, AppName, text, icon); } catch (Exception) { /* ignore */ }
    }

    private bool _quitting;

    private void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        try { _warmTimer.Dispose(); } catch (Exception) { }
        try { _previewTimer.Dispose(); } catch (Exception) { }
        try { if (_settingsForm is { IsDisposed: false }) _settingsForm.Close(); } catch (Exception) { }
        StopLoop();
        _pipeline?.Dispose();
        _hotkeys?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _overlay?.Close();
        _ocr?.Dispose();
        _translator.Dispose();
        ExitThread();
    }
}
