#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{ABF21114-F363-45EC-8B68-5A33B9AA76D1}
AppName=Ducky
AppVersion={#AppVersion}
AppPublisher=deticek
AppPublisherURL=https://github.com/deticek/Ducky
AppSupportURL=https://github.com/deticek/Ducky
AppUpdatesURL=https://github.com/deticek/Ducky
DefaultDirName={localappdata}\Programs\Ducky
DefaultGroupName=Ducky
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=artifacts\release
OutputBaseFilename=Ducky-installer
SetupIconFile=Assets\logo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
UninstallDisplayIcon={app}\Ducky.Bootstrapper.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "artifacts\release\win-x64\App\*"; DestDir: "{app}\Current"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "artifacts\release\win-x64\Bootstrapper\Ducky.Bootstrapper.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Ducky"; Filename: "{app}\Ducky.Bootstrapper.exe"; Parameters: "--launch"; WorkingDir: "{app}\Current"
Name: "{autodesktop}\Ducky"; Filename: "{app}\Ducky.Bootstrapper.exe"; Parameters: "--launch"; WorkingDir: "{app}\Current"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\Ducky.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\Ducky.Bootstrapper.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\Ducky.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\Ducky"; ValueType: string; ValueName: ""; ValueData: "Open here with Ducky"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\Ducky"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Ducky.Bootstrapper.exe"
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\Ducky\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Ducky.Bootstrapper.exe"" --launch --working-directory ""%V"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Ducky"; ValueType: string; ValueName: ""; ValueData: "Open here with Ducky"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Ducky"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Ducky.Bootstrapper.exe"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Ducky\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Ducky.Bootstrapper.exe"" --launch --working-directory ""%1"""

[Run]
Filename: "{app}\Ducky.Bootstrapper.exe"; Parameters: "--launch"; WorkingDir: "{app}\Current"; Description: "Launch Ducky"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Current"
Type: filesandordirs; Name: "{app}\Versions"
Type: filesandordirs; Name: "{app}\Update"
