namespace AltyaziCevirici.Core;

/// <summary>Maps a subtitle language to the Windows OCR capability. No WinRT calls.</summary>
public static class OcrDilPaketi
{
    public static string Kod(string? sourceLang)
    {
        if (string.IsNullOrWhiteSpace(sourceLang)) return "en";
        var code = sourceLang.Trim().Split('-')[0].ToLowerInvariant();
        return code.Length is > 0 and <= 8 && code.All(char.IsAsciiLetter) ? code : "en";
    }

    public static string DilAdi(string? sourceLang) => Kod(sourceLang) switch
    {
        "en" => "İngilizce",
        "de" => "Almanca",
        "fr" => "Fransızca",
        "es" => "İspanyolca",
        "it" => "İtalyanca",
        "pt" => "Portekizce",
        "nl" => "Felemenkçe",
        "pl" => "Lehçe",
        "ru" => "Rusça",
        "tr" => "Türkçe",
        _ => "Seçilen dil",
    };

    /// <summary>Windows capability id, for example Language.OCR~~~en-US~0.0.1.0.</summary>
    public static string Capability(string? sourceLang)
    {
        var tag = Kod(sourceLang) switch
        {
            "en" => "en-US",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "es" => "es-ES",
            "it" => "it-IT",
            "pt" => "pt-BR",
            "nl" => "nl-NL",
            "pl" => "pl-PL",
            "ru" => "ru-RU",
            "tr" => "tr-TR",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "zh" => "zh-CN",
            _ => "en-US",
        };
        return "Language.OCR~~~" + tag + "~0.0.1.0";
    }

    public static string Banner(string? sourceLang) => DilAdi(sourceLang) + " yazı tanıma paketi eksik — Kur";
}
