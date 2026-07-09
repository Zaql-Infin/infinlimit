#define MyAppName      "InfinLimit"
#define MyAppVersion   "5.0.0"
#define MyAppPublisher "InfinLimit"
#define MyAppExeName   "InfinLimit.exe"
#define MyAppURL       "https://github.com/Zaql-Infin/infinlimit"

[Setup]
AppId={{F3C2A1B0-9E8D-4F7C-B6A5-123456789ABC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\installer-output
OutputBaseFilename=InfinLimitSetup
SetupIconFile=..\Windows\Assets\appLogo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checked

[Files]
; Main executable
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Npcap — only extracted/run if Npcap is not already installed
Source: "npcap-installer.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NpcapNotInstalled

[Icons]
Name: "{group}\{#MyAppName}";                        Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}";  Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}";                Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Install Npcap silently in WinPcap compatibility mode (required for ARP spoofing)
Filename: "{tmp}\npcap-installer.exe"; \
  Parameters: "/S /winpcap_mode=yes"; \
  StatusMsg: "Installing Npcap (required for Console Limiter)..."; \
  Check: NpcapNotInstalled; \
  Flags: waituntilterminated

; Offer to launch the app after install
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill the running process before uninstalling so the exe isn't locked
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
function NpcapNotInstalled: Boolean;
var
  dummy: String;
begin
  Result := not (
    FileExists('C:\Windows\System32\Npcap\wpcap.dll') or
    RegQueryStringValue(HKLM, 'SOFTWARE\Npcap', '', dummy) or
    RegQueryStringValue(HKLM64, 'SOFTWARE\Npcap', '', dummy)
  );
end;
