using AltyaziCevirici.Core;
using Xunit;

public class OcrDilPaketiTests
{
    [Theory]
    [InlineData("en", "Language.OCR~~~en-US~0.0.1.0")]
    [InlineData("en-GB", "Language.OCR~~~en-US~0.0.1.0")]
    [InlineData("de", "Language.OCR~~~de-DE~0.0.1.0")]
    [InlineData("fr", "Language.OCR~~~fr-FR~0.0.1.0")]
    [InlineData("es", "Language.OCR~~~es-ES~0.0.1.0")]
    [InlineData("it", "Language.OCR~~~it-IT~0.0.1.0")]
    [InlineData("pt", "Language.OCR~~~pt-BR~0.0.1.0")]
    [InlineData("nl", "Language.OCR~~~nl-NL~0.0.1.0")]
    [InlineData("pl", "Language.OCR~~~pl-PL~0.0.1.0")]
    [InlineData("ru", "Language.OCR~~~ru-RU~0.0.1.0")]
    [InlineData(null, "Language.OCR~~~en-US~0.0.1.0")]
    [InlineData("??", "Language.OCR~~~en-US~0.0.1.0")]
    public void MapsSourceLanguageToWindowsCapability(string? lang, string capability)
    {
        Assert.Equal(capability, OcrDilPaketi.Capability(lang));
    }

    [Fact]
    public void EnglishBannerMatchesTheInAppText()
    {
        Assert.Equal("İngilizce yazı tanıma paketi eksik — Kur", OcrDilPaketi.Banner("en"));
        Assert.Equal("Almanca yazı tanıma paketi eksik — Kur", OcrDilPaketi.Banner("de-DE"));
    }
}
