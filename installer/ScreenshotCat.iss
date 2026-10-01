#ifndef AppVersion
  #error AppVersion must be provided by Build-ScreenshotCat.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be provided by Build-ScreenshotCat.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be provided by Build-ScreenshotCat.ps1
#endif
#ifndef OutputBaseFilename
  #error OutputBaseFilename must be provided by Build-ScreenshotCat.ps1
#endif

[Setup]
AppId={{E6B25591-1727-4AC6-BA8D-94C8B6A40D32}
AppName=ScreenshotCat
AppVersion={#AppVersion}
AppVerName=ScreenshotCat {#AppVersion}
AppPublisher=nekobyran
AppPublisherURL=https://kacha.nkbr.cc/
AppSupportURL=https://github.com/nekobyran/kacha/issues
AppUpdatesURL=https://kacha.nkbr.cc/
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\ScreenshotCat
DefaultGroupName=ScreenshotCat
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\ScreenshotCat\Assets\AppIcon.ico
UninstallDisplayIcon={app}\ScreenshotCat.exe
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenshotCat"; ValueData: """{app}\ScreenshotCat.exe"" --startup"; Flags: uninsdeletevalue

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ScreenshotCat"; Filename: "{app}\ScreenshotCat.exe"
Name: "{autodesktop}\ScreenshotCat"; Filename: "{app}\ScreenshotCat.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ScreenshotCat.exe"; Description: "{cm:LaunchProgram,ScreenshotCat}"; Flags: nowait postinstall skipifsilent
