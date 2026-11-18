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
    /// <summary>Settings schema version (0 = file from v1, 2 = v1.1, 3 = v1.2).</summary>
    public int Surum { get; set; }
    public const int CurrentVersion = 3;
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
    /// <summary>User glossary. Null in old files → defaults.</summary>
    public List<SozlukGirdisi>? Sozluk { get; set; }

    [JsonIgnore]
    public bool HasRegion => RegionWidth >= 20 && RegionHeight >= 10;

    public static List<SozlukGirdisi> DefaultGlossary() => new()
    {
        new("Vice City"), new("Leonida"), new("Leonida Keys"), new("Port Gellhorn"), new("Grassrivers"),
        new("Ambrosia"), new("Mount Kalaga"), new("Jason"), new("Lucia"),
    };

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
        KaynakDil = CleanLang(KaynakDil, "en");
        HedefDil = CleanLang(HedefDil, "tr");
        if (!TryParseColor(YaziRengi, out _)) YaziRengi = "#FFFFFF";
        Sozluk ??= DefaultGlossary();
        Sozluk.RemoveAll(g => g == null || string.IsNullOrWhiteSpace(g.Ingilizce));
        foreach (var g in Sozluk) { g.Ingilizce = g.Ingilizce.Trim(); g.Turkce = (g.Turkce ?? string.Empty).Trim(); }
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

    public ProcessorOptions ToProcessorOptions() => new(KaynakDil, HedefDil, Sozluk?.Select(g => new SozlukGirdisi(g.Ingilizce, g.Turkce)).ToList(), OzelIsimleriKoru);
}
