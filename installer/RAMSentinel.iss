#define MyAppName "RAM Sentinel"
#define MyAppPublisher "IS-IVR"
#define MyAppExeName "RamSentinel.exe"

#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif

#ifndef BuildDirectory
  #define BuildDirectory "..\\artifacts\\win-x64"
#endif

[Setup]
AppId={{B0AC2F28-7F67-4D88-9A38-A7EDB86A51A2}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\\Programs\\RAM Sentinel
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\\{#MyAppExeName}
OutputBaseFilename=RAM-Sentinel-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
CloseApplications=no
RestartIfNeededByRun=no

[Languages]
Name: "hebrew"; MessagesFile: "compiler:Languages\\Hebrew.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BuildDirectory}\\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\{MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
