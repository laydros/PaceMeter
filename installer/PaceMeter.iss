; Inno Setup script for PaceMeter. Per-user install, no admin rights needed.
; Build with tools/Build-Release.ps1, which publishes the exe first.

#define AppName "PaceMeter"
#define AppExe "PaceMeter.exe"
#define SourceExe "..\src\PaceMeter\bin\Release\net9.0-windows\win-x64\publish\PaceMeter.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

#if !FileExists(SourceExe)
  #error Published exe not found. Run: dotnet publish src/PaceMeter -c Release -r win-x64
#endif

; File version is major.minor.patch.0; drop the trailing .0.
#define FullVersion GetVersionNumbersString(SourceExe)
#define AppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)

[Setup]
; AppId identifies the install for upgrades and uninstall. Never change it.
AppId={{C2030C79-C288-47D3-9987-C97F0FA6FD42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=laydros
AppPublisherURL=https://github.com/laydros/PaceMeter
AppSupportURL=https://github.com/laydros/PaceMeter/issues
AppUpdatesURL=https://github.com/laydros/PaceMeter/releases
VersionInfoVersion={#FullVersion}
PrivilegesRequired=lowest
; With lowest privileges {autopf} is %LOCALAPPDATA%\Programs.
DefaultDirName={autopf}\{#AppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\PaceMeter\PaceMeter.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts
OutputBaseFilename=PaceMeter-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in to Windows"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Same value the app's "Start with Windows" menu item writes, so the two stay in sync.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// PaceMeter is a tray app with no main window, so Restart Manager can't close it cleanly.
// Stop it directly before files are replaced or removed.
procedure StopPaceMeter();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopPaceMeter();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Unchecking the task on an upgrade should also turn off an existing startup entry.
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKCU, '{#RunKey}', '{#AppName}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopPaceMeter();
    // Also covers the case where startup was turned on from the app's menu rather than the installer.
    RegDeleteValue(HKCU, '{#RunKey}', '{#AppName}');
  end;
end;
