using AltyaziCevirici.Core;

namespace AltyaziCevirici;

/// <summary>GitHub Releases lookup and Setup.exe download. Failures stay silent for the caller.</summary>
internal static class GuncellemeIstegi
{
    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AltyaziCevirici");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static Version UygulamaSurumu =>
        typeof(GuncellemeIstegi).Assembly.GetName().Version ?? new Version(1, 3, 0);

    public static bool KurulumKopyasi =>
        File.Exists(Path.Combine(AppPaths.Dir, "unins000.exe"));

    public static async Task<GuncellemeBilgisi?> SorgulaAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var res = await Http.GetAsync(GuncellemeOkuyucu.RepoApi, cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            var json = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return GuncellemeOkuyucu.Oku(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<string?> IndirAsync(GuncellemeBilgisi bilgi, IProgress<double>? progress, CancellationToken ct)
    {
        if (!GuncellemeOkuyucu.GuvenliKurulumAdresi(bilgi.KurulumUrl)) return null;
        var name = string.IsNullOrWhiteSpace(bilgi.KurulumAdi) ? "AltyaziCevirici-Setup.exe" : bilgi.KurulumAdi;
        var dest = Path.Combine(Path.GetTempPath(), name);
        try
        {
            using var res = await Http.GetAsync(bilgi.KurulumUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            var total = res.Content.Headers.ContentLength ?? -1L;
            await using var input = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0) progress?.Report(Math.Clamp(read / (double)total, 0, 1));
            }
            progress?.Report(1);
            return dest;
        }
        catch (Exception)
        {
            try { File.Delete(dest); } catch (Exception) { /* ignore */ }
            return null;
        }
    }

    public static void KurulumuBaslat(string exePath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "/SILENT /CURRENTUSER",
            UseShellExecute = true,
        });
    }

    public static void SayfayiAc(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            url = "https://github.com/TeomanB/Remote-Play-TR-Subtitle/releases";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
}
