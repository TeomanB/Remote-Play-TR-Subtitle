using System.Text.Json;
using System.Text.Json.Serialization;

namespace AltyaziCevirici.Core;

public enum OverlayPlacement { Ust, Alt, Uzerinde }

public sealed class AppSettings
{
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; }
    public int RegionHeight { get; set; }
    /// <summary>Settings schema version (0 = file from v1, 2 = v1.1, 3 = v1.2, 4 = v1.3, 5 = v1.4).</summary>
    public int Surum { get; set; }
    public const int CurrentVersion = 5;
    public int Fps { get; set; } = 15;
    public bool BeyazYaziFiltresi { get; set; } = true;
    public int BeyazEsik { get; set; } = 170;
    public OverlayPlacement OverlayKonumu { get; set; } = OverlayPlacement.Ust;
    public float YaziBoyutu { get; set; } = 26f;
    public int GizlemeMs { get; set; } = 2000;
    public double BenzerlikEsigi { get; set; } = 0.85;

    // ---- v1.2 ----
    public string KaynakDil { get; set; } = "en";
    public string HedefDil { get; set; } = "tr";
    /// <summary>Keep capitalized names mid-sentence as written (heuristic).</summary>
    public bool OzelIsimleriKoru { get; set; } = true;
    /// <summary>Text color as #RRGGBB.</summary>
    public string YaziRengi { get; set; } = "#FFFFFF";
    /// <summary>Background box opacity in percent (0-100).</summary>
    public int ArkaPlanOpakligi { get; set; } = 65;
    /// <summary>Extra vertical offset of the translation box in pixels (+ down, - up).</summary>
    public int DikeyKaydirma { get; set; }
    /// <summary>User glossary entries. Null in old files becomes empty; built-in packs are separate.</summary>
    public List<SozlukGirdisi>? Sozluk { get; set; }
    /// <summary>Enabled pack ids. Null → Genel, Argo, Oyun, İsimler. gta6 stays off until the user turns it on.</summary>
    public List<string>? AcikPaketler { get; set; }

    // ---- v1.3 control center / free subtitle placement ----
    /// <summary>False keeps the v1.2 anchor (above, below, or over the region).</summary>
    public bool KonumAyarlandi { get; set; }
    public bool BolgeyiTakipEt { get; set; } = true;
    public AltyaziOnAyar OnAyar { get; set; } = AltyaziOnAyar.Serbest;
    public int KutuX { get; set; }
    public int KutuY { get; set; }
    /// <summary>Subtitle box width. 0 = derive from the capture region.</summary>
    public int KutuGenislik { get; set; }
    /// <summary>Subtitle box height drawn on the preview. 0 = measure from the text.</summary>
    public int KutuYukseklik { get; set; }
    public int BagilX { get; set; }
    public int BagilY { get; set; }
    /// <summary>Outline thickness in pixels. 0 draws no outline.</summary>
    public int DisCizgi { get; set; } = 4;
    /// <summary>Screen.DeviceName of the monitor used for presets. Empty = the region's monitor.</summary>
    public string MonitorAdi { get; set; } = "";
    public bool TepsiIpucuGosterildi { get; set; }
    /// <summary>Check GitHub Releases on startup. Default on.</summary>
    public bool AcilistaGuncellemeDenetle { get; set; } = true;
    /// <summary>Tag the user chose to ignore, such as v1.4.0. Empty = none.</summary>
    public string AtlananSurum { get; set; } = "";

    // ---- v1.5 appearance ----
    /// <summary>Font family of the Turkish line. Missing fonts fall back to Segoe UI when drawn.</summary>
    public string YaziTipi { get; set; } = "Segoe UI";
    /// <summary>Outline color as #RRGGBB.</summary>
    public string DisCizgiRengi { get; set; } = "#000000";

    // ---- v1.5 control center state (restored on the next start) ----
    public bool OrijinalKilitli { get; set; }
    public bool CeviriKilitli { get; set; }
    /// <summary>Preview source: "" = not chosen (auto-detect Remote Play), "ekran" = whole monitor, otherwise a window title.</summary>
    public string KaynakPencere { get; set; } = "";
    /// <summary>Control center bounds in screen pixels. Width 0 = default size, centered.</summary>
    public int PencereX { get; set; }
    public int PencereY { get; set; }
    public int PencereGenislik { get; set; }
    public int PencereYukseklik { get; set; }
    public bool PencereBuyuk { get; set; }
    /// <summary>Last sidebar page: 0 Önizleme, 1 Görünüm, 2 Sözlük, 3 Gelişmiş.</summary>
    public int SonSayfa { get; set; }
    /// <summary>Start with Windows (mirrored to the HKCU Run key).</summary>
    public bool WindowsIleBaslat { get; set; }
    /// <summary>The first-run coach tour has been finished or skipped.</summary>
    public bool TanitimGoruldu { get; set; }

    [JsonIgnore]
    public bool HasRegion => RegionWidth >= 20 && RegionHeight >= 10;

    public static List<SozlukGirdisi> DefaultGlossary() => new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                if (s != null)
                {
                    if (s.Surum < 2) s.Fps = 15; // v1 used 6 fps
                    s.Surum = CurrentVersion;
                    s.Clamp();
                    return s;
                }
            }
        }
        catch { /* corrupt file: use defaults */ }
        var d = new AppSettings { Surum = CurrentVersion };
        d.Clamp();
        return d;
    }

    public void Save(string path)
    {
        Clamp();
        Surum = CurrentVersion;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Options), Options)!;

    public void Clamp()
    {
        Fps = Math.Clamp(Fps, 1, 30);
        BeyazEsik = Math.Clamp(BeyazEsik, 50, 250);
        YaziBoyutu = Math.Clamp(YaziBoyutu, 10f, 72f);
        GizlemeMs = Math.Clamp(GizlemeMs, 300, 20000);
        BenzerlikEsigi = Math.Clamp(BenzerlikEsigi, 0.5, 0.99);
        ArkaPlanOpakligi = Math.Clamp(ArkaPlanOpakligi, 0, 100);
        DikeyKaydirma = Math.Clamp(DikeyKaydirma, -1000, 1000);
        if (!Enum.IsDefined(OverlayKonumu)) OverlayKonumu = OverlayPlacement.Ust;
        if (!Enum.IsDefined(OnAyar)) OnAyar = AltyaziOnAyar.Serbest;
        DisCizgi = Math.Clamp(DisCizgi, 0, 16);
        KutuGenislik = KutuGenislik < SubtitleLayout.MinWidth ? 0 : Math.Clamp(KutuGenislik, SubtitleLayout.MinWidth, 8000);
        KutuYukseklik = KutuYukseklik <= 0 ? 0 : Math.Clamp(KutuYukseklik, 24, 800);
        KutuX = Math.Clamp(KutuX, -8000, 16000);
        KutuY = Math.Clamp(KutuY, -8000, 16000);
        BagilX = Math.Clamp(BagilX, -8000, 8000);
        BagilY = Math.Clamp(BagilY, -8000, 8000);
        MonitorAdi = CleanMonitor(MonitorAdi);
        AtlananSurum = CleanTag(AtlananSurum);
        KaynakDil = CleanLang(KaynakDil, "en");
        HedefDil = CleanLang(HedefDil, "tr");
        if (!TryParseColor(YaziRengi, out _)) YaziRengi = "#FFFFFF";
        if (!TryParseColor(DisCizgiRengi, out _)) DisCizgiRengi = "#000000";
        YaziTipi = CleanFont(YaziTipi);
        KaynakPencere = string.IsNullOrWhiteSpace(KaynakPencere) || KaynakPencere.Length > 256 || KaynakPencere.Any(char.IsControl) ? "" : KaynakPencere.Trim();
        SonSayfa = Math.Clamp(SonSayfa, 0, 3);
        if (PencereGenislik < 200 || PencereYukseklik < 150) { PencereGenislik = 0; PencereYukseklik = 0; }
        PencereGenislik = Math.Min(PencereGenislik, 16000);
        PencereYukseklik = Math.Min(PencereYukseklik, 16000);
        PencereX = Math.Clamp(PencereX, -16000, 32000);
        PencereY = Math.Clamp(PencereY, -16000, 32000);
        Sozluk ??= new List<SozlukGirdisi>();
        AcikPaketler ??= new List<string>(SozlukKutuphanesi.VarsayilanAcik);
        AcikPaketler.RemoveAll(string.IsNullOrWhiteSpace);
        for (int i = 0; i < AcikPaketler.Count; i++) AcikPaketler[i] = AcikPaketler[i].Trim().ToLowerInvariant();
        Sozluk.RemoveAll(g => g == null || string.IsNullOrWhiteSpace(g.Ingilizce));
        foreach (var g in Sozluk) { g.Ingilizce = g.Ingilizce.Trim(); g.Turkce = (g.Turkce ?? string.Empty).Trim(); }
    }

    private static string CleanTag(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        v = v.Trim();
        if (v.Length > 32 || !SurumKarsilastirma.TryParse(v, out _)) return "";
        return v.StartsWith('v') || v.StartsWith('V') ? "v" + v[1..] : "v" + v;
    }

    private static string CleanMonitor(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        v = v.Trim();
        if (v.Length > 64 || v.Any(c => c < 32 || c == '"')) return "";
        return v;
    }

    private static string CleanFont(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "Segoe UI";
        v = v.Trim();
        return v.Length <= 64 && v.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_') ? v : "Segoe UI";
    }

    private static string CleanLang(string? v, string def)
    {
        if (string.IsNullOrWhiteSpace(v)) return def;
        v = v.Trim();
        return v.Length <= 10 && v.All(c => char.IsAsciiLetter(c) || c == '-') ? v : def;
    }

    /// <summary>Parses "#RRGGBB" into r,g,b.</summary>
    public static bool TryParseColor(string? hex, out (byte R, byte G, byte B) rgb)
    {
        rgb = (255, 255, 255);
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int v)) return false;
        rgb = ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public ProcessorOptions ToProcessorOptions() => new(KaynakDil, HedefDil, SozlukKutuphanesi.Etkin(this), OzelIsimleriKoru);
}
