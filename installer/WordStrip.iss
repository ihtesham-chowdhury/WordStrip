; Inno Setup script for WordStrip.
; Build with:  iscc installer\WordStrip.iss
; Expects the self-contained publish output in publish\portable\.

#define AppName        "WordStrip"
#define AppVersion     "0.10.0"
#define AppPublisher   "WordStrip"
#define AppExeName     "WordStrip.exe"

[Setup]
AppId={{7C4B9E52-1A3D-4F6B-9E2C-8D5A0B3F71C4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
OutputDir=..\publish
OutputBaseFilename=WordStrip-Setup-{#AppVersion}
SetupIconFile=..\assets\wordstrip.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Per-user install into LocalAppData: no UAC prompt at all, which matters when handing this to people who
; are doing you a favour by testing it. It is also the correct scope for this app - autostart and settings
; both live under HKCU, and a keyboard hook installed by an elevated process cannot see input going to
; non-elevated windows, so WordStrip must run unelevated regardless.
PrivilegesRequired=lowest

; Windows 10 1809 or newer: the DWM backdrop APIs the glass uses simply no-op below this, and the app
; has not been tested on older builds.
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startupicon"; Description: "Start {#AppName} automatically when Windows starts"; GroupDescription: "Startup"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts"; Flags: unchecked

[Files]
; One file: the dictionary is embedded in the executable.
Source: "..\publish\portable\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} Settings"; Filename: "{app}\{#AppExeName}"; Parameters: "--settings"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Per-user autostart. Written under HKCU so it follows the installing user and needs no elevation to change
; later from the app's own settings window.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "WordStrip"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the running copy before removing files, otherwise the exe is locked and left behind.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExeName} /F"; Flags: runhidden; RunOnceId: "StopWordStrip"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\WordStrip"








