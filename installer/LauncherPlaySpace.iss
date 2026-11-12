#ifndef PayloadDir
  #error PayloadDir must point to the prepared application directory
#endif
#ifndef BuildOutput
  #error BuildOutput must point to the installer output directory
#endif
#define AppExe "LauncherPlaySpace.exe"
#define AppVersion GetVersionNumbersString(AddBackslash(PayloadDir) + AppExe)

[Setup]
#ifdef SmokeTest
AppId=LauncherPlaySpace.LocalSmoke
#else
AppId={{9DB01324-66DD-4DA3-B7B0-042139280B20}
#endif
AppName=LauncherPlaySpace
AppVersion={#AppVersion}
#ifdef ReleaseBuild
AppVerName=LauncherPlaySpace {#AppVersion}
#else
AppVerName=LauncherPlaySpace {#AppVersion} (local preview)
#endif
AppPublisher=d3a0g
AppPublisherURL=https://github.com/d3a0g/LauncherPlaySpace
AppSupportURL=https://t.me/PlaySpaceSupport_bot
DefaultDirName={localappdata}\Programs\LauncherPlaySpace
#ifdef SmokeTest
DefaultGroupName=LauncherPlaySpace Local Smoke
#else
DefaultGroupName=LauncherPlaySpace
#endif
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\GameLauncherGUI\Assets\playspace.ico
UninstallDisplayIcon={app}\{#AppExe}
#ifndef SmokeTest
AppMutex=Local\LauncherPlaySpace.Launcher
#endif
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter=LauncherPlaySpace.exe
Compression=lzma2
SolidCompression=yes
OutputDir={#BuildOutput}
#ifdef InstallerName
OutputBaseFilename={#InstallerName}
#else
OutputBaseFilename=LauncherPlaySpace-Setup-preview
#endif
LicenseFile=..\LICENSE
InfoBeforeFile=INSTALL-NOTICE.txt
Uninstallable=yes
UninstallLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut / Создать ярлык на рабочем столе"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\START-HERE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\update-source.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\LauncherPlaySpace"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\LauncherPlaySpace - Safe Mode"; Filename: "{app}\{#AppExe}"; Parameters: "--safe-mode"; WorkingDir: "{app}"
Name: "{autodesktop}\LauncherPlaySpace"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch LauncherPlaySpace / Запустить лаунчер"; Flags: nowait postinstall skipifsilent unchecked

[Code]
procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  ProgressFile: String;
begin
  ProgressFile := ExpandConstant('{param:PLAYSPACEPROGRESS|}');
  if (ProgressFile <> '') and (MaxProgress > 0) then
    SaveStringToFile(ProgressFile, IntToStr(Round(100.0 * CurProgress / MaxProgress)), False);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion, NewVersion: Int64;
begin
  Result := '';
  if GetPackedVersion(ExpandConstant('{app}\LauncherPlaySpace.exe'), InstalledVersion) and
     StrToVersion('{#AppVersion}', NewVersion) then
  begin
    if ComparePackedVersion(InstalledVersion, NewVersion) > 0 then
      Result := 'A newer version is installed. Downgrade is not supported. / Уже установлена более новая версия. Понижение версии запрещено.';
  end;
end;

// No UninstallDelete rules: user data and games must never be removed.
