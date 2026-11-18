using System.Runtime.InteropServices;
using AltyaziCevirici.Core;

namespace AltyaziCevirici;

/// <summary>Borderless control center: a large preview is the canvas, settings sit underneath.</summary>
internal sealed class MainWindow : Form
{
    public const string OrnekAltyazi = PreviewCanvas.Ornek;

    private readonly IAppHost _host;
    private readonly PreviewCanvas _preview = new();
    private readonly FlowLayoutPanel _sources = new() { Height = 44, Dock = DockStyle.Top, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 6, 8, 0) };
    private readonly Label _status = new() { AutoSize = false, Height = 28, Dock = DockStyle.Bottom, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
    private readonly Panel _banner = new() { Height = 40, Dock = DockStyle.Top, Visible = false };
    private readonly Panel _ocrBanner = new() { Height = 40, Dock = DockStyle.Top, Visible = false };
    private readonly Label _bannerText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
    private readonly Label _ocrText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
    private readonly ProgressBar _progress = new() { Width = 140, Height = 16, Visible = false };
    private readonly Panel _pages = new() { Height = 230, Dock = DockStyle.Bottom, Padding = new Padding(12, 4, 12, 8) };
    private readonly FlatButton _start = new() { Text = "Başlat", Primary = true, Width = 140, Height = 40 };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 400 };
    private readonly List<FlatButton> _segments = new();
    private readonly List<(string Id, ToggleSwitch Toggle)> _packs = new();
    private Rectangle _screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
    private IntPtr _window;
    private int _page;
    private bool _loading = true;
    private bool _allowExit;
    private bool _checking;
    private bool _ocrInstalling;
    private bool _sync;
    private GuncellemeBilgisi? _offer;
    private FlatSlider? _size, _opacity, _outline;
    private Label? _sizeValue, _opacityValue, _outlineValue;
    private TextBox? _search;
    private ListBox? _glossary;
    private CheckBox? _autoUpdate;

    public MainWindow(IAppHost host)
    {
        _host = host;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 780);
        MinimumSize = new Size(960, 640);
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Text;
        Font = UiTheme.Font(10f);
        DoubleBuffered = true;
        Text = "Altyazı Çevirici";
        try { Icon = IconFactory.Window(); } catch (Exception) { /* exe icon still applies */ }

        Controls.Add(_preview);
        Controls.Add(BuildSourceRow());
        Controls.Add(_pages);
        Controls.Add(_status);
        Controls.Add(BuildOcrBanner());
        Controls.Add(BuildUpdateBanner());
        Controls.Add(BuildTitle());
        _preview.RegionsChanged += (_, _) => SaveRegions();
        _start.Click += (_, _) => { if (_host.IsRunning) _host.StopCapture(); else _host.StartCapture(); };
        _tick.Tick += (_, _) => { Grab(); RefreshStatus(); };
        _tick.Start();
        BuildPages();
        LoadFromHost();
        _host.StateChanged += OnState;
        Shown += (_, _) =>
        {
            RefreshOcrBanner();
            if (_host.Current.AcilistaGuncellemeDenetle) _ = CheckUpdatesAsync(false);
        };
        _loading = false;
    }

    public void AllowExit() => _allowExit = true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); }
        catch (Exception ex) { ErrorLog.Write(ex, "affinity-main"); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _host.NotifyMinimizedToTray();
            return;
        }
        _tick.Stop();
        _host.StateChanged -= OnState;
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1;
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                var p = PointToClient(Cursor.Position);
                const int g = 6;
                bool left = p.X <= g, right = p.X >= ClientSize.Width - g, top = p.Y <= g, bottom = p.Y >= ClientSize.Height - g;
                int hit = (top, bottom, left, right) switch
                {
                    (true, _, true, _) => 13,
                    (true, _, _, true) => 14,
                    (_, true, true, _) => 16,
                    (_, true, _, true) => 17,
                    (true, _, _, _) => 12,
                    (_, true, _, _) => 15,
                    (_, _, true, _) => 10,
                    (_, _, _, true) => 11,
                    _ => HTCLIENT,
                };
                if (hit != HTCLIENT) m.Result = (IntPtr)hit;
            }
            return;
        }
        base.WndProc(ref m);
    }

    private Panel BuildTitle()
    {
        var bar = new Panel { Height = 40, Dock = DockStyle.Top, BackColor = UiTheme.Bg };
        var title = new Label { Text = "Altyazı Çevirici", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), Font = UiTheme.Font(11f, FontStyle.Bold), ForeColor = UiTheme.Text };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 132, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = UiTheme.Bg };
        buttons.Controls.Add(CaptionButton("✕", Close));
        buttons.Controls.Add(CaptionButton("▢", () => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized));
        buttons.Controls.Add(CaptionButton("—", () => WindowState = FormWindowState.Minimized));
        bar.Controls.Add(title);
        bar.Controls.Add(buttons);
        return bar;
    }

    private static FlatButton CaptionButton(string text, Action click)
    {
        var b = new FlatButton { Text = text, Width = 40, Height = 28, Margin = new Padding(2) };
        b.Click += (_, _) => click();
        return b;
    }

    private Panel BuildSourceRow()
    {
        var row = new Panel { Height = 92, Dock = DockStyle.Bottom, BackColor = UiTheme.Bg, Padding = new Padding(8, 0, 8, 4) };
        _sources.BackColor = UiTheme.Bg;
        _sources.Dock = DockStyle.Top;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, WrapContents = false, BackColor = UiTheme.Bg };
        var fallback = new FlatButton { Text = "Tam ekran seçici", Width = 160 };
        fallback.Click += (_, _) => _host.SelectRegion();
        var windows = new FlatButton { Text = "Pencere seç", Width = 130 };
        windows.Click += (_, _) => PickWindow();
        tools.Controls.Add(_start);
        tools.Controls.Add(fallback);
        tools.Controls.Add(windows);
        row.Controls.Add(tools);
        row.Controls.Add(_sources);
        return row;
    }

    private Panel BuildUpdateBanner()
    {
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 430, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var update = new FlatButton { Text = "Güncelle", Width = 100, Primary = true };
        var later = new FlatButton { Text = "Daha sonra", Width = 110 };
        var skip = new FlatButton { Text = "Bu sürümü atla", Width = 140 };
        update.Click += (_, _) => _ = InstallAsync();
        later.Click += (_, _) => _banner.Visible = false;
        skip.Click += (_, _) => { if (_offer != null) _host.SkipRelease(_offer.Tag); _banner.Visible = false; };
        _progress.Margin = new Padding(8, 10, 8, 0);
        buttons.Controls.Add(skip);
        buttons.Controls.Add(later);
        buttons.Controls.Add(update);
        buttons.Controls.Add(_progress);
        _banner.BackColor = Color.FromArgb(28, 48, 44);
        _banner.Controls.Add(_bannerText);
        _banner.Controls.Add(buttons);
        return _banner;
    }

    private Panel BuildOcrBanner()
    {
        var kur = new FlatButton { Text = "Kur", Width = 80, Primary = true };
        kur.Click += (_, _) => _ = KurOcrAsync(kur);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 100, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.Add(kur);
        _ocrBanner.BackColor = Color.FromArgb(64, 42, 28);
        _ocrBanner.Controls.Add(_ocrText);
        _ocrBanner.Controls.Add(buttons);
        return _ocrBanner;
    }

    private void BuildPages()
    {
        var seg = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, BackColor = UiTheme.Bg };
        foreach (var name in new[] { "Çeviri", "Görünüm", "Sözlük", "Kısayollar" })
        {
            int index = _segments.Count;
            var b = new FlatButton { Text = name, Width = 120 };
            b.Click += (_, _) => ShowPage(index);
            _segments.Add(b);
            seg.Controls.Add(b);
        }
        _pages.BackColor = UiTheme.Bg;
        _pages.Controls.Add(seg);
        ShowPage(0);
    }

    private void ShowPage(int index)
    {
        _page = index;
        for (int i = 0; i < _segments.Count; i++) _segments[i].Primary = i == index;
        for (int i = _pages.Controls.Count - 1; i >= 0; i--)
        {
            if (_pages.Controls[i] is FlowLayoutPanel) continue;
            _pages.Controls.RemoveAt(i);
        }
        var body = index switch
        {
            1 => LookPage(),
            2 => GlossaryPage(),
            3 => HotkeyPage(),
            _ => TranslatePage(),
        };
        body.Dock = DockStyle.Fill;
        _pages.Controls.Add(body);
        body.BringToFront();
    }

    private FlowLayoutPanel TranslatePage()
    {
        var flow = Column();
        flow.Controls.Add(Note("Küfürler her zaman sansürsüz çevrilir. Altyazı dili ve çeviri dili:"));
        var src = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 700 };
        foreach (var (code, name) in new[] { ("en", "İngilizce"), ("de", "Almanca"), ("fr", "Fransızca"), ("es", "İspanyolca"), ("tr", "Türkçe") })
        {
            var b = new FlatButton { Text = name, Width = 110, Tag = code };
            b.Primary = _host.Current.KaynakDil.StartsWith(code, StringComparison.OrdinalIgnoreCase);
            b.Click += (_, _) => Patch(s => s.KaynakDil = code);
            src.Controls.Add(b);
        }
        flow.Controls.Add(src);
        flow.Controls.Add(ToggleRow("Cümle ortasındaki isimleri koru", _host.Current.OzelIsimleriKoru, v => Patch(s => s.OzelIsimleriKoru = v)));
        flow.Controls.Add(ToggleRow("Beyaz yazı filtresi", _host.Current.BeyazYaziFiltresi, v => Patch(s => s.BeyazYaziFiltresi = v)));
        return flow;
    }

    private FlowLayoutPanel LookPage()
    {
        var flow = Column();
        _size = Slider(10, 72, (int)_host.Current.YaziBoyutu, out _sizeValue);
        _opacity = Slider(0, 100, _host.Current.ArkaPlanOpakligi, out _opacityValue);
        _outline = Slider(0, 12, _host.Current.DisCizgi, out _outlineValue);
        _size.ValueChanged += (_, _) => { _sizeValue!.Text = _size.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); Patch(s => s.YaziBoyutu = _size.Value); _host.ShowPreview(); };
        _opacity.ValueChanged += (_, _) => { _opacityValue!.Text = "%" + _opacity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); Patch(s => s.ArkaPlanOpakligi = _opacity.Value); _host.ShowPreview(); };
        _outline.ValueChanged += (_, _) => { _outlineValue!.Text = _outline.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); Patch(s => s.DisCizgi = _outline.Value); _host.ShowPreview(); };
        flow.Controls.Add(Labeled("Yazı boyutu", _size, _sizeValue));
        flow.Controls.Add(Labeled("Arka plan", _opacity, _opacityValue));
        flow.Controls.Add(Labeled("Dış çizgi", _outline, _outlineValue));
        var colors = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        foreach (var (name, hex) in new[] { ("Beyaz", "#FFFFFF"), ("Sarı", "#FFE14D"), ("Turkuaz", "#3ED6B0") })
        {
            var b = new FlatButton { Text = name, Width = 90 };
            b.Click += (_, _) => { Patch(s => s.YaziRengi = hex); _host.ShowPreview(); };
            colors.Controls.Add(b);
        }
        flow.Controls.Add(colors);
        flow.Controls.Add(Note("Örnek cümle hem tuvalde hem oyunun üstündeki kutuda anında değişir."));
        return flow;
    }

    private FlowLayoutPanel GlossaryPage()
    {
        var flow = Column();
        var toggles = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(860, 0) };
        _packs.Clear();
        IEnumerable<string> packIds = _host.Current.AcikPaketler == null ? SozlukKutuphanesi.VarsayilanAcik : _host.Current.AcikPaketler;
        var open = new HashSet<string>(packIds, StringComparer.OrdinalIgnoreCase);
        foreach (var paket in SozlukKutuphanesi.Paketler())
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 16, 4) };
            var toggle = new ToggleSwitch { On = open.Contains(paket.Id) };
            var id = paket.Id;
            toggle.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                Patch(s =>
                {
                    s.AcikPaketler ??= new List<string>();
                    if (toggle.On) { if (!s.AcikPaketler.Contains(id)) s.AcikPaketler.Add(id); }
                    else s.AcikPaketler.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
                });
                FillGlossary();
            };
            _packs.Add((id, toggle));
            row.Controls.Add(toggle);
            row.Controls.Add(new Label { Text = paket.Ad, AutoSize = true, Margin = new Padding(6, 4, 0, 0), ForeColor = UiTheme.Text });
            toggles.Controls.Add(row);
        }
        flow.Controls.Add(toggles);
        _search = new TextBox { Width = 220, BackColor = UiTheme.Card, ForeColor = UiTheme.Text, BorderStyle = BorderStyle.FixedSingle };
        _search.TextChanged += (_, _) => FillGlossary();
        var add = new FlatButton { Text = "Ekle", Width = 70 };
        var del = new FlatButton { Text = "Sil", Width = 70 };
        var export = new FlatButton { Text = "Dışa aktar", Width = 110 };
        var import = new FlatButton { Text = "İçe aktar", Width = 110 };
        add.Click += (_, _) => AddEntry();
        del.Click += (_, _) => DeleteEntry();
        export.Click += (_, _) => ExportEntries();
        import.Click += (_, _) => ImportEntries();
        var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        tools.Controls.Add(_search);
        tools.Controls.Add(add);
        tools.Controls.Add(del);
        tools.Controls.Add(export);
        tools.Controls.Add(import);
        flow.Controls.Add(tools);
        _glossary = new ListBox { Height = 90, Width = 820, BackColor = UiTheme.Card, ForeColor = UiTheme.Text, BorderStyle = BorderStyle.None };
        flow.Controls.Add(_glossary);
        FillGlossary();
        return flow;
    }

    private FlowLayoutPanel HotkeyPage()
    {
        var flow = Column();
        flow.Controls.Add(Note("F8 bölge (tam ekran yedek)   F9 duraklat   F10 çıkış. Pencereyi kapatmak tepsiye küçültür."));
        _autoUpdate = new CheckBox { Text = "Açılışta güncellemeleri denetle", AutoSize = true, ForeColor = UiTheme.Text, Checked = _host.Current.AcilistaGuncellemeDenetle };
        _autoUpdate.CheckedChanged += (_, _) => { if (!_loading && _autoUpdate != null) Patch(s => s.AcilistaGuncellemeDenetle = _autoUpdate.Checked); };
        flow.Controls.Add(_autoUpdate);
        var check = new FlatButton { Text = "Güncellemeleri denetle", Width = 200 };
        check.Click += (_, _) => _ = CheckUpdatesAsync(true);
        flow.Controls.Add(check);
        flow.Controls.Add(Note("Sürüm " + SurumKarsilastirma.Tag(GuncellemeIstegi.UygulamaSurumu) + ". Yazı tanıma paketi eksikse üstteki Kur düğmesi yönetici izni ister."));
        return flow;
    }

    private void LoadFromHost()
    {
        _sync = true;
        var s = _host.Current;
        _screen = MonitorBounds(s);
        RebuildSources();
        var ocr = s.HasRegion ? new Rectangle(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight) : Rectangle.Empty;
        var cap = CaptionRect(s, ocr);
        _preview.SetRegions(ocr, cap, s, true);
        _sync = false;
        RefreshStatus();
    }

    private void RebuildSources()
    {
        _sources.Controls.Clear();
        _sources.Controls.Add(new Label { Text = "Kaynak", AutoSize = true, Margin = new Padding(0, 8, 8, 0), ForeColor = UiTheme.Muted });
        int n = 1;
        foreach (var screen in Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            var b = new FlatButton { Text = "Ekran " + n + (screen.Primary ? " · ana" : ""), Width = 130, Primary = bounds == _screen && _window == IntPtr.Zero };
            b.Click += (_, _) => { _window = IntPtr.Zero; _screen = bounds; Patch(s => s.MonitorAdi = screen.DeviceName); RebuildSources(); Grab(); };
            _sources.Controls.Add(b);
            n++;
        }
    }

    private void SaveRegions()
    {
        if (_sync || _loading) return;
        var s = _host.Current.Clone();
        var ocr = _preview.OcrRegion;
        s.RegionX = ocr.X;
        s.RegionY = ocr.Y;
        s.RegionWidth = ocr.Width;
        s.RegionHeight = ocr.Height;
        var cap = _preview.CaptionRegion;
        if (cap.Width >= 20)
        {
            var region = new EkranDikdortgen(ocr.X, ocr.Y, ocr.Width, ocr.Height);
            SubtitleLayout.Remember(s, new EkranDikdortgen(cap.X, cap.Y, cap.Width, cap.Height), region, follow: false);
            s.KutuYukseklik = cap.Height;
            s.KonumAyarlandi = true;
        }
        _host.Apply(s, true);
        _host.ShowPreview();
    }

    private void Grab()
    {
        if (!Visible || _screen.Width < 20 || _screen.Height < 20) return;
        try
        {
            int maxW = 1280;
            int w = Math.Min(maxW, _screen.Width);
            int h = Math.Max(1, _screen.Height * w / _screen.Width);
            using var full = new Bitmap(_screen.Width, _screen.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(full))
                g.CopyFromScreen(_screen.X, _screen.Y, 0, 0, _screen.Size);
            var small = new Bitmap(w, h);
            using (var g = Graphics.FromImage(small))
                g.DrawImage(full, new Rectangle(0, 0, w, h));
            _preview.SetFrame(small, _screen);
        }
        catch (Exception) { /* display changing */ }
    }

    private void RefreshStatus()
    {
        string line = string.IsNullOrWhiteSpace(_host.SonMetin) ? "—" : _host.SonMetin;
        string latency = _host.SonGecikmeMs > 0 ? _host.SonGecikmeMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms" : "—";
        _status.Text = "OCR: " + _host.OcrDurum + "    Son çeviri: " + line + "    Gecikme: " + latency;
        _status.ForeColor = UiTheme.Muted;
        bool run = _host.IsRunning;
        _start.Text = run ? "Durdur" : "Başlat";
        _start.Primary = !run;
        _start.Danger = run;
        var s = _host.Current;
        if (_sync) return;
        var ocr = s.HasRegion ? new Rectangle(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight) : _preview.OcrRegion;
        var cap = CaptionRect(s, ocr);
        _preview.SetRegions(ocr.Width > 0 ? ocr : _preview.OcrRegion, cap.Width > 0 ? cap : _preview.CaptionRegion, s, true);
    }

    private static Rectangle CaptionRect(AppSettings s, Rectangle ocr)
    {
        if (s.KonumAyarlandi && s.KutuGenislik >= 20)
            return new Rectangle(s.KutuX, s.KutuY, s.KutuGenislik, s.KutuYukseklik > 0 ? s.KutuYukseklik : 72);
        if (ocr.Width < 20) return Rectangle.Empty;
        var screen = new EkranDikdortgen(0, 0, 8000, 8000);
        var placed = SubtitleLayout.Resolve(s, new EkranDikdortgen(ocr.X, ocr.Y, ocr.Width, ocr.Height), screen, Math.Max(ocr.Width, 400), 72);
        return new Rectangle(placed.X, placed.Y, placed.Width, placed.Height);
    }

    private static Rectangle MonitorBounds(AppSettings s)
    {
        foreach (var screen in Screen.AllScreens)
            if (!string.IsNullOrEmpty(s.MonitorAdi) && screen.DeviceName.Equals(s.MonitorAdi, StringComparison.OrdinalIgnoreCase))
                return screen.Bounds;
        if (s.HasRegion) return Screen.FromRectangle(new Rectangle(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight)).Bounds;
        return Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
    }

    private void OnState(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        try { BeginInvoke(new Action(() => { RefreshStatus(); RefreshOcrBanner(); })); }
        catch (InvalidOperationException) { /* closing */ }
    }

    private void Patch(Action<AppSettings> edit)
    {
        if (_loading) return;
        var s = _host.Current.Clone();
        edit(s);
        _host.Apply(s, true);
    }

    private void FillGlossary()
    {
        if (_glossary == null) return;
        string q = _search?.Text?.Trim() ?? "";
        _glossary.Items.Clear();
        foreach (var g in SozlukKutuphanesi.Etkin(_host.Current))
        {
            string line = g.Ingilizce + (string.IsNullOrEmpty(g.Turkce) ? "  →  koru" : "  →  " + g.Turkce);
            if (q.Length > 0 && line.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            _glossary.Items.Add(line);
        }
    }

    private void AddEntry()
    {
        string? term = Prompt("İngilizce terim");
        if (string.IsNullOrWhiteSpace(term)) return;
        string? tr = Prompt("Türkçe (boşsa olduğu gibi kalır)") ?? "";
        Patch(s =>
        {
            s.Sozluk ??= new List<SozlukGirdisi>();
            s.Sozluk.RemoveAll(g => g.Ingilizce.Equals(term, StringComparison.OrdinalIgnoreCase));
            s.Sozluk.Add(new SozlukGirdisi(term.Trim(), tr.Trim()));
        });
        FillGlossary();
    }

    private void DeleteEntry()
    {
        if (_glossary?.SelectedItem is not string line) return;
        string term = line.Split("  →  ")[0];
        Patch(s =>
        {
            s.Sozluk ??= new List<SozlukGirdisi>();
            s.Sozluk.RemoveAll(g => g.Ingilizce.Equals(term, StringComparison.OrdinalIgnoreCase));
            if (SozlukKutuphanesi.Etkin(new AppSettings { AcikPaketler = s.AcikPaketler, Sozluk = new() }).Any(g => g.Ingilizce.Equals(term, StringComparison.OrdinalIgnoreCase)))
                s.Sozluk.Add(new SozlukGirdisi(term, term));
        });
        FillGlossary();
    }

    private void ExportEntries()
    {
        using var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "sozluk.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, SozlukKutuphanesi.DisaAktar(_host.Current.Sozluk ?? new List<SozlukGirdisi>()));
    }

    private void ImportEntries()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var rows = SozlukKutuphanesi.IceAktar(File.ReadAllText(dlg.FileName));
        Patch(s =>
        {
            s.Sozluk ??= new List<SozlukGirdisi>();
            foreach (var row in rows)
            {
                s.Sozluk.RemoveAll(g => g.Ingilizce.Equals(row.Ingilizce, StringComparison.OrdinalIgnoreCase));
                s.Sozluk.Add(row);
            }
        });
        FillGlossary();
    }

    private string? Prompt(string title)
    {
        using var form = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(360, 110), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            BackColor = UiTheme.Bg, ForeColor = UiTheme.Text,
        };
        var box = new TextBox { Left = 12, Top = 16, Width = 336, BackColor = UiTheme.Card, ForeColor = UiTheme.Text };
        var ok = new FlatButton { Text = "Tamam", Primary = true, Left = 180, Top = 56, Width = 80 };
        var cancel = new FlatButton { Text = "Vazgeç", Left = 268, Top = 56, Width = 80 };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; form.Close(); };
        cancel.Click += (_, _) => form.Close();
        form.Controls.Add(box);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.ShowDialog(this);
        return result;
    }

    private void PickWindow()
    {
        var windows = WindowList();
        if (windows.Count == 0) return;
        using var form = new Form
        {
            Text = "Pencere", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 320), BackColor = UiTheme.Bg, ForeColor = UiTheme.Text,
        };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = UiTheme.Card, ForeColor = UiTheme.Text, BorderStyle = BorderStyle.None };
        foreach (var w in windows) list.Items.Add(w.Title);
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var chosen = windows[list.SelectedIndex];
            _window = chosen.Handle;
            _screen = chosen.Bounds;
            form.Close();
            Grab();
        };
        form.Controls.Add(list);
        form.ShowDialog(this);
    }

    private void RefreshOcrBanner()
    {
        if (_ocrInstalling) return;
        if (_host.OcrPaketiEksik)
        {
            _ocrText.Text = OcrDilPaketi.Banner(_host.Current.KaynakDil);
            _ocrBanner.Visible = true;
        }
        else _ocrBanner.Visible = false;
    }

    private async Task KurOcrAsync(FlatButton button)
    {
        if (_ocrInstalling) return;
        _ocrInstalling = true;
        button.Enabled = false;
        button.Text = "Kuruluyor…";
        try
        {
            var (ok, mesaj) = await OcrPaketKur.KurAsync(_host.Current.KaynakDil).ConfigureAwait(true);
            if (IsDisposed) return;
            if (!ok)
            {
                MessageBox.Show(this, mesaj, "Altyazı Çevirici", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_host.YenidenYukleOcr())
                MessageBox.Show(this, "Yazı tanıma paketi hazır.", "Altyazı Çevirici", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, "Paket kuruldu. Tanıma açılmazsa Altyazı Çevirici'yi kapatıp yeniden aç.", "Altyazı Çevirici", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            _ocrInstalling = false;
            if (!IsDisposed) { button.Text = "Kur"; button.Enabled = true; RefreshOcrBanner(); }
        }
    }

    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var info = await GuncellemeIstegi.SorgulaAsync().ConfigureAwait(true);
            if (IsDisposed) return;
            if (info == null)
            {
                if (interactive) MessageBox.Show(this, "Güncelleme denetlenemedi.", "Altyazı Çevirici");
                return;
            }
            if (!SurumKarsilastirma.IsNewer(info.Tag, GuncellemeIstegi.UygulamaSurumu))
            {
                if (interactive) MessageBox.Show(this, "Bu sürüm güncel.", "Altyazı Çevirici");
                return;
            }
            if (!interactive && !GuncellemeOkuyucu.BannerGoster(GuncellemeIstegi.UygulamaSurumu, info, _host.Current.AtlananSurum))
                return;
            _offer = info;
            _bannerText.Text = "Yeni sürüm var: " + info.Tag + " — Güncelle / Daha sonra / Bu sürümü atla";
            _banner.Visible = true;
        }
        finally { _checking = false; }
    }

    private async Task InstallAsync()
    {
        var info = _offer;
        if (info == null) return;
        if (!_host.KurulumKopyasi || !GuncellemeOkuyucu.GuvenliKurulumAdresi(info.KurulumUrl))
        {
            GuncellemeIstegi.SayfayiAc(info.Sayfa);
            return;
        }
        _progress.Visible = true;
        var path = await GuncellemeIstegi.IndirAsync(info, new Progress<double>(p =>
        {
            if (IsHandleCreated) BeginInvoke(new Action(() => _progress.Value = (int)Math.Clamp(p * 100, 0, 100)));
        }), CancellationToken.None).ConfigureAwait(true);
        if (path == null)
        {
            GuncellemeIstegi.SayfayiAc(info.Sayfa);
            return;
        }
        try { GuncellemeIstegi.KurulumuBaslat(path); }
        catch (Exception ex) { ErrorLog.Write(ex, "update"); GuncellemeIstegi.SayfayiAc(info.Sayfa); return; }
        _host.Quit();
    }

    private static List<(IntPtr Handle, string Title, Rectangle Bounds)> WindowList()
    {
        var list = new List<(IntPtr, string, Rectangle)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var chars = new char[512];
            int n = GetWindowText(h, chars, chars.Length);
            if (n <= 0) return true;
            string title = new string(chars, 0, n);
            if (!GetWindowRect(h, out var rect)) return true;
            int w = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (w < 200 || height < 120) return true;
            list.Add((h, title, new Rectangle(rect.Left, rect.Top, w, height)));
            return list.Count < 40;
        }, IntPtr.Zero);
        return list;
    }

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static FlowLayoutPanel Column() => new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = UiTheme.Bg, Padding = new Padding(4),
    };

    private static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(860, 0), ForeColor = UiTheme.Muted, Margin = new Padding(0, 4, 0, 4),
    };

    private static FlowLayoutPanel ToggleRow(string text, bool on, Action<bool> set)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var toggle = new ToggleSwitch { On = on };
        toggle.CheckedChanged += (_, _) => set(toggle.On);
        row.Controls.Add(toggle);
        row.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(8, 4, 0, 0), ForeColor = UiTheme.Text });
        return row;
    }

    private static FlatSlider Slider(int min, int max, int value, out Label readout)
    {
        var slider = new FlatSlider { Minimum = min, Maximum = max, Width = 240 };
        slider.Value = Math.Clamp(value, min, max);
        readout = new Label { AutoSize = true, ForeColor = UiTheme.Text, Margin = new Padding(8, 4, 0, 0), Text = slider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return slider;
    }

    private static FlowLayoutPanel Labeled(string name, Control slider, Label value)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = name, AutoSize = true, Width = 110, ForeColor = UiTheme.Text, Margin = new Padding(0, 4, 8, 0) });
        row.Controls.Add(slider);
        row.Controls.Add(value);
        return row;
    }
}
