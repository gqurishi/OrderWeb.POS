; OrderWeb POS unified setup
; One installer for both Mother and Child terminals.
;
; Mother:
;   - Installs the app
;   - Installs/starts MariaDB when needed
;   - Creates orderweb_pos + orderweb_app
;   - Runs migrations, verify, and schema version check
;
; Child:
;   - Installs the app
;   - Optionally copies orderweb-database.json for DB credentials
;   - Pairing/sync is completed in the app with Mother IP + pairing code

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
OutputBaseFilename=OrderWebPOS-Setup-{#MyAppVersion}
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

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Prerequisites\MariaDB\*"; DestDir: "{tmp}\MariaDB"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsMotherInstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
#include "shared\DatabaseSetup.iss.inc"

var
  TerminalModePage: TInputOptionWizardPage;
  MariaDbRootPasswordPage: TInputQueryWizardPage;
  LanSubnetPage: TInputQueryWizardPage;
  ChildConfigPage: TInputFileWizardPage;

function IsMotherInstall(): Boolean;
begin
  Result := TerminalModePage.Values[0];
end;

function IsFreshMotherInstall(): Boolean;
begin
  Result := IsMotherInstall() and (not ConfigExists());
end;

function IsChildInstall(): Boolean;
begin
  Result := not IsMotherInstall();
end;

procedure InitializeWizard;
begin
  TerminalModePage := CreateInputOptionPage(wpSelectDir,
    'Terminal Type', 'Choose how this PC will be used',
    'Use Mother for the main POS/database PC. Use Child for extra terminals that connect to the Mother over LAN.',
    True, False);
  TerminalModePage.Add('Mother terminal - main POS, local MariaDB database, migrations, backups, reports, sync master');
  TerminalModePage.Add('Child terminal - extra POS station, connects to the Mother terminal database');
  TerminalModePage.Values[0] := True;

  MariaDbRootPasswordPage := CreateInputQueryPage(TerminalModePage.ID,
    'MariaDB Admin Password', 'Required for first-time Mother setup',
    'If MariaDB is already installed, enter its existing root/admin password.' + #13#10 +
    'If MariaDB is missing, this password will be used for the bundled MariaDB install.' + #13#10 +
    'OrderWeb.DatabaseSetup.exe will create orderweb_pos and orderweb_app automatically.');
  MariaDbRootPasswordPage.Add('MariaDB root password:', True);
  MariaDbRootPasswordPage.Values[0] := '';

  LanSubnetPage := CreateInputQueryPage(MariaDbRootPasswordPage.ID,
    'Child Terminal Network Access', 'LAN access for child terminals',
    'Child terminals connect from your restaurant LAN. Keep the default unless your subnet differs.');
  LanSubnetPage.Add('LAN subnet pattern:', False);
  LanSubnetPage.Values[0] := '192.168.%';

  ChildConfigPage := CreateInputFilePage(LanSubnetPage.ID,
    'Database Credentials (Optional)', 'Use credentials from the Mother terminal',
    'If you have orderweb-database.json from the Mother PC, select it here.' + #13#10 +
    'Otherwise leave this blank. The app will ask for Mother IP and pairing code during Terminal Setup.');
  ChildConfigPage.Add('Path to orderweb-database.json:', 'JSON files|*.json|All files|*.*', '.json');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if ((PageID = MariaDbRootPasswordPage.ID) or (PageID = LanSubnetPage.ID)) and (not IsFreshMotherInstall()) then
    Result := True;

  if (PageID = ChildConfigPage.ID) and (not IsChildInstall()) then
    Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = MariaDbRootPasswordPage.ID then
  begin
    if IsFreshMotherInstall() and (Trim(MariaDbRootPasswordPage.Values[0]) = '') then
    begin
      MsgBox('MariaDB root password is required for the first Mother install.', mbError, MB_OK);
      Result := False;
    end;
    if IsFreshMotherInstall() and (not IsMariaDbInstalled()) and (Length(MariaDbRootPasswordPage.Values[0]) < 8) then
    begin
      MsgBox('For a new bundled MariaDB install, use a root password with at least 8 characters.', mbError, MB_OK);
      Result := False;
    end;
    if Pos('"', MariaDbRootPasswordPage.Values[0]) > 0 then
    begin
      MsgBox('MariaDB root password cannot contain a double quote character for silent installation.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function RunFreshMotherInstall(): Boolean;
begin
  Result := False;

  if not EnsureMariaDbReady(MariaDbRootPasswordPage.Values[0]) then
    Exit;

  if not RunMotherInstallDatabase(
       MariaDbRootPasswordPage.Values[0],
       LanSubnetPage.Values[0]) then
    Exit;

  if not RunVerifyDatabase() then
    Exit;

  if not RunCheckVersionDatabase() then
    Exit;

  Result := True;
end;

function RunExistingMotherUpdate(): Boolean;
begin
  Result := False;

  if not EnsureExistingMariaDbReady() then
    Exit;

  if not RunMotherUpdateDatabase() then
    Exit;

  Result := True;
end;

function WarnChildVersionIfPresent(): Boolean;
var
  ResultCode: Integer;
  Output: String;
begin
  Result := True;

  if not ConfigExists() then
  begin
    AppendSetupLog('[Child] No orderweb-database.json yet. Pairing will happen in the app.');
    Exit;
  end;

  ResultCode := RunDatabaseSetup('check-version', '--quiet', Output);
  if ResultCode <> 0 then
  begin
    AppendSetupLog('[Child] Version check warning: ' + Output);
    MsgBox(
      'Child terminal installed, but the installer could not verify the Mother database yet.' + #13#10#13#10 +
      Output + #13#10#13#10 +
      'Open the app, choose Child terminal, enter the Mother IP address, and complete pairing with the code from the Mother terminal.',
      mbInformation, MB_OK);
  end;
end;

function RunChildInstall(): Boolean;
begin
  Result := False;

  if not CopyOptionalChildDatabaseConfig(ChildConfigPage.Values[0]) then
    Exit;

  if not WarnChildVersionIfPresent() then
    Exit;

  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    AppendSetupLog('=== OrderWeb POS Unified Setup Started ===');

    if IsMotherInstall() then
    begin
      if ConfigExists() then
      begin
        AppendSetupLog('[Mode] Mother update/reinstall');
        if not RunExistingMotherUpdate() then
          Abort;
        MsgBox('Mother terminal installed/updated. Database backup, migrate, verify, and version check completed.', mbInformation, MB_OK);
      end
      else
      begin
        AppendSetupLog('[Mode] Fresh Mother install');
        if not RunFreshMotherInstall() then
          Abort;
        MsgBox(
          'Mother terminal installed.' + #13#10#13#10 +
          'Next: open the app, choose Mother Terminal in Terminal Setup, then create the first admin user.' + #13#10#13#10 +
          'For child terminals, create a pairing code from Terminal Health on this Mother PC.',
          mbInformation, MB_OK);
      end;
    end
    else
    begin
      AppendSetupLog('[Mode] Child install/update');
      if not RunChildInstall() then
        Abort;
      MsgBox(
        'Child terminal installed.' + #13#10#13#10 +
        'Next steps in the app:' + #13#10 +
        '1. Choose Child Terminal' + #13#10 +
        '2. Enter the Mother terminal IP address' + #13#10 +
        '3. Enter the pairing code created from Terminal Health on the Mother PC',
        mbInformation, MB_OK);
    end;

    AppendSetupLog('=== OrderWeb POS Unified Setup Completed ===');
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;
