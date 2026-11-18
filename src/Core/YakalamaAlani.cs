namespace AltyaziCevirici.Core;

/// <summary>One OCR rectangle and the translation box that belongs to it.</summary>
public sealed class YakalamaAlani
{
    public const int EnFazla = 4;
    public static readonly string[] Palet = { "#F5A524", "#14B8B2", "#7A4FBF", "#E8456B" };
    public static readonly string[] Adlar = { "Konuşma", "Görev", "İpucu", "Diğer" };

    public string Id { get; set; } = "konusma";
    public string Ad { get; set; } = "Konuşma";
    public bool Acik { get; set; } = true;
    public string Renk { get; set; } = "#F5A524";
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; }
    public int RegionHeight { get; set; }
    public bool KonumAyarlandi { get; set; }
    public int KutuX { get; set; }
    public int KutuY { get; set; }
    public int KutuGenislik { get; set; }
    public int KutuYukseklik { get; set; }
    public bool BolgeyiTakipEt { get; set; }
    public int BagilX { get; set; }
    public int BagilY { get; set; }

    public YakalamaAlani Copy()
    {
        var c = (YakalamaAlani)MemberwiseClone();
        return c;
    }

    public static YakalamaAlani FromLegacy(AppSettings s) => new()
    {
        Id = "konusma",
        Ad = "Konuşma",
        Acik = true,
        Renk = Palet[0],
        RegionX = s.RegionX,
        RegionY = s.RegionY,
        RegionWidth = s.RegionWidth,
        RegionHeight = s.RegionHeight,
        KonumAyarlandi = s.KonumAyarlandi,
        KutuX = s.KutuX,
        KutuY = s.KutuY,
        KutuGenislik = s.KutuGenislik,
        KutuYukseklik = s.KutuYukseklik,
        BolgeyiTakipEt = s.BolgeyiTakipEt,
        BagilX = s.BagilX,
        BagilY = s.BagilY,
    };

    public void MirrorTo(AppSettings s)
    {
        s.RegionX = RegionX;
        s.RegionY = RegionY;
        s.RegionWidth = RegionWidth;
        s.RegionHeight = RegionHeight;
        s.KonumAyarlandi = KonumAyarlandi;
        s.KutuX = KutuX;
        s.KutuY = KutuY;
        s.KutuGenislik = KutuGenislik;
        s.KutuYukseklik = KutuYukseklik;
        s.BolgeyiTakipEt = BolgeyiTakipEt;
        s.BagilX = BagilX;
        s.BagilY = BagilY;
    }

    public void Clamp()
    {
        Ad = string.IsNullOrWhiteSpace(Ad) ? "Alan" : Ad.Trim();
        if (Ad.Length > 24) Ad = Ad[..24];
        RegionWidth = Math.Clamp(RegionWidth, 0, 8000);
        RegionHeight = Math.Clamp(RegionHeight, 0, 8000);
        KutuGenislik = KutuGenislik <= 0 ? 0 : Math.Clamp(KutuGenislik, 0, 8000);
        KutuYukseklik = KutuYukseklik <= 0 ? 0 : Math.Clamp(KutuYukseklik, 0, 800);
    }
}
