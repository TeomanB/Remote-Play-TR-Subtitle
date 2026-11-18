using System.ComponentModel;
using AltyaziCevirici.Core;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace AltyaziCevirici;

/// <summary>Detects a missing Windows OCR language and installs it with one UAC prompt.</summary>
internal static class OcrPaketKur
{
    public static bool KaynakHazir(string? sourceLang)
    {
        try
        {
            var code = OcrDilPaketi.Kod(sourceLang);
            string[] tags = code == "en"
                ? new[] { "en-US", "en-GB", "en" }
                : new[] { string.IsNullOrWhiteSpace(sourceLang) ? code : sourceLang, code };
            foreach (var tag in tags)
            {
                try
                {
                    var lang = new Language(tag);
                    if (OcrEngine.IsLanguageSupported(lang)) return true;
                }
                catch (Exception ex) { ErrorLog.Write(ex, "ocr-lang"); }
            }
            return OcrEngine.AvailableRecognizerLanguages.Any(l =>
                l.LanguageTag.StartsWith(code, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "ocr-lang");
            return false;
        }
    }

    /// <summary>Runs the capability command elevated. Returns false when the user cancels UAC or the command fails.</summary>
    public static async Task<(bool Ok, string Mesaj)> KurAsync(string? sourceLang)
    {
        var capability = OcrDilPaketi.Capability(sourceLang);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"try { Add-WindowsCapability -Online -Name '" + capability + "' | Out-Null; exit 0 } catch { exit 1 }\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        try
        {
            var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
                return (false, "Yazı tanıma paketi başlatılamadı.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0)
                return (false, "Yazı tanıma paketi kurulamadı (kod " + process.ExitCode + "). Windows bu dili sunmuyorsa Ayarlar → Zaman ve dil bölümünden ekleyebilirsin.");
            return (true, "");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Yönetici izni verilmedi. Yazı tanıma paketi kurulmadı.");
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "ocr-kur");
            return (false, "Yazı tanıma paketi kurulamadı. Daha sonra Kur'a tekrar basabilirsin.");
        }
    }
}
