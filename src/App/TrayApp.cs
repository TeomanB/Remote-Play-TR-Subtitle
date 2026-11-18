using AltyaziCevirici.Core;

namespace AltyaziCevirici;

internal sealed class TrayApp : ApplicationContext, IAppHost
{
    private const string AppName = AppInfo.DisplayName;

    private readonly string _settingsPath;
    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly HotkeyWindow _hotkeys;
    private readonly OverlayForm _overlay;
    private readonly OverlayForm _goal;
    private readonly GoogleTranslator _translator = new();
    private readonly TranslationProcessor _processor;
    private HazirKutuphane _hazir = HazirKutuphane.Bos;
    private MainWindow? _center;
    private readonly ToolStripMenuItem _runItem;
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 3500 };
    public event EventHandler? StateChanged;
    private readonly TranslationCache _cache = new(4000);
    private readonly System.Threading.Timer _warmTimer;
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 2000 };
    private int _loopGen;
    private long _lastLoopTicks = DateTime.UtcNow.Ticks;
    private int _recoveries;
    private SubtitlePipeline? _pipeline;
    private long _lastAppliedSeq;
    private readonly Dictionary<string, OverlayForm> _ekOverlays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _ekSeq = new(StringComparer.Ordinal);
    private readonly List<TranslationProcessor> _ekProcessors = new();
    private readonly List<SubtitlePipeline> _ekPipes = new();
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
        if (Ui.Theme.ForcedScale is float forced) Ui.Theme.SetScale(forced);
        else using (var screen = Graphics.FromHwnd(IntPtr.Zero)) Ui.Theme.SetScale(screen.DpiX / 96f);
        _settingsPath = AppPaths.SettingsFile;
        _settings = AppSettings.Load(_settingsPath);
        var monitor = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        if (_settings.YerlesikIpucu(monitor.X, monitor.Y, monitor.Width, monitor.Height))
            SaveSettings();

        _overlay = new OverlayForm();
        _goal = new OverlayForm();
        _ = _overlay.Handle; // create handle on UI thread
        _ = _goal.Handle;

        _hazir = HazirKutuphane.Yukle();
        _processor = new TranslationProcessor(_translator, _settings.ToProcessorOptions(_hazir));
        _overlay.Configure(Region, _settings);
        ConfigureGoal();
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); if (_previewing) { _previewing = false; HideOverlays(); } };
        _watch.Tick += (_, _) => WatchLoop();
        _watch.Start();

        _pauseItem = new ToolStripMenuItem("Duraklat (F9)", null, (_, _) => SafeRun(TogglePause, "pause"));
        _runItem = new ToolStripMenuItem("Başlat", null, (_, _) => SafeRun(ToggleRun, "run"));
        var showItem = new ToolStripMenuItem("Göster", null, (_, _) => SafeRun(ShowMain, "show")) { Font = Ui.Theme.BodySemi };

        var menu = new ContextMenuStrip { ShowImageMargin = false, Font = Ui.Theme.Body };
        Ui.Menus.Style(menu);
        menu.Items.Add(showItem);
        menu.Items.Add(_runItem);
        menu.Items.Add(new ToolStripMenuItem("Bölge Seç (F8)", null, (_, _) => SafeRun(SelectRegion, "select")));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Çıkış (F10)", null, (_, _) => SafeRun(Quit, "quit")));
        foreach (ToolStripItem item in menu.Items)
            if (item is ToolStripMenuItem) item.Padding = new Padding(Ui.Theme.D(4), Ui.Theme.D(5), Ui.Theme.D(16), Ui.Theme.D(5));

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
        IconFactory.RefreshShellIcon();
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

    private sealed class BoundBackend(TrayApp app, TranslationProcessor processor) : ITranslateBackend
    {
        public Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct)
        {
            app._lastNetUse = DateTime.UtcNow;
            return processor.TranslateAsync(text, ct);
        }
    }

    private void StartLoop()
    {
        StopLoop();
        if (_ocr == null) return;
        _settings.EnsureEkAlanlar();
        bool main = _settings.HasRegion;
        bool extra = _settings.EkAlanlar!.Any(a => a.Acik && a.RegionWidth >= 20 && a.RegionHeight >= 12);
        if (!main && !extra) return;
        if (main)
        {
            _overlay.Configure(Region, _settings);
            ConfigureGoal();
        }
        SyncEkOverlays();
        _pipeline?.Dispose();
        DisposeEkPipes();
        _pipeline = main ? CreatePipeline() : null;
        var jobs = new List<(string Slot, Rectangle Region, SubtitlePipeline Pipe)>();
        if (main && _pipeline != null) jobs.Add(("main", Region, _pipeline));
        foreach (var alan in _settings.EkAlanlar!.Where(a => a.Acik && a.RegionWidth >= 20 && a.RegionHeight >= 12))
        {
            var proc = new TranslationProcessor(_translator, _settings.ToProcessorOptions(_hazir));
            _ekProcessors.Add(proc);
            string id = alan.Id;
            var pipe = new SubtitlePipeline(
                new SubtitleTracker(_settings.BenzerlikEsigi, TimeSpan.FromMilliseconds(_settings.GizlemeMs)),
                new TranslationScheduler(new BoundBackend(this, proc), _cache, u => OnEkDisplay(id, u)));
            _ekPipes.Add(pipe);
            jobs.Add((id, new Rectangle(alan.RegionX, alan.RegionY, alan.RegionWidth, alan.RegionHeight), pipe));
        }
        var cts = _loopCts = new CancellationTokenSource();
        var captured = jobs;
        int gen = ++_loopGen;
        Volatile.Write(ref _lastLoopTicks, DateTime.UtcNow.Ticks);
        _loopTask = Task.Run(() => LoopAsync(captured, gen, cts.Token));
    }

    private void WatchLoop()
    {
        try
        {
            if (_quitting || _paused || _selecting || _loopTask == null) return;
            var lastBeat = new DateTime(Volatile.Read(ref _lastLoopTicks), DateTimeKind.Utc);
            bool loopStale = LoopWatch.ShouldRestart(lastBeat, DateTime.UtcNow, running: true, paused: false);
            bool ocrStuck = _ocr?.BusyTooLong(TimeSpan.FromSeconds(8)) == true;
            if (!loopStale && !ocrStuck) { _recoveries = 0; return; }
            if (_recoveries >= 6) return;
            _recoveries++;
            ErrorLog.Write(new TimeoutException(ocrStuck ? "OCR call stuck" : "capture loop produced no frame"), "watchdog");
            var stuck = _ocr;
            YenidenYukleOcr();
            if (stuck != null && !ReferenceEquals(stuck, _ocr) && stuck.InFlight)
                _ = stuck.WhenQuiet().ContinueWith(t => { try { stuck.Dispose(); } catch (Exception) { } }, TaskScheduler.Default);
            StartLoop();
        }
        catch (Exception ex) { ErrorLog.Write(ex, "watchdog"); }
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
        foreach (var pipe in _ekPipes) pipe.Reset();
    }

    /// <summary>
    /// Sequential capture loop: one shared screen copy, then each area. Unchanged areas skip OCR.
    /// </summary>
    private async Task LoopAsync(List<(string Slot, Rectangle Region, SubtitlePipeline Pipe)> jobs, int gen, CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / _settings.Fps);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int errors = 0;
        while (!ct.IsCancellationRequested && gen == Volatile.Read(ref _loopGen))
        {
            Volatile.Write(ref _lastLoopTicks, DateTime.UtcNow.Ticks);
            var t0 = sw.Elapsed;
            try
            {
                if (_resetRequested)
                {
                    _resetRequested = false;
                    foreach (var job in jobs) job.Pipe.Reset();
                }
                if (!_paused && _ocr != null && jobs.Count > 0)
                {
                    var texts = await _ocr.ReadFrameAsync(jobs.Select(j => (j.Slot, j.Region)).ToList(), _settings.BeyazYaziFiltresi, _settings.BeyazEsik).ConfigureAwait(false);
                    for (int i = 0; i < jobs.Count && i < texts.Length; i++)
                    {
                        string text = texts[i];
                        if (!OcrGate.Kabul(text, _settings.HedefDil)) text = string.Empty;
                        jobs[i].Pipe.OnFrame(text, DateTime.UtcNow);
                    }
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

    private void OnEkDisplay(string id, DisplayUpdate u) => _ui.Post(state => ApplyEk(id, (DisplayUpdate)state!), u);

    private void ApplyEk(string id, DisplayUpdate u)
    {
        try
        {
            if (!_ekOverlays.TryGetValue(id, out var form)) return;
            long seq = _ekSeq.GetValueOrDefault(id);
            if (u.Seq <= seq) return;
            _ekSeq[id] = u.Seq;
            if (_previewing) { _previewing = false; _previewTimer.Stop(); }
            if (u.Kind == DisplayKind.Hide || _paused || _selecting) { form.HideText(); return; }
            var alan = _settings.EkAlanlar?.FirstOrDefault(a => a.Id == id);
            if (alan != null) ConfigureEk(form, alan);
            form.ShowText(u.Shown ?? string.Empty, u.IsFallback);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "overlay-ek"); }
    }

    private void HideOverlays()
    {
        _overlay.HideText();
        _goal.HideText();
        foreach (var form in _ekOverlays.Values) form.HideText();
    }

    private void DisposeEkPipes()
    {
        foreach (var pipe in _ekPipes) pipe.Dispose();
        _ekPipes.Clear();
        _ekProcessors.Clear();
    }

    private void SyncEkOverlays()
    {
        _settings.EnsureEkAlanlar();
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alan in _settings.EkAlanlar!)
        {
            keep.Add(alan.Id);
            if (!_ekOverlays.TryGetValue(alan.Id, out var form))
            {
                form = new OverlayForm();
                _ = form.Handle;
                _ekOverlays[alan.Id] = form;
            }
            ConfigureEk(form, alan);
            if (!alan.Acik) form.HideText();
        }
        foreach (var id in _ekOverlays.Keys.Where(id => !keep.Contains(id)).ToArray())
        {
            _ekOverlays[id].HideText();
            _ekOverlays[id].Close();
            _ekOverlays.Remove(id);
            _ekSeq.Remove(id);
        }
    }

    private void ConfigureEk(OverlayForm form, EkAlan alan)
    {
        var look = _settings.Clone();
        look.KonumAyarlandi = alan.KonumAyarlandi || alan.KutuGenislik >= SubtitleLayout.MinWidth;
        look.KutuX = alan.KutuX;
        look.KutuY = alan.KutuY;
        look.KutuGenislik = alan.KutuGenislik > 0 ? alan.KutuGenislik : Math.Max(SubtitleLayout.MinWidth, 360);
        look.KutuYukseklik = alan.KutuYukseklik > 0 ? alan.KutuYukseklik : 48;
        look.DisCizgiRengi = alan.Renk;
        if (look.DisCizgi < 2) look.DisCizgi = 3;
        form.Configure(new Rectangle(alan.RegionX, alan.RegionY, Math.Max(1, alan.RegionWidth), Math.Max(1, alan.RegionHeight)), look);
    }

    private static string EkStamp(AppSettings s)
    {
        s.EnsureEkAlanlar();
        return string.Join("|", s.EkAlanlar!.Select(a => a.Id + ":" + a.Acik + ":" + a.RegionX + "," + a.RegionY + "," + a.RegionWidth + "," + a.RegionHeight + ":" + a.KutuX + "," + a.KutuY + "," + a.KutuGenislik + "," + a.KutuYukseklik));
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
            if (u.Kind == DisplayKind.Hide || _paused || _selecting) { _overlay.HideText(); _goal.HideText(); return; }
            bool objective = string.Equals(u.LineKind, "objective", StringComparison.OrdinalIgnoreCase);
            var box = objective ? _goal : _overlay;
            if (objective) ConfigureGoal();
            else _overlay.Configure(Region, _settings);
            _lastText = u.Shown ?? string.Empty;
            box.ShowText(_lastText, u.IsFallback);
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
        HideOverlays();
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
            HideOverlays();
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
        HideOverlays();
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

    public void Apply(AppSettings e, bool persist)
    {
        var oldOptions = OptionsStamp();
        string oldSource = _settings.KaynakDil;
        var oldRegion = Region;
        string oldEk = EkStamp(_settings);

        // OCR strip edited on the preview (not only through F8).
        if (e.RegionWidth >= 20 && e.RegionHeight >= 10)
        {
            _settings.RegionX = e.RegionX;
            _settings.RegionY = e.RegionY;
            _settings.RegionWidth = e.RegionWidth;
            _settings.RegionHeight = e.RegionHeight;
        }

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
        _settings.DisCizgiRengi = e.DisCizgiRengi;
        _settings.YaziTipi = e.YaziTipi;
        _settings.OrijinalKilitli = e.OrijinalKilitli;
        _settings.CeviriKilitli = e.CeviriKilitli;
        _settings.KaynakPencere = e.KaynakPencere;
        _settings.PencereX = e.PencereX;
        _settings.PencereY = e.PencereY;
        _settings.PencereGenislik = e.PencereGenislik;
        _settings.PencereYukseklik = e.PencereYukseklik;
        _settings.PencereBuyuk = e.PencereBuyuk;
        _settings.SonSayfa = e.SonSayfa;
        _settings.WindowsIleBaslat = e.WindowsIleBaslat;
        _settings.TanitimGoruldu = e.TanitimGoruldu;
        _settings.SeciliHazirOyun = e.SeciliHazirOyun;
        _settings.SadeceHazirPaket = e.SadeceHazirPaket?.ToList() ?? new List<string>();
        _settings.KonumAyarlandi = e.KonumAyarlandi;
        _settings.BolgeyiTakipEt = e.BolgeyiTakipEt;
        _settings.OnAyar = e.OnAyar;
        _settings.KutuX = e.KutuX;
        _settings.KutuY = e.KutuY;
        _settings.KutuGenislik = e.KutuGenislik;
        _settings.KutuYukseklik = e.KutuYukseklik;
        _settings.GorevKonumAyarlandi = e.GorevKonumAyarlandi;
        _settings.GorevKutuX = e.GorevKutuX;
        _settings.GorevKutuY = e.GorevKutuY;
        _settings.GorevKutuGenislik = e.GorevKutuGenislik;
        _settings.GorevKutuYukseklik = e.GorevKutuYukseklik;
        _settings.EkAlanlar = e.EkAlanlar?.Select(a => a.Copy()).ToList() ?? new List<EkAlan>();
        _settings.IpucuVarsayilanEklendi = e.IpucuVarsayilanEklendi;
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
        ConfigureGoal();

        // Translation rules: new options, drop cached translations, re-translate the current line.
        _hazir = HazirKutuphane.Yukle();
        var opts = _settings.ToProcessorOptions(_hazir);
        if (OptionsStamp() != oldOptions)
        {
            _processor.Options = opts;
            foreach (var proc in _ekProcessors) proc.Options = opts;
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
        else if (_loopTask != null && !_selecting && (Region != oldRegion || oldEk != EkStamp(_settings)))
        {
            StartLoop(); // the capture loop holds the rectangles it started with
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
            if (old != null && !old.InFlight) old.Dispose();
        }
        OcrPaketiEksik = missing || _ocr == null || !_ocr.Matches(_settings.KaynakDil);
        return !OcrPaketiEksik;
    }

    private void ConfigureGoal()
    {
        var look = _settings.Clone();
        look.KonumAyarlandi = _settings.GorevKonumAyarlandi || _settings.GorevKutuGenislik >= SubtitleLayout.MinWidth;
        look.KutuX = _settings.GorevKutuX;
        look.KutuY = _settings.GorevKutuY;
        look.KutuGenislik = _settings.GorevKutuGenislik > 0 ? _settings.GorevKutuGenislik : Math.Max(SubtitleLayout.MinWidth, _settings.KutuGenislik > 0 ? _settings.KutuGenislik - 80 : 420);
        look.KutuYukseklik = _settings.GorevKutuYukseklik > 0 ? _settings.GorevKutuYukseklik : 48;
        look.DisCizgiRengi = "#7A4FBF";
        look.YaziRengi = "#EDE4FF";
        if (look.DisCizgi < 2) look.DisCizgi = 3;
        _goal.Configure(Region, look);
    }

    public void ShowPreview()
    {
        if (!_settings.HasRegion) { Balloon("Önce altyazı bölgesini seç (F8).", ToolTipIcon.Info); return; }
        _previewing = true;
        _overlay.Configure(Region, _settings);
        _overlay.ShowText(MainWindow.OrnekAltyazi, false);
        ConfigureGoal();
        _goal.ShowText("Phoenix'i yeniden boya.", false);
        SyncEkOverlays();
        foreach (var alan in _settings.EkAlanlar!.Where(a => a.Acik))
            if (_ekOverlays.TryGetValue(alan.Id, out var form)) form.ShowText("Türbine bakmak için R3'e bas.", false);
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private string OptionsStamp()
    {
        var opts = _settings.ToProcessorOptions(_hazir);
        return System.Text.Json.JsonSerializer.Serialize(opts) + "\n" + _hazir.Imza + "\n" + (opts.Hazir?.Oyun ?? "") + ":" + (opts.Hazir?.Satir ?? 0);
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
        try { _watch.Dispose(); } catch (Exception) { }
        try { _previewTimer.Dispose(); } catch (Exception) { }
        try { if (_center is { IsDisposed: false }) { _center.AllowExit(); _center.Close(); } } catch (Exception) { }
        StopLoop();
        _pipeline?.Dispose();
        _hotkeys?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _overlay?.Close();
        _goal?.Close();
        DisposeEkPipes();
        foreach (var form in _ekOverlays.Values) form.Close();
        _ekOverlays.Clear();
        _ocr?.Dispose();
        _translator.Dispose();
        ExitThread();
    }
}
