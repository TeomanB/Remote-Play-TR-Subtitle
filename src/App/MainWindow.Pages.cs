using System.Globalization;
using AltyaziCevirici.Core;
using AltyaziCevirici.Ui;

namespace AltyaziCevirici;

/// <summary>Inspector pages: Alanlar, Görünüm, Sözlük, Gelişmiş.</summary>
internal sealed partial class MainWindow
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private InspectorPage _areasPage = null!, _lookPage = null!, _glossaryPage = null!, _advancedPage = null!;

    // Alanlar
    private readonly UiSegmented _boxSeg = new();
    private readonly UiInput _gx = new() { Prefix = "X", Suffix = "px", Numeric = true };
    private readonly UiInput _gy = new() { Prefix = "Y", Suffix = "px", Numeric = true };
    private readonly UiInput _gw = new() { Prefix = "G", Suffix = "px", Numeric = true };
    private readonly UiInput _gh = new() { Prefix = "Y", Suffix = "px", Numeric = true };
    private readonly UiButton _resetBox = new() { Text = "Varsayılana döndür", Glyph = Ic.Reset };
    private readonly UiButton _pickRegion = new() { Text = "Ekranda işaretle (F8)", Glyph = Ic.Select, Kind = ButtonKind.Ghost };
    private readonly UiToggle _lockToggle = new();

    // Görünüm
    private readonly UiDropdown _fontDrop = new();
    private readonly UiSlider _size = new() { Minimum = 10, Maximum = 72 };
    private readonly UiSlider _outline = new() { Minimum = 0, Maximum = 12 };
    private readonly UiSlider _opacity = new() { Minimum = 0, Maximum = 100, Step = 5 };
    private static readonly string[] TextColors = { "#FFFFFF", "#FFE45C", "#7DF3EC", "#9BFF8A", "#FFB3D1" };
    private static readonly string[] OutlineColors = { "#000000", "#1F2A44", "#086E6B", "#7A1F3D" };
    private readonly List<UiSwatch> _textSwatches = new();
    private readonly List<UiSwatch> _outlineSwatches = new();

    // Sözlük
    private readonly UiInput _search = new() { Glyph = Ic.Search, Placeholder = "Paket veya terim ara" };
    private readonly PackList _gamePacks = new();
    private readonly PackList _basePacks = new();
    private readonly UiButton _import = new() { Text = "İçe aktar", Glyph = Ic.Download };
    private readonly UiButton _export = new() { Text = "Dışa aktar", Glyph = Ic.Upload };
    private readonly UiInput _termEn = new() { Placeholder = "İngilizce" };
    private readonly UiInput _termTr = new() { Placeholder = "Türkçe" };
    private readonly UiButton _termAdd = new() { Kind = ButtonKind.Primary, Glyph = Ic.Add };
    private readonly TermList _terms = new() { Empty = "Henüz terim eklemedin." };
    private readonly UiToggle _namesToggle = new();
    private readonly UiToggle _packOnly = new();
    private readonly UiButton _openLines = new() { Text = "Klasörü aç", Kind = ButtonKind.Secondary };
    private readonly LinePackList _linePacks = new();

    // Gelişmiş
    private readonly UiButton _updateBtn = new() { Text = "Denetle" };
    private readonly UiButton _skipBtn = new() { Text = "Bu sürümü atla", Kind = ButtonKind.Ghost };
    private readonly UiButton _ocrBtn = new() { Text = "Kur", Kind = ButtonKind.Primary };
    private readonly UiDropdown _langDrop = new();
    private readonly UiToggle _whiteToggle = new();
    private readonly UiToggle _startupToggle = new();
    private readonly UiToggle _autoUpdateToggle = new();
    private readonly UiButton _settingsBtn = new() { Text = "Göster" };
    private readonly UiButton _tourBtn = new() { Text = "Göster" };

    /// <summary>Opens Explorer with ayarlar.json selected (writes the file first if it does not exist yet).</summary>
    private void RevealSettings()
    {
        try
        {
            string path = AppPaths.SettingsFile;
            if (!File.Exists(path)) Patch(_ => { });
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ErrorLog.Write(ex, "settings-reveal"); Notice("Ayar dosyası: " + AppPaths.SettingsFile); }
    }

    private UiInput[] AllInputs() => new[] { _gx, _gy, _gw, _gh, _search, _termEn, _termTr };
    private UiInput[] GeometryInputs() => new[] { _gx, _gy, _gw, _gh };

    private void BuildInspector()
    {
        _inspector.Controls.Add(_scroll);
        Controls.Add(_inspector);
        BuildAreas();
        BuildLook();
        BuildGlossary();
        BuildAdvanced();
    }

    // ---------------- Alanlar ----------------

    private void BuildAreas()
    {
        _boxSeg.Add("Orijinal altyazı", Theme.KeyOrig);
        _boxSeg.Add("Çeviri", Theme.Acc);
        _boxSeg.Add("Görev", Theme.Hex(0x7A4FBF));
        _boxSeg.SelectedIndexChanged += (_, _) => SelectBox(_boxSeg.SelectedIndex);
        foreach (var input in GeometryInputs())
        {
            input.ValueChanged += (_, _) => OnGeometryTyped();
            input.Committed += (_, _) => { if (!_loading) { SaveRegions(showOverlay: true); RefreshGeometryFields(); } };
        }
        _resetBox.Click += (_, _) => ResetBox();
        _pickRegion.Click += (_, _) => _host.SelectRegion();
        _lockToggle.CheckedChanged += (_, _) => SetLock(_stage.Selected, _lockToggle.Checked);

        _areasPage = new InspectorPage("Alanlar", "Seçili kutunun tam konumu ve boyutu.", (p, w, y) =>
        {
            InspectorPage.Put(_boxSeg, 0, y, w, Theme.D(34));
            y += Theme.D(34) + Theme.D(18);
            int col = (w - Theme.D(8)) / 2, fh = Theme.D(34);
            y = p.AddLabel(y, w, "Konum");
            InspectorPage.PutInput(_gx, 0, y, col, fh);
            InspectorPage.PutInput(_gy, col + Theme.D(8), y, w - col - Theme.D(8), fh);
            y += fh + Theme.D(18);
            y = p.AddLabel(y, w, "Boyut");
            InspectorPage.PutInput(_gw, 0, y, col, fh);
            InspectorPage.PutInput(_gh, col + Theme.D(8), y, w - col - Theme.D(8), fh);
            y += fh + Theme.D(18);
            InspectorPage.Put(_resetBox, 0, y, w, Theme.D(34));
            y += Theme.D(34) + Theme.D(6);
            InspectorPage.Put(_pickRegion, 0, y, w, Theme.D(34));
            y += Theme.D(34) + Theme.D(14);
            y += OptionBox(p, y, w, "Bu alanı kilitle", "Kilitliyken taşınmaz ve boyutlanmaz; tıklamalar alttaki kutuya geçer.", _lockToggle) + Theme.D(14);
            string src = _screen.Width.ToString(Inv) + " × " + _screen.Height.ToString(Inv);
            y += p.AddHint(y, w, "Kutular üst üste gelince seçili olan üstte kalır; diğerini seçmek için etiketine ya da yukarıdaki adına tıkla. Sayılar seçili kaynağın " + src + " görüntüsüne göredir.");
            return y;
        });
        foreach (var c in new Control[] { _boxSeg, _gx, _gy, _gw, _gh, _resetBox, _pickRegion, _lockToggle }) _areasPage.Controls.Add(c);
    }

    /// <summary>Gray rounded option row with a title, explanation, and a switch on the right. Returns its height.</summary>
    private static int OptionBox(InspectorPage p, int y, int w, string title, string sub, UiToggle toggle)
    {
        int pad = Theme.D(12), sw = Theme.D(44);
        int tw = w - 2 * pad - sw - Theme.D(10);
        int subH = Theme.WrapHeight(sub, Theme.Small, tw);
        int titleH = Theme.WrapHeight(title, Theme.BodySemi, tw);
        int h = pad * 2 + titleH + Theme.D(3) + subH;
        p.AddBox(new Rectangle(0, y, w, h), Theme.Subtle, Theme.Line);
        p.AddWrap(pad, y + pad, tw, title, Theme.BodySemi, Theme.Tx);
        p.AddWrap(pad, y + pad + titleH + Theme.D(3), tw, sub, Theme.Small, Theme.Tx2);
        toggle.BackColor = Theme.Subtle;
        InspectorPage.Put(toggle, w - pad - sw, y + (h - Theme.D(24)) / 2, sw, Theme.D(24));
        return h;
    }

    private bool _fieldSync;

    private void RefreshGeometryFields()
    {
        var b = _stage.Box(_stage.Selected);
        var src = _stage.Source;
        _fieldSync = true;
        SetField(_gx, b.X - src.X);
        SetField(_gy, b.Y - src.Y);
        SetField(_gw, b.Width);
        SetField(_gh, b.Height);
        _fieldSync = false;
    }

    private static void SetField(UiInput input, int value)
    {
        if (input.Box.Focused) return;
        input.SetText(value.ToString(Inv));
    }

    private void OnGeometryTyped()
    {
        if (_fieldSync || _loading) return;
        int sel = _stage.Selected;
        if (_stage.IsLocked(sel)) return;
        var src = _stage.Source;
        var b = _stage.Box(sel);
        static int? Read(UiInput i) => int.TryParse(i.Value, NumberStyles.Integer, Inv, out int v) ? v : null;
        int x = Read(_gx) is { } vx ? src.X + vx : b.X;
        int y = Read(_gy) is { } vy ? src.Y + vy : b.Y;
        int w = Read(_gw) ?? b.Width;
        int h = Read(_gh) ?? b.Height;
        _stage.SetBox(sel, Fit(new Rectangle(x, y, w, h), src, sel));
    }

    private void ResetBox()
    {
        int sel = _stage.Selected;
        if (_stage.IsLocked(sel)) return;
        var src = _stage.Source;
        var r = sel == PreviewStage.Orig
            ? new Rectangle(src.X + src.Width * 20 / 100, src.Y + src.Height * 79 / 100, src.Width * 60 / 100, src.Height * 95 / 1000)
            : new Rectangle(src.X + src.Width * 18 / 100, src.Y + src.Height * 635 / 1000, src.Width * 64 / 100, src.Height * 105 / 1000);
        _stage.SetBox(sel, Fit(r, src, sel));
        SaveRegions(showOverlay: true);
        RefreshGeometryFields();
    }

    // ---------------- Görünüm ----------------

    private void BuildLook()
    {
        var s = _host.Current;
        var fonts = SubtitleFonts.Available().ToList();
        string current = SubtitleFonts.Resolve(s.YaziTipi);
        if (!fonts.Contains(current)) fonts.Insert(0, current);
        _fontDrop.SetItems(fonts.Select(f => (f, (object?)f)), fonts.IndexOf(current));
        _fontDrop.SelectedChanged += (_, _) => { if (_fontDrop.SelectedTag is string f) Patch(x => x.YaziTipi = f); RefreshLook(); };

        _size.SetSilently((int)Math.Round(s.YaziBoyutu));
        _outline.SetSilently(s.DisCizgi);
        _opacity.SetSilently(s.ArkaPlanOpakligi);
        _size.ValueChanged += (_, _) => { Patch(x => x.YaziBoyutu = _size.Value, persist: false); RefreshLook(); };
        _outline.ValueChanged += (_, _) => { Patch(x => x.DisCizgi = _outline.Value, persist: false); RefreshLook(); };
        _opacity.ValueChanged += (_, _) => { Patch(x => x.ArkaPlanOpakligi = _opacity.Value, persist: false); RefreshLook(); };
        foreach (var slider in new[] { _size, _outline, _opacity })
            slider.ValueCommitted += (_, _) => { Patch(_ => { }); ShowOverlaySample(); };

        foreach (var hex in TextColors) _textSwatches.Add(Swatch(hex, c => Patch(x => x.YaziRengi = c)));
        _textSwatches.Add(Picker(() => _host.Current.YaziRengi, c => Patch(x => x.YaziRengi = c)));
        foreach (var hex in OutlineColors) _outlineSwatches.Add(Swatch(hex, c => Patch(x => x.DisCizgiRengi = c)));
        _outlineSwatches.Add(Picker(() => _host.Current.DisCizgiRengi, c => Patch(x => x.DisCizgiRengi = c)));

        _lookPage = new InspectorPage("Görünüm", "Değişiklikler önizlemedeki çeviriye ve oyundaki yazıya anında yansır.", (p, w, y) =>
        {
            var look = _host.Current;
            y = p.AddLabel(y, w, "Yazı tipi");
            InspectorPage.Put(_fontDrop, 0, y, w, Theme.D(34));
            y += Theme.D(34) + Theme.D(18);
            y = p.AddLabel(y, w, "Boyut", ((int)Math.Round(look.YaziBoyutu)).ToString(Inv) + " px");
            InspectorPage.Put(_size, 0, y, w, Theme.D(22));
            y += Theme.D(22) + Theme.D(18);
            y = p.AddLabel(y, w, "Yazı rengi");
            y = PlaceSwatches(_textSwatches, y) + Theme.D(16);
            y = p.AddLabel(y, w, "Dış çizgi", look.DisCizgi.ToString(Inv) + " px");
            y = PlaceSwatches(_outlineSwatches, y) + Theme.D(8);
            InspectorPage.Put(_outline, 0, y, w, Theme.D(22));
            y += Theme.D(22) + Theme.D(18);
            y = p.AddLabel(y, w, "Arka plan opaklığı", "%" + look.ArkaPlanOpakligi.ToString(Inv));
            InspectorPage.Put(_opacity, 0, y, w, Theme.D(22));
            y += Theme.D(22) + Theme.D(18);
            y += p.AddHint(y, w, "Bölge seçiliyse ayar değiştirdiğinde örnek çeviri birkaç saniye oyunun üstünde de görünür.");
            return y;
        });
        _lookPage.Controls.Add(_fontDrop);
        _lookPage.Controls.Add(_size);
        _lookPage.Controls.Add(_outline);
        _lookPage.Controls.Add(_opacity);
        foreach (var sw in _textSwatches.Concat(_outlineSwatches)) _lookPage.Controls.Add(sw);
        MarkSwatches();
    }

    private static int PlaceSwatches(List<UiSwatch> swatches, int y)
    {
        int d = Theme.D(36), x = -Theme.D(4);
        foreach (var sw in swatches)
        {
            InspectorPage.Put(sw, x, y, d, d);
            x += d;
        }
        return y + d;
    }

    private UiSwatch Swatch(string hex, Action<string> apply)
    {
        if (!AppSettings.TryParseColor(hex, out var c)) c = (255, 255, 255);
        var sw = new UiSwatch { Swatch = Color.FromArgb(255, c.R, c.G, c.B), Tag = hex };
        sw.Click += (_, _) => { apply(hex); RefreshLook(); ShowOverlaySample(); };
        return sw;
    }

    private UiSwatch Picker(Func<string> current, Action<string> apply)
    {
        var sw = new UiSwatch { Picker = true };
        sw.Click += (_, _) =>
        {
            if (!AppSettings.TryParseColor(current(), out var c)) c = (255, 255, 255);
            using var dlg = new ColorDialog { FullOpen = true, Color = Color.FromArgb(c.R, c.G, c.B) };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            apply("#" + dlg.Color.R.ToString("X2", Inv) + dlg.Color.G.ToString("X2", Inv) + dlg.Color.B.ToString("X2", Inv));
            RefreshLook();
            ShowOverlaySample();
        };
        return sw;
    }

    private void MarkSwatches()
    {
        var s = _host.Current;
        Mark(_textSwatches, TextColors, s.YaziRengi);
        Mark(_outlineSwatches, OutlineColors, s.DisCizgiRengi);
    }

    private static void Mark(List<UiSwatch> swatches, string[] colors, string current)
    {
        int match = Array.FindIndex(colors, c => c.Equals(current, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < swatches.Count; i++) swatches[i].Selected = match < 0 ? i == swatches.Count - 1 : i == match;
    }

    private void RefreshLook()
    {
        MarkSwatches();
        _stage.SetLook(_host.Current, _host.IsRunning && !string.IsNullOrWhiteSpace(_host.SonMetin) ? _host.SonMetin : OrnekAltyazi);
        if (_scroll.Page == _lookPage) _lookPage.Arrange(_lookPage.Width);
    }

    private void ShowOverlaySample()
    {
        if (_host.Current.HasRegion && !_host.IsRunning) _host.ShowPreview();
    }

    // ---------------- Sözlük ----------------

    private static readonly Dictionary<string, (string Abbrev, int Color)> PackLook = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gta6"] = ("G6", 0xE8456B), ["rdr2"] = ("RD", 0xB4532A), ["tlou"] = ("TL", 0x3F6B4E), ["gow"] = ("GW", 0x3B6FB5),
        ["spiderman"] = ("SM", 0xC23B3B), ["genel"] = ("GN", 0x0A8480), ["argo"] = ("AR", 0x7A4FBF), ["oyun"] = ("OY", 0x2F7D9A),
        ["isimler"] = ("İS", 0x8A6D1F),
    };

    private void BuildGlossary()
    {
        _search.ValueChanged += (_, _) => { FillGlossary(); _glossaryPage.Arrange(_glossaryPage.Width); _scroll.Relayout(); };
        _gamePacks.Toggled += TogglePack;
        _basePacks.Toggled += TogglePack;
        _terms.DeleteClicked += row => { DeleteEntry(row.English); };
        _termAdd.Click += (_, _) => AddEntry();
        _termTr.EnterPressed += (_, _) => AddEntry();
        _termEn.EnterPressed += (_, _) => _termTr.Box.Focus();
        _import.Click += (_, _) => ImportEntries();
        _export.Click += (_, _) => ExportEntries();
        _namesToggle.SetSilently(_host.Current.OzelIsimleriKoru);
        _namesToggle.CheckedChanged += (_, _) => Patch(s => s.OzelIsimleriKoru = _namesToggle.Checked);
        _packOnly.CheckedChanged += (_, _) => SetPackOnly(_packOnly.Checked);
        _openLines.Click += (_, _) => OpenLinesFolder();
        _linePacks.Chosen += (_, game) => { Patch(s => s.SeciliHazirOyun = game); FillGlossary(); _scroll.Relayout(); };

        _gamePacks.Opened += OpenPack;
        _basePacks.Opened += OpenPack;
        _packBack.Click += (_, _) => OpenPack(null);
        _packToggle.CheckedChanged += (_, _) => { if (_openPack != null) { TogglePack(_openPack, _packToggle.Checked); _scroll.Relayout(); } };

        _glossaryPage = new InspectorPage("Sözlük", "Oyuna özel terimler ve isimler doğru çevrilsin.", (p, w, y) =>
        {
            bool detail = _openPack != null;
            foreach (var c in new Control[] { _gamePacks, _basePacks, _import, _export, _termEn, _termTr, _termAdd, _terms, _namesToggle, _packOnly, _openLines, _linePacks }) c.Visible = !detail;
            foreach (var c in new Control[] { _packBack, _packToggle, _packTerms }) c.Visible = detail;
            if (detail) return ArrangePackDetail(p, w, y);

            // _legacySozluk is only set while saving the 1.5.0 "before" screenshot.
            if (_legacySozluk)
                y = OptionBox(p, y, w, "Canlı çeviri yapma (sadece hazır paket)", "Seçili oyunda eşleşme yoksa Google'a gitmez, satır gizlenir. Eşleşen satır paketin Türkçesini gösterir.", _packOnly);
            else
                y += OptionBox(p, y, w, "Canlı çeviri yapma (sadece hazır paket)", "Seçili oyunda eşleşme yoksa Google'a gitmez, satır gizlenir. Eşleşen satır paketin Türkçesini gösterir.", _packOnly) + Theme.D(14);
            y += p.AddSection(y, w, "Hazır çeviri paketleri") + Theme.D(8);
            if (_linePacks.Count == 0)
                y += p.AddWrap(0, y, w, "Paket yok. json dosyalarını klasöre bırak.", Theme.Small, Theme.Tx2) + Theme.D(8);
            else
            {
                InspectorPage.Put(_linePacks, 0, y, w, _linePacks.Height);
                y += _linePacks.Height + Theme.D(8);
            }
            int openW = Math.Max(Theme.D(120), Theme.Measure(_openLines.Text, Theme.SmallSemi).Width + Theme.D(28));
            InspectorPage.Put(_openLines, 0, y, Math.Min(w, openW), Theme.D(32));
            y += Theme.D(32) + Theme.D(16);

            InspectorPage.PutInput(_search, 0, y, w, Theme.D(36));
            y += Theme.D(36) + Theme.D(18);
            if (_gamePacks.Count > 0)
            {
                y += p.AddSection(y, w, "Oyun paketleri") + Theme.D(8);
                InspectorPage.Put(_gamePacks, 0, y, w, _gamePacks.Height);
                y += _gamePacks.Height + Theme.D(14);
            }
            _gamePacks.Visible = _gamePacks.Count > 0;
            if (_basePacks.Count > 0)
            {
                y += p.AddSection(y, w, "Genel paketler") + Theme.D(8);
                InspectorPage.Put(_basePacks, 0, y, w, _basePacks.Height);
                y += _basePacks.Height + Theme.D(14);
            }
            _basePacks.Visible = _basePacks.Count > 0;
            if (_gamePacks.Count == 0 && _basePacks.Count == 0) y += Theme.D(4);

            y += p.AddSection(y, w, "Terimlerim") + Theme.D(8);
            int add = Theme.D(34), arrow = Theme.D(20), gap = Theme.D(4);
            int col = (w - add - arrow - 2 * gap - Theme.D(4)) / 2;
            InspectorPage.PutInput(_termEn, 0, y, col, Theme.D(34));
            var ar = new Rectangle(col + gap, y, arrow, Theme.D(34));
            p.AddPaint(g => Theme.Glyph(g, Ic.Arrow, Theme.Icon12, ar, Theme.Tx3));
            InspectorPage.PutInput(_termTr, col + arrow + 2 * gap, y, col, Theme.D(34));
            InspectorPage.Put(_termAdd, w - add, y, add, add);
            y += Theme.D(34) + Theme.D(8);
            InspectorPage.Put(_terms, 0, y, w, _terms.Height);
            y += _terms.Height + Theme.D(6);
            int half = (w - Theme.D(8)) / 2;
            InspectorPage.Put(_import, 0, y, half, Theme.D(32));
            InspectorPage.Put(_export, half + Theme.D(8), y, w - half - Theme.D(8), Theme.D(32));
            y += Theme.D(32) + Theme.D(14);
            y += OptionBox(p, y, w, "Büyük harfli özel isimleri çevirme", "“Vice City”, “Marco” gibi cümle ortasındaki adlar olduğu gibi kalır.", _namesToggle);
            return y;
        });
        foreach (var c in new Control[] { _search, _gamePacks, _basePacks, _import, _export, _termEn, _termTr, _termAdd, _terms, _namesToggle, _packOnly, _openLines, _linePacks, _packBack, _packToggle, _packTerms })
            _glossaryPage.Controls.Add(c);
        HazirKutuphane.Yukle();
        FillGlossary();
    }

    // ---- one pack's terms ----

    private readonly UiButton _packBack = new() { Text = "Paketler", Glyph = Ic.Back, Kind = ButtonKind.Ghost, AlignLeft = true };
    private readonly UiToggle _packToggle = new();
    private readonly TermList _packTerms = new();
    private string? _openPack;

    private void OpenPack(string? id)
    {
        _openPack = id;
        _search.SetText("");
        _search.Placeholder = id == null ? "Paket veya terim ara" : "Bu pakette ara";
        FillGlossary();
        _scroll.ScrollTo(0);
        _scroll.Relayout();
    }

    private int ArrangePackDetail(InspectorPage p, int w, int y)
    {
        var pack = SozlukKutuphanesi.Paketler().FirstOrDefault(x => x.Id.Equals(_openPack, StringComparison.OrdinalIgnoreCase));
        if (pack == null) { _openPack = null; return y; }
        InspectorPage.Put(_packBack, -Theme.D(10), y - Theme.D(4), Theme.D(120), Theme.D(30));
        y += Theme.D(34);

        var look = PackLook.TryGetValue(pack.Id, out var l) ? l : (pack.Ad.Length >= 2 ? pack.Ad[..2].ToUpper(Tr) : pack.Ad.ToUpper(Tr), 0x566371);
        int t = Theme.D(36);
        var tile = new Rectangle(0, y, t, t);
        p.AddPaint(g =>
        {
            Theme.FillRound(g, tile, Theme.D(10), Theme.Hex(look.Item2));
            Theme.Text(g, look.Item1, Theme.Tag, tile, Color.White, Theme.Center);
        });
        int x = t + Theme.D(10), tw = w - x - Theme.D(54);
        p.AddText(new Rectangle(x, y, tw, Theme.D(20)), pack.Ad, Theme.BodySemi, Theme.Tx);
        bool on = (_host.Current.AcikPaketler ?? (IEnumerable<string>)SozlukKutuphanesi.VarsayilanAcik).Contains(pack.Id, StringComparer.OrdinalIgnoreCase);
        p.AddText(new Rectangle(x, y + Theme.D(19), tw, Theme.D(17)), pack.Entries.Count.ToString(Inv) + " terim · " + (on ? "açık" : "kapalı"), Theme.Small, Theme.Tx2);
        _packToggle.SetSilently(on);
        InspectorPage.Put(_packToggle, w - Theme.D(44), y + (t - Theme.D(24)) / 2, Theme.D(44), Theme.D(24));
        y += t + Theme.D(16);

        InspectorPage.PutInput(_search, 0, y, w, Theme.D(36));
        y += Theme.D(36) + Theme.D(10);
        y += p.AddWrap(0, y, w, "Sağda “olduğu gibi” yazanlar çevrilmez, oyundaki hâliyle kalır. Pakete ekleme yapmak için kendi terimini Terimlerim'e ekle.", Theme.Small, Theme.Tx2) + Theme.D(10);
        InspectorPage.Put(_packTerms, 0, y, w, _packTerms.Height);
        return y + _packTerms.Height;
    }

    private void FillGlossary()
    {
        string q = _search.Value.Trim();
        bool Match(string? s) => q.Length == 0 || (s != null && s.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        var s = _host.Current;
        var hazir = HazirKutuphane.Guncel;
        var secili = hazir.Sec(s.SeciliHazirOyun);
        _linePacks.Set(hazir.Paketler, secili?.Oyun ?? "");
        _packOnly.SetSilently(secili != null && (s.SadeceHazirPaket?.Any(g => g.Equals(secili.Oyun, StringComparison.OrdinalIgnoreCase)) ?? false));
        var open = new HashSet<string>(s.AcikPaketler ?? (IEnumerable<string>)SozlukKutuphanesi.VarsayilanAcik, StringComparer.OrdinalIgnoreCase);
        var packs = SozlukKutuphanesi.Paketler();
        if (_openPack != null)
        {
            var pack = packs.FirstOrDefault(x => x.Id.Equals(_openPack, StringComparison.OrdinalIgnoreCase));
            var entries = pack?.Entries.Where(e => Match(e.Term) || Match(e.Tr)) ?? Enumerable.Empty<SozlukKaydi>();
            _packTerms.Empty = q.Length > 0 ? "Bu pakette eşleşen terim yok." : "Bu paket boş.";
            _packTerms.SetRows(entries.Select(e => { var gi = e.ToGirdi(); return new TermRow(gi.Ingilizce, gi.Turkce, e.Category ?? ""); }));
            return;
        }
        PackRow Row(SozlukPaket p)
        {
            var look = PackLook.TryGetValue(p.Id, out var l) ? l
                : (p.Ad.Length >= 2 ? p.Ad[..2].ToUpper(Tr) : p.Ad.ToUpper(Tr), 0x566371);
            return new PackRow(p.Id, p.Ad, look.Item1, Theme.Hex(look.Item2), p.Entries.Count, open.Contains(p.Id));
        }
        bool PackMatch(SozlukPaket p) => Match(p.Ad) || (q.Length > 0 && p.Entries.Any(e => Match(e.Term) || Match(e.Tr)));
        _gamePacks.SetRows(packs.Where(p => p.OyunPaketi && PackMatch(p)).OrderByDescending(p => p.Id == "gta6").Select(Row));
        _basePacks.SetRows(packs.Where(p => !p.OyunPaketi && PackMatch(p)).Select(Row));

        var rows = new List<TermRow>();
        foreach (var g in s.Sozluk ?? new List<SozlukGirdisi>())
            if (Match(g.Ingilizce) || Match(g.Turkce)) rows.Add(new TermRow(g.Ingilizce, g.Turkce, null));
        if (q.Length > 0)
        {
            foreach (var p in packs.Where(p => open.Contains(p.Id)))
                foreach (var e in p.Entries.Where(e => Match(e.Term) || Match(e.Tr)))
                {
                    if (rows.Count >= 40) break;
                    var girdi = e.ToGirdi();
                    rows.Add(new TermRow(girdi.Ingilizce, girdi.Turkce, p.Ad));
                }
        }
        _terms.Empty = q.Length > 0 ? "Eşleşen terim yok." : "Henüz terim eklemedin.";
        _terms.SetRows(rows);
    }

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private void SetPackOnly(bool on)
    {
        var game = HazirKutuphane.Guncel.Sec(_host.Current.SeciliHazirOyun)?.Oyun;
        if (string.IsNullOrEmpty(game))
        {
            _packOnly.SetSilently(false);
            return;
        }
        Patch(s =>
        {
            s.SeciliHazirOyun = game;
            s.SadeceHazirPaket ??= new List<string>();
            s.SadeceHazirPaket.RemoveAll(x => x.Equals(game, StringComparison.OrdinalIgnoreCase));
            if (on) s.SadeceHazirPaket.Add(game);
        });
    }

    private void OpenLinesFolder()
    {
        try
        {
            HazirKutuphane.Yukle();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + HazirKutuphane.Klasor() + "\"") { UseShellExecute = true });
            FillGlossary();
            _scroll.Relayout();
        }
        catch (Exception ex) { ErrorLog.Write(ex, "lines-folder"); Notice(HazirKutuphane.Klasor()); }
    }

    private void TogglePack(string id, bool on)
    {
        Patch(s =>
        {
            s.AcikPaketler ??= new List<string>(SozlukKutuphanesi.VarsayilanAcik);
            s.AcikPaketler.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (on) s.AcikPaketler.Add(id);
        });
    }

    private void AddEntry()
    {
        string en = _termEn.Value.Trim(), tr = _termTr.Value.Trim();
        if (en.Length == 0) { _termEn.Box.Focus(); return; }
        Patch(s =>
        {
            s.Sozluk ??= new List<SozlukGirdisi>();
            s.Sozluk.RemoveAll(g => g.Ingilizce.Equals(en, StringComparison.OrdinalIgnoreCase));
            s.Sozluk.Insert(0, new SozlukGirdisi(en, tr));
        });
        _termEn.SetText("");
        _termTr.SetText("");
        _termEn.Box.Focus();
        FillGlossary();
        _scroll.Relayout();
    }

    private void DeleteEntry(string term)
    {
        Patch(s => s.Sozluk?.RemoveAll(g => g.Ingilizce.Equals(term, StringComparison.OrdinalIgnoreCase)));
        FillGlossary();
        _scroll.Relayout();
    }

    private void ExportEntries()
    {
        using var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "sozluk.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dlg.FileName, SozlukKutuphanesi.DisaAktar(_host.Current.Sozluk ?? new List<SozlukGirdisi>())); }
        catch (Exception ex) { ErrorLog.Write(ex, "glossary-export"); Notice("Sözlük kaydedilemedi."); }
    }

    private void ImportEntries()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        List<SozlukGirdisi> rows;
        try { rows = SozlukKutuphanesi.IceAktar(File.ReadAllText(dlg.FileName)); }
        catch (Exception ex) { ErrorLog.Write(ex, "glossary-import"); Notice("Bu dosya okunamadı. Dışa aktar ile kaydedilmiş bir sözlük seç."); return; }
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
        _scroll.Relayout();
    }

    private void Notice(string text) => MessageBox.Show(this, text, AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);

    // ---------------- Gelişmiş ----------------

    private GuncellemeBilgisi? _offer;
    private bool _checking, _ocrInstalling;
    private double _download = -1;
    private string _updateNote = "";

    private static readonly (string Code, string Name)[] Languages = { ("en", "İngilizce"), ("de", "Almanca"), ("fr", "Fransızca"), ("es", "İspanyolca"), ("it", "İtalyanca") };

    private void BuildAdvanced()
    {
        _updateBtn.Click += (_, _) => { if (_offer != null) _ = InstallAsync(); else _ = CheckUpdatesAsync(true); };
        _skipBtn.Click += (_, _) => { if (_offer != null) _host.SkipRelease(_offer.Tag); _offer = null; _updateNote = "Bu sürüm atlandı"; RefreshAdvanced(); RefreshStatus(); };
        _ocrBtn.Click += (_, _) => _ = InstallOcrAsync();
        var s = _host.Current;
        int lang = Array.FindIndex(Languages, l => OcrDilPaketi.Kod(s.KaynakDil) == l.Code);
        _langDrop.SetItems(Languages.Select(l => (l.Name, (object?)l.Code)), Math.Max(0, lang));
        _langDrop.SelectedChanged += (_, _) => { if (_langDrop.SelectedTag is string code) Patch(x => x.KaynakDil = code); RefreshAdvanced(); Invalidate(); };
        _whiteToggle.SetSilently(s.BeyazYaziFiltresi);
        _whiteToggle.CheckedChanged += (_, _) => Patch(x => x.BeyazYaziFiltresi = _whiteToggle.Checked);
        _startupToggle.SetSilently(AutoStart.Enabled);
        _startupToggle.CheckedChanged += (_, _) =>
        {
            if (!AutoStart.Set(_startupToggle.Checked))
            {
                _startupToggle.SetSilently(AutoStart.Enabled);
                Notice("Başlangıç ayarı değiştirilemedi.");
                return;
            }
            Patch(x => x.WindowsIleBaslat = _startupToggle.Checked);
        };
        _settingsBtn.Click += (_, _) => RevealSettings();
        _tourBtn.Click += (_, _) => StartTour();
        _autoUpdateToggle.SetSilently(s.AcilistaGuncellemeDenetle);
        _autoUpdateToggle.CheckedChanged += (_, _) => Patch(x => x.AcilistaGuncellemeDenetle = _autoUpdateToggle.Checked);

        _advancedPage = new InspectorPage("Gelişmiş", "Kurulum ve bakım ayarları.", (p, w, y) =>
        {
            var cur = _host.Current;
            string version = SurumKarsilastirma.Tag(GuncellemeIstegi.UygulamaSurumu);
            string updateSub = _offer != null
                ? "Yeni sürüm hazır: " + _offer.Tag + " (şu an " + version + ")"
                : "Sürüm " + version[1..] + (_updateNote.Length > 0 ? " · " + _updateNote : "");
            _updateBtn.Text = _checking ? "Denetleniyor…" : _offer != null ? (_download >= 0 ? "İndiriliyor…" : "Güncelle") : "Denetle";
            _updateBtn.Kind = _offer != null ? ButtonKind.Primary : ButtonKind.Secondary;
            _updateBtn.Enabled = !_checking && _download < 0;
            y = Row(p, y, w, Ic.Sync, "Güncellemeler", updateSub, _updateBtn, Theme.D(_offer != null ? 98 : 84), null, first: true);
            if (_offer != null)
            {
                InspectorPage.Put(_skipBtn, Theme.D(46) - Theme.D(14), y - Theme.D(10), Theme.D(136), Theme.D(28));
                y += Theme.D(24);
            }
            _skipBtn.Visible = _offer != null && _download < 0;
            if (_download >= 0)
            {
                var bar = new Rectangle(Theme.D(46), y - Theme.D(12), w - Theme.D(46), Theme.D(4));
                double v = _download;
                p.AddPaint(g =>
                {
                    Theme.FillRound(g, bar, Theme.D(2), Theme.Hex(0xE3E8EC));
                    Theme.FillRound(g, new Rectangle(bar.X, bar.Y, Math.Max(Theme.D(4), (int)(bar.Width * v)), bar.Height), Theme.D(2), Theme.AccD);
                });
                y += Theme.D(6);
            }
            p.AddDivider(y, w);

            bool missing = _host.OcrPaketiEksik;
            string code = OcrDilPaketi.Capability(cur.KaynakDil).Split('~')[3];
            _ocrBtn.Visible = missing;
            _ocrBtn.Text = _ocrInstalling ? "Kuruluyor…" : "Kur";
            _ocrBtn.Enabled = !_ocrInstalling;
            y = Row(p, y, w, Ic.Globe, "OCR dil paketi", OcrDilPaketi.DilAdi(cur.KaynakDil) + " (" + code + ") · Windows OCR",
                missing ? _ocrBtn : null, Theme.D(_ocrInstalling ? 104 : 64), (missing ? "Kurulu değil" : "Kurulu", missing ? BadgeKind.Warn : BadgeKind.Ok));
            p.AddDivider(y, w);
            y = Row(p, y, w, Ic.Font, "Altyazı dili", "Oyundaki altyazının dili. OCR bu dile göre okur.", null, 0, null);
            InspectorPage.Put(_langDrop, Theme.D(46), y - Theme.D(6), w - Theme.D(46), Theme.D(34));
            y += Theme.D(34) + Theme.D(8);
            p.AddDivider(y, w);
            y = Row(p, y, w, Ic.Filter, "Beyaz yazı filtresi", "Yalnızca beyaz yazıyı okur; karışık arka planda daha iyi çalışır.", _whiteToggle, Theme.D(44), null);
            p.AddDivider(y, w);
            y = Row(p, y, w, Ic.Power, "Windows açılınca başlat", "Oturum açınca arka planda hazır bekler.", _startupToggle, Theme.D(44), null);
            p.AddDivider(y, w);
            y = Row(p, y, w, Ic.Download, "Otomatik denetim", "Açılışta yeni sürüm var mı diye bakar.", _autoUpdateToggle, Theme.D(44), null);
            p.AddDivider(y, w);
            y = Row(p, y, w, Ic.Select, "Ayar dosyası", "Tüm ayarlar ayarlar.json dosyasına kaydedilir; program açılırken buradan okur.", _settingsBtn, Theme.D(72), null);
            p.AddDivider(y, w);
            y = Row(p, y, w, Ic.Keyboard, "Kısayollar", "F8 alanı ekranda işaretle · F9 duraklat · F10 çıkış. Pencereyi kapatmak tepsiye küçültür.", null, 0, null);
            p.AddDivider(y, w);
            y = Row(p, y, w, Ic.Info, "Tanıtımı tekrar göster", "Pencereyi adım adım anlatır. İlk açılışta bir kez kendiliğinden çıkar.", _tourBtn, Theme.D(84), null);
            return y;
        });
        foreach (var c in new Control[] { _updateBtn, _skipBtn, _ocrBtn, _langDrop, _whiteToggle, _startupToggle, _autoUpdateToggle, _settingsBtn, _tourBtn })
            _advancedPage.Controls.Add(c);
    }

    /// <summary>Settings row: icon tile, title, explanation, optional badge, and a control on the right.</summary>
    private static int Row(InspectorPage p, int y, int w, string glyph, string title, string sub, Control? right, int rightW, (string Text, BadgeKind Kind)? badge, bool first = false)
    {
        int pad = first ? 0 : Theme.D(14);
        int tile = Theme.D(36), gap = Theme.D(10);
        int x = tile + gap;
        int tw = w - x - (right != null ? rightW + gap : 0);
        y += pad;
        int titleH = Math.Max(Theme.D(19), Theme.Measure(title, Theme.BodySemi).Height);
        int subH = Theme.WrapHeight(sub, Theme.Small, tw);
        int h = titleH + Theme.D(2) + subH + (badge != null ? Theme.D(28) : 0);
        h = Math.Max(h, tile);
        p.AddTile(new Rectangle(0, y, tile, tile), glyph, Theme.AccT, Theme.AccD);
        p.AddText(new Rectangle(x, y, tw, titleH), title, Theme.BodySemi, Theme.Tx);
        p.AddWrap(x, y + titleH + Theme.D(2), tw, sub, Theme.Small, Theme.Tx2);
        if (badge is { } b)
        {
            var size = Badge.Measure(b.Text, null);
            p.AddBadge(new Rectangle(x, y + titleH + Theme.D(6) + subH, size.Width, size.Height), b.Text, b.Kind);
        }
        if (right != null)
        {
            int rh = right is UiToggle ? Theme.D(24) : Theme.D(34);
            InspectorPage.Put(right, w - rightW, y + Math.Max(0, (Math.Min(h, tile) - rh) / 2), rightW, rh);
        }
        return y + h + Theme.D(14);
    }

    private void RefreshAdvanced()
    {
        int lang = Array.FindIndex(Languages, l => OcrDilPaketi.Kod(_host.Current.KaynakDil) == l.Code);
        _langDrop.SelectSilently(Math.Max(0, lang));
        if (_scroll.Page == _advancedPage) _scroll.Relayout();
    }

    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (_checking) return;
        _checking = true;
        RefreshAdvanced();
        try
        {
            var info = await GuncellemeIstegi.SorgulaAsync().ConfigureAwait(true);
            if (IsDisposed) return;
            if (info == null) { if (interactive) _updateNote = "denetlenemedi, internet bağlantını kontrol et"; return; }
            if (!SurumKarsilastirma.IsNewer(info.Tag, GuncellemeIstegi.UygulamaSurumu)) { _updateNote = "güncel, az önce denetlendi"; return; }
            if (!interactive && !GuncellemeOkuyucu.BannerGoster(GuncellemeIstegi.UygulamaSurumu, info, _host.Current.AtlananSurum)) return;
            _offer = info;
        }
        finally
        {
            _checking = false;
            if (!IsDisposed) { RefreshAdvanced(); RefreshStatus(); }
        }
    }

    private async Task InstallAsync()
    {
        var info = _offer;
        if (info == null || _download >= 0) return;
        if (!_host.KurulumKopyasi || !GuncellemeOkuyucu.GuvenliKurulumAdresi(info.KurulumUrl))
        {
            GuncellemeIstegi.SayfayiAc(info.Sayfa);
            return;
        }
        _download = 0;
        RefreshAdvanced();
        var path = await GuncellemeIstegi.IndirAsync(info, new Progress<double>(p =>
        {
            _download = p;
            if (_scroll.Page == _advancedPage) _scroll.Relayout();
        }), CancellationToken.None).ConfigureAwait(true);
        _download = -1;
        if (path == null)
        {
            RefreshAdvanced();
            GuncellemeIstegi.SayfayiAc(info.Sayfa);
            return;
        }
        try { GuncellemeIstegi.KurulumuBaslat(path); }
        catch (Exception ex) { ErrorLog.Write(ex, "update"); GuncellemeIstegi.SayfayiAc(info.Sayfa); RefreshAdvanced(); return; }
        _host.Quit();
    }

    private async Task InstallOcrAsync()
    {
        if (_ocrInstalling) return;
        _ocrInstalling = true;
        RefreshAdvanced();
        try
        {
            var (ok, mesaj) = await OcrPaketKur.KurAsync(_host.Current.KaynakDil).ConfigureAwait(true);
            if (IsDisposed) return;
            if (!ok) { Notice(mesaj); return; }
            if (!_host.YenidenYukleOcr())
                Notice("Paket kuruldu. Tanıma açılmazsa " + AppInfo.DisplayName + "'ı kapatıp yeniden aç.");
        }
        finally
        {
            _ocrInstalling = false;
            if (!IsDisposed) { RefreshAdvanced(); RefreshStatus(); }
        }
    }
}

/// <summary>Per-user "start with Windows" through the HKCU Run key. The installer removes it on uninstall.</summary>
internal static class AutoStart
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "AltyaziCevirici";

    public static bool Enabled
    {
        get
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
                return k?.GetValue(Name) is string v && v.Length > 0;
            }
            catch (Exception ex) { ErrorLog.Write(ex, "autostart"); return false; }
        }
    }

    public static bool Set(bool on)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
            if (on)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                k.SetValue(Name, "\"" + exe + "\"");
            }
            else k.DeleteValue(Name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) { ErrorLog.Write(ex, "autostart"); return false; }
    }
}
