using AltyaziCevirici.Core;

namespace AltyaziCevirici;

/// <summary>OBS-style control center. Closing it hides to the tray; it does not exit.</summary>
internal sealed class ControlCenterForm : Form
{
    private static readonly (string Code, string Name)[] SourceLangs =
    {
        ("en", "İngilizce"), ("de", "Almanca"), ("fr", "Fransızca"), ("es", "İspanyolca"), ("it", "İtalyanca"),
        ("pt", "Portekizce"), ("ru", "Rusça"), ("pl", "Lehçe"), ("nl", "Felemenkçe"),
    };
    private static readonly (string Code, string Name)[] TargetLangs =
    {
        ("tr", "Türkçe"), ("en", "İngilizce"), ("de", "Almanca"), ("fr", "Fransızca"), ("es", "İspanyolca"),
        ("it", "İtalyanca"), ("ru", "Rusça"), ("ar", "Arapça"), ("az", "Azerbaycan Türkçesi"),
    };

    private readonly IAppHost _host;
    private bool _loading = true;
    private bool _allowExit;
    private bool _checking;
    private GuncellemeBilgisi? _offer;

    private readonly Panel _banner = new() { Dock = DockStyle.Top, Height = 46, Visible = false };
    private readonly Panel _ocrBanner = new() { Dock = DockStyle.Top, Height = 46, Visible = false };
    private readonly Label _ocrBannerText = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
    private readonly Button _ocrKur = new() { Text = "Kur", AutoSize = true };
    private bool _ocrInstalling;
    private readonly Label _bannerText = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
    private readonly ProgressBar _progress = new() { Width = 160, Height = 18, Visible = false, Minimum = 0, Maximum = 100 };
    private readonly Button _start = new() { Height = 46, Dock = DockStyle.Top, Text = "Başlat" };
    private readonly Label _regionLabel = new() { AutoSize = true };
    private readonly ComboBox _monitor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly List<string> _monitorIds = new();
    private readonly CheckBox _follow = new() { AutoSize = true, Text = "Altyazı bölgeyi takip etsin" };
    private readonly CheckBox _filter = new() { AutoSize = true, Text = "Beyaz yazı filtresi" };
    private readonly ComboBox _src = Combo();
    private readonly ComboBox _dst = Combo();
    private readonly CheckBox _names = new() { AutoSize = true, Text = "Cümle ortasındaki isimleri olduğu gibi bırak" };
    private readonly TrackBar _size = new() { Minimum = 10, Maximum = 72, TickFrequency = 8, Width = 220 };
    private readonly TrackBar _opacity = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 220 };
    private readonly TrackBar _outline = new() { Minimum = 0, Maximum = 12, TickFrequency = 2, Width = 220 };
    private readonly Label _sizeLabel = new() { AutoSize = true };
    private readonly Label _opacityLabel = new() { AutoSize = true };
    private readonly Label _outlineLabel = new() { AutoSize = true };
    private readonly Panel _swatch = new() { Width = 36, Height = 22, BorderStyle = BorderStyle.FixedSingle };
    private readonly DataGridView _grid = new();
    private readonly CheckBox _autoUpdate = new() { AutoSize = true, Text = "Açılışta güncellemeleri denetle" };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly Label _previewHint = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 250 };

    public ControlCenterForm(IAppHost host)
    {
        _host = host;
        Text = "Altyazı Çevirici";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 700);
        MinimumSize = new Size(900, 560);
        try { Icon = IconFactory.Window(); } catch (Exception) { /* default */ }

        Controls.Add(BuildBody());
        Controls.Add(BuildOcrBanner());
        Controls.Add(BuildBanner());
        Controls.Add(BuildStatus());
        DarkTheme.Apply(this);
        _start.FlatAppearance.BorderSize = 0;
        _banner.BackColor = Color.FromArgb(46, 58, 82);
        _ocrBanner.BackColor = Color.FromArgb(92, 58, 42);
        _ocrKur.BackColor = DarkTheme.Accent;
        _ocrKur.ForeColor = Color.White;

        LoadValues();
        _host.StateChanged += OnState;
        _tick.Tick += (_, _) => { GrabPreview(); RefreshStatus(); };
        _tick.Start();
        _loading = false;
        Shown += (_, _) =>
        {
            RefreshOcrBanner();
            if (_host.Current.AcilistaGuncellemeDenetle) _ = CheckUpdatesAsync(false);
        };
    }

    public void AllowExit() => _allowExit = true;

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
        _preview.Image?.Dispose();
        base.OnFormClosing(e);
    }

    private Panel BuildBanner()
    {
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Width = 460, Padding = new Padding(4),
        };
        var update = new Button { Text = "Güncelle", AutoSize = true };
        var later = new Button { Text = "Daha sonra", AutoSize = true };
        var skip = new Button { Text = "Bu sürümü atla", AutoSize = true };
        update.Click += (_, _) => _ = InstallAsync();
        later.Click += (_, _) => _banner.Visible = false;
        skip.Click += (_, _) =>
        {
            if (_offer != null) _host.SkipRelease(_offer.Tag);
            _banner.Visible = false;
        };
        _progress.Margin = new Padding(8, 10, 8, 0);
        buttons.Controls.Add(skip);
        buttons.Controls.Add(later);
        buttons.Controls.Add(update);
        buttons.Controls.Add(_progress);
        _bannerText.Padding = new Padding(12, 0, 0, 0);
        _banner.Controls.Add(_bannerText);
        _banner.Controls.Add(buttons);
        return _banner;
    }

    private Panel BuildStatus()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 32 };
        _status.Padding = new Padding(10, 0, 0, 0);
        bar.Controls.Add(_status);
        return bar;
    }

    private TableLayoutPanel BuildBody()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(8) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        split.Controls.Add(BuildSources(), 0, 0);
        split.Controls.Add(BuildPreview(), 1, 0);
        split.Controls.Add(BuildTabs(), 2, 0);
        return split;
    }

    private Panel BuildSources()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _start.Click += (_, _) => { if (_host.IsRunning) _host.StopCapture(); else _host.StartCapture(); };
        var title = new Label { Text = "Kaynaklar", Dock = DockStyle.Top, Height = 28, Font = new Font(Font, FontStyle.Bold) };
        var source = new Label
        {
            Text = "Ekran bölgesi",
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
        };
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var pick = new Button { Text = "Bölge Seç", AutoSize = true, Width = 240 };
        pick.Click += (_, _) => _host.SelectRegion();
        var kur = new Button { Text = "Yazı tanıma paketini kur", AutoSize = true, Width = 240 };
        kur.Click += (_, _) => _ = KurOcrAsync();
        body.Controls.Add(pick);
        body.Controls.Add(kur);
        body.Controls.Add(_regionLabel);
        body.Controls.Add(new Label { Text = "Ekran", AutoSize = true, Margin = new Padding(3, 12, 3, 0) });
        _monitor.SelectedIndexChanged += (_, _) => Changed();
        body.Controls.Add(_monitor);
        body.Controls.Add(_follow);
        _follow.CheckedChanged += (_, _) => Changed();
        var edit = new Button { Text = "Altyazı konumunu düzenle", AutoSize = true, Width = 240 };
        edit.Click += (_, _) => _host.BeginPlacementEdit();
        body.Controls.Add(edit);
        body.Controls.Add(PresetRow());
        _filter.CheckedChanged += (_, _) => Changed();
        body.Controls.Add(_filter);
        panel.Controls.Add(body);
        panel.Controls.Add(source);
        panel.Controls.Add(title);
        panel.Controls.Add(_start);
        return panel;
    }

    private FlowLayoutPanel PresetRow()
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 250, Margin = new Padding(0, 8, 0, 0) };
        foreach (var (text, preset) in new[]
                 {
                     ("Alt orta", AltyaziOnAyar.AltOrta), ("Üst orta", AltyaziOnAyar.UstOrta),
                     ("Bölgenin altı", AltyaziOnAyar.BolgeninAlti), ("Bölgenin üstü", AltyaziOnAyar.BolgeninUstu),
                 })
        {
            var b = new Button { Text = text, AutoSize = true };
            var chosen = preset;
            b.Click += (_, _) => ApplyPreset(chosen);
            row.Controls.Add(b);
        }
        return row;
    }

    private Panel BuildPreview()
    {
        var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        _previewHint.Text = "Bölge seçilmedi.\nAltyazı şeridini işaretlemek için Bölge Seç'e bas.\nKayıtlı bir bölge varsa burada canlı görünür.";
        _previewHint.ForeColor = DarkTheme.Muted;
        wrap.Controls.Add(_preview);
        wrap.Controls.Add(_previewHint);
        return wrap;
    }

    private TabControl BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(TranslationTab());
        tabs.TabPages.Add(LookTab());
        tabs.TabPages.Add(GlossaryTab());
        tabs.TabPages.Add(HotkeysTab());
        return tabs;
    }

    private TabPage TranslationTab()
    {
        var page = new TabPage("Çeviri");
        var t = Grid();
        foreach (var (code, name) in SourceLangs) _src.Items.Add($"{name} ({code})");
        foreach (var (code, name) in TargetLangs) _dst.Items.Add($"{name} ({code})");
        _src.SelectedIndexChanged += (_, _) => Changed();
        _dst.SelectedIndexChanged += (_, _) => Changed();
        Row(t, "Altyazı dili", _src);
        Row(t, "Çeviri dili", _dst);
        _names.CheckedChanged += (_, _) => Changed();
        Full(t, _names);
        Full(t, Note("Küfürler her zaman sansürsüz çevrilir; bunu kapatan bir ayar yoktur. Sözlükteki adlar da korunur."));
        page.Controls.Add(t);
        return page;
    }

    private TabPage LookTab()
    {
        var page = new TabPage("Altyazı Görünümü");
        var t = Grid();
        _size.Scroll += (_, _) => { _sizeLabel.Text = _size.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); Changed(); _host.ShowPreview(); };
        _opacity.Scroll += (_, _) => { _opacityLabel.Text = "%" + _opacity.Value; Changed(); _host.ShowPreview(); };
        _outline.Scroll += (_, _) => { _outlineLabel.Text = _outline.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); Changed(); _host.ShowPreview(); };
        Row(t, "Yazı boyutu", Pair(_size, _sizeLabel));
        Row(t, "Arka plan", Pair(_opacity, _opacityLabel));
        Row(t, "Dış çizgi", Pair(_outline, _outlineLabel));
        var colors = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 240 };
        colors.Controls.Add(_swatch);
        foreach (var (name, hex) in new[] { ("Beyaz", "#FFFFFF"), ("Sarı", "#FFE14D"), ("Mavi", "#9ED8FF") })
        {
            var b = new Button { Text = name, AutoSize = true };
            b.Click += (_, _) => { ApplyColor(hex); };
            colors.Controls.Add(b);
        }
        var pick = new Button { Text = "Renk…", AutoSize = true };
        pick.Click += (_, _) => PickColor();
        colors.Controls.Add(pick);
        Row(t, "Yazı rengi", colors);
        Full(t, Note("Kaydırıcılar altyazıyı hemen yeniler. Konum için soldaki önayarları ya da düzenleme kipini kullan."));
        page.Controls.Add(t);
        return page;
    }

    private TabPage GlossaryTab()
    {
        var page = new TabPage("Sözlük");
        var info = Note("İngilizce görünen ad solda, istediğin çıktı sağda. Sağ boşsa kelime çevrilmez.");
        info.Dock = DockStyle.Top;
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = true;
        _grid.RowHeadersWidth = 24;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "İngilizce" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Türkçe (boş = olduğu gibi)" });
        _grid.CellEndEdit += (_, _) => BeginInvoke(new Action(Changed));
        _grid.UserDeletedRow += (_, _) => Changed();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var add = new Button { Text = "Satır ekle", AutoSize = true };
        add.Click += (_, _) => { int i = _grid.Rows.Add("", ""); _grid.CurrentCell = _grid.Rows[i].Cells[0]; _grid.BeginEdit(true); };
        var del = new Button { Text = "Sil", AutoSize = true };
        del.Click += (_, _) =>
        {
            if (_grid.CurrentRow is { IsNewRow: false } row) { _grid.Rows.Remove(row); Changed(); }
        };
        var gta = new Button { Text = "GTA 6 isimleri", AutoSize = true };
        gta.Click += (_, _) =>
        {
            var have = Rows().Select(g => g.Ingilizce).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var g in AppSettings.DefaultGlossary())
                if (!have.Contains(g.Ingilizce)) _grid.Rows.Add(g.Ingilizce, g.Turkce);
            Changed();
        };
        buttons.Controls.Add(add);
        buttons.Controls.Add(del);
        buttons.Controls.Add(gta);
        page.Controls.Add(_grid);
        page.Controls.Add(buttons);
        page.Controls.Add(info);
        return page;
    }

    private TabPage HotkeysTab()
    {
        var page = new TabPage("Kısayollar");
        var t = Grid();
        foreach (var (k, d) in new[]
                 {
                     ("F8", "Bölge seç"), ("F9", "Duraklat / devam"), ("F10", "Çıkış"),
                     ("Simgeye çift tık", "Bu pencereyi göster"), ("Pencereyi kapat", "Tepsiye küçült, program açık kalır"),
                 })
        {
            t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = k, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 8, 12, 3) }, 0, t.RowCount - 1);
            t.Controls.Add(new Label { Text = d, AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 1, t.RowCount - 1);
        }
        _autoUpdate.CheckedChanged += (_, _) => Changed();
        Full(t, _autoUpdate);
        var check = new Button { Text = "Güncellemeleri denetle", AutoSize = true };
        check.Click += (_, _) => _ = CheckUpdatesAsync(true);
        Full(t, check);
        Full(t, Note("Sürüm " + SurumKarsilastirma.Tag(GuncellemeIstegi.UygulamaSurumu) + ". Kurulu kopya kurulumu sessiz indirir; taşınabilir kopya sürüm sayfasını açar."));
        page.Controls.Add(t);
        return page;
    }

    private void LoadValues()
    {
        var s = _host.Current;
        FillMonitors(s.MonitorAdi);
        _follow.Checked = s.BolgeyiTakipEt;
        _filter.Checked = s.BeyazYaziFiltresi;
        _src.SelectedIndex = IndexOf(SourceLangs, s.KaynakDil, _src);
        _dst.SelectedIndex = IndexOf(TargetLangs, s.HedefDil, _dst);
        _names.Checked = s.OzelIsimleriKoru;
        _size.Value = (int)Math.Clamp(s.YaziBoyutu, 10f, 72f);
        _opacity.Value = Math.Clamp(s.ArkaPlanOpakligi, 0, 100);
        _outline.Value = Math.Clamp(s.DisCizgi, 0, 12);
        _sizeLabel.Text = _size.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _opacityLabel.Text = "%" + _opacity.Value;
        _outlineLabel.Text = _outline.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _autoUpdate.Checked = s.AcilistaGuncellemeDenetle;
        UpdateSwatch(s.YaziRengi);
        _grid.Rows.Clear();
        foreach (var g in s.Sozluk ?? new List<SozlukGirdisi>()) _grid.Rows.Add(g.Ingilizce, g.Turkce);
        UpdateRegionLabel();
        RefreshStatus();
    }

    private void FillMonitors(string selected)
    {
        _monitor.Items.Clear();
        _monitorIds.Clear();
        _monitor.Items.Add("Bölgenin ekranı");
        _monitorIds.Add("");
        int index = 0;
        int n = 1;
        foreach (var sc in Screen.AllScreens)
        {
            string mark = sc.Primary ? " (ana)" : "";
            _monitor.Items.Add($"Ekran {n}: {sc.Bounds.Width}×{sc.Bounds.Height}{mark}");
            _monitorIds.Add(sc.DeviceName);
            if (string.Equals(sc.DeviceName, selected, StringComparison.OrdinalIgnoreCase)) index = n;
            n++;
        }
        _monitor.SelectedIndex = index;
    }

    private void Changed()
    {
        if (_loading) return;
        try
        {
            var work = _host.Current.Clone();
            work.MonitorAdi = _monitor.SelectedIndex > 0 && _monitor.SelectedIndex < _monitorIds.Count ? _monitorIds[_monitor.SelectedIndex] : "";
            work.BolgeyiTakipEt = _follow.Checked;
            work.BeyazYaziFiltresi = _filter.Checked;
            work.KaynakDil = SelectedCode(_src, SourceLangs, "en");
            work.HedefDil = SelectedCode(_dst, TargetLangs, "tr");
            work.OzelIsimleriKoru = _names.Checked;
            work.YaziBoyutu = _size.Value;
            work.ArkaPlanOpakligi = _opacity.Value;
            work.DisCizgi = _outline.Value;
            work.AcilistaGuncellemeDenetle = _autoUpdate.Checked;
            work.Sozluk = Rows();
            _host.Apply(work, persist: true);
            UpdateSwatch(work.YaziRengi);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "control"); }
    }

    private void ApplyColor(string hex)
    {
        var work = _host.Current.Clone();
        work.YaziRengi = hex;
        _host.Apply(work, persist: true);
        UpdateSwatch(hex);
        _host.ShowPreview();
    }

    private void PickColor()
    {
        using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
        if (AppSettings.TryParseColor(_host.Current.YaziRengi, out var c)) dlg.Color = Color.FromArgb(c.R, c.G, c.B);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        ApplyColor($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
    }

    private void ApplyPreset(AltyaziOnAyar preset)
    {
        var s = _host.Current.Clone();
        var region = new EkranDikdortgen(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight);
        var screenB = MonitorBounds(s);
        SubtitleLayout.ApplyPreset(s, preset, region, new EkranDikdortgen(screenB.X, screenB.Y, screenB.Width, screenB.Height), 80, _follow.Checked);
        s.MonitorAdi = string.IsNullOrEmpty(s.MonitorAdi) ? Screen.FromRectangle(screenB).DeviceName : s.MonitorAdi;
        _host.Apply(s, persist: true);
        _loading = true;
        _follow.Checked = s.BolgeyiTakipEt;
        _loading = false;
        _host.ShowPreview();
    }

    private static Rectangle MonitorBounds(AppSettings s)
    {
        if (!string.IsNullOrEmpty(s.MonitorAdi))
        {
            foreach (var sc in Screen.AllScreens)
                if (string.Equals(sc.DeviceName, s.MonitorAdi, StringComparison.OrdinalIgnoreCase))
                    return sc.Bounds;
        }
        var probe = s.HasRegion ? new Rectangle(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight) : new Rectangle(0, 0, 1, 1);
        return Screen.FromRectangle(probe).Bounds;
    }

    private List<SozlukGirdisi> Rows()
    {
        var list = new List<SozlukGirdisi>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            var en = (r.Cells[0].Value?.ToString() ?? "").Trim();
            var tr = (r.Cells[1].Value?.ToString() ?? "").Trim();
            if (en.Length == 0 || !seen.Add(en)) continue;
            list.Add(new SozlukGirdisi(en, tr));
        }
        return list;
    }

    private void OnState(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                _loading = true;
                UpdateRegionLabel();
                _follow.Checked = _host.Current.BolgeyiTakipEt;
                _loading = false;
                RefreshStatus();
                RefreshOcrBanner();
            }));
        }
        catch (InvalidOperationException) { /* closing */ }
    }

    private void UpdateRegionLabel()
    {
        var s = _host.Current;
        _regionLabel.Text = s.HasRegion
            ? $"{s.RegionWidth}×{s.RegionHeight}  ·  {s.RegionX}, {s.RegionY}"
            : "Henüz bölge seçilmedi";
        _regionLabel.ForeColor = DarkTheme.Muted;
    }

    private void RefreshStatus()
    {
        string latency = _host.SonGecikmeMs > 0 ? _host.SonGecikmeMs + " ms" : "—";
        string line = string.IsNullOrWhiteSpace(_host.SonMetin) ? "—" : _host.SonMetin;
        _status.Text = $"OCR: {_host.OcrDurum}    Son çeviri: {line}    Gecikme: {latency}";
        bool run = _host.IsRunning;
        _start.Text = run ? "Durdur" : "Başlat";
        _start.BackColor = run ? DarkTheme.Stop : DarkTheme.Good;
        _start.ForeColor = Color.White;
    }

    private void GrabPreview()
    {
        if (!Visible) return;
        var s = _host.Current;
        if (!s.HasRegion)
        {
            _previewHint.Visible = true;
            return;
        }
        try
        {
            var bmp = new Bitmap(s.RegionWidth, s.RegionHeight);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(s.RegionX, s.RegionY, 0, 0, bmp.Size);
            var old = _preview.Image;
            _preview.Image = bmp;
            old?.Dispose();
            _previewHint.Visible = false;
        }
        catch (Exception)
        {
            /* locked screen or a changing display */
        }
    }

    private Panel BuildOcrBanner()
    {
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Width = 140, Padding = new Padding(4),
        };
        _ocrKur.Click += (_, _) => _ = KurOcrAsync();
        buttons.Controls.Add(_ocrKur);
        _ocrBanner.Controls.Add(_ocrBannerText);
        _ocrBanner.Controls.Add(buttons);
        return _ocrBanner;
    }

    private void RefreshOcrBanner()
    {
        if (_ocrInstalling) return;
        if (_host.OcrPaketiEksik)
        {
            _ocrBannerText.Text = OcrDilPaketi.Banner(_host.Current.KaynakDil);
            _ocrKur.Text = "Kur";
            _ocrKur.Enabled = true;
            _ocrBanner.Visible = true;
        }
        else
        {
            _ocrBanner.Visible = false;
        }
    }

    private async Task KurOcrAsync()
    {
        if (_ocrInstalling) return;
        _ocrInstalling = true;
        _ocrKur.Enabled = false;
        _ocrKur.Text = "Kuruluyor…";
        _ocrBannerText.Text = OcrDilPaketi.DilAdi(_host.Current.KaynakDil) + " yazı tanıma paketi kuruluyor…";
        _ocrBanner.Visible = true;
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
            {
                _ocrBanner.Visible = false;
                MessageBox.Show(this, "Yazı tanıma paketi hazır. Başlat ile çevirebilirsin.", "Altyazı Çevirici", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this,
                    "Paket kuruldu ama Windows tanımayı henüz açmadı. Altyazı Çevirici'yi kapatıp yeniden aç.",
                    "Altyazı Çevirici", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally
        {
            _ocrInstalling = false;
            if (!IsDisposed) RefreshOcrBanner();
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
                if (interactive)
                    MessageBox.Show(this, "Güncelleme denetlenemedi. İnternet yoksa daha sonra tekrar dene.", "Altyazı Çevirici", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!SurumKarsilastirma.IsNewer(info.Tag, GuncellemeIstegi.UygulamaSurumu))
            {
                if (interactive)
                    MessageBox.Show(this, "Bu sürüm güncel (" + SurumKarsilastirma.Tag(GuncellemeIstegi.UygulamaSurumu) + ").", "Altyazı Çevirici", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!interactive && !GuncellemeOkuyucu.BannerGoster(GuncellemeIstegi.UygulamaSurumu, info, _host.Current.AtlananSurum))
                return;
            _offer = info;
            _bannerText.Text = "Yeni sürüm var: " + info.Tag + " — Güncelle / Daha sonra / Bu sürümü atla";
            _progress.Visible = false;
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
        _progress.Value = 0;
        var path = await GuncellemeIstegi.IndirAsync(info, new Progress<double>(p =>
        {
            if (IsHandleCreated) BeginInvoke(new Action(() => _progress.Value = (int)Math.Clamp(p * 100, 0, 100)));
        }), CancellationToken.None).ConfigureAwait(true);
        if (IsDisposed) return;
        if (path == null)
        {
            _progress.Visible = false;
            MessageBox.Show(this, "Kurulum dosyası indirilemedi. Sürüm sayfasını açıyorum.", "Altyazı Çevirici", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            GuncellemeIstegi.SayfayiAc(info.Sayfa);
            return;
        }
        try { GuncellemeIstegi.KurulumuBaslat(path); }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "update");
            GuncellemeIstegi.SayfayiAc(info.Sayfa);
            return;
        }
        _host.Quit();
    }

    private void UpdateSwatch(string hex) =>
        _swatch.BackColor = AppSettings.TryParseColor(hex, out var c) ? Color.FromArgb(c.R, c.G, c.B) : Color.White;

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };

    private static TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private static void Row(TableLayoutPanel t, string label, Control c)
    {
        t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 10, 8, 3) }, 0, t.RowCount - 1);
        c.Margin = new Padding(3, 4, 3, 4);
        t.Controls.Add(c, 1, t.RowCount - 1);
    }

    private static void Full(TableLayoutPanel t, Control c)
    {
        t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.Margin = new Padding(3, 6, 3, 6);
        t.Controls.Add(c, 0, t.RowCount - 1);
        t.SetColumnSpan(c, 2);
    }

    private static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = DarkTheme.Muted,
    };

    private static FlowLayoutPanel Pair(Control a, Control b)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(a);
        b.Margin = new Padding(8, 8, 0, 0);
        row.Controls.Add(b);
        return row;
    }

    private static int IndexOf((string Code, string Name)[] list, string code, ComboBox box)
    {
        int i = Array.FindIndex(list, l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;
        box.Items.Add(code);
        return box.Items.Count - 1;
    }

    private static string SelectedCode(ComboBox box, (string Code, string Name)[] list, string def)
    {
        int i = box.SelectedIndex;
        if (i < 0) return def;
        if (i < list.Length) return list[i].Code;
        var s = box.Items[i]?.ToString() ?? def;
        int p = s.LastIndexOf('(');
        return p >= 0 ? s[(p + 1)..].TrimEnd(')') : s;
    }
}
