; OrderWeb POS — Child terminal update
; Flow: replace app files only → version check if orderweb-database.json exists

#include "shared\Version.iss.inc"

[Setup]
AppId={{B8C5F3A2-0D4E-5F6A-9B2C-3E7D0A1F5B9C}
AppName={#MyAppName} (Child)
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=OrderWebPOS-Update-Child-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishRoot}\{#DatabaseSetupExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
#include "shared\DatabaseSetup.iss.inc"

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    AppendSetupLog('=== OrderWeb POS Child Update Started ===');

    if not ChildConfigCheckIfPresent() then
      Abort;

    AppendSetupLog('=== OrderWeb POS Child Update Completed ===');

    if ConfigExists() then
      MsgBox('Child terminal updated. Database schema version is compatible with this app.', mbInformation, MB_OK)
    else
      MsgBox(
        'Child terminal updated.' + #13#10#13#10 +
        'No orderweb-database.json was found. Copy it from the mother PC or complete Terminal Setup in the app.',
        mbInformation, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  AppendSetupLog('=== Preparing child update. Config path: ' + GetProductionConfigPath() + ' ===');
end;
