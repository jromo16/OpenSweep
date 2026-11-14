; Instalador de OpenSweep para Windows (Inno Setup 6)
; Compilar:  iscc /DAppVersion=1.1.0 /DSourceExe=..\dist\windows\OpenSweep.exe OpenSweep.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\dist\windows\OpenSweep.exe"
#endif

[Setup]
AppId={{6F3C2A1E-8B5D-4E7A-9C21-0A5B7E4D3F10}
AppName=OpenSweep
AppVersion={#AppVersion}
AppVerName=OpenSweep {#AppVersion}
AppPublisher=OpenSweep
AppPublisherURL=https://github.com/jromo16/OpenSweep
AppSupportURL=https://github.com/jromo16/OpenSweep/issues
AppUpdatesURL=https://github.com/jromo16/OpenSweep/releases
DefaultDirName={autopf}\OpenSweep
DefaultGroupName=OpenSweep
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=OpenSweep-Setup
SetupIconFile=..\windows\OpenSweep\app.ico
UninstallDisplayIcon={app}\OpenSweep.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "OpenSweep.exe"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\OpenSweep"; Filename: "{app}\OpenSweep.exe"
Name: "{group}\{cm:UninstallProgram,OpenSweep}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\OpenSweep"; Filename: "{app}\OpenSweep.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OpenSweep.exe"; Description: "{cm:LaunchProgram,OpenSweep}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Quitar las tareas programadas (limpieza automática y monitor de la bandeja)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-ScheduledTask -TaskPath '\OpenSweep\' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false"""; Flags: runhidden; RunOnceId: "RemoveTasks"
Filename: "taskkill.exe"; Parameters: "/F /IM OpenSweep.exe"; Flags: runhidden; RunOnceId: "KillTray"
