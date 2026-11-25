#define MyAppName "RAM Sentinel"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{A7E41AC4-9D1B-47A2-8B13-B6B349A0C90A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=IS-IVR
AppPublisherURL=https://github.com/IS-IVR/ram-sentinel
DefaultDirName={localappdata}\Programs\RAM Sentinel
DefaultGroupName=RAM Sentinel
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName=RAM Sentinel
UninstallDisplayIcon={app}\RamSentinel.exe
OutputBaseFilename=RAM-Sentinel-Setup-{#MyAppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\RAM Sentinel"; Filename: "{app}\RamSentinel.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\RAM Sentinel"; Filename: "{app}\RamSentinel.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\RamSentinel.exe"; Description: "Launch RAM Sentinel"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent unchecked
