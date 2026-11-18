using System.Windows.Threading;

namespace AltyaziCevirici.Ui;

public sealed class ShellWindow : Window
{
    private readonly IAppHost _host;
    private readonly bool _demo;
    private readonly PreviewSurface _preview = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _startLabel = new()
    {
        Text = "Başlat",
        Foreground = new SolidColorBrush(Color.FromRgb(10, 24, 20)),
        FontWeight = FontWeights.Bold,
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _start = new();
    private readonly StackPanel _pageHost = new() { Margin = new Thickness(16, 8, 16, 12) };
    private readonly ScrollViewer _pageScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 220 };
    private readonly List<Border> _tabs = new();
    private readonly Dictionary<string, TextBox> _nums = new();
    private bool _loading = true;
    private bool _allowClose;
    private int _page;
    private DispatcherTimer? _timer;

    public ShellWindow(IAppHost host, bool demo = false)
    {
        _host = host;
        _demo = demo;
        Title = "Altyazı Çevirici";
        Width = 1240;
        Height = 840;
        MinWidth = 980;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = BrushOf("#F2F4F8");
        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/AltyaziCevirici.Ui;component/Assets/icon.png", UriKind.Absolute));
        }
        catch (Exception) { /* exe icon remains */ }

        var root = new Border
        {
            Background = BrushOf("#101216"),
            BorderBrush = BrushOf("#2A3140"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = BuildLayout(),
        };
        Content = root;
        _preview.Edited += SaveFromPreview;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            try { NativeWindow.ExcludeFromCapture(hwnd); } catch (Exception) { /* older Windows */ }
        };
        _host.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        if (!_demo)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _timer.Tick += (_, _) => { _preview.GrabLive(); Refresh(); };
            _timer.Start();
        }
        Loaded += (_, _) => { LoadScene(); _loading = false; if (_demo) _preview.SetFrame(DemoFrame()); else _preview.GrabLive(); };
        Closing += OnClosing;
    }

    public void ShowFromHost()
    {
        if (System.Windows.Application.Current == null)
        {
            var created = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _ = created.Dispatcher;
        }
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void AllowClose() => _allowClose = true;

    public void CaptureShots(string dir)
    {
        Directory.CreateDirectory(dir);
        Height = 1180;
        _pageScroll.MaxHeight = 520;
        ShowPage(0);
        UpdateLayout();
        Save(this, Path.Combine(dir, "01-onizleme.png"));
        Save(_preview, Path.Combine(dir, "05-tutamaclar.png"));
        ShowPage(1);
        UpdateLayout();
        Save(this, Path.Combine(dir, "02-gorunum.png"));
        ShowPage(2);
        UpdateLayout();
        Save(this, Path.Combine(dir, "03-sozluk.png"));
        ShowPage(3);
        UpdateLayout();
        Save(this, Path.Combine(dir, "04-gelismis.png"));
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose || _demo) return;
        e.Cancel = true;
        Hide();
        _host.NotifyMinimizedToTray();
    }

    private Grid BuildLayout()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 380 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(TitleBar());
        Grid.SetRow(_preview, 1);
        _preview.Margin = new Thickness(16, 4, 16, 8);
        grid.Children.Add(_preview);
        var sources = SourceBar();
        Grid.SetRow(sources, 2);
        grid.Children.Add(sources);
        var action = ActionBar();
        Grid.SetRow(action, 3);
        grid.Children.Add(action);
        var lower = new DockPanel();
        var tabs = TabBar();
        DockPanel.SetDock(tabs, Dock.Top);
        lower.Children.Add(tabs);
        _pageScroll.Content = _pageHost;
        lower.Children.Add(_pageScroll);
        Grid.SetRow(lower, 4);
        grid.Children.Add(lower);
        ShowPage(0);
        return grid;
    }

    private DockPanel TitleBar()
    {
        var bar = new DockPanel { Margin = new Thickness(8, 6, 8, 0) };
        bar.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ToggleMax(); else DragMove(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Chrome("—", () => WindowState = WindowState.Minimized));
        buttons.Children.Add(Chrome("▢", ToggleMax));
        buttons.Children.Add(Chrome("✕", Close, danger: true));
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new Image { Width = 22, Height = 22, Margin = new Thickness(8, 0, 8, 0), Source = Icon });
        title.Children.Add(new TextBlock { Text = "Altyazı Çevirici", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(title);
        return bar;
    }

    private WrapPanel SourceBar()
    {
        var wrap = new WrapPanel { Margin = new Thickness(16, 0, 16, 4) };
        wrap.Children.Add(Muted("Kaynak"));
        int n = 1;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            var label = "Ekran " + n.ToString(CultureInfo.InvariantCulture) + (screen.Primary ? " · ana" : "");
            var chip = Chip(label, () =>
            {
                _preview.SetScene(new EkranDikdortgen(bounds.X, bounds.Y, bounds.Width, bounds.Height), _preview.Ocr, _preview.Caption, _host.Current);
                Patch(s => s.MonitorAdi = screen.DeviceName);
                if (!_demo) _preview.GrabLive();
            });
            wrap.Children.Add(chip);
            n++;
        }
        return wrap;
    }

    private StackPanel ActionBar()
    {
        var grid = new Grid { Margin = new Thickness(16, 4, 16, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _start.Background = BrushOf("#3DDCB4");
        _start.CornerRadius = new CornerRadius(10);
        _start.Width = 168;
        _start.Height = 46;
        _start.Cursor = Cursors.Hand;
        _start.Child = _startLabel;
        _start.MouseLeftButtonUp += (_, _) => { if (_host.IsRunning) _host.StopCapture(); else _host.StartCapture(); Refresh(); };
        _start.MouseEnter += (_, _) => _start.Opacity = 0.88;
        _start.MouseLeave += (_, _) => _start.Opacity = 1;
        grid.Children.Add(_start);
        _status.Margin = new Thickness(16, 0, 0, 0);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Foreground = BrushOf("#93A0B0");
        _status.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(_status, 1);
        grid.Children.Add(_status);
        var fields = FieldRow();
        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(fields);
        return stack;
    }

    private WrapPanel FieldRow()
    {
        var row = new WrapPanel { Margin = new Thickness(16, 0, 16, 6) };
        row.Children.Add(Muted("Orijinal"));
        AddNum(row, "ox", "X"); AddNum(row, "oy", "Y"); AddNum(row, "ow", "W"); AddNum(row, "oh", "H");
        row.Children.Add(Muted("Çeviri"));
        AddNum(row, "cx", "X"); AddNum(row, "cy", "Y"); AddNum(row, "cw", "W"); AddNum(row, "ch", "H");
        var snap = Chip("Kenarlara yapış", () => _preview.SnapEnabled = !_preview.SnapEnabled);
        row.Children.Add(snap);
        return row;
    }

    private void AddNum(Panel row, string key, string caption)
    {
        row.Children.Add(Muted(caption));
        var box = new TextBox
        {
            Width = 72,
            Height = 28,
            Margin = new Thickness(4, 0, 4, 4),
            Background = BrushOf("#1C212B"),
            Foreground = BrushOf("#F2F4F8"),
            BorderBrush = BrushOf("#313844"),
            CaretBrush = BrushOf("#3DDCB4"),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.LostFocus += (_, _) => ApplyNumbers();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyNumbers(); };
        _nums[key] = box;
        row.Children.Add(box);
    }

    private StackPanel TabBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 6, 16, 0) };
        string[] names = { "Tuval", "Görünüm", "Sözlük", "Gelişmiş" };
        for (int i = 0; i < names.Length; i++)
        {
            int page = i;
            var b = Chip(names[i], () => ShowPage(page), selected: i == 0);
            b.Tag = page;
            _tabs.Add(b);
            bar.Children.Add(b);
        }
        return bar;
    }

    public void ShowPage(int page)
    {
        _page = page;
        foreach (var tab in _tabs)
            tab.Background = (int)tab.Tag! == page ? BrushOf("#24342F") : BrushOf("#1C212B");
        _pageHost.Children.Clear();
        _pageHost.Children.Add(page switch
        {
            1 => StylePage(),
            2 => DictionaryPage(),
            3 => AdvancedPage(),
            _ => CanvasHelp(),
        });
    }

    private static TextBlock CanvasHelp() => Block("Önizlemenin üstündeki iki kutu serbestçe hareket eder. Beyaz kareler 8 tutamacıdır: köşeler ve kenarlar. Sarı kutu oyunun İngilizce altyazısını okur, turkuaz kutu Türkçe yazının yeridir. Altındaki sayılar X Y W H yedekleridir.");

    private StackPanel StylePage()
    {
        var s = _host.Current;
        var stack = new StackPanel();
        stack.Children.Add(SliderRow("Yazı boyutu", 10, 72, (int)s.YaziBoyutu, v => { Patch(x => x.YaziBoyutu = v); _host.ShowPreview(); PaintLook(); }));
        stack.Children.Add(SliderRow("Arka plan", 0, 100, s.ArkaPlanOpakligi, v => { Patch(x => x.ArkaPlanOpakligi = v); _host.ShowPreview(); PaintLook(); }));
        stack.Children.Add(SliderRow("Dış çizgi", 0, 12, s.DisCizgi, v => { Patch(x => x.DisCizgi = v); _host.ShowPreview(); PaintLook(); }));
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (name, hex) in new[] { ("Beyaz", "#FFFFFF"), ("Sarı", "#FFE14D"), ("Turkuaz", "#3DDCB4") })
        {
            var chip = Chip(name, () => { Patch(x => x.YaziRengi = hex); _host.ShowPreview(); PaintLook(); });
            colors.Children.Add(chip);
        }
        stack.Children.Add(colors);
        stack.Children.Add(Block("Örnek cümle tuvaldeki turkuaz kutuda ve gerçek katmanda anında değişir."));
        return stack;
    }

    private StackPanel DictionaryPage()
    {
        var stack = new StackPanel();
        var toggles = new WrapPanel();
        var open = new HashSet<string>(_host.Current.AcikPaketler ?? SozlukKutuphanesi.VarsayilanAcik.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var paket in SozlukKutuphanesi.Paketler())
        {
            bool on = open.Contains(paket.Id);
            var id = paket.Id;
            toggles.Children.Add(Chip(paket.Ad + (on ? "  · açık" : "  · kapalı"), () => SetPack(id, !on), on));
        }
        stack.Children.Add(toggles);
        var search = new TextBox { Width = 280, Height = 32, Margin = new Thickness(0, 4, 0, 8), Background = BrushOf("#1C212B"), Foreground = BrushOf("#F2F4F8"), BorderBrush = BrushOf("#313844") };
        var list = new ListBox
        {
            Height = 120,
            Background = BrushOf("#1C212B"),
            Foreground = BrushOf("#F2F4F8"),
            BorderThickness = new Thickness(0),
            ItemContainerStyle = new Style(typeof(ListBoxItem))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, BrushOf("#1C212B")),
                    new Setter(Control.ForegroundProperty, BrushOf("#F2F4F8")),
                },
            },
        };
        void Fill()
        {
            list.Items.Clear();
            foreach (var g in SozlukKutuphanesi.Etkin(_host.Current))
            {
                string line = g.Ingilizce + (string.IsNullOrEmpty(g.Turkce) ? "  →  koru" : "  →  " + g.Turkce);
                if (search.Text.Length > 0 && line.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Items.Add(line);
            }
        }
        search.TextChanged += (_, _) => Fill();
        var add = Chip("Terim ekle", () =>
        {
            Patch(s =>
            {
                s.Sozluk ??= new List<SozlukGirdisi>();
                s.Sozluk.Add(new SozlukGirdisi("new term", "yeni"));
            });
            Fill();
        });
        stack.Children.Add(search);
        stack.Children.Add(add);
        stack.Children.Add(list);
        Fill();
        return stack;
    }

    private StackPanel AdvancedPage()
    {
        var stack = new StackPanel();
        stack.Children.Add(Block("F8 ana pencere · F9 duraklat · F10 çıkış. Pencereyi kapatmak tepsiye küçültür."));
        stack.Children.Add(Chip(_host.Current.AcilistaGuncellemeDenetle ? "Açılışta güncelleme  · açık" : "Açılışta güncelleme  · kapalı",
            () => { Patch(s => s.AcilistaGuncellemeDenetle = !s.AcilistaGuncellemeDenetle); ShowPage(3); }, _host.Current.AcilistaGuncellemeDenetle));
        if (!string.IsNullOrEmpty(_host.UpdateBanner)) stack.Children.Add(Block(_host.UpdateBanner));
        stack.Children.Add(Chip("Güncellemeleri denetle", () => _host.CheckUpdates()));
        stack.Children.Add(Chip("Güncelle", () => _host.BeginUpdateInstall()));
        stack.Children.Add(Chip("Bu sürümü atla", () => _host.SkipUpdateOffer()));
        stack.Children.Add(Chip(_host.Current.OzelIsimleriKoru ? "Büyük harfli isimleri koru  · açık" : "Büyük harfli isimleri koru  · kapalı",
            () => { Patch(s => s.OzelIsimleriKoru = !s.OzelIsimleriKoru); ShowPage(3); }, _host.Current.OzelIsimleriKoru));
        stack.Children.Add(Block(_host.OcrPaketiEksik ? OcrDilPaketi.Banner(_host.Current.KaynakDil) : "Yazı tanıma paketi hazır."));
        stack.Children.Add(Chip("Yazı tanıma paketini kur", () => _host.InstallOcrPack()));
        return stack;
    }

    private void SetPack(string id, bool on)
    {
        Patch(s =>
        {
            s.AcikPaketler ??= new List<string>(SozlukKutuphanesi.VarsayilanAcik);
            if (on)
            {
                if (!s.AcikPaketler.Contains(id, StringComparer.OrdinalIgnoreCase)) s.AcikPaketler.Add(id);
            }
            else s.AcikPaketler.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        });
        if (_page == 2) ShowPage(2);
    }

    private void LoadScene()
    {
        var s = _host.Current;
        var screen = _demo ? new EkranDikdortgen(0, 0, 1920, 1080) : ScreenOf(s);
        var ocr = s.HasRegion ? new EkranDikdortgen(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight) : new EkranDikdortgen(screen.X + 200, screen.Bottom - 140, Math.Min(900, screen.Width - 400), 80);
        var cap = s.KonumAyarlandi && s.KutuGenislik >= 32
            ? new EkranDikdortgen(s.KutuX, s.KutuY, s.KutuGenislik, s.KutuYukseklik > 0 ? s.KutuYukseklik : 72)
            : new EkranDikdortgen(ocr.X + 40, ocr.Y - 100, ocr.Width - 80, 72);
        _preview.SetScene(screen, ocr, cap, s);
        WriteNumbers();
        Refresh();
    }

    private void SaveFromPreview()
    {
        var s = _host.Current.Clone();
        var ocr = _preview.Ocr;
        s.RegionX = ocr.X; s.RegionY = ocr.Y; s.RegionWidth = ocr.Width; s.RegionHeight = ocr.Height;
        var cap = _preview.Caption;
        SubtitleLayout.Remember(s, cap, ocr, follow: false);
        s.KutuYukseklik = cap.Height;
        s.KonumAyarlandi = true;
        _host.Apply(s, true);
        WriteNumbers();
        _host.ShowPreview();
    }

    private void ApplyNumbers()
    {
        if (_loading) return;
        if (!TryNum("ox", out int ox) || !TryNum("oy", out int oy) || !TryNum("ow", out int ow) || !TryNum("oh", out int oh)) return;
        if (!TryNum("cx", out int cx) || !TryNum("cy", out int cy) || !TryNum("cw", out int cw) || !TryNum("ch", out int ch)) return;
        ow = Math.Max(BoxEdit.MinSize, ow); oh = Math.Max(BoxEdit.MinSize, oh);
        cw = Math.Max(BoxEdit.MinSize, cw); ch = Math.Max(BoxEdit.MinSize, ch);
        var s = _host.Current.Clone();
        s.RegionX = ox; s.RegionY = oy; s.RegionWidth = ow; s.RegionHeight = oh;
        SubtitleLayout.Remember(s, new EkranDikdortgen(cx, cy, cw, ch), new EkranDikdortgen(ox, oy, ow, oh), false);
        s.KutuYukseklik = ch;
        s.KonumAyarlandi = true;
        _host.Apply(s, true);
        _preview.SetScene(_preview.ScreenBox, new EkranDikdortgen(ox, oy, ow, oh), new EkranDikdortgen(cx, cy, cw, ch), s);
        _host.ShowPreview();
    }

    private void WriteNumbers()
    {
        _loading = true;
        SetNum("ox", _preview.Ocr.X); SetNum("oy", _preview.Ocr.Y); SetNum("ow", _preview.Ocr.Width); SetNum("oh", _preview.Ocr.Height);
        SetNum("cx", _preview.Caption.X); SetNum("cy", _preview.Caption.Y); SetNum("cw", _preview.Caption.Width); SetNum("ch", _preview.Caption.Height);
        _loading = false;
    }

    private void SetNum(string key, int value) { if (_nums.TryGetValue(key, out var box)) box.Text = value.ToString(CultureInfo.InvariantCulture); }
    private bool TryNum(string key, out int value) => int.TryParse(_nums[key].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void Refresh()
    {
        bool run = _host.IsRunning;
        _startLabel.Text = run ? "Durdur" : "Başlat";
        _start.Background = run ? BrushOf("#FF5D5D") : BrushOf("#3DDCB4");
        _startLabel.Foreground = run ? Brushes.White : new SolidColorBrush(Color.FromRgb(10, 24, 20));
        string line = string.IsNullOrWhiteSpace(_host.SonMetin) ? "—" : _host.SonMetin;
        _status.Text = _host.OcrDurum + "   ·   Son çeviri: " + line + "   ·   " + (_host.SonGecikmeMs > 0 ? _host.SonGecikmeMs.ToString(CultureInfo.InvariantCulture) + " ms" : "gecikme —");
    }

    private void PaintLook() => _preview.SetScene(_preview.ScreenBox, _preview.Ocr, _preview.Caption, _host.Current);

    private void Patch(Action<AppSettings> edit)
    {
        var s = _host.Current.Clone();
        edit(s);
        _host.Apply(s, true);
    }

    private static EkranDikdortgen ScreenOf(AppSettings s)
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            if (!string.IsNullOrEmpty(s.MonitorAdi) && screen.DeviceName.Equals(s.MonitorAdi, StringComparison.OrdinalIgnoreCase))
                return new EkranDikdortgen(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        var primary = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        return new EkranDikdortgen(primary.X, primary.Y, primary.Width, primary.Height);
    }

    private static StackPanel SliderRow(string label, int min, int max, int value, Action<int> changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center, Foreground = BrushOf("#F2F4F8") });
        var fill = new Border
        {
            Height = 8,
            Width = 8,
            CornerRadius = new CornerRadius(4),
            Background = BrushOf("#3DDCB4"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var track = new Border
        {
            Width = 280,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = BrushOf("#2A3140"),
            Cursor = Cursors.Hand,
            Child = fill,
        };
        var readout = new TextBlock { Text = value.ToString(CultureInfo.InvariantCulture), Width = 48, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = BrushOf("#3DDCB4") };
        void Paint(int v)
        {
            double t = max == min ? 0 : (v - min) / (double)(max - min);
            fill.Width = Math.Max(16, t * track.Width);
            readout.Text = v.ToString(CultureInfo.InvariantCulture);
        }
        void Seek(Point at)
        {
            double t = Math.Clamp(at.X / track.Width, 0, 1);
            int v = (int)Math.Round(min + (t * (max - min)));
            Paint(v);
            changed(v);
        }
        track.MouseLeftButtonDown += (_, e) => { track.CaptureMouse(); Seek(e.GetPosition(track)); };
        track.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) Seek(e.GetPosition(track)); };
        track.MouseLeftButtonUp += (_, _) => track.ReleaseMouseCapture();
        Paint(value);
        row.Children.Add(track);
        row.Children.Add(readout);
        return row;
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = BrushOf("#93A0B0"),
        Margin = new Thickness(0, 4, 0, 4),
        MaxWidth = 900,
    };

    private static TextBlock Muted(string text) => new() { Text = text, Foreground = BrushOf("#93A0B0"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) };

    private static Border Chip(string text, Action click, bool selected = false)
    {
        var idle = selected ? BrushOf("#24342F") : BrushOf("#1C212B");
        var label = new TextBlock { Text = text, Foreground = BrushOf("#F2F4F8"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var box = new Border
        {
            Child = label,
            Background = idle,
            BorderBrush = selected ? BrushOf("#3DDCB4") : BrushOf("#313844"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 6),
            Cursor = Cursors.Hand,
        };
        box.MouseEnter += (_, _) => box.Background = BrushOf("#2C3A36");
        box.MouseLeave += (_, _) => box.Background = idle;
        box.MouseLeftButtonUp += (_, _) => click();
        return box;
    }

    private static Border Chrome(string glyph, Action click, bool danger = false)
    {
        var box = new Border
        {
            Width = 36, Height = 28, CornerRadius = new CornerRadius(7), Margin = new Thickness(4, 0, 0, 0),
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
            Child = new TextBlock { Text = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = BrushOf("#F2F4F8") },
        };
        box.MouseEnter += (_, _) => box.Background = danger ? BrushOf("#FF5D5D") : BrushOf("#2A3140");
        box.MouseLeave += (_, _) => box.Background = Brushes.Transparent;
        box.MouseLeftButtonUp += (_, _) => click();
        return box;
    }

    private void ToggleMax() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public static void ExportShots(string directory)
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) =>
        {
            var win = new ShellWindow(new DemoHost(), demo: true);
            win.Show();
            win.CaptureShots(directory);
            win.AllowClose();
            win.Close();
            app.Shutdown();
        };
        if (!app.Dispatcher.HasShutdownStarted) app.Run();
    }

    private static RenderTargetBitmap DemoFrame()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 20, 28)), null, new Rect(0, 0, 1920, 1080));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(236, 236, 236)), null, new Rect(360, 900, 1200, 90));
            var type = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var text = new FormattedText("Get in the car, we're late.", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, type, 42, Brushes.Black, 1);
            dc.DrawText(text, new Point(520, 918));
        }
        var bmp = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void Save(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        double w = Math.Max(1, element.ActualWidth);
        double h = Math.Max(1, element.ActualHeight);
        var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(element);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        enc.Save(file);
    }

    private static SolidColorBrush BrushOf(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}

public sealed class DemoHost : IAppHost
{
    private AppSettings _settings = new()
    {
        RegionX = 360, RegionY = 900, RegionWidth = 1200, RegionHeight = 90,
        KonumAyarlandi = true, KutuX = 420, KutuY = 640, KutuGenislik = 1080, KutuYukseklik = 120,
        YaziBoyutu = 28, ArkaPlanOpakligi = 70, DisCizgi = 4, YaziRengi = "#FFFFFF",
        OzelIsimleriKoru = true, AcilistaGuncellemeDenetle = true,
    };

    public DemoHost() { _settings.Clamp(); }
    public AppSettings Current => _settings;
    public bool IsRunning => false;
    public bool IsPaused => false;
    public string SonMetin => "Örnek çeviri hazır";
    public int SonGecikmeMs => 180;
    public string OcrDurum => "Durdu";
    public bool KurulumKopyasi => true;
    public bool OcrPaketiEksik => false;
    public bool YenidenYukleOcr() => true;
    public event EventHandler? StateChanged;
    public void Apply(AppSettings edited, bool persist) { _settings = edited; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void SelectRegion() { }
    public void TogglePause() { }
    public void StartCapture() { }
    public void StopCapture() { }
    public void ShowPreview() { }
    public void BeginPlacementEdit() { }
    public void ShowMain() { }
    public void NotifyMinimizedToTray() { }
    public void SkipRelease(string tag) { }
    public void Quit() { }
    public string UpdateBanner => "";
    public void CheckUpdates() { }
    public void BeginUpdateInstall() { }
    public void SkipUpdateOffer() { }
    public void InstallOcrPack() { }
}

internal static class NativeWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public static void ExcludeFromCapture(IntPtr hwnd) => SetWindowDisplayAffinity(hwnd, 0x11);
}
