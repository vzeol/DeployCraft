; DeployCraft installer: installs the service (automatic start) and the Windows console.
; Nothing else to install: .NET is embedded.
;
; Build: build.ps1 at the repository root.

#define AppName "DeployCraft"
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#define ServiceName "DeployCraft"

[Setup]
AppId={{6F1C2B7E-4D0A-4C61-9E3B-DEC0DE0A0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
AppPublisherURL=https://github.com/vzeol/DeployCraft
AppCopyright=Copyright (C) 2026 vzeol - GPL v3
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=DeployCraft-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#AppName}
; Console open: closed cleanly before the files are replaced (asked in interactive mode,
; automatic with /VERYSILENT), and not relaunched by the installer.
CloseApplications=yes
RestartApplications=no
; Console only: the service is stopped (and, on failure, restarted) by the code below.
CloseApplicationsFilter=DeployCraft.exe

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[InstallDelete]
; Web interface of v0.1, replaced by the Windows console.
Type: filesandordirs; Name: "{app}\wwwroot"
Type: files; Name: "{app}\DeployCraft.url"
Type: files; Name: "{group}\DeployCraft.url"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DeployCraft"; Filename: "{app}\DeployCraft.exe"
Name: "{autodesktop}\DeployCraft"; Filename: "{app}\DeployCraft.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"

[Run]
; Service under LocalSystem: server administrator. Access to computers uses the deployment account.
; Created at first installation; on an update it already exists and "config" puts it back in order.
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\DeployCraft.Server.exe\"""" start= delayed-auto DisplayName= ""DeployCraft"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "config {#ServiceName} binPath= ""\""{app}\DeployCraft.Server.exe\"""" start= delayed-auto DisplayName= ""DeployCraft"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Serveur de déploiement de logiciels DeployCraft"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden
Filename: "{app}\DeployCraft.exe"; Description: "Ouvrir DeployCraft"; Flags: postinstall nowait skipifsilent runasoriginaluser
; Update launched from the console (/RELAUNCH=1): the console, closed by the installer, is reopened.
Filename: "{app}\DeployCraft.exe"; Flags: nowait runasoriginaluser; Check: RelaunchAsked

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\timeout.exe"; Parameters: "/t 3 /nobreak"; Flags: runhidden; RunOnceId: "WaitService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
// Update: the service is no longer deleted. It is only stopped, just before the files are copied (after the
// console is closed), then reconfigured and restarted by [Run]. If the installation fails or is cancelled
// after the stop, it is restarted as is: the server is never left without a service.
var
  ServiceStopped: Boolean;
  InstallDone: Boolean;

function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssInstall) and ServiceExists() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    ServiceStopped := True;
    // Time for the service to release DeployCraft.Server.exe.
    Sleep(3000);
  end;
  if CurStep = ssPostInstall then InstallDone := True;
end;

function RelaunchAsked(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

procedure DeinitializeSetup();
var
  ResultCode: Integer;
begin
  if ServiceStopped and not InstallDone then
    Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
