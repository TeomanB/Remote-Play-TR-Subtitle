#define MyAppName "Altyazı Çevirici"
#ifndef MyAppVersion
#define MyAppVersion "1.4.0"
#endif
#define MyAppVersionInfo MyAppVersion + ".0"
#define MyAppPublisher "Teo"
#define MyAppExeName "AltyaziCevirici.exe"

[Setup]
AppId={{8F4C2E1A-6B7D-4C9E-A1F3-5D8E0B2C7A64}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersionInfo}
VersionInfoProductVersion={#MyAppVersionInfo}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Ekran altyazısını Türkçeye çeviren tepsi uygulaması
DefaultDirName={localappdata}\Programs\AltyaziCevirici
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install. The default folder is writable, so ayarlar.json stays beside the exe.
; Copying the exe into Program Files still works: the app falls back to %AppData%\AltyaziCevirici.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
UsePreviousTasks=no
UsedUserAreasWarning=no
OutputDir=..\artifacts
OutputBaseFilename=AltyaziCevirici-Setup-{#MyAppVersion}
SetupIconFile=..\src\App\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex=AltyaziCevirici_TekOrnek
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter={#MyAppExeName}
InfoBeforeFile=onbilgi.txt
ShowLanguageDialog=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Kısayollar:"
Name: "ocrpack"; Description: "İngilizce yazı tanıma paketini şimdi kur (isteğe bağlı, yalnızca bu kutu işaretliyse yönetici izni ister)"; GroupDescription: "Metin tanıma:"; Flags: unchecked

[Files]
Source: "..\publish\AltyaziCevirici.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\data\packs\gta6.json"; DestDir: "{app}\data\packs"; Flags: ignoreversion
Source: "..\KULLANIM.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Checkbox on the last page. Leaving it clear does not fail the install.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; Silent update (/SILENT or /VERYSILENT) has no checkbox, so start the new build after the files are replaced.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Params: String;
begin
  if CurStep <> ssPostInstall then
    Exit;
  if not WizardIsTaskSelected('ocrpack') then
    Exit;

  // Optional. A declined UAC prompt or a failed capability command must not roll back Setup.
  Params := '-NoProfile -ExecutionPolicy Bypass -Command "Add-WindowsCapability -Online -Name ''Language.OCR~~~en-US~0.0.1.0''"';
  if not ShellExec('runas', ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
       Params, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('İngilizce yazı tanıma paketi yüklenmedi (yönetici izni verilmedi). Kurulum yine de tamamlandı. Programı açınca Kur ile yeniden deneyebilirsin.',
      mbInformation, MB_OK);
  end
  else if ResultCode <> 0 then
  begin
    MsgBox('İngilizce yazı tanıma paketi kurulamadı. Kurulum yine de tamamlandı. Programı açınca Kur ile yeniden deneyebilirsin.',
      mbInformation, MB_OK);
  end;
end;
