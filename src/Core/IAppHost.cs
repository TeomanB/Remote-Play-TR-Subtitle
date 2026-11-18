namespace AltyaziCevirici.Core;

public interface IAppHost
{
    AppSettings Current { get; }
    bool IsRunning { get; }
    bool IsPaused { get; }
    string SonMetin { get; }
    int SonGecikmeMs { get; }
    string OcrDurum { get; }
    bool KurulumKopyasi { get; }
    bool OcrPaketiEksik { get; }
    bool YenidenYukleOcr();
    event EventHandler? StateChanged;
    void Apply(AppSettings edited, bool persist);
    void SelectRegion();
    void TogglePause();
    void StartCapture();
    void StopCapture();
    void ShowPreview();
    void BeginPlacementEdit();
    void ShowMain();
    void NotifyMinimizedToTray();
    void SkipRelease(string tag);
    void Quit();
    string UpdateBanner { get; }
    void CheckUpdates();
    void BeginUpdateInstall();
    void SkipUpdateOffer();
    void InstallOcrPack();
}
