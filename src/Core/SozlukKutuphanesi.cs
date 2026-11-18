using System.Text.Json;
using System.Text.Json.Serialization;

namespace AltyaziCevirici.Core;

public enum SozlukTuru { Protect, Translate }

public sealed class SozlukKaydi
{
    [JsonPropertyName("term")] public string Term { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "protect";
    [JsonPropertyName("tr")] public string? Tr { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("sources")] public List<string>? Sources { get; set; }

    public SozlukGirdisi ToGirdi()
    {
        bool translate = Type.Equals("translate", StringComparison.OrdinalIgnoreCase);
        return new SozlukGirdisi(Term, translate ? (Tr ?? "") : "");
    }
}

public sealed class SozlukPaketDosyasi
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("ad")] public string Ad { get; set; } = "";
    [JsonPropertyName("entries")] public List<SozlukKaydi> Entries { get; set; } = new();
}

public sealed record SozlukPaket(string Id, string Ad, bool OyunPaketi, IReadOnlyList<SozlukKaydi> Entries);

/// <summary>
/// Built-in glossary packs plus the GTA 6 pack, which is only whatever is in data/packs/gta6.json.
/// Unverified GTA 6 names are not hardcoded.
/// </summary>
public static class SozlukKutuphanesi
{
    public static readonly string[] VarsayilanAcik = { "genel", "argo", "oyun", "isimler" };

    /// <summary>Raw gta6.json text. The app sets this from the data file or the embedded copy.</summary>
    public static string? Gta6Json { get; set; }

    private static readonly SozlukPaket[] Yerlesik =
    {
        Paket("genel", "Genel", false,
            Cevir("got it", "Anladım", "ifade"), Cevir("hang on", "Bekle", "ifade"), Cevir("hold on", "Dur bir dakika", "ifade"),
            Cevir("come on", "Hadi", "ifade"), Cevir("no way", "Yok artık", "ifade"), Cevir("let's go", "Hadi gidelim", "ifade"),
            Cevir("watch out", "Dikkat", "ifade"), Cevir("behind you", "Arkanda", "ifade"), Cevir("take cover", "Siper al", "ifade"),
            Cevir("I got you", "Seni korurum", "ifade"), Cevir("my bad", "Benim hatam", "ifade"), Cevir("fair enough", "Haklısın", "ifade")),
        Paket("argo", "Argo / Küfür", false,
            Cevir("dude", "kanka", "argo"), Cevir("bro", "kanka", "argo"), Cevir("man", "adamım", "argo"),
            Cevir("pissed", "kafası bozuk", "argo"), Cevir("screw this", "Siktir et", "küfür"),
            Cevir("what the hell", "Ne oluyor be", "argo"), Cevir("holy crap", "Hassiktir", "küfür"),
            Cevir("son of a bitch", "orospu çocuğu", "küfür")),
        Paket("oyun", "Oyun terimleri", false,
            Cevir("checkpoint", "kontrol noktası", "oyun"), Cevir("quest", "görev", "oyun"),
            Cevir("inventory", "envanter", "oyun"), Cevir("ammo", "cephane", "oyun"),
            Cevir("respawn", "yeniden doğ", "oyun"), Cevir("loot", "ganimet", "oyun"),
            Koru("save point", "oyun"), Koru("level up", "oyun")),
        Paket("isimler", "İsimler / Yerler", false,
            Koru("PlayStation", "marka"), Koru("Xbox", "marka"), Koru("Netflix", "marka"),
            Koru("YouTube", "marka"), Koru("Instagram", "marka"), Koru("Spotify", "marka"),
            Koru("Google", "marka"), Koru("Windows", "marka"), Koru("New York", "yer"), Koru("Los Angeles", "yer")),
        Paket("rdr2", "Red Dead Redemption 2", true,
            Koru("Arthur Morgan", "kişi"), Koru("Dutch", "kişi"), Koru("John Marston", "kişi"),
            Koru("Saint Denis", "yer"), Koru("Blackwater", "yer"), Koru("Valentine", "yer")),
        Paket("tlou", "The Last of Us", true, Koru("Joel", "kişi"), Koru("Ellie", "kişi"), Koru("Clicker", "terim")),
        Paket("gow", "God of War", true, Koru("Kratos", "kişi"), Koru("Atreus", "kişi"), Koru("Brok", "kişi"), Koru("Sindri", "kişi")),
        Paket("spiderman", "Spider-Man", true, Koru("Peter Parker", "kişi"), Koru("Miles Morales", "kişi"), Koru("Mary Jane", "kişi")),
    };

    public static IReadOnlyList<SozlukPaket> Paketler()
    {
        var list = new List<SozlukPaket>(Yerlesik);
        list.Add(Gta6());
        return list;
    }

    public static SozlukPaket Gta6()
    {
        var dosya = OkuGta(Gta6Json);
        return new SozlukPaket("gta6", string.IsNullOrWhiteSpace(dosya?.Ad) ? "GTA 6" : dosya!.Ad, true,
            dosya?.Entries ?? new List<SozlukKaydi>());
    }

    public static SozlukPaketDosyasi? OkuGta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SozlukPaketDosyasi { Id = "gta6", Ad = "GTA 6" };
        try
        {
            var dosya = JsonSerializer.Deserialize<SozlukPaketDosyasi>(json, JsonOpts);
            if (dosya == null) return new SozlukPaketDosyasi { Id = "gta6", Ad = "GTA 6" };
            dosya.Entries.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.Term));
            return dosya;
        }
        catch (JsonException)
        {
            return new SozlukPaketDosyasi { Id = "gta6", Ad = "GTA 6" };
        }
    }

    public static List<SozlukGirdisi> Etkin(AppSettings settings)
    {
        IEnumerable<string> ids = settings.AcikPaketler == null ? VarsayilanAcik : settings.AcikPaketler;
        var acik = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var list = new List<SozlukGirdisi>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var paket in Paketler())
        {
            if (!acik.Contains(paket.Id)) continue;
            foreach (var kayit in paket.Entries)
            {
                if (string.IsNullOrWhiteSpace(kayit.Term) || !seen.Add(kayit.Term)) continue;
                list.Add(kayit.ToGirdi());
            }
        }
        if (settings.Sozluk != null)
        {
            foreach (var g in settings.Sozluk)
            {
                if (g == null || string.IsNullOrWhiteSpace(g.Ingilizce)) continue;
                list.RemoveAll(x => x.Ingilizce.Equals(g.Ingilizce, StringComparison.OrdinalIgnoreCase));
                list.Add(new SozlukGirdisi(g.Ingilizce, g.Turkce));
            }
        }
        return list;
    }

    public static string DisaAktar(IEnumerable<SozlukGirdisi> entries) =>
        JsonSerializer.Serialize(entries.Select(g => new SozlukKaydi { Term = g.Ingilizce, Type = string.IsNullOrEmpty(g.Turkce) ? "protect" : "translate", Tr = g.Turkce, Category = "kullanici" }), JsonOpts);

    public static List<SozlukGirdisi> IceAktar(string json)
    {
        var kayitlar = JsonSerializer.Deserialize<List<SozlukKaydi>>(json, JsonOpts) ?? new();
        return kayitlar.Where(k => !string.IsNullOrWhiteSpace(k.Term)).Select(k => k.ToGirdi()).ToList();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static SozlukPaket Paket(string id, string ad, bool oyun, params SozlukKaydi[] entries) =>
        new(id, ad, oyun, entries);

    private static SozlukKaydi Cevir(string term, string tr, string kategori) =>
        new() { Term = term, Type = "translate", Tr = tr, Category = kategori };

    private static SozlukKaydi Koru(string term, string kategori) =>
        new() { Term = term, Type = "protect", Tr = "", Category = kategori };
}
