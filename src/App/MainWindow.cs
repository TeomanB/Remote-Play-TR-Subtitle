using System.Globalization;
using System.Runtime.InteropServices;
using AltyaziCevirici.Core;
using AltyaziCevirici.Ui;

namespace AltyaziCevirici;

/// <summary>
/// Control center (docs/design/claude-a, concept A "Stüdyo"): sidebar, large live preview with the two boxes,
/// source bar, last translation, and an inspector on the right. Custom title bar on a normal sizable window, so
/// Windows keeps the shadow, rounded corners, snapping, and resize borders.
/// </summary>
internal sealed partial class MainWindow : Form
{
    public const string OrnekAltyazi = "Polisler gelmeden buradan çıkmamız lazım.";

    private readonly IAppHost _host;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 400 };

    private readonly TitleButton _min = new() { Glyph = Ic.Min };
    private readonly TitleButton _max = new() { Glyph = Ic.Max };
    private readonly TitleButton _close = new() { Glyph = Ic.ChromeClose, IsClose = true };
    private readonly TitleButton _help = new() { Glyph = Ic.Info };
    private readonly CoachTour _tour = new();
    private int _pageBeforeTour;
    /// <summary>Screenshot-only: reproduce the 1.5.0 Sözlük stacking bug.</summary>
    private bool _legacySozluk;

    private readonly NavItem[] _nav =
    {
        new() { Text = "Önizleme", Glyph = Ic.Crop },
        new() { Text = "Görünüm", Glyph = Ic.Font },
        new() { Text = "Sözlük", Glyph = Ic.Book },
        new() { Text = "Gelişmiş", Glyph = Ic.Sliders },
    };
    private readonly UiCard _statusCard = new();
    private readonly UiButton _run = new() { Kind = ButtonKind.Primary, Large = true, Glyph = Ic.Play, Text = "Başlat" };

    private readonly LegendChip _chipOrig = new() { Text = "Orijinal altyazı", Key = Theme.KeyOrig };
    private readonly LegendChip _chipTrans = new() { Text = "Çeviri", Key = Theme.Acc };
    private readonly UiCard _previewCard = new();
    private readonly PreviewStage _stage = new();
    private readonly UiCard _sourceCard = new();
    private readonly UiDropdown _sourceDrop = new() { Glyph = Ic.Screen };
    private readonly UiDropdown _monitorDrop = new();
    private readonly UiButton _refresh = new() { Kind = ButtonKind.Ghost, Glyph = Ic.Refresh };
    private readonly UiCard _lastCard = new();

    private readonly UiCard _inspector = new();
    private readonly ScrollHost _scroll = new();

    private Rectangle _screen;
    private IntPtr _window;
    private string _windowTitle = "";
    private DateTime _lastFrame = DateTime.MinValue;
    private Rectangle _savedRegion;
    private int _navIndex;
    private bool _loading = true;
    private bool _allowExit;
    private Rectangle _warnHit = Rectangle.Empty;
    private Rectangle _headerRect, _titleRect;

    public MainWindow(IAppHost host)
    {
        _host = host;
        Theme.SetScale(Theme.ForcedScale ?? DeviceDpi / 96f);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        Text = AppInfo.DisplayName;
        BackColor = Theme.Bg;
        ForeColor = Theme.Tx;
        Font = Theme.Body;
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        ClientSize = new Size(Theme.D(1280), Theme.D(800));
        MinimumSize = new Size(Theme.D(1060), Theme.D(680));
        try { Icon = IconFactory.Window(); } catch (Exception ex) { ErrorLog.Write(ex, "icon"); }

        BuildChrome();
        BuildSidebar();
        BuildCenter();
        BuildInspector();
        _tour.Preparing += (_, index) =>
        {
            if ((uint)index < (uint)_cues.Length) ShowNav(_cues[index].Page);
            Arrange();
        };
        _tour.Closed += (_, _) => EndTour();
        Controls.Add(_tour);

        _tick.Tick += (_, _) => { Grab(); RefreshStatus(); };
        _host.StateChanged += OnState;
        LoadFromHost();
        ShowNav(_host.Current.SonSayfa);
        _loading = false;
        SyncAutoStart();
        Shown += (_, _) =>
        {
            if (_host.Current.PencereBuyuk) WindowState = FormWindowState.Maximized;
            _windowStateSeen = WindowState;
            _tick.Start();
            Grab();
            RefreshAll();
            if (_host.Current.AcilistaGuncellemeDenetle) _ = CheckUpdatesAsync(false);
            var areasShot = Environment.GetEnvironmentVariable("AREAS_SHOTS");
            if (!string.IsNullOrEmpty(areasShot))
            {
                try { CaptureAreas(areasShot); Environment.Exit(0); }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(areasShot, "error.txt"), ex.ToString()); } catch (Exception) { }
                    Environment.Exit(1);
                }
                return;
            }
            var sozluk = Environment.GetEnvironmentVariable("SOZLUK_SHOTS");
            if (!string.IsNullOrEmpty(sozluk))
            {
                try { CaptureSozluk(sozluk); Environment.Exit(0); }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(sozluk, "error.txt"), ex.ToString()); } catch (Exception) { }
                    Environment.Exit(1);
                }
                return;
            }
            var shots = Environment.GetEnvironmentVariable("CEVIRMEN_SHOTS");
            if (!string.IsNullOrEmpty(shots))
            {
                try { CaptureShots(shots); Environment.Exit(0); }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(shots, "error.txt"), ex.ToString()); } catch (Exception) { }
                    Environment.Exit(1);
                }
                return;
            }
            if (!_host.Current.TanitimGoruldu) StartTour();
        };
    }

    public void AllowExit() => _allowExit = true;

    // ---------------- window chrome ----------------

    private void BuildChrome()
    {
        foreach (var b in new[] { _help, _min, _max, _close }) Controls.Add(b);
        _help.Click += (_, _) => StartTour();
        _min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _max.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        _close.Click += (_, _) => Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); }
        catch (Exception ex) { ErrorLog.Write(ex, "affinity-main"); }
        Dwm.RoundCorners(Handle, small: false);
        // Recalculate the frame now that WM_NCCALCSIZE removes the standard caption; the window is all client now.
        NativeUi.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, NativeUi.SWP_FRAMECHANGED | Native.SWP_NOMOVE | Native.SWP_NOSIZE | NativeUi.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        var work = Screen.FromHandle(Handle).WorkingArea;
        MinimumSize = new Size(Math.Min(Theme.D(1060), work.Width), Math.Min(Theme.D(680), work.Height));
        Size = new Size(Math.Min(Theme.D(1280), work.Width - Theme.D(16)), Math.Min(Theme.D(800), work.Height - Theme.D(16)));
        // Last size and position from ayarlar.json, if that spot is still on a connected monitor.
        var s = _host.Current;
        var saved = new Rectangle(s.PencereX, s.PencereY, s.PencereGenislik, s.PencereYukseklik);
        if (saved.Width > 0 && Screen.AllScreens.Any(sc => Rectangle.Intersect(sc.WorkingArea, saved) is { Width: > 200, Height: > 120 }))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = saved;
        }
    }

    private FormWindowState _windowStateSeen = FormWindowState.Normal;

    /// <summary>Writes the window size, position, and maximized state to ayarlar.json.</summary>
    private void SaveWindow()
    {
        if (_loading || !IsHandleCreated || WindowState == FormWindowState.Minimized) return;
        var b = WindowState == FormWindowState.Maximized ? RestoreBounds : Bounds;
        bool max = WindowState == FormWindowState.Maximized;
        var s = _host.Current;
        if (s.PencereX == b.X && s.PencereY == b.Y && s.PencereGenislik == b.Width && s.PencereYukseklik == b.Height && s.PencereBuyuk == max) return;
        Patch(x => { x.PencereX = b.X; x.PencereY = b.Y; x.PencereGenislik = b.Width; x.PencereYukseklik = b.Height; x.PencereBuyuk = max; });
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        SaveWindow();
    }

    /// <summary>ayarlar.json decides; the Run key follows it (and picks up a moved exe).</summary>
    private void SyncAutoStart()
    {
        bool want = _host.Current.WindowsIleBaslat;
        if (want || AutoStart.Enabled) AutoStart.Set(want);
        _startupToggle.SetSilently(AutoStart.Enabled);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Arrange();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveWindow();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084;
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // Client area = whole window. When maximized, Windows places the frame off-screen; trim it back.
            if (NativeUi.IsZoomed(m.HWnd))
            {
                var p = Marshal.PtrToStructure<NativeUi.NCCALCSIZE_PARAMS>(m.LParam);
                int dpi = DeviceDpi;
                int fx = NativeUi.Frame(dpi, horizontal: true), fy = NativeUi.Frame(dpi, horizontal: false);
                p.Rect0.Left += fx;
                p.Rect0.Top += fy;
                p.Rect0.Right -= fx;
                p.Rect0.Bottom -= fy;
                Marshal.StructureToPtr(p, m.LParam, false);
            }
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == WM_NCHITTEST)
        {
            int lp = unchecked((int)(long)m.LParam);
            var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            m.Result = (IntPtr)HitTest(pt);
            return;
        }
        base.WndProc(ref m);
    }

    private int HitTest(Point p)
    {
        const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        if (WindowState != FormWindowState.Maximized)
        {
            int g = Theme.D(6);
            bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g, b = p.Y >= ClientSize.Height - g;
            if (t && l) return HTTOPLEFT;
            if (t && r) return HTTOPRIGHT;
            if (b && l) return HTBOTTOMLEFT;
            if (b && r) return HTBOTTOMRIGHT;
            if (l) return HTLEFT;
            if (r) return HTRIGHT;
            if (t) return HTTOP;
            if (b) return HTBOTTOM;
        }
        return p.Y < Theme.D(40) ? HTCAPTION : HTCLIENT;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Theme.SetScale(e.DeviceDpiNew / 96f);
        Font = Theme.Body;
        MinimumSize = new Size(Theme.D(1060), Theme.D(680));
        Bounds = e.SuggestedRectangle;
        ApplyFonts();
        Arrange();
        Invalidate(true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _max.Glyph = WindowState == FormWindowState.Maximized ? Ic.Restore : Ic.Max;
        _max.Invalidate();
        if (!_loading) Arrange();
        if (WindowState != _windowStateSeen)
        {
            _windowStateSeen = WindowState;
            SaveWindow();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode is >= Keys.D1 and <= Keys.D4) { ShowNav(e.KeyCode - Keys.D1); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // ---------------- layout ----------------

    private void Arrange()
    {
        if (ClientSize.Width < 50 || ClientSize.Height < 50) return;
        int W = ClientSize.Width, H = ClientSize.Height;
        int cap = Theme.D(46), top = Theme.D(40);
        _close.SetBounds(W - cap, 0, cap, top);
        _max.SetBounds(W - 2 * cap, 0, cap, top);
        _min.SetBounds(W - 3 * cap, 0, cap, top);
        _help.SetBounds(W - 4 * cap, 0, cap, top);
        _titleRect = new Rectangle(0, 0, W - 4 * cap, top);
        _tour.Bounds = ClientRectangle;
        if (_tour.Running) _tour.BringToFront();

        // Sidebar
        int nx = Theme.D(12), nw = Theme.D(188) - Theme.D(22);
        int ny = top + Theme.D(4) + Theme.D(4) + Theme.D(38) + Theme.D(16);
        foreach (var n in _nav)
        {
            n.SetBounds(nx, ny, nw, Theme.D(40));
            ny += Theme.D(42);
        }
        int statusH = StatusHeight();
        var status = new Rectangle(nx, H - Theme.D(14) - statusH, nw, statusH);
        _statusCard.Place(status);
        int pad = Theme.D(14);
        _run.SetBounds(UiCard.Pad + pad, UiCard.Pad + statusH - pad - Theme.D(44), nw - 2 * pad, Theme.D(44));

        // Inspector
        int iw = Theme.D(308) - Theme.D(14);
        var insp = new Rectangle(W - Theme.D(14) - iw, top + Theme.D(4), iw, H - top - Theme.D(4) - Theme.D(16));
        _inspector.Place(insp);
        int ip = Theme.D(18);
        _scroll.SetBounds(UiCard.Pad + ip, UiCard.Pad + ip, insp.Width - ip - Theme.D(8), insp.Height - 2 * ip);

        // Center column
        int cx = Theme.D(188) + Theme.D(6);
        int cr = insp.X - Theme.D(16);
        int cw = cr - cx;
        int y = top + Theme.D(4);
        int headerH = Theme.D(52);
        _headerRect = new Rectangle(cx + Theme.D(4), y, cw - Theme.D(8), headerH);
        int chipH = Theme.D(30) + 2 * Theme.D(3);
        int chipX = cr - Theme.D(4) + Theme.D(3);
        foreach (var chip in new[] { _chipTrans, _chipOrig })
        {
            int w = chip.PreferredWidth;
            chipX -= w;
            chip.SetBounds(chipX, y + (headerH - chipH) / 2, w, chipH);
            chipX -= Theme.D(8) - 2 * Theme.D(3);
        }
        y += headerH + Theme.D(12);

        int sourceH = Theme.D(54);
        int lastMin = Theme.D(76);
        int gap = Theme.D(12);
        int bottom = H - Theme.D(16);
        int previewH = (cw - Theme.D(16)) * 9 / 16 + Theme.D(16);
        previewH = Math.Min(previewH, bottom - y - sourceH - lastMin - 2 * gap);
        previewH = Math.Max(previewH, Theme.D(160));
        _previewCard.Place(new Rectangle(cx, y, cw, previewH));
        _stage.SetBounds(UiCard.Pad + Theme.D(8), UiCard.Pad + Theme.D(8), cw - Theme.D(16), previewH - Theme.D(16));
        y += previewH + gap;

        _sourceCard.Place(new Rectangle(cx, y, cw, sourceH));
        ArrangeSource(cw, sourceH);
        y += sourceH + gap;
        _lastCard.Place(new Rectangle(cx, y, cw, Math.Max(lastMin, bottom - y)));

        _scroll.Relayout();
        Invalidate();
    }

    private int StatusHeight() => Theme.D(14 + 20 + 12 + 18 + 12 + 44 + 14) + (StatusWarning() != null ? Theme.D(44) : 0);

    private void ApplyFonts()
    {
        foreach (var input in AllInputs()) input.ApplyFont();
        _run.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        Theme.HighQuality(g);
        // Title bar
        int top = Theme.D(40);
        Mascot.Draw(g, new RectangleF(Theme.D(14), (top - Theme.D(20)) / 2f, Theme.D(20), Theme.D(20)));
        Theme.Text(g, Text, Theme.Title, new Rectangle(Theme.D(44), 0, Math.Max(10, _titleRect.Width - Theme.D(44)), top), Theme.Tx2);
        // Brand
        int bx = Theme.D(18), by = top + Theme.D(8);
        Mascot.Draw(g, new RectangleF(bx, by, Theme.D(38), Theme.D(38)));
        int tx = bx + Theme.D(48);
        Theme.Text(g, AppInfo.DisplayName, Theme.Brand, new Rectangle(tx, by + Theme.D(2), Theme.D(188) - tx, Theme.D(20)), Theme.Tx);
        Theme.Text(g, OcrDilPaketi.DilAdi(_host.Current.KaynakDil) + " → Türkçe", Theme.Small, new Rectangle(tx, by + Theme.D(21), Theme.D(188) - tx, Theme.D(16)), Theme.Tx2);
        // Center header
        var h = _headerRect;
        int chipsLeft = Math.Min(_chipOrig.Left, _chipTrans.Left);
        int textW = Math.Max(10, chipsLeft - h.X - Theme.D(12));
        Theme.Text(g, "Canlı önizleme", Theme.H1, new Rectangle(h.X, h.Y + Theme.D(2), textW, Theme.D(28)), Theme.Tx);
        Theme.Text(g, "Kutuları sürükleyip yerleştir, yerini bulunca kilitle.", Theme.Body, new Rectangle(h.X, h.Y + Theme.D(31), textW, Theme.D(19)), Theme.Tx2);
    }

    // ---------------- sidebar ----------------

    private void BuildSidebar()
    {
        for (int i = 0; i < _nav.Length; i++)
        {
            int index = i;
            _nav[i].Click += (_, _) => ShowNav(index);
            Controls.Add(_nav[i]);
        }
        _statusCard.Controls.Add(_run);
        _statusCard.PaintBody += PaintStatus;
        _statusCard.MouseClick += (_, e) => { if (_warnHit.Contains(e.Location)) ShowNav(3); };
        _statusCard.MouseMove += (_, e) => _statusCard.Cursor = _warnHit.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        _run.Click += (_, _) => ToggleRun();
        Controls.Add(_statusCard);
    }

    private void ShowNav(int index)
    {
        index = Math.Clamp(index, 0, _nav.Length - 1);
        _navIndex = index;
        for (int i = 0; i < _nav.Length; i++) _nav[i].Active = i == index;
        var page = index switch { 1 => _lookPage, 2 => _glossaryPage, 3 => _advancedPage, _ => _areasPage };
        if (index == 1) SelectBox(PreviewStage.Trans);
        if (index == 2) { HazirKutuphane.Yukle(); FillGlossary(); }
        _scroll.Show(page);
        _scroll.Relayout();
        if (_host.Current.SonSayfa != index) Patch(s => s.SonSayfa = index);
    }

    private string? StatusWarning()
    {
        if (_host.IsRunning) return null;
        if (_host.OcrPaketiEksik) return "OCR paketi eksik";
        if (_offer != null) return "Yeni sürüm: " + _offer.Tag;
        return null;
    }

    private void PaintStatus(Graphics g, Rectangle b)
    {
        int pad = Theme.D(14);
        int x = b.X + pad, w = b.Width - 2 * pad, y = b.Y + pad;
        bool running = _host.IsRunning, paused = _host.IsPaused;
        Color dot = running ? (paused ? Theme.KeyOrig : Theme.OkDot) : _host.OcrPaketiEksik ? Theme.Warn : Theme.Neutral;
        float d = Theme.D(8);
        float cy = y + Theme.D(10);
        if (running && !paused)
        {
            using var halo = new SolidBrush(Color.FromArgb(46, 22, 163, 74));
            float hd = Theme.D(16);
            g.FillEllipse(halo, x + d / 2 - hd / 2, cy - hd / 2, hd, hd);
        }
        using (var bd = new SolidBrush(dot)) g.FillEllipse(bd, x, cy - d / 2, d, d);
        string status = running ? (paused ? "Duraklatıldı" : "Çalışıyor") : "Hazır";
        Theme.Text(g, status, Theme.BodySemi, new Rectangle(x + Theme.D(17), y, w - Theme.D(17), Theme.D(20)), Theme.Tx);
        y += Theme.D(32);
        Theme.Text(g, "Gecikme", Theme.Small, new Rectangle(x, y, w, Theme.D(18)), Theme.Tx2);
        Theme.Text(g, Latency(), Theme.SmallSemi, new Rectangle(x, y, w, Theme.D(18)), Theme.Tx, Theme.Right);
        y += Theme.D(30);
        var warn = StatusWarning();
        _warnHit = Rectangle.Empty;
        if (warn != null)
        {
            var r = new Rectangle(x, y, w, Theme.D(34));
            bool update = !_host.OcrPaketiEksik;
            Theme.FillRound(g, r, Theme.D(8), update ? Theme.AccT : Theme.WarnT);
            Theme.Glyph(g, update ? Ic.Download : Ic.Warning, Theme.Icon12, new Rectangle(r.X + Theme.D(8), r.Y, Theme.D(16), r.Height), update ? Theme.AccD : Theme.Warn);
            Theme.Text(g, warn, Theme.Caption, new Rectangle(r.X + Theme.D(30), r.Y, r.Width - Theme.D(36), r.Height), update ? Theme.AccInk : Theme.Warn);
            _warnHit = r;
        }
    }

    private string Latency() => _host.SonGecikmeMs > 0 && _host.IsRunning
        ? _host.SonGecikmeMs.ToString(CultureInfo.InvariantCulture) + " ms" : "—";

    private void ToggleRun()
    {
        if (_host.IsRunning) { _host.StopCapture(); return; }
        if (_host.OcrPaketiEksik) { ShowNav(3); return; }
        if (!_host.Current.HasRegion) SaveRegions(showOverlay: false);
        _host.StartCapture();
    }

    // ---------------- center ----------------

    private void BuildCenter()
    {
        _chipOrig.SelectClicked += (_, _) => SelectBox(PreviewStage.Orig);
        _chipTrans.SelectClicked += (_, _) => SelectBox(PreviewStage.Trans);
        _chipOrig.LockClicked += (_, _) => ToggleLock(PreviewStage.Orig);
        _chipTrans.LockClicked += (_, _) => ToggleLock(PreviewStage.Trans);
        Controls.Add(_chipOrig);
        Controls.Add(_chipTrans);

        _previewCard.Controls.Add(_stage);
        _stage.SelectionChanged += (_, _) => SyncSelection();
        _stage.BoxesChanged += (_, _) => RefreshGeometryFields();
        _stage.BoxesCommitted += (_, _) => SaveRegions(showOverlay: true);
        _stage.AreaChanged += (_, _) => { SaveRegions(showOverlay: false); RefreshAreaEditor(); SyncSelection(); };
        Controls.Add(_previewCard);

        _sourceCard.Controls.Add(_sourceDrop);
        _sourceCard.Controls.Add(_monitorDrop);
        _sourceCard.Controls.Add(_refresh);
        _sourceCard.PaintBody += PaintSource;
        _sourceDrop.Opening += (_, _) => FillSources();
        _sourceDrop.SelectedChanged += (_, _) => OnSourcePicked();
        _monitorDrop.Opening += (_, _) => FillMonitors();
        _monitorDrop.SelectedChanged += (_, _) => OnMonitorPicked();
        _refresh.Click += (_, _) => { FillSources(); FillMonitors(); Grab(); };
        Controls.Add(_sourceCard);

        _lastCard.PaintBody += PaintLast;
        Controls.Add(_lastCard);
    }

    private int _labelSource, _labelMonitor;

    private void ArrangeSource(int cw, int h)
    {
        int pad = Theme.D(14), x = UiCard.Pad + pad, cy = UiCard.Pad + h / 2;
        int dh = Theme.D(34);
        int refresh = Theme.D(34);
        int badgeW = Badge.Measure("Görüntü alınıyor", Ic.Check).Width;
        int avail = cw - 2 * pad - refresh - badgeW - Theme.D(16) - Theme.D(10);
        _labelSource = Theme.Measure("Kaynak", Theme.SmallSemi).Width;
        _labelMonitor = Theme.Measure("Monitör", Theme.SmallSemi).Width;
        int labels = _labelSource + _labelMonitor + Theme.D(8) * 2 + Theme.D(16);
        int drops = Math.Max(Theme.D(200), avail - labels);
        int mw = Math.Min(Theme.D(200), drops * 45 / 100), sw = Math.Min(Theme.D(250), drops - mw);
        x += _labelSource + Theme.D(8);
        _sourceDrop.SetBounds(x, cy - dh / 2, sw, dh);
        x += sw + Theme.D(16) + _labelMonitor + Theme.D(8);
        _monitorDrop.SetBounds(x, cy - dh / 2, mw, dh);
        _refresh.SetBounds(UiCard.Pad + cw - pad - refresh, cy - refresh / 2, refresh, refresh);
    }

    private void PaintSource(Graphics g, Rectangle b)
    {
        int pad = Theme.D(14);
        Theme.Text(g, "Kaynak", Theme.SmallSemi, new Rectangle(b.X + pad, b.Y, _labelSource + 2, b.Height), Theme.Tx2);
        int mx = _sourceDrop.Right + Theme.D(16);
        Theme.Text(g, "Monitör", Theme.SmallSemi, new Rectangle(mx, b.Y, _labelMonitor + 2, b.Height), Theme.Tx2);
        bool ok = _stage.HasFrame && DateTime.UtcNow - _lastFrame < TimeSpan.FromSeconds(3);
        string text = ok ? "Görüntü alınıyor" : "Görüntü yok";
        var size = Badge.Measure(text, ok ? Ic.Check : Ic.Warning);
        var r = new Rectangle(_refresh.Left - Theme.D(10) - size.Width, b.Y + (b.Height - size.Height) / 2, size.Width, size.Height);
        if (r.X > _monitorDrop.Right + Theme.D(8)) Badge.Draw(g, r, text, ok ? BadgeKind.Ok : BadgeKind.Warn, ok ? Ic.Check : Ic.Warning);
    }

    private void PaintLast(Graphics g, Rectangle b)
    {
        int pad = Theme.D(16);
        int tile = Theme.D(36);
        int cy = b.Y + b.Height / 2;
        var t = new Rectangle(b.X + pad, cy - tile / 2, tile, tile);
        Theme.FillRound(g, t, Theme.D(10), Theme.AccT);
        Theme.Glyph(g, Ic.Bolt, Theme.Icon16, t, Theme.AccD);
        string ms = Latency();
        var bs = Badge.Measure(ms, null);
        var br = new Rectangle(b.Right - pad - bs.Width, cy - bs.Height / 2, bs.Width, bs.Height);
        Badge.Draw(g, br, ms, BadgeKind.Neutral);
        int x = t.Right + Theme.D(14);
        int w = br.X - Theme.D(12) - x;
        bool has = !string.IsNullOrWhiteSpace(_host.SonMetin);
        string line = has ? _host.SonMetin : _host.IsRunning ? "Altyazı bekleniyor…" : "Henüz çeviri yok";
        string sub = _host.IsRunning
            ? (_host.IsPaused ? "Duraklatıldı · devam için F9" : "Ekrandaki altyazı okunuyor")
            : "Başlat'a bastığında son çeviri burada görünür";
        Theme.Text(g, "Son çeviri", Theme.Small, new Rectangle(x, cy - Theme.D(30), w, Theme.D(16)), Theme.Tx2);
        Theme.Text(g, line, Theme.Big, new Rectangle(x, cy - Theme.D(13), w, Theme.D(24)), has ? Theme.Tx : Theme.Tx3);
        Theme.Text(g, sub, Theme.Small, new Rectangle(x, cy + Theme.D(12), w, Theme.D(16)), Theme.Tx2);
    }

    // ---------------- sources ----------------

    private void FillMonitors()
    {
        var items = new List<(string, object?)>();
        int selected = 0, n = 1;
        foreach (var s in Screen.AllScreens)
        {
            items.Add(("Monitör " + n.ToString(CultureInfo.InvariantCulture) + " · " + s.Bounds.Width.ToString(CultureInfo.InvariantCulture) + "×" + s.Bounds.Height.ToString(CultureInfo.InvariantCulture), s.DeviceName));
            if (string.Equals(s.DeviceName, _host.Current.MonitorAdi, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrEmpty(_host.Current.MonitorAdi) && s.Bounds.Contains(Center(_screen))))
                selected = n - 1;
            n++;
        }
        _monitorDrop.SetItems(items, selected);
    }

    private void FillSources()
    {
        var items = new List<(string, object?)> { ("Tüm ekran", IntPtr.Zero) };
        int selected = 0;
        foreach (var w in NativeUi.Windows())
        {
            items.Add((w.Title, w.Handle));
            if (w.Handle == _window) selected = items.Count - 1;
        }
        _sourceDrop.SetItems(items, selected);
    }

    private void OnSourcePicked()
    {
        if (_sourceDrop.SelectedTag is IntPtr h && h != IntPtr.Zero && NativeUi.TryWindowRect(h, out var rect))
        {
            _window = h;
            _windowTitle = _sourceDrop.SelectedText;
            _screen = rect;
        }
        else
        {
            _window = IntPtr.Zero;
            _windowTitle = "";
            _screen = MonitorBounds(_host.Current);
        }
        string choice = _window != IntPtr.Zero ? _windowTitle : "ekran";
        Patch(s => s.KaynakPencere = choice);
        _stage.SetSource(_screen);
        RefreshGeometryFields();
        Grab();
    }

    private void OnMonitorPicked()
    {
        if (_monitorDrop.SelectedTag is not string device) return;
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device);
        if (screen == null) return;
        Patch(s => { s.MonitorAdi = device; s.KaynakPencere = "ekran"; });
        _window = IntPtr.Zero;
        _windowTitle = "";
        _screen = screen.Bounds;
        _sourceDrop.SelectSilently(0);
        _stage.SetSource(_screen);
        // Boxes from another monitor would be off-canvas: bring them onto this one.
        for (int i = 0; i < 2; i++)
        {
            var b = _stage.Box(i);
            if (!b.IsEmpty && !_screen.IntersectsWith(b))
                _stage.SetBox(i, Fit(new Rectangle(_screen.X + b.X - Screen.FromRectangle(b).Bounds.X, _screen.Y + b.Y - Screen.FromRectangle(b).Bounds.Y, b.Width, b.Height), _screen, i));
        }
        SaveRegions(showOverlay: false);
        RefreshGeometryFields();
        Grab();
    }

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private int _sourceProbe;

    /// <summary>The window saved in ayarlar.json (exact title first, then partial); if none was chosen, Remote Play or Chiaki.</summary>
    private (IntPtr Handle, string Title, Rectangle Bounds)? FindSourceWindow()
    {
        string saved = _host.Current.KaynakPencere;
        var windows = NativeUi.Windows();
        (IntPtr Handle, string Title) pick;
        if (saved.Length == 0)
            pick = windows.FirstOrDefault(w => w.Title.Contains("PS Remote Play", StringComparison.OrdinalIgnoreCase) || w.Title.Contains("Chiaki", StringComparison.OrdinalIgnoreCase));
        else
        {
            pick = windows.FirstOrDefault(w => w.Title.Equals(saved, StringComparison.OrdinalIgnoreCase));
            if (pick.Handle == IntPtr.Zero)
                pick = windows.FirstOrDefault(w => w.Title.Contains(saved, StringComparison.OrdinalIgnoreCase) || saved.Contains(w.Title, StringComparison.OrdinalIgnoreCase));
        }
        if (pick.Handle == IntPtr.Zero || !NativeUi.TryWindowRect(pick.Handle, out var rect)) return null;
        return (pick.Handle, pick.Title, rect);
    }

    private void Grab()
    {
        if (!Visible || WindowState == FormWindowState.Minimized) return;
        // The saved source window may open after us (Remote Play started later): pick it up when it appears.
        if (_window == IntPtr.Zero && _host.Current.KaynakPencere != "ekran" && ++_sourceProbe % 5 == 0 && FindSourceWindow() is { } found)
        {
            _window = found.Handle;
            _windowTitle = found.Title;
            _screen = found.Bounds;
            FillSources();
            _stage.SetSource(_screen);
            RefreshGeometryFields();
        }
        if (_window != IntPtr.Zero)
        {
            if (NativeUi.TryWindowRect(_window, out var rect))
            {
                if (rect != _screen) { _screen = rect; _stage.SetSource(rect); RefreshGeometryFields(); }
            }
            else
            {
                _window = IntPtr.Zero;
                _windowTitle = "";
                _screen = MonitorBounds(_host.Current);
                _sourceDrop.SelectSilently(0);
                _stage.SetSource(_screen);
            }
        }
        if (_screen.Width < 20 || _screen.Height < 20) return;
        try
        {
            int target = Math.Clamp(_stage.Width, 320, 1600);
            int w = Math.Min(target, _screen.Width);
            int h = Math.Max(1, _screen.Height * w / _screen.Width);
            using var full = new Bitmap(_screen.Width, _screen.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(full))
                g.CopyFromScreen(_screen.X, _screen.Y, 0, 0, _screen.Size);
            var small = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.DrawImage(full, new Rectangle(0, 0, w, h));
            }
            _stage.SetFrame(small, _screen);
            _lastFrame = DateTime.UtcNow;
        }
        catch (Exception)
        {
            /* display changing, secure desktop, or lock screen */
        }
    }

    private static Rectangle MonitorBounds(AppSettings s)
    {
        foreach (var screen in Screen.AllScreens)
            if (!string.IsNullOrEmpty(s.MonitorAdi) && screen.DeviceName.Equals(s.MonitorAdi, StringComparison.OrdinalIgnoreCase))
                return screen.Bounds;
        if (s.HasRegion) return Screen.FromRectangle(new Rectangle(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight)).Bounds;
        return Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
    }

    // ---------------- boxes ----------------

    private void LoadFromHost()
    {
        var s = _host.Current;
        _screen = MonitorBounds(s);
        if (s.KaynakPencere != "ekran" && FindSourceWindow() is { } found)
        {
            _window = found.Handle;
            _windowTitle = found.Title;
            _screen = found.Bounds;
        }
        FillSources();
        FillMonitors();
        _stage.SetSource(_screen);
        LoadBoxes();
        _stage.SetLook(s, OrnekAltyazi);
        SyncSelection();
    }

    private void LoadBoxes()
    {
        var s = _host.Current;
        s.EnsureAlanlar();
        var src = _screen;
        var views = new List<PreviewArea>();
        for (int i = 0; i < s.Alanlar!.Count; i++)
        {
            var a = s.Alanlar[i];
            var orig = a.RegionWidth >= 20
                ? new Rectangle(a.RegionX, a.RegionY, a.RegionWidth, a.RegionHeight)
                : new Rectangle(src.X + src.Width * 18 / 100, src.Y + src.Height * (72 - i * 16) / 100, src.Width * 58 / 100, Math.Max(40, src.Height * 9 / 100));
            var temp = new AppSettings();
            a.MirrorTo(temp);
            var trans = a.KonumAyarlandi || a.KutuGenislik >= SubtitleLayout.MinWidth ? CaptionRect(temp, orig) : Rectangle.Empty;
            if (trans.Width < 20)
            {
                int w = Math.Max(SubtitleLayout.MinWidth, src.Width * 50 / 100);
                trans = new Rectangle(src.X + (src.Width - w) / 2, orig.Y - Math.Max(72, src.Height * 12 / 100), w, Math.Max(48, src.Height * 8 / 100));
            }
            views.Add(new PreviewArea
            {
                Id = a.Id, Name = a.Ad, ColorHex = a.Renk, Enabled = a.Acik,
                Capture = orig, Overlay = trans,
            });
        }
        _stage.SetAreas(views, s.SeciliAlan);
        _savedRegion = s.HasRegion ? new Rectangle(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight) : Rectangle.Empty;
        RefreshGeometryFields();
        RefreshAreaEditor();
    }

    private static Rectangle CaptionRect(AppSettings s, Rectangle ocr)
    {
        int h = s.KutuYukseklik > 0 ? s.KutuYukseklik : 72;
        if (s.KonumAyarlandi && s.KutuGenislik >= SubtitleLayout.MinWidth)
        {
            if (s.BolgeyiTakipEt && ocr.Width > 0)
                return new Rectangle(ocr.X + s.BagilX, ocr.Y + s.BagilY, s.KutuGenislik, h);
            return new Rectangle(s.KutuX, s.KutuY, s.KutuGenislik, h);
        }
        if (ocr.Width < 20) return Rectangle.Empty;
        var screen = Screen.FromRectangle(ocr).Bounds;
        var placed = SubtitleLayout.Resolve(s, new EkranDikdortgen(ocr.X, ocr.Y, ocr.Width, ocr.Height),
            new EkranDikdortgen(screen.X, screen.Y, screen.Width, screen.Height), Math.Max(ocr.Width, 400), h);
        return new Rectangle(placed.X, placed.Y, placed.Width, placed.Height);
    }

    private static Rectangle Fit(Rectangle r, Rectangle src, int box)
    {
        int w = Math.Clamp(r.Width, Math.Min(PreviewStage.MinWidth(box), src.Width), src.Width);
        int h = Math.Clamp(r.Height, Math.Min(PreviewStage.MinHeight(box), src.Height), src.Height);
        int x = Math.Clamp(r.X, src.X, src.Right - w);
        int y = Math.Clamp(r.Y, src.Y, src.Bottom - h);
        return new Rectangle(x, y, w, h);
    }

    private void SaveRegions(bool showOverlay)
    {
        if (_loading) return;
        var s = _host.Current.Clone();
        s.EnsureAlanlar();
        int n = Math.Min(_stage.Areas.Count, s.Alanlar!.Count);
        for (int i = 0; i < n; i++)
        {
            var view = _stage.Areas[i];
            var a = s.Alanlar[i];
            a.RegionX = view.Capture.X;
            a.RegionY = view.Capture.Y;
            a.RegionWidth = view.Capture.Width;
            a.RegionHeight = view.Capture.Height;
            a.Acik = view.Enabled;
            a.Ad = view.Name;
            a.Renk = view.ColorHex;
            if (view.Overlay.Width >= SubtitleLayout.MinWidth)
            {
                a.KonumAyarlandi = true;
                a.KutuX = view.Overlay.X;
                a.KutuY = view.Overlay.Y;
                a.KutuGenislik = view.Overlay.Width;
                a.KutuYukseklik = view.Overlay.Height;
                a.BolgeyiTakipEt = false;
            }
        }
        s.SeciliAlan = _stage.AreaIndex;
        s.Alanlar[Math.Clamp(s.SeciliAlan, 0, s.Alanlar.Count - 1)].MirrorTo(s);
        var ocr = _stage.Box(PreviewStage.Orig);
        _savedRegion = ocr;
        _host.Apply(s, true);
        if (showOverlay && s.HasRegion && !_host.IsRunning) _host.ShowPreview();
    }

    private void RefreshAreaEditor()
    {
        var s = _host.Current;
        s.EnsureAlanlar();
        _areaList.Set(s.Alanlar!.Select(a => new AreaList.Row(a.Ad, a.Renk, a.Acik)).ToList(), _stage.AreaIndex);
        var cur = s.Alanlar![Math.Clamp(_stage.AreaIndex, 0, s.Alanlar.Count - 1)];
        _areaName.SetText(cur.Ad);
        _areaOn.SetSilently(cur.Acik);
        _areaRemove.Enabled = s.Alanlar.Count > 1;
        _areaAdd.Enabled = s.Alanlar.Count < YakalamaAlani.EnFazla;
        if (_scroll.Page == _areasPage) _scroll.Relayout();
    }

    private void SelectCaptureArea(int index)
    {
        if (_stage.Areas.Count == 0) return;
        index = Math.Clamp(index, 0, _stage.Areas.Count - 1);
        var copy = _stage.Areas.ToList();
        _stage.SetAreas(copy, index);
        Patch(s => s.SeciliAlan = index);
        SyncSelection();
        RefreshAreaEditor();
    }

    private void SetAreaEnabled(int index, bool on)
    {
        if (index < 0 || index >= _stage.Areas.Count) return;
        _stage.Areas[index].Enabled = on;
        SaveRegions(showOverlay: false);
        RefreshAreaEditor();
    }

    private void RenameArea(string name)
    {
        if (_stage.Areas.Count == 0) return;
        var area = _stage.Areas[_stage.AreaIndex];
        area.Name = string.IsNullOrWhiteSpace(name) ? area.Name : name.Trim();
        SaveRegions(showOverlay: false);
        RefreshAreaEditor();
    }

    private void AddCaptureArea()
    {
        var s = _host.Current.Clone();
        s.EnsureAlanlar();
        if (s.Alanlar!.Count >= YakalamaAlani.EnFazla) return;
        int i = s.Alanlar.Count;
        var src = _screen;
        var area = new YakalamaAlani
        {
            Id = "alan" + (i + 1).ToString(Inv),
            Ad = YakalamaAlani.Adlar[i % YakalamaAlani.Adlar.Length],
            Renk = YakalamaAlani.Palet[i % YakalamaAlani.Palet.Length],
            Acik = true,
            RegionX = src.X + src.Width * 22 / 100,
            RegionY = src.Y + src.Height * (40 - i * 8) / 100,
            RegionWidth = src.Width * 50 / 100,
            RegionHeight = Math.Max(36, src.Height * 8 / 100),
        };
        s.Alanlar.Add(area);
        s.SeciliAlan = s.Alanlar.Count - 1;
        _host.Apply(s, true);
        LoadBoxes();
    }

    private void CycleAreaColor()
    {
        if (_stage.Areas.Count == 0) return;
        var area = _stage.Areas[_stage.AreaIndex];
        int i = Array.FindIndex(YakalamaAlani.Palet, c => c.Equals(area.ColorHex, StringComparison.OrdinalIgnoreCase));
        area.ColorHex = YakalamaAlani.Palet[(i + 1) % YakalamaAlani.Palet.Length];
        _stage.Invalidate();
        SaveRegions(showOverlay: false);
        RefreshAreaEditor();
    }

    private void RemoveCaptureArea()
    {
        var s = _host.Current.Clone();
        s.EnsureAlanlar();
        if (s.Alanlar!.Count <= 1) return;
        int i = Math.Clamp(_stage.AreaIndex, 0, s.Alanlar.Count - 1);
        s.Alanlar.RemoveAt(i);
        s.SeciliAlan = Math.Clamp(i, 0, s.Alanlar.Count - 1);
        _host.Apply(s, true);
        LoadBoxes();
    }

    private void SelectBox(int i)
    {
        _stage.Select(i);
        SyncSelection();
    }

    private void ToggleLock(int i) => SetLock(i, !_stage.IsLocked(i));

    private void SetLock(int i, bool locked)
    {
        _stage.SetLocked(i, locked);
        SyncSelection();
        if (i == PreviewStage.Orig) Patch(s => s.OrijinalKilitli = locked);
        else Patch(s => s.CeviriKilitli = locked);
    }

    private void SyncSelection()
    {
        int sel = _stage.Selected;
        _chipOrig.Active = sel == PreviewStage.Orig;
        _chipTrans.Active = sel == PreviewStage.Trans;
        _chipOrig.Locked = _stage.IsLocked(PreviewStage.Orig);
        _chipTrans.Locked = _stage.IsLocked(PreviewStage.Trans);
        _boxSeg.SetSilently(sel);
        _lockToggle.SetSilently(_stage.IsLocked(sel));
        bool locked = _stage.IsLocked(sel);
        foreach (var input in GeometryInputs()) input.Enabled = !locked;
        _resetBox.Enabled = !locked;
        RefreshGeometryFields();
    }

    // ---------------- host state ----------------

    private void OnState(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(new Action(RefreshAll)); }
        catch (InvalidOperationException) { /* closing */ }
    }

    private void RefreshAll()
    {
        if (IsDisposed) return;
        var s = _host.Current;
        // Region picked with F8 (full-screen selector): adopt it.
        var region = s.HasRegion ? new Rectangle(s.RegionX, s.RegionY, s.RegionWidth, s.RegionHeight) : Rectangle.Empty;
        if (region != _savedRegion && !Capture && !_stage.Capture)
        {
            _savedRegion = region;
            if (!region.IsEmpty)
            {
                if (!_screen.IntersectsWith(region) && _window == IntPtr.Zero)
                {
                    _screen = Screen.FromRectangle(region).Bounds;
                    _stage.SetSource(_screen);
                }
                _stage.SetBox(PreviewStage.Orig, region);
                var cap = CaptionRect(s, region);
                if (cap.Width >= 20) _stage.SetBox(PreviewStage.Trans, cap);
                RefreshGeometryFields();
            }
        }
        RefreshStatus();
        RefreshAdvanced();
        Invalidate(new Rectangle(0, 0, Theme.D(188), Theme.D(110)));
    }

    private int _statusHeight;

    private void RefreshStatus()
    {
        bool run = _host.IsRunning;
        _run.Text = run ? "Durdur" : "Başlat";
        _run.Glyph = run ? Ic.Stop : Ic.Play;
        _run.Kind = run ? ButtonKind.Danger : ButtonKind.Primary;
        _nav[3].Dot = _offer != null || _host.OcrPaketiEksik;
        string sample = run && !string.IsNullOrWhiteSpace(_host.SonMetin) ? _host.SonMetin : OrnekAltyazi;
        _stage.SetLook(_host.Current, sample);
        int sh = StatusHeight();
        if (sh != _statusHeight) { _statusHeight = sh; Arrange(); }
        _statusCard.Invalidate();
        _lastCard.Invalidate();
        _sourceCard.Invalidate();
    }

    private TourCue[] _cues = Array.Empty<TourCue>();

    private void StartTour()
    {
        _pageBeforeTour = _navIndex;
        _cues = BuildCues();
        _tour.Play(_cues);
    }

    private void EndTour()
    {
        if (!_host.Current.TanitimGoruldu) Patch(s => s.TanitimGoruldu = true);
        ShowNav(_pageBeforeTour);
    }

    private TourCue[] BuildCues()
    {
        ShowNav(0);
        Arrange();
        Rectangle UnionNav()
        {
            var r = _nav[0].Bounds;
            foreach (var n in _nav) r = Rectangle.Union(r, n.Bounds);
            return r;
        }
        Rectangle OnForm(Control c, Rectangle inner)
        {
            var p = PointToClient(c.PointToScreen(inner.Location));
            return new Rectangle(p, inner.Size);
        }
        var list = new List<TourCue>
        {
            new(UnionNav(), "Sol menü", "Önizleme, Görünüm, Sözlük ve Gelişmiş burada. Her biri sağdaki paneli değiştirir.", 0),
            new(_previewCard.Bounds, "Canlı önizleme", "Seçtiğin ekran burada görünür. Altyazı kutularını bu görüntünün üstüne koyarsın.", 0),
            new(OnForm(_stage, _stage.BoxClient(PreviewStage.Orig)), "Orijinal altyazı", "Bu kutu oyunun İngilizce altyazısının okunduğu alandır. Sürükleyerek taşı, tutamaçlardan boyutlandır.", 0),
            new(OnForm(_stage, _stage.BoxClient(PreviewStage.Trans)), "Çeviri kutusu", "Türkçe yazı bu kutunun yerinde çıkar. Onu da sürükleyip boyutlandırabilirsin.", 0),
            new(_sourceCard.Bounds, "Kaynak ve monitör", "Kaynak hangi pencereyi, Monitör hangi ekranı okuyacağını seçer.", 0),
            new(_inspector.Bounds, "Alanlar", "Seçili kutunun konumu burada. Birden fazla alan ekleyebilirsin: konuşma ve görev ayrı kutularda durur.", 0),
            new(_statusCard.Bounds, "Başlat ve durum", "Başlat okumayı açar. Alttaki yazı hazır mı, çalışıyor mu onu söyler.", 0),
            new(_lastCard.Bounds, "Son çeviri", "En son okunan satır ve Türkçesi bu kartta görünür.", 0),
            new(_inspector.Bounds, "Görünüm", "Yazı boyutu, renk ve arka plan burada. Örnek cümle önizlemede hemen değişir.", 1),
            new(_inspector.Bounds, "Sözlük", "Oyun adlarını ve terimleri buradan korursun. Paketleri açıp kapatabilirsin.", 2),
            new(_inspector.Bounds, "Gelişmiş", "Güncelleme, OCR paketi ve bu tanıtımın tekrarı buradadır.", 3),
        };
        ShowNav(0);
        return list.ToArray();
    }

    private void CaptureAreas(string dir)
    {
        Directory.CreateDirectory(dir);
        _tour.Visible = false;
        var s = _host.Current.Clone();
        s.EnsureAlanlar();
        var src = _screen.Width > 0 ? _screen : new Rectangle(0, 0, 1920, 1080);
        s.Alanlar = new List<YakalamaAlani>
        {
            new()
            {
                Id = "konusma", Ad = "Konuşma", Renk = YakalamaAlani.Palet[0], Acik = true,
                RegionX = src.X + 280, RegionY = src.Y + 760, RegionWidth = 900, RegionHeight = 90,
                KonumAyarlandi = true, KutuX = src.X + 360, KutuY = src.Y + 620, KutuGenislik = 760, KutuYukseklik = 90,
            },
            new()
            {
                Id = "gorev", Ad = "Görev", Renk = YakalamaAlani.Palet[1], Acik = true,
                RegionX = src.X + 520, RegionY = src.Y + 80, RegionWidth = 640, RegionHeight = 70,
                KonumAyarlandi = true, KutuX = src.X + 560, KutuY = src.Y + 170, KutuGenislik = 560, KutuYukseklik = 80,
            },
        };
        s.SeciliAlan = 0;
        _host.Apply(s, false);
        LoadBoxes();
        foreach (var scale in new[] { 1f, 1.5f })
        {
            Theme.SetScale(scale);
            var size = new Size(Theme.D(1280), Theme.D(800));
            MinimumSize = new Size(Theme.D(1060), Theme.D(680));
            var work = Screen.FromHandle(Handle).WorkingArea;
            Size = new Size(Math.Min(size.Width, work.Width), Math.Min(size.Height, work.Height));
            WindowState = FormWindowState.Normal;
            Arrange();
            ShowNav(0);
            LoadBoxes();
            Refresh();
            string tag = scale < 1.2f ? "100" : "150";
            SaveShot(dir, "areas-" + tag + ".png");
            SaveOverlayCard(dir, "overlays-" + tag + ".png", scale);
        }
    }

    private static void SaveOverlayCard(string dir, string name, float scale)
    {
        int w = (int)(1280 * Math.Min(scale, 1.25f));
        int h = (int)(720 * Math.Min(scale, 1.25f));
        using var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(18, 22, 32));
        using (var sky = new SolidBrush(Color.FromArgb(22, 28, 42)))
            g.FillRectangle(sky, 0, 0, w, h);
        void Box(Rectangle r, Color color, string title, string line)
        {
            using var bg = new SolidBrush(Color.FromArgb(210, 10, 12, 16));
            g.FillRectangle(bg, r);
            using var pen = new Pen(color, 4f);
            g.DrawRectangle(pen, r);
            using var font = new Font("Segoe UI", 18f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            g.DrawString(title, font, brush, r.X + 12, r.Y - 28);
            using var white = new SolidBrush(Color.White);
            g.DrawString(line, font, white, new RectangleF(r.X + 16, r.Y + 16, r.Width - 32, r.Height - 24));
        }
        Box(new Rectangle(w / 10, (int)(h * 0.62), (int)(w * 0.8), (int)(h * 0.16)), Color.FromArgb(245, 165, 36), "Konuşma", "Polisler gelmeden buradan çıkmamız lazım.");
        Box(new Rectangle(w / 4, (int)(h * 0.16), (int)(w * 0.5), (int)(h * 0.14)), Color.FromArgb(20, 184, 178), "Görev", "Phoenix'i yeniden boya.");
        bmp.Save(Path.Combine(dir, name), System.Drawing.Imaging.ImageFormat.Png);
    }

    private void CaptureSozluk(string dir)
    {
        Directory.CreateDirectory(dir);
        _tour.Visible = false;
        var seeded = HazirKutuphane.From(
            new HazirDizin("Grand Theft Auto VI — Leonida", new (string En, string Tr)[] { ("Get in.", "Bin.") }),
            new HazirDizin("Red Dead Redemption 2", new (string En, string Tr)[] { ("Howdy.", "Selam.") }));
        Patch(s =>
        {
            s.TanitimGoruldu = true;
            s.SeciliHazirOyun = "Grand Theft Auto VI — Leonida";
            s.Sozluk = new List<SozlukGirdisi>
            {
                new("Mount Kalaga National Park", "Mount Kalaga Ulusal Parkı"),
                new("a long english line", "Çok uzun bir Türkçe karşılık satırı kaymadan dursun"),
            };
        });
        (float Scale, bool Legacy, string Name)[] frames =
        {
            (1f, true, "before-100.png"),
            (1.5f, true, "before-150.png"),
            (1f, false, "after-100.png"),
            (1.5f, false, "after-150.png"),
        };
        foreach (var frame in frames)
        {
            Theme.SetScale(frame.Scale);
            _legacySozluk = frame.Legacy;
            var min = new Size(Theme.D(1060), Theme.D(680));
            MinimumSize = min;
            var work = Screen.FromHandle(Handle).WorkingArea;
            Size = new Size(Math.Min(min.Width, work.Width), Math.Min(min.Height, work.Height));
            WindowState = FormWindowState.Normal;
            Arrange();
            ShowNav(2);
            HazirKutuphane.Use(seeded);
            FillGlossary();
            _scroll.Relayout();
            Refresh();
            SaveShot(dir, frame.Name);
        }
    }

    private void CaptureShots(string dir)
    {
        Directory.CreateDirectory(dir);
        Arrange();
        Refresh();
        SaveShot(dir, "01-ana.png");
        StartTour();
        (int Step, string Name)[] frames =
        {
            (0, "02-tur-menu.png"),
            (2, "03-tur-orijinal.png"),
            (5, "04-tur-alanlar.png"),
            (8, "05-tur-gorunum.png"),
        };
        foreach (var (step, name) in frames)
        {
            _tour.Go(step);
            SaveShot(dir, name);
        }
    }

    private void SaveShot(string dir, string name)
    {
        using var bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        if (_tour.Visible && _tour.Width > 0 && _tour.Height > 0)
        {
            using var overlay = new Bitmap(_tour.Width, _tour.Height);
            _tour.DrawToBitmap(overlay, new Rectangle(Point.Empty, overlay.Size));
            using var g = Graphics.FromImage(bmp);
            g.DrawImage(overlay, _tour.Location);
        }
        bmp.Save(Path.Combine(dir, name), System.Drawing.Imaging.ImageFormat.Png);
    }

    private void Patch(Action<AppSettings> edit, bool persist = true)
    {
        if (_loading) return;
        var s = _host.Current.Clone();
        edit(s);
        _host.Apply(s, persist);
    }
}

/// <summary>Window enumeration and frame metrics for the custom title bar.</summary>
internal static class NativeUi
{
    public const uint SWP_FRAMECHANGED = 0x0020, SWP_NOZORDER = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct NCCALCSIZE_PARAMS
    {
        public RECT Rect0, Rect1, Rect2;
        public IntPtr Pos;
    }

    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    public static int Frame(int dpi, bool horizontal)
    {
        const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;
        try
        {
            return GetSystemMetricsForDpi(horizontal ? SM_CXSIZEFRAME : SM_CYSIZEFRAME, (uint)dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, (uint)dpi);
        }
        catch (EntryPointNotFoundException) { return 8; }
    }

    /// <summary>Visible bounds of a window (without the invisible resize border on Windows 10/11).</summary>
    public static bool TryWindowRect(IntPtr h, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!IsWindow(h) || IsIconic(h)) return false;
        RECT r;
        bool ok = DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out r, Marshal.SizeOf<RECT>()) == 0;
        if (!ok && !GetWindowRect(h, out r)) return false;
        rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return rect.Width >= 50 && rect.Height >= 50;
    }

    public static List<(IntPtr Handle, string Title)> Windows()
    {
        var list = new List<(IntPtr, string)>();
        uint self = (uint)Environment.ProcessId;
        EnumWindows((h, lp) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            if (GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            _ = GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) return true;
            if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var chars = new char[512];
            int n = GetWindowText(h, chars, chars.Length);
            if (n <= 0) return true;
            string title = new string(chars, 0, n).Trim();
            if (title.Length == 0 || title == "Program Manager") return true;
            if (!TryWindowRect(h, out var r) || r.Width < 200 || r.Height < 120) return true;
            list.Add((h, title));
            return list.Count < 40;
        }, IntPtr.Zero);
        return list;
    }
}
