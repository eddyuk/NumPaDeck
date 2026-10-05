; NumPaDeck — Inno Setup script (per-user install, no UAC)
; Build with a version injected from the CI pipeline:
;   ISCC /DMyAppVersion=1.0.0 installer\NumPaDeck.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "NumPaDeck"
#define MyAppPublisher "eddyuk"
#define MyAppUrl "https://github.com/eddyuk/NumPaDeck"
; Fixed per-app GUID so upgrades replace the previous install cleanly.
#define MyAppId "7125D052-D3AE-481E-BA64-728DEF9C5984"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
VersionInfoVersion={#MyAppVersion}.0
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=NumPaDeck-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\NumPaDeck.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start {#MyAppName} automatically when you sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\NumPaDeck.exe"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; Opt-in: only written when the "autostart" task is checked, and removed
; automatically on uninstall (value only -- never the whole Run key).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\NumPaDeck.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\NumPaDeck.exe"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
