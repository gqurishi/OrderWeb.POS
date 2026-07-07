; OrderWeb POS — Mother terminal update
; Flow: replace app files → backup → migrate → verify → check-version

#include "shared\Version.iss.inc"

[Setup]
AppId={{A7B4E2F1-9C3D-4E5F-8A1B-2D6C9E0F4A8B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=OrderWebPOS-Update-Mother-{#MyAppVersion}
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
    AppendSetupLog('=== OrderWeb POS Mother Update Started ===');

    if not EnsureExistingMariaDbReady() then
      Abort;

    if not RunMotherUpdateDatabase() then
      Abort;

    AppendSetupLog('=== OrderWeb POS Mother Update Completed ===');
    MsgBox('Mother terminal updated successfully. Database backup, migrate, and verify completed.', mbInformation, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not ConfigExists() then
  begin
    MsgBox(
      'This update installer expects an existing mother installation.' + #13#10 +
      'Missing: ' + GetProductionConfigPath() + #13#10#13#10 +
      'Use OrderWebPOS-Mother.iss for first-time setup.',
      mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  AppendSetupLog('=== Preparing mother update. Existing config: ' + GetProductionConfigPath() + ' ===');
end;
