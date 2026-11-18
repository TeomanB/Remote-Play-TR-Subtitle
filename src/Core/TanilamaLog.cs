using System.Globalization;
using System.Text;

namespace AltyaziCevirici.Core;

/// <summary>
/// One line per decision (OCR gate, translation, show, hide), beside ayarlar.json.
/// Identical lines are collapsed for two seconds. The file rolls at about 1 MB. Never throws.
/// </summary>
public static class TanilamaLog
{
    private static readonly object Lock = new();
    public static string? Path { get; set; }
    public static bool Enabled { get; set; } = true;
    public static long MaxBytes { get; set; } = 1024 * 1024;
    private static string? _lastSig;
    private static DateTime _lastWrite = DateTime.MinValue;

    public static void Yaz(string olay, string ayrinti)
    {
        if (!Enabled) return;
        try
        {
            var path = Path;
            if (string.IsNullOrEmpty(path)) return;
            var sig = olay + "|" + ayrinti;
            var now = DateTime.Now;
            lock (Lock)
            {
                if (sig == _lastSig && now - _lastWrite < TimeSpan.FromSeconds(2)) return;
                _lastSig = sig;
                _lastWrite = now;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var old = path + ".1";
                    File.Delete(old);
                    File.Move(path, old);
                }
                var line = now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + " " + olay + " " + Temiz(ayrinti) + Environment.NewLine;
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception) { /* diagnostics must not stop capture */ }
    }

    private static string Temiz(string s)
    {
        if (s.Length > 180) s = s[..180];
        return s.Replace('\r', ' ').Replace('\n', ' ');
    }
}
