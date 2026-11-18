using System.Globalization;
using System.Text;

namespace AltyaziCevirici.Core;

/// <summary>Tiny local error file (exception type, message, stack only). Size-capped. Never throws.</summary>
public static class ErrorLog
{
    private static readonly object Lock = new();
    public static string? Path { get; set; }
    public static long MaxBytes { get; set; } = 256 * 1024;
    private static DateTime _lastWrite = DateTime.MinValue;
    private static string? _lastSignature;

    public static void Write(Exception ex, string where)
    {
        try
        {
            var path = Path;
            if (path == null) return;
            lock (Lock)
            {
                // Avoid flooding with the same error every frame.
                var sig = where + "|" + ex.GetType().FullName + "|" + ex.Message;
                var now = DateTime.Now;
                if (sig == _lastSignature && now - _lastWrite < TimeSpan.FromSeconds(30)) return;
                _lastSignature = sig;
                _lastWrite = now;

                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var old = path + ".1";
                    File.Delete(old);
                    File.Move(path, old);
                }
                var sb = new StringBuilder();
                sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" [").Append(where).Append("] ")
                  .Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message)
                  .AppendLine(ex.StackTrace ?? "")
                  .AppendLine();
                File.AppendAllText(path, sb.ToString());
            }
        }
        catch (Exception) { /* logging must never fail the app */ }
    }
}
