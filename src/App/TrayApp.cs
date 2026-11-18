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
    private readonly GoogleTranslator _translator = new();
    private readonly TranslationProcessor _processor;
    private HazirKutuphane _hazir = HazirKutuphane.Bos;
    private MainWindow? _center;
    private readonly ToolStripMenuItem _runItem;
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 3500 };
    public event EventHandler? StateChanged;
    private readonly TranslationCache _cache = new(4000);
    private readonly System.Threading.Timer _warmTimer;
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

        _settings.EnsureAlanlar();
        _hazir = HazirKutuphane.Yukle();
        _processor = new TranslationProcessor(_translator, _settings.ToProcessorOptions(_hazir));
        _overlay = OverlayFor(_settings.Alanlar![0].Id);
        ConfigureOverlay(_overlay, _settings.Alanlar[0]);
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            if (!_previewing) return;
            _previewing = false;
            foreach (var o in _areaOverlays.Values) o.HideText();
        };

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

    /// <summary>Wraps the translator to remember the last network use (for idle warm-up).</summary>
    private sealed class TrackingBackend(TrayApp app, TranslationProcessor processor) : ITranslateBackend
    {
        public Task<TranslateOutcome> TranslateAsync(string text, CancellationToken ct)
        {
            app._lastNetUse = DateTime.UtcNow;
            var started = System.Diagnostics.Stopwatch.StartNew();
            var task = processor.TranslateAsync(text, ct);
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
        _settings.EnsureAlanlar();
        if (_ocr == null || !_settings.AcikAlanlar().Any()) return;
        foreach (var area in _settings.Alanlar!) ConfigureOverlay(OverlayFor(area.Id), area);
        var cts = _loopCts = new CancellationTokenSource();
        var areas = _settings.Alanlar!.Select(a => a.Copy()).ToList();
        _loopTask = Task.Run(() => LoopAsync(areas, cts.Token));
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
    }

    /// <summary>
    /// Sequential capture loop: one frame at a time (never overlapping, never queued). If a frame takes
    /// longer than the interval, the next one starts immediately; missed ticks are skipped.
    /// </summary>
    private async Task LoopAsync(List<YakalamaAlani> areas, CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / _settings.Fps);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int errors = 0;
        var pipes = areas.ToDictionary(a => a.Id, a => CreateAreaPipeline(a.Id));
        while (!ct.IsCancellationRequested)
        {
            var t0 = sw.Elapsed;
            try
            {
                if (_resetRequested)
                {
                    _resetRequested = false;
                    foreach (var p in pipes.Values) p.Reset();
                }
                if (!_paused && _ocr != null)
                {
                    foreach (var area in areas)
                    {
                        if (!pipes.TryGetValue(area.Id, out var pipe)) continue;
                        if (!area.Acik || area.RegionWidth < 20 || area.RegionHeight < 10)
                        {
                            pipe.Reset();
                            continue;
                        }
                        var rect = new Rectangle(area.RegionX, area.RegionY, area.RegionWidth, area.RegionHeight);
                        string text = await _ocr.ReadAsync(area.Id, rect, _settings.BeyazYaziFiltresi, _settings.BeyazEsik).ConfigureAwait(false);
                        if (!OcrGate.Kabul(text, _settings.HedefDil)) text = string.Empty;
                        pipe.OnFrame(text, DateTime.UtcNow);
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
        foreach (var pipe in pipes.Values) pipe.Dispose();
    }

    /// <summary>Called by the scheduler (under its lock): only marshal to the UI thread here.</summary>
    private SubtitlePipeline CreateAreaPipeline(string id)
    {
        var proc = new TranslationProcessor(_translator, _settings.ToProcessorOptions(_hazir));
        var tracker = new SubtitleTracker(_settings.BenzerlikEsigi, TimeSpan.FromMilliseconds(_settings.GizlemeMs));
        var scheduler = new TranslationScheduler(new TrackingBackend(this, proc), _cache, update => OnAreaDisplay(id, update));
        return new SubtitlePipeline(tracker, scheduler);
    }

    private void OnAreaDisplay(string id, DisplayUpdate u) => _ui.Post(state => ApplyArea(id, (DisplayUpdate)state!), u);

    private readonly Dictionary<string, OverlayForm> _areaOverlays = new(StringComparer.Ordinal);

    private OverlayForm OverlayFor(string id)
    {
        if (_areaOverlays.TryGetValue(id, out var existing)) return existing;
        var created = new OverlayForm();
        _ = created.Handle;
        _areaOverlays[id] = created;
        return created;
    }

    private void ConfigureOverlay(OverlayForm overlay, YakalamaAlani area)
    {
        var look = _settings.Clone();
        area.MirrorTo(look);
        if (area.KutuGenislik >= SubtitleLayout.MinWidth) look.KonumAyarlandi = true;
        look.DisCizgiRengi = area.Renk;
        if (look.DisCizgi < 2) look.DisCizgi = 3;
        overlay.Configure(new Rectangle(area.RegionX, area.RegionY, area.RegionWidth, area.RegionHeight), look);
    }

    private readonly Dictionary<string, long> _areaSeq = new(StringComparer.Ordinal);

    private void ApplyArea(string id, DisplayUpdate u)
    {
        try
        {
            if (!_areaOverlays.TryGetValue(id, out var overlay)) return;
            if (_areaSeq.TryGetValue(id, out var seen) && u.Seq <= seen) return;
            _areaSeq[id] = u.Seq;
            if (_previewing) { _previewing = false; _previewTimer.Stop(); }
            if (u.Kind == DisplayKind.Hide || _paused || _selecting) { overlay.HideText(); return; }
            string shown = u.Shown ?? string.Empty;
            if (id == _settings.Alanlar![Math.Clamp(_settings.SeciliAlan, 0, _settings.Alanlar.Count - 1)].Id)
                _lastText = shown;
            var area = _settings.Alanlar?.FirstOrDefault(a => a.Id == id);
            if (area != null) ConfigureOverlay(overlay, area);
            overlay.ShowText(shown, u.IsFallback);
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
                _settings.EnsureAlanlar();
                var alan = _settings.Alanlar![Math.Clamp(_settings.SeciliAlan, 0, _settings.Alanlar.Count - 1)];
                alan.RegionX = r.X;
                alan.RegionY = r.Y;
                alan.RegionWidth = r.Width;
                alan.RegionHeight = r.Height;
                alan.MirrorTo(_settings);
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

    public void Apply(AppSettings e, bool persist)
    {
        var oldOptions = OptionsStamp();
        string oldSource = _settings.KaynakDil;
        var oldRegion = Region;

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
        _settings.SeciliAlan = e.SeciliAlan;
        _settings.Alanlar = e.Alanlar?.Select(a => a.Copy()).ToList();
        _settings.SeciliHazirOyun = e.SeciliHazirOyun;
        _settings.SadeceHazirPaket = e.SadeceHazirPaket?.ToList() ?? new List<string>();
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
        _hazir = HazirKutuphane.Yukle();
        var opts = _settings.ToProcessorOptions(_hazir);
        if (OptionsStamp() != oldOptions)
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
        else if (Region != oldRegion && _loopTask != null && !_selecting)
        {
            StartLoop(); // the capture loop holds the region it started with
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
        _settings.EnsureAlanlar();
        if (!_settings.AcikAlanlar().Any()) { Balloon("Önce bir altyazı alanı aç.", ToolTipIcon.Info); return; }
        _previewing = true;
        foreach (var area in _settings.Alanlar!.Where(a => a.Acik))
        {
            var overlay = OverlayFor(area.Id);
            ConfigureOverlay(overlay, area);
            overlay.ShowText(MainWindow.OrnekAltyazi, false);
        }
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
        try { _previewTimer.Dispose(); } catch (Exception) { }
        try { if (_center is { IsDisposed: false }) { _center.AllowExit(); _center.Close(); } } catch (Exception) { }
        StopLoop();
        _hotkeys?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        foreach (var overlay in _areaOverlays.Values) overlay.Close();
        _ocr?.Dispose();
        _translator.Dispose();
        ExitThread();
    }
}
