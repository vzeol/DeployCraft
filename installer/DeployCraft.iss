; Installeur DeployCraft : installe le service (démarrage automatique) et la console Windows.
; Rien d'autre à installer : .NET est embarqué.
;
; Compilation : build.ps1 à la racine du dépôt.

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
; Console ouverte : fermée proprement avant de remplacer les fichiers (demandé en mode interactif,
; automatique en /VERYSILENT), et pas relancée par l'installeur.
CloseApplications=yes
RestartApplications=no
; Seulement la console : le service est arrêté (et, en cas d'échec, redémarré) par le code plus bas.
CloseApplicationsFilter=DeployCraft.exe

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[InstallDelete]
; Interface web de la v0.1, remplacée par la console Windows.
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
; Service sous LocalSystem : administrateur du serveur. Les accès aux postes utilisent le compte de déploiement.
; Création à la première installation ; lors d'une mise à jour, il existe déjà et « config » le remet d'aplomb.
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\DeployCraft.Server.exe\"""" start= delayed-auto DisplayName= ""DeployCraft"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "config {#ServiceName} binPath= ""\""{app}\DeployCraft.Server.exe\"""" start= delayed-auto DisplayName= ""DeployCraft"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Serveur de déploiement de logiciels DeployCraft"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden
Filename: "{app}\DeployCraft.exe"; Description: "Ouvrir DeployCraft"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\timeout.exe"; Parameters: "/t 3 /nobreak"; Flags: runhidden; RunOnceId: "WaitService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
// Mise à jour : le service n'est plus supprimé. Il est seulement arrêté, juste avant la copie des fichiers (après la
// fermeture de la console), puis reconfiguré et redémarré par [Run]. Si l'installation échoue ou est annulée
// après l'arrêt, il est redémarré tel quel : le serveur n'est jamais laissé sans service.
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
    // Le temps que le service libère DeployCraft.Server.exe.
    Sleep(3000);
  end;
  if CurStep = ssPostInstall then InstallDone := True;
end;

procedure DeinitializeSetup();
var
  ResultCode: Integer;
begin
  if ServiceStopped and not InstallDone then
    Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
