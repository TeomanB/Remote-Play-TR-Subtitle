namespace AltyaziCevirici.Core;

/// <summary>
/// An extra capture area beside the main subtitle strip. It has its own rectangle and translation box.
/// Objective/dialogue routing stays on the main strip; an extra area shows every line in its own box.
/// </summary>
public sealed class EkAlan
{
    public const int EnFazla = 3;
    public static readonly string[] Palet = { "#E8456B", "#F5A524", "#3B82F6" };
    public static readonly string[] Adlar = { "İpucu", "Diğer", "Ek" };

    public string Id { get; set; } = "";
    public string Ad { get; set; } = "İpucu";
    public bool Acik { get; set; } = true;
    public string Renk { get; set; } = "#E8456B";
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; }
    public int RegionHeight { get; set; }
    public bool KonumAyarlandi { get; set; }
    public int KutuX { get; set; }
    public int KutuY { get; set; }
    public int KutuGenislik { get; set; }
    public int KutuYukseklik { get; set; }

    public EkAlan Copy() => (EkAlan)MemberwiseClone();

    /// <summary>Top-left hint area used the first time extra areas are empty. Ratios match the preview shots.</summary>
    public static EkAlan VarsayilanIpucu(int srcX, int srcY, int srcW, int srcH) => new()
    {
        Id = "ipucu",
        Ad = "İpucu",
        Acik = true,
        Renk = Palet[0],
        RegionX = srcX + srcW * 3 / 100,
        RegionY = srcY + srcH * 4 / 100,
        RegionWidth = Math.Max(200, srcW * 26 / 100),
        RegionHeight = Math.Max(36, srcH * 6 / 100),
        KonumAyarlandi = true,
        KutuX = srcX + srcW * 3 / 100,
        KutuY = srcY + srcH * 16 / 100,
        KutuGenislik = Math.Max(SubtitleLayout.MinWidth, srcW * 42 / 100),
        KutuYukseklik = Math.Max(84, srcH * 12 / 100),
    };

    public void Clamp(int index)
    {
        int i = Math.Clamp(index, 0, Adlar.Length - 1);
        Ad = string.IsNullOrWhiteSpace(Ad) ? Adlar[i] : Ad.Trim();
        if (Ad.Length > 24) Ad = Ad[..24];
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 16 || Id.Any(c => !char.IsAsciiLetterOrDigit(c)))
            Id = "ek" + (index + 1);
        if (!AppSettings.TryParseColor(Renk, out _)) Renk = Palet[index % Palet.Length];
        RegionX = Math.Clamp(RegionX, -8000, 16000);
        RegionY = Math.Clamp(RegionY, -8000, 16000);
        RegionWidth = Math.Clamp(RegionWidth, 0, 8000);
        RegionHeight = Math.Clamp(RegionHeight, 0, 8000);
        KutuX = Math.Clamp(KutuX, -8000, 16000);
        KutuY = Math.Clamp(KutuY, -8000, 16000);
        KutuGenislik = KutuGenislik <= 0 ? 0 : Math.Clamp(KutuGenislik, 0, 8000);
        KutuYukseklik = KutuYukseklik <= 0 ? 0 : Math.Clamp(KutuYukseklik, 0, 800);
    }
}
