using AltyaziCevirici.Core;

namespace AltyaziCevirici;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--screenshots")
            Environment.SetEnvironmentVariable("CEVIRMEN_SHOTS", Path.GetFullPath(args[1]));
        if (args.Length >= 2 && args[0] == "--sozluk-shots")
            Environment.SetEnvironmentVariable("SOZLUK_SHOTS", Path.GetFullPath(args[1]));
        if (args.Length >= 2 && args[0] == "--goal-shots")
            Environment.SetEnvironmentVariable("GOAL_SHOTS", Path.GetFullPath(args[1]));
        if (float.TryParse(Environment.GetEnvironmentVariable("SOZLUK_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float scale))
            AltyaziCevirici.Ui.Theme.ForcedScale = scale;

        AppPaths.Init();
        ErrorLog.Path = AppPaths.ErrorLogFile;
        SozlukDosyasi.Yukle();

        // Last-resort handlers: log and keep running wherever the runtime allows it.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorLog.Write(e.Exception, "ui");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ErrorLog.Write(ex, e.IsTerminating ? "fatal" : "domain");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write(e.Exception, "task");
            e.SetObserved();
        };

        using var mutex = new Mutex(true, "AltyaziCevirici_TekOrnek", out bool first);
        if (!first)
        {
            MessageBox.Show(AppInfo.DisplayName + " zaten çalışıyor (saat yanındaki simgeye bak).", AppInfo.DisplayName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        _ = Native.TimeBeginPeriod(1); // precise ~66 ms frame timing
        try
        {
            Application.Run(new TrayApp());
        }
        finally
        {
            _ = Native.TimeEndPeriod(1);
        }
    }
}

internal static class AppPaths
{
    public static string Dir { get; private set; } = AppContext.BaseDirectory;
    public static string SettingsFile => Path.Combine(Dir, "ayarlar.json");
    public static string ErrorLogFile => Path.Combine(Dir, "hata.log");

    /// <summary>
    /// Prefer the executable directory so a portable copy and the per-user install
    /// (<c>%LocalAppData%\Programs\AltyaziCevirici</c>, which the user can write) keep
    /// ayarlar.json and hata.log beside the exe. For a single-file publish,
    /// <see cref="AppContext.BaseDirectory"/> is that directory, not the native-library
    /// extract folder. Program Files is not writable, so both files move to
    /// <c>%AppData%\AltyaziCevirici</c>.
    /// </summary>
    public static void Init()
    {
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, ".yazma_testi");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            Dir = AppContext.BaseDirectory;
        }
        catch (Exception)
        {
            Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AltyaziCevirici");
            try { Directory.CreateDirectory(Dir); } catch (Exception) { /* ignore */ }
        }
    }
}
