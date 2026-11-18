using AltyaziCevirici.Core;

namespace AltyaziCevirici;

internal static class SozlukDosyasi
{
    public static void Yukle()
    {
        try
        {
            var path = Path.Combine(AppPaths.Dir, "data", "packs", "gta6.json");
            if (File.Exists(path))
            {
                SozlukKutuphanesi.Gta6Json = File.ReadAllText(path);
                return;
            }
            using var stream = typeof(SozlukDosyasi).Assembly.GetManifestResourceStream("AltyaziCevirici.data.packs.gta6.json");
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            SozlukKutuphanesi.Gta6Json = reader.ReadToEnd();
        }
        catch (Exception ex) { ErrorLog.Write(ex, "gta-pack"); }
    }
}
