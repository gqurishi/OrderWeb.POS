; OrderWeb POS — Child terminal fresh install
; Flow: app only → optional copy of orderweb-database.json → version check if config present → pairing in app

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
OutputBaseFilename=OrderWebPOS-Child-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishRoot}\{#DatabaseSetupExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} and complete child pairing"; Flags: nowait postinstall skipifsilent

[Code]
#include "shared\DatabaseSetup.iss.inc"

var
  ChildConfigPage: TInputFileWizardPage;

procedure InitializeWizard;
begin
  ChildConfigPage := CreateInputFilePage(wpSelectDir,
    'Database Credentials (Optional)', 'Copy orderweb-database.json from the mother terminal',
    'Child terminals use the same orderweb_app credentials as the mother terminal.' + #13#10#13#10 +
    'If you already have orderweb-database.json from the mother PC, select it now.' + #13#10 +
    'Otherwise leave this blank and complete Terminal Setup + pairing in the app.');
  ChildConfigPage.Add('Path to orderweb-database.json:', 'JSON files|*.json|All files|*.*', '.json');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  SelectedConfig: String;
begin
  if CurStep = ssPostInstall then
  begin
    AppendSetupLog('=== OrderWeb POS Child Install Started ===');

    SelectedConfig := ChildConfigPage.Values[0];
    if not CopyOptionalChildDatabaseConfig(SelectedConfig) then
      Abort;

    if not ChildConfigCheckIfPresent() then
      Abort;

    AppendSetupLog('=== OrderWeb POS Child Install Completed ===');
    MsgBox(
      'Child terminal installed.' + #13#10#13#10 +
      'Next steps in the app:' + #13#10 +
      '1. Choose Child terminal mode' + #13#10 +
      '2. Enter the mother terminal IP address' + #13#10 +
      '3. Enter the pairing code from Terminal Health on the mother PC' + #13#10#13#10 +
      'The app will verify the mother database schema version on connect.',
      mbInformation, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not FileExists(ExpandConstant('{src}\{#PublishRoot}\{#MyAppExeName}')) then
  begin
    MsgBox(
      'Build inputs are missing. Run Installer\build-installer-inputs.ps1 first.',
      mbError, MB_OK);
    Result := False;
  end;
end;
