using AltyaziCevirici.Core;

namespace AltyaziCevirici;

/// <summary>What the settings window needs from the tray app.</summary>
internal interface ISettingsHost
{
    AppSettings Current { get; }
    bool IsPaused { get; }
    event EventHandler? StateChanged;
    /// <summary>Applies everything except the region live (and saves when <paramref name="persist"/>).</summary>
    void Apply(AppSettings edited, bool persist);
    void SelectRegion();
    void TogglePause();
    void ShowPreview();
}

/// <summary>Turkish settings window: general, appearance, glossary and hotkeys. Changes apply live.</summary>
internal sealed class SettingsForm : Form
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
    private static readonly string[] PlacementNames = { "Bölgenin üstünde", "Bölgenin altında", "Altyazının üzerinde" };

    private readonly ISettingsHost _host;
    private readonly AppSettings _original;
    private readonly AppSettings _work;
    private bool _loading = true;
    private bool _cancelled;

    private readonly Label _regionLabel = new() { AutoSize = true };
    private readonly Button _pauseBtn = new() { AutoSize = true };
    private readonly ComboBox _src = Combo();
    private readonly ComboBox _dst = Combo();
    private readonly CheckBox _names = new() { AutoSize = true, Text = "Cümle ortasındaki isimleri (kişi, yer) olduğu gibi bırak" };
    private readonly CheckBox _filter = new() { AutoSize = true, Text = "Beyaz yazı filtresi (karışık arka planda daha iyi okur)" };
    private readonly NumericUpDown _size = new() { Minimum = 10, Maximum = 72, Width = 80 };
    private readonly Panel _swatch = new() { Width = 48, Height = 26, BorderStyle = BorderStyle.FixedSingle };
    private readonly TrackBar _opacity = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 260, AutoSize = true };
    private readonly Label _opacityLabel = new() { AutoSize = true, MinimumSize = new Size(50, 0) };
    private readonly ComboBox _placement = Combo();
    private readonly NumericUpDown _offset = new() { Minimum = -500, Maximum = 500, Width = 80, Increment = 5 };
    private readonly DataGridView _grid = new();

    public SettingsForm(ISettingsHost host)
    {
        _host = host;
        _original = host.Current.Clone();
        _work = host.Current.Clone();

        Text = "Altyazı Çevirici - Ayarlar";
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        try { Icon = IconFactory.Create(); } catch (Exception) { /* default icon */ }
        ClientSize = new Size(640, 560);
        MinimumSize = new Size(560, 480);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        tabs.TabPages.Add(GeneralTab());
        tabs.TabPages.Add(AppearanceTab());
        tabs.TabPages.Add(GlossaryTab());
        tabs.TabPages.Add(HotkeysTab());

        var save = new Button { Text = "Kaydet", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(10, 2, 10, 2) };
        var cancel = new Button { Text = "Vazgeç", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(10, 2, 10, 2) };
        save.Click += (_, _) => Close();
        cancel.Click += (_, _) => { _cancelled = true; Close(); };
        AcceptButton = save;
        CancelButton = cancel;
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8),
        };
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(save);
        bottom.Controls.Add(new Label { Text = "Değişiklikler hemen uygulanır.", AutoSize = true, Padding = new Padding(0, 8, 16, 0), ForeColor = SystemColors.GrayText });

        Controls.Add(tabs);
        Controls.Add(bottom);

        LoadValues();
        _host.StateChanged += OnHostStateChanged;
        _loading = false;
    }

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };

    private static TableLayoutPanel Grid2()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private static void Row(TableLayoutPanel t, string label, Control c)
    {
        t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 9, 12, 3) };
        c.Margin = new Padding(3, 5, 3, 5);
        t.Controls.Add(l, 0, t.RowCount - 1);
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

    private TabPage GeneralTab()
    {
        var page = new TabPage("Genel");
        var t = Grid2();
        var regionBtn = new Button { Text = "Bölgeyi yeniden seç (F8)", AutoSize = true };
        regionBtn.Click += (_, _) => Safe(() =>
        {
            Hide();
            try { _host.SelectRegion(); }
            finally { Show(); Activate(); UpdateRegionLabel(); }
        });
        var regionRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        regionRow.Controls.Add(regionBtn);
        _regionLabel.Margin = new Padding(10, 8, 0, 0);
        regionRow.Controls.Add(_regionLabel);
        Row(t, "Altyazı bölgesi:", regionRow);

        _pauseBtn.Click += (_, _) => Safe(() => { _host.TogglePause(); UpdatePause(); });
        Row(t, "Çeviri:", _pauseBtn);

        foreach (var (code, name) in SourceLangs) _src.Items.Add($"{name} ({code})");
        foreach (var (code, name) in TargetLangs) _dst.Items.Add($"{name} ({code})");
        _src.SelectedIndexChanged += (_, _) => Changed();
        _dst.SelectedIndexChanged += (_, _) => Changed();
        Row(t, "Altyazı dili:", _src);
        Row(t, "Çeviri dili:", _dst);

        _names.CheckedChanged += (_, _) => Changed();
        _filter.CheckedChanged += (_, _) => Changed();
        Full(t, _names);
        Full(t, _filter);
        Full(t, new Label
        {
            AutoSize = true, MaximumSize = new Size(580, 0), ForeColor = SystemColors.GrayText,
            Text = "Küfürler sansürsüz, doğal Türkçe karşılıklarıyla çevrilir (İngilizce → Türkçe). " +
                   "Oyundaki yer ve kişi adlarını \"Sözlük\" sekmesinden ekleyebilirsin. " +
                   "Altyazı dilini değiştirirsen Windows'ta o dilin metin tanıma (OCR) paketi yüklü olmalı.",
        });
        page.Controls.Add(t);
        return page;
    }

    private TabPage AppearanceTab()
    {
        var page = new TabPage("Görünüm");
        var t = Grid2();
        _size.ValueChanged += (_, _) => Changed();
        Row(t, "Yazı boyutu:", _size);

        var colorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var pick = new Button { Text = "Renk seç…", AutoSize = true };
        pick.Click += (_, _) => Safe(PickColor);
        colorRow.Controls.Add(_swatch);
        colorRow.Controls.Add(pick);
        foreach (var (name, hex) in new[] { ("Beyaz", "#FFFFFF"), ("Sarı", "#FFE14D"), ("Altın", "#F2C14E"), ("Açık mavi", "#9ED8FF") })
        {
            var b = new Button { Text = name, AutoSize = true };
            b.Click += (_, _) => { _work.YaziRengi = hex; UpdateSwatch(); Changed(); };
            colorRow.Controls.Add(b);
        }
        Row(t, "Yazı rengi:", colorRow);

        var opRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _opacity.Scroll += (_, _) => { _opacityLabel.Text = $"%{_opacity.Value}"; Changed(); };
        _opacityLabel.Margin = new Padding(6, 10, 0, 0);
        opRow.Controls.Add(_opacity);
        opRow.Controls.Add(_opacityLabel);
        Row(t, "Arka plan opaklığı:", opRow);

        _placement.Items.AddRange(PlacementNames);
        _placement.SelectedIndexChanged += (_, _) => Changed();
        Row(t, "Çeviri konumu:", _placement);

        var offRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _offset.ValueChanged += (_, _) => Changed();
        offRow.Controls.Add(_offset);
        offRow.Controls.Add(new Label { Text = "piksel (eksi: yukarı, artı: aşağı)", AutoSize = true, Margin = new Padding(6, 8, 0, 0) });
        Row(t, "Dikey kaydırma:", offRow);

        var preview = new Button { Text = "Önizleme göster", AutoSize = true };
        preview.Click += (_, _) => Safe(_host.ShowPreview);
        Full(t, preview);
        page.Controls.Add(t);
        return page;
    }

    private TabPage GlossaryTab()
    {
        var page = new TabPage("Sözlük");
        var info = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 6), MaximumSize = new Size(620, 0),
            Text = "Burada yazdığın İngilizce kelime ya da isim her zaman sağdaki gibi çıkar. " +
                   "Türkçe boş bırakılırsa olduğu gibi (çevrilmeden) yazılır. Ör: \"Port Gellhorn\" → boş, \"Blackwater\" → \"Karasu\". " +
                   "Türkçe ekler (Port Gellhorn'da, Vice City'ye) otomatik düzeltilir.",
        };
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.RowHeadersWidth = 28;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "en", HeaderText = "İngilizce (ekranda görünen)" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "tr", HeaderText = "Türkçe çıktı (boş = olduğu gibi)" });
        _grid.CellEndEdit += (_, _) => GlossaryChanged();
        _grid.UserDeletedRow += (_, _) => GlossaryChanged();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        var add = new Button { Text = "Satır ekle", AutoSize = true };
        add.Click += (_, _) => Safe(() =>
        {
            int i = _grid.Rows.Add("", "");
            _grid.CurrentCell = _grid.Rows[i].Cells[0];
            _grid.BeginEdit(true);
        });
        var del = new Button { Text = "Seçili satırları sil", AutoSize = true };
        del.Click += (_, _) => Safe(() =>
        {
            var rows = _grid.SelectedRows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
            if (rows.Count == 0 && _grid.CurrentRow is { IsNewRow: false } cr) rows.Add(cr);
            if (_grid.IsCurrentCellInEditMode) _grid.CancelEdit();
            foreach (var r in rows) _grid.Rows.Remove(r);
            GlossaryChanged();
        });
        var defaults = new Button { Text = "GTA 6 isimlerini ekle", AutoSize = true };
        defaults.Click += (_, _) => Safe(() =>
        {
            var have = RowsToList().Select(g => g.Ingilizce).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var g in AppSettings.DefaultGlossary()) if (!have.Contains(g.Ingilizce)) _grid.Rows.Add(g.Ingilizce, g.Turkce);
            GlossaryChanged();
        });
        buttons.Controls.Add(add);
        buttons.Controls.Add(del);
        buttons.Controls.Add(defaults);

        page.Controls.Add(_grid);
        page.Controls.Add(buttons);
        page.Controls.Add(info);
        return page;
    }

    private static TabPage HotkeysTab()
    {
        var page = new TabPage("Kısayollar");
        var t = Grid2();
        foreach (var (k, d) in new[]
                 {
                     ("F8", "Altyazı bölgesini yeniden seç"), ("F9", "Duraklat / devam et"), ("F10", "Programı kapat"),
                     ("Simgeye çift tık", "Bu ayarlar penceresini aç"), ("Simgeye sağ tık", "Menü (ayarlar, bölge, duraklat, çıkış)"),
                 })
        {
            var key = new Label { Text = k, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(3, 8, 16, 3) };
            t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(key, 0, t.RowCount - 1);
            t.Controls.Add(new Label { Text = d, AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 1, t.RowCount - 1);
        }
        Full(t, new Label
        {
            AutoSize = true, MaximumSize = new Size(580, 0), ForeColor = SystemColors.GrayText,
            Text = "Kısayollar oyun ya da Remote Play açıkken de çalışır. Bir tuş başka bir program tarafından kullanılıyorsa açılışta uyarı çıkar; o zaman simge menüsünü kullan.",
        });
        page.Controls.Add(t);
        return page;
    }

    // ---------------- values ----------------

    private void LoadValues()
    {
        UpdateRegionLabel();
        UpdatePause();
        _src.SelectedIndex = IndexOf(SourceLangs, _work.KaynakDil, _src);
        _dst.SelectedIndex = IndexOf(TargetLangs, _work.HedefDil, _dst);
        _names.Checked = _work.OzelIsimleriKoru;
        _filter.Checked = _work.BeyazYaziFiltresi;
        _size.Value = (decimal)Math.Clamp(_work.YaziBoyutu, 10f, 72f);
        UpdateSwatch();
        _opacity.Value = Math.Clamp(_work.ArkaPlanOpakligi, 0, 100);
        _opacityLabel.Text = $"%{_opacity.Value}";
        _placement.SelectedIndex = Math.Clamp((int)_work.OverlayKonumu, 0, PlacementNames.Length - 1);
        _offset.Value = Math.Clamp(_work.DikeyKaydirma, -500, 500);
        _grid.Rows.Clear();
        foreach (var g in _work.Sozluk ?? new List<SozlukGirdisi>()) _grid.Rows.Add(g.Ingilizce, g.Turkce);
    }

    private static int IndexOf((string Code, string Name)[] list, string code, ComboBox box)
    {
        int i = Array.FindIndex(list, l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;
        // A code typed into ayarlar.json by hand: keep it selectable.
        box.Items.Add($"{code} ({code})");
        return box.Items.Count - 1;
    }

    private static string SelectedCode(ComboBox box, (string Code, string Name)[] list, string def)
    {
        int i = box.SelectedIndex;
        if (i < 0) return def;
        if (i < list.Length) return list[i].Code;
        var s = box.Items[i]?.ToString() ?? def;
        int p = s.LastIndexOf('(');
        return p >= 0 ? s[(p + 1)..].TrimEnd(')') : def;
    }

    private void Changed()
    {
        if (_loading) return;
        Safe(() =>
        {
            Collect();
            _host.Apply(_work, persist: false);
        });
    }

    private void GlossaryChanged()
    {
        if (_loading) return;
        BeginInvoke(new Action(Changed)); // after the grid commits the edit
    }

    private void Collect()
    {
        _work.KaynakDil = SelectedCode(_src, SourceLangs, "en");
        _work.HedefDil = SelectedCode(_dst, TargetLangs, "tr");
        _work.OzelIsimleriKoru = _names.Checked;
        _work.BeyazYaziFiltresi = _filter.Checked;
        _work.YaziBoyutu = (float)_size.Value;
        _work.ArkaPlanOpakligi = _opacity.Value;
        _work.OverlayKonumu = (OverlayPlacement)Math.Max(0, _placement.SelectedIndex);
        _work.DikeyKaydirma = (int)_offset.Value;
        _work.Sozluk = RowsToList();
    }

    private List<SozlukGirdisi> RowsToList()
    {
        var list = new List<SozlukGirdisi>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            var en = (r.Cells[0].Value?.ToString() ?? string.Empty).Trim();
            var tr = (r.Cells[1].Value?.ToString() ?? string.Empty).Trim();
            if (en.Length == 0 || !seen.Add(en)) continue;
            list.Add(new SozlukGirdisi(en, tr));
        }
        return list;
    }

    private void PickColor()
    {
        using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
        if (AppSettings.TryParseColor(_work.YaziRengi, out var c)) dlg.Color = Color.FromArgb(c.R, c.G, c.B);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _work.YaziRengi = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        UpdateSwatch();
        Changed();
    }

    private void UpdateSwatch() =>
        _swatch.BackColor = AppSettings.TryParseColor(_work.YaziRengi, out var c) ? Color.FromArgb(c.R, c.G, c.B) : Color.White;

    private void UpdateRegionLabel()
    {
        var s = _host.Current;
        _regionLabel.Text = s.HasRegion ? $"{s.RegionWidth}×{s.RegionHeight} (konum {s.RegionX}, {s.RegionY})" : "Henüz seçilmedi";
    }

    private void UpdatePause() => _pauseBtn.Text = _host.IsPaused ? "Devam et (F9)" : "Duraklat (F9)";

    private void OnHostStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        try { BeginInvoke(new Action(() => { UpdatePause(); UpdateRegionLabel(); })); } catch (InvalidOperationException) { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Safe(() =>
        {
            _grid.EndEdit();
            if (_cancelled) _host.Apply(_original, persist: true);
            else { Collect(); _host.Apply(_work, persist: true); }
        });
        _host.StateChanged -= OnHostStateChanged;
        base.OnFormClosing(e);
    }

    private static void Safe(Action a)
    {
        try { a(); } catch (Exception ex) { ErrorLog.Write(ex, "settings-ui"); }
    }
}
