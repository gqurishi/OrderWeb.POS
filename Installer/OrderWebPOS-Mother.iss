; OrderWeb POS — Mother terminal fresh install
; Flow: install app + DatabaseSetup.exe → MariaDB → install-mother → verify → launch app

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
OutputBaseFilename=OrderWebPOS-Mother-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
#ifdef SignInstaller
SignTool=orderweb
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishRoot}\{#DatabaseSetupExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\database\migrations\*.sql"; DestDir: "{app}\Migrations"; Flags: ignoreversion
Source: "Prerequisites\MariaDB\*"; DestDir: "{tmp}\MariaDB"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
#include "shared\DatabaseSetup.iss.inc"

var
  MariaDbRootPasswordPage: TInputQueryWizardPage;
  LanSubnetPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  MariaDbRootPasswordPage := CreateInputQueryPage(wpSelectTasks,
    'MariaDB Admin Password', 'Required for first-time database setup',
    'If MariaDB is already installed, enter its existing root/admin password.' + #13#10 +
    'If MariaDB is missing, this password will be used for the new bundled MariaDB install.' + #13#10 +
    'OrderWeb.DatabaseSetup.exe will then create orderweb_pos and orderweb_app automatically.');
  MariaDbRootPasswordPage.Add('MariaDB root password:', True);
  MariaDbRootPasswordPage.Values[0] := '';

  LanSubnetPage := CreateInputQueryPage(MariaDbRootPasswordPage.ID,
    'Child Terminal Network Access', 'Allow only known Child terminals',
    'Enter exact private Child IP addresses separated by commas, or leave blank until DHCP reservations are ready. Wildcards are rejected.');
  LanSubnetPage.Add('Child IP addresses:', False);
  LanSubnetPage.Values[0] := '';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = MariaDbRootPasswordPage.ID then
  begin
    if Trim(MariaDbRootPasswordPage.Values[0]) = '' then
    begin
      MsgBox('MariaDB root password is required for the mother install.', mbError, MB_OK);
      Result := False;
    end;
    if (not IsMariaDbInstalled()) and (Length(MariaDbRootPasswordPage.Values[0]) < 8) then
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

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    AppendSetupLog('=== OrderWeb POS Mother Install Started ===');

    if not EnsureMariaDbReady(MariaDbRootPasswordPage.Values[0]) then
      Abort;

    if not RunMotherInstallDatabase(
         MariaDbRootPasswordPage.Values[0],
         LanSubnetPage.Values[0]) then
      Abort;

    if not RunVerifyDatabase() then
      Abort;

    if not RunCheckVersionDatabase() then
      Abort;

    AppendSetupLog('=== OrderWeb POS Mother Install Completed ===');
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;
