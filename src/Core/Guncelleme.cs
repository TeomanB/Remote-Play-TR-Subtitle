using System.Text.Json;

namespace AltyaziCevirici.Core;

/// <summary>Compares vX.Y.Z release tags with the running assembly version. No network.</summary>
public static class SurumKarsilastirma
{
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length > 0 && (t[0] == 'v' || t[0] == 'V')) t = t[1..];
        var parts = t.Split('.');
        if (parts.Length is < 2 or > 4) return false;
        var numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || parts[i].Length > 6 || !int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
                return false;
        }
        version = numbers.Length switch
        {
            2 => new Version(numbers[0], numbers[1]),
            3 => new Version(numbers[0], numbers[1], numbers[2]),
            _ => new Version(numbers[0], numbers[1], numbers[2], numbers[3]),
        };
        return true;
    }

    /// <summary>True when <paramref name="latestTag"/> is a newer X.Y.Z than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? latestTag, Version current)
    {
        if (!TryParse(latestTag, out var latest)) return false;
        return latest.CompareTo(Normalize(current)) > 0;
    }

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static string Tag(Version v) => "v" + Normalize(v).ToString(3);
}

public sealed record GuncellemeBilgisi(string Tag, Version Surum, string Sayfa, string? KurulumAdi, string? KurulumUrl);

public static class GuncellemeOkuyucu
{
    public const string RepoApi = "https://api.github.com/repos/TeomanB/Remote-Play-TR-Subtitle/releases/latest";

    public static GuncellemeBilgisi? Oku(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            if (!SurumKarsilastirma.TryParse(tag, out var surum)) return null;
            var sayfa = root.TryGetProperty("html_url", out var pageEl) ? pageEl.GetString() ?? "" : "";
            string? ad = null, url = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!name.StartsWith("AltyaziCevirici-Setup-", StringComparison.OrdinalIgnoreCase) ||
                        !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        continue;
                    ad = name;
                    url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
            return new GuncellemeBilgisi(SurumKarsilastirma.Tag(surum), surum, sayfa, ad, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Startup banner: newer than the running build, and not the tag the user skipped.</summary>
    public static bool BannerGoster(Version mevcut, GuncellemeBilgisi? bilgi, string? atlananEtiket)
    {
        if (bilgi == null || !SurumKarsilastirma.IsNewer(bilgi.Tag, mevcut)) return false;
        return !string.Equals(bilgi.Tag, atlananEtiket?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool GuvenliKurulumAdresi(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (!string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        var host = u.IdnHost;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }
}
