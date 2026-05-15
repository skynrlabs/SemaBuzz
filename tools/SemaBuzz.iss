; ─────────────────────────────────────────────────────────────────────────────
; SemaBuzz Inno Setup script
; Build:  iscc tools\SemaBuzz.iss  (from repo root)
; Output: dist\SemaBuzz-Setup-x64.exe
; ─────────────────────────────────────────────────────────────────────────────

#define AppName      "SemaBuzz"
#define AppVersion   "1.0.0"
#define AppPublisher "Skynr Labs"
#define AppURL       "https://semabuzz.me"
#define AppExeName   "SemaBuzz.App.exe"
#define PublishDir   "..\dist\publish"
#define OutputDir    "..\dist"

[Setup]
AppId={{B3A2F1C0-4E8D-4A2B-9C6F-1D0E5F7A3B8C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL=https://github.com/skynrlabs/SemaBuzz/issues
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=SemaBuzz-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Main publish output — everything in dist\publish\
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";          Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}";     Filename: "{app}\{#AppExeName}"; Comment: "Start with Windows (optional — remove if unwanted)"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up settings left in %APPDATA% only if the user explicitly chose to
; (done via a separate "wipe settings" checkbox — left for a future release)
