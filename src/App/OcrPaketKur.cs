using System.ComponentModel;
using System.Text;
using AltyaziCevirici.Core;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace AltyaziCevirici;

/// <summary>Detects a missing Windows OCR language and installs it with one UAC prompt. The console stays hidden.</summary>
internal static class OcrPaketKur
{
    public static readonly TimeSpan ZamanAsimi = TimeSpan.FromMinutes(8);

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

    /// <summary>Runs the capability command elevated. Returns false when the user cancels UAC, the command fails, or it times out.</summary>
    public static async Task<(bool Ok, string Mesaj)> KurAsync(string? sourceLang, IProgress<string>? durum = null, CancellationToken ct = default)
    {
        string ad = OcrDilPaketi.DilAdi(sourceLang);
        if (KaynakHazir(sourceLang))
            return (true, ad + " yazı tanıma paketi zaten kurulu.");

        durum?.Report(ad + " yazı tanıma paketi indiriliyor, bu birkaç dakika sürebilir…");
        var capability = OcrDilPaketi.Capability(sourceLang);
        string path = Path.Combine(Path.GetTempPath(), "altyazi-ocr-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(path, Script(capability), Encoding.ASCII, ct).ConfigureAwait(false);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + path + "\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        };
        try
        {
            var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
                return (false, "Yazı tanıma paketi başlatılamadı.");
            using var timeout = new CancellationTokenSource(ZamanAsimi + TimeSpan.FromSeconds(20));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
                return (false, ad + " yazı tanıma paketi zaman aşımına uğradı. Gelişmiş sayfasındaki Kur ile yeniden deneyebilirsin.");
            }
            if (process.ExitCode == 2)
                return (false, ad + " yazı tanıma paketi zaman aşımına uğradı. Gelişmiş sayfasındaki Kur ile yeniden deneyebilirsin.");
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
        finally
        {
            try { File.Delete(path); } catch (Exception) { /* temp script */ }
        }
    }

    private static string Script(string capability) =>
        "$ErrorActionPreference='Stop'\r\n" +
        "$n='" + capability + "'\r\n" +
        "try {\r\n" +
        "  $s=(Get-WindowsCapability -Online -Name $n).State\r\n" +
        "  if ($s -eq 'Installed') { exit 0 }\r\n" +
        "  $j=Start-Job -ScriptBlock { param($n) Add-WindowsCapability -Online -Name $n | Out-Null } -ArgumentList $n\r\n" +
        "  if (-not (Wait-Job $j -Timeout 480)) { Stop-Job $j -ErrorAction SilentlyContinue; Remove-Job $j -Force -ErrorAction SilentlyContinue; exit 2 }\r\n" +
        "  Receive-Job $j -ErrorAction SilentlyContinue | Out-Null\r\n" +
        "  $ok=(Get-WindowsCapability -Online -Name $n).State -eq 'Installed'\r\n" +
        "  Remove-Job $j -Force -ErrorAction SilentlyContinue\r\n" +
        "  if ($ok) { exit 0 } else { exit 1 }\r\n" +
        "} catch { exit 1 }\r\n";
}
