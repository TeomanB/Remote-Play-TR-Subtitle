using AltyaziCevirici.Core;

namespace AltyaziCevirici;

internal sealed class TrayApp : ApplicationContext, IAppHost
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
    private MainWindow? _center;
    private readonly ToolStripMenuItem _runItem;
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 3500 };
    public event EventHandler? StateChanged;
    private readonly TranslationCache _cache = new(4000);
    private readonly System.Threading.Timer _warmTimer;
    private SubtitlePipeline? _pipeline;
    private long _lastAppliedSeq;
    private DateTime _lastNetUse = DateTime.UtcNow;
    private readonly SynchronizationContext _ui;
    private Icon _icon;

    private OcrCapture? _ocr;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private volatile bool _paused;
    private volatile bool _resetRequested;
    private bool _selecting;
    private int _lastLatencyMs;
    private string _lastText = "";

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
        _runItem = new ToolStripMenuItem("Başlat", null, (_, _) => SafeRun(ToggleRun, "run")) { Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold) };
        var showItem = new ToolStripMenuItem("Göster", null, (_, _) => SafeRun(ShowMain, "show"));

        var menu = new ContextMenuStrip();
        menu.Items.Add(showItem);
        menu.Items.Add(_runItem);
        menu.Items.Add(new ToolStripMenuItem("Bölge Seç (F8)", null, (_, _) => SafeRun(SelectRegion, "select")));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Çıkış (F10)", null, (_, _) => SafeRun(Quit, "quit")));

        _icon = IconFactory.Tray(false);
        _tray = new NotifyIcon { Icon = _icon, Text = AppName, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => SafeRun(ShowMain, "show");

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
        _center = new MainWindow(this);
        _center.Show();
        YenidenYukleOcr();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

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
            var started = System.Diagnostics.Stopwatch.StartNew();
            var task = app._processor.TranslateAsync(text, ct);
            _ = task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                    app._lastLatencyMs = (int)started.ElapsedMilliseconds;
            }, TaskScheduler.Default);
            return task;
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
                    if (!OcrGate.Kabul(text, _settings.HedefDil)) text = string.Empty;
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
            _lastText = u.Shown ?? string.Empty;
            _overlay.ShowText(_lastText, u.IsFallback);
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
            bool wasVisible = _center is { Visible: true };
            _center?.Hide();
            try
            {
            if (f.ShowDialog() == DialogResult.OK)
            {
                var r = f.SelectedRegion;
                _settings.RegionX = r.X;
                _settings.RegionY = r.Y;
                _settings.RegionWidth = r.Width;
                _settings.RegionHeight = r.Height;
                SaveSettings();
                StateChanged?.Invoke(this, EventArgs.Empty);
                Balloon("Bölge kaydedildi. Çeviri için Başlat'a bas.", ToolTipIcon.Info);
                if (wasRunning) StartLoop();
            }
            else if (wasRunning)
            {
                StartLoop();
            }
            }
            finally
            {
                if (wasVisible) ShowMain();
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
        ApplyTrayIcon();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- settings window ----------------

    private bool _previewing;

    public AppSettings Current => _settings;
    public bool IsPaused => _paused;

    public void ShowMain()
    {
        if (_center == null || _center.IsDisposed) _center = new MainWindow(this);
        if (!_center.Visible) _center.Show();
        if (_center.WindowState == FormWindowState.Minimized) _center.WindowState = FormWindowState.Normal;
        _center.Activate();
    }

    public void NotifyMinimizedToTray()
    {
        if (_settings.TepsiIpucuGosterildi) return;
        _settings.TepsiIpucuGosterildi = true;
        SaveSettings();
        Balloon("Pencere tepsiye küçültüldü. Geri açmak için simgeye çift tıkla ya da menüden Göster'i seç.", ToolTipIcon.Info);
    }

    public void SkipRelease(string tag)
    {
        _settings.AtlananSurum = tag;
        SaveSettings();
    }

    private void ToggleRun()
    {
        if (IsRunning) StopCapture();
        else StartCapture();
    }

    public void StartCapture()
    {
        if (!_settings.HasRegion)
        {
            Balloon("Önce Bölge Seç ile altyazı şeridini işaretle.", ToolTipIcon.Info);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_ocr == null)
        {
            Balloon("Windows metin tanıma (OCR) kullanılamıyor.", ToolTipIcon.Warning);
            return;
        }
        _paused = false;
        _pauseItem.Text = "Duraklat (F9)";
        StartLoop();
        _runItem.Text = "Durdur";
        ApplyTrayIcon();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopCapture()
    {
        StopLoop();
        _overlay.HideText();
        _runItem.Text = "Başlat";
        ApplyTrayIcon();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTrayIcon()
    {
        if (_quitting || _tray == null) return;
        var next = IconFactory.Tray(IsRunning && !_paused);
        _tray.Icon = next;
        var old = _icon;
        _icon = next;
        if (old != null && !ReferenceEquals(old, next)) old.Dispose();
    }

    public void BeginPlacementEdit()
    {
        bool wasRunning = IsRunning;
        if (wasRunning) StopLoop();
        _overlay.HideText();
        var copy = _settings.Clone();
        using var editor = new PlacementEditorForm(copy, Region, "Örnek çeviri: Siktir, Port Gellhorn'a geç kaldık!");
        if (editor.ShowDialog(_center) == DialogResult.OK)
            Apply(copy, persist: true);
        if (wasRunning) StartLoop();
        else ShowPreview();
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
        _settings.DisCizgi = e.DisCizgi;
        _settings.KonumAyarlandi = e.KonumAyarlandi;
        _settings.BolgeyiTakipEt = e.BolgeyiTakipEt;
        _settings.OnAyar = e.OnAyar;
        _settings.KutuX = e.KutuX;
        _settings.KutuY = e.KutuY;
        _settings.KutuGenislik = e.KutuGenislik;
        _settings.KutuYukseklik = e.KutuYukseklik;
        _settings.AcikPaketler = e.AcikPaketler?.ToList() ?? new List<string>(SozlukKutuphanesi.VarsayilanAcik);
        _settings.BagilX = e.BagilX;
        _settings.BagilY = e.BagilY;
        _settings.MonitorAdi = e.MonitorAdi;
        _settings.AcilistaGuncellemeDenetle = e.AcilistaGuncellemeDenetle;
        if (!string.IsNullOrEmpty(e.AtlananSurum)) _settings.AtlananSurum = e.AtlananSurum;
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
        if (!string.Equals(oldSource, _settings.KaynakDil, StringComparison.OrdinalIgnoreCase))
        {
            bool wasRunning = _loopTask != null;
            StopLoop();
            YenidenYukleOcr();
            if (wasRunning && !OcrPaketiEksik) StartLoop();
        }

        if (persist) SaveSettings();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool OcrPaketiEksik { get; private set; }

    public bool YenidenYukleOcr()
    {
        bool missing = !OcrPaketKur.KaynakHazir(_settings.KaynakDil);
        var fresh = OcrCapture.Create(out _, _settings.KaynakDil);
        if (fresh != null)
        {
            var old = _ocr;
            _ocr = fresh;
            old?.Dispose();
        }
        OcrPaketiEksik = missing || _ocr == null || !_ocr.Matches(_settings.KaynakDil);
        return !OcrPaketiEksik;
    }

    public void ShowPreview()
    {
        if (!_settings.HasRegion) { Balloon("Önce altyazı bölgesini seç (F8).", ToolTipIcon.Info); return; }
        _previewing = true;
        _overlay.Configure(Region, _settings);
        _overlay.ShowText(MainWindow.OrnekAltyazi, false);
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    public bool IsRunning => _loopTask != null;
    public string SonMetin => _lastText;
    public int SonGecikmeMs => _lastLatencyMs;
    public string OcrDurum => _ocr == null ? "OCR yok" : !IsRunning ? "Durdu" : _paused ? "Duraklatıldı" : "Çalışıyor";
    public bool KurulumKopyasi => GuncellemeIstegi.KurulumKopyasi;

    void IAppHost.SelectRegion() => SafeRun(SelectRegion, "select");
    void IAppHost.TogglePause() => SafeRun(TogglePause, "pause");

    private void Balloon(string text, ToolTipIcon icon)
    {
        try { _tray?.ShowBalloonTip(2500, AppName, text, icon); } catch (Exception) { /* ignore */ }
    }

    private bool _quitting;

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        try { _warmTimer.Dispose(); } catch (Exception) { }
        try { _previewTimer.Dispose(); } catch (Exception) { }
        try { if (_center is { IsDisposed: false }) { _center.AllowExit(); _center.Close(); } } catch (Exception) { }
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
