[Code]
var
  DatabaseSetupLogPath: String;

function GetProductionConfigPath(): String;
begin
  Result := ExpandConstant('{commonappdata}\{#ConfigFolderName}\{#ConfigFileName}');
end;

function GetDatabaseSetupExePath(): String;
begin
  Result := ExpandConstant('{app}\{#DatabaseSetupExe}');
end;

function GetDatabaseSetupLogPath(): String;
begin
  if DatabaseSetupLogPath = '' then
    DatabaseSetupLogPath := ExpandConstant('{commonappdata}\{#ConfigFolderName}\installer-database-setup.log');
  Result := DatabaseSetupLogPath;
end;

function GetMariaDbInstallLogPath(): String;
begin
  Result := ExpandConstant('{commonappdata}\{#ConfigFolderName}\mariadb-msi-install.log');
end;

function AppendSetupLog(const Line: String): Boolean;
var
  LogFile: String;
begin
  LogFile := GetDatabaseSetupLogPath();
  ForceDirectories(ExtractFilePath(LogFile));
  if FileExists(LogFile) then
    Result := SaveStringToFile(LogFile, Line + #13#10, True)
  else
    Result := SaveStringToFile(LogFile, Line + #13#10, False);
end;

function ExitCodeToMessage(Code: Integer): String;
begin
  case Code of
    0: Result := 'Success.';
    1: Result := 'Invalid arguments were supplied to OrderWeb.DatabaseSetup.exe.';
    2: Result := 'Database config error. Expected ' + GetProductionConfigPath() + '.';
    3: Result := 'MariaDB connection failed. Check that MariaDB is running and admin credentials are correct.';
    4: Result := 'Migration failed. Review the installer log and restore from backup if needed.';
    5: Result := 'Schema verification failed.';
    6: Result := 'Backup failed.';
    7: Result := 'Restore failed.';
    8: Result := 'Database schema version is older than this app requires.';
    99: Result := 'Unexpected error from OrderWeb.DatabaseSetup.exe.';
  else
    Result := 'OrderWeb.DatabaseSetup.exe returned exit code ' + IntToStr(Code) + '.';
  end;
end;

function RunDatabaseSetupLogged(const Command, ExtraArgs, SafeLogArgs: String; var Output: String): Integer;
var
  ResultCode: Integer;
  SetupExe, Params, LogParams, LogLine: String;
begin
  SetupExe := GetDatabaseSetupExePath();
  Output := '';

  if not FileExists(SetupExe) then
  begin
    Output := 'OrderWeb.DatabaseSetup.exe was not found at ' + SetupExe;
    Result := 99;
    Exit;
  end;

  Params := Command;
  if ExtraArgs <> '' then
    Params := Params + ' ' + ExtraArgs;

  LogParams := Command;
  if SafeLogArgs <> '' then
    LogParams := LogParams + ' ' + SafeLogArgs
  else if ExtraArgs <> '' then
    LogParams := LogParams + ' ' + ExtraArgs;

  LogLine := '[Run] ' + SetupExe + ' ' + LogParams;
  AppendSetupLog(LogLine);

  if Exec(SetupExe, Params, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Output := ExitCodeToMessage(ResultCode);
    AppendSetupLog('[ExitCode] ' + IntToStr(ResultCode) + ' ' + Output);
    Result := ResultCode;
  end
  else
  begin
    Output := 'Failed to launch OrderWeb.DatabaseSetup.exe.';
    AppendSetupLog('[Error] ' + Output);
    Result := 99;
  end;
end;

function RunDatabaseSetup(const Command, ExtraArgs: String; var Output: String): Integer;
begin
  Result := RunDatabaseSetupLogged(Command, ExtraArgs, '', Output);
end;

function RunDatabaseSetupOrAbort(const Command, ExtraArgs, FailureTitle: String): Boolean;
var
  ResultCode: Integer;
  Output: String;
begin
  ResultCode := RunDatabaseSetup(Command, ExtraArgs, Output);
  Result := ResultCode = 0;
  if not Result then
    MsgBox(FailureTitle + #13#10#13#10 + Output + #13#10#13#10 +
      'Log: ' + GetDatabaseSetupLogPath(), mbError, MB_OK);
end;

function ConfigExists(): Boolean;
begin
  Result := FileExists(GetProductionConfigPath());
end;

function ApplyDatabaseConfigAcl(const FilePath: String): Boolean;
var
  ResultCode: Integer;
  Params: String;
begin
  if FilePath = '' then
  begin
    Result := False;
    Exit;
  end;

  Params :=
    '"' + FilePath + '" ' +
    '/inheritance:r ' +
    '/grant:r "*S-1-5-32-544:F" ' +
    '/grant:r "*S-1-5-18:F" ' +
    '/grant:r "*S-1-5-32-545:RX"';

  Result := Exec(ExpandConstant('{sys}\icacls.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);

  if Result then
    AppendSetupLog('[Config] Applied ACL: Administrators/System full control, Users read-only.')
  else
    AppendSetupLog('[Config] Warning: could not apply ACL to ' + FilePath + '. icacls exit code: ' + IntToStr(ResultCode));
end;

function ApplyDatabaseConfigDirectoryAcl(const DirectoryPath: String): Boolean;
var
  ResultCode: Integer;
  Params: String;
begin
  Params :=
    '"' + DirectoryPath + '" ' +
    '/inheritance:r ' +
    '/grant:r "*S-1-5-32-544:(OI)(CI)F" ' +
    '/grant:r "*S-1-5-18:(OI)(CI)F" ' +
    '/grant:r "*S-1-5-32-545:(OI)(CI)RX"';

  Result := Exec(ExpandConstant('{sys}\icacls.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);

  if Result then
    AppendSetupLog('[Config] Restricted ProgramData directory to Administrators/System full control and Users read-only.')
  else
    AppendSetupLog('[Config] Error: could not restrict ' + DirectoryPath + '. icacls exit code: ' + IntToStr(ResultCode));
end;

function MariaDbServiceName(): String;
begin
  if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\OrderWebMariaDB') then
    Result := 'OrderWebMariaDB'
  else if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\MariaDB') then
    Result := 'MariaDB'
  else if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\MySQL') then
    Result := 'MySQL'
  else
    Result := '';
end;

function IsMariaDbInstalled(): Boolean;
begin
  Result := MariaDbServiceName() <> '';
end;

function StartMariaDbService(): Boolean;
var
  ServiceName: String;
  ResultCode: Integer;
begin
  ServiceName := MariaDbServiceName();
  if ServiceName = '' then
  begin
    Result := False;
    Exit;
  end;

  if not Exec(ExpandConstant('{sys}\sc.exe'), 'config ' + ServiceName + ' start= auto', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    or (ResultCode <> 0) then
  begin
    AppendSetupLog('[MariaDB] Could not set ' + ServiceName + ' startup type to Automatic.');
    Result := False;
    Exit;
  end;

  Exec(ExpandConstant('{sys}\sc.exe'), 'start ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0) or (ResultCode = 1056);
end;

function FindBundledMariaDbMsi(): String;
var
  FindRec: TFindRec;
  SearchPaths: array[0..1] of String;
  I: Integer;
begin
  Result := '';
  SearchPaths[0] := ExpandConstant('{tmp}\MariaDB\*.msi');
  SearchPaths[1] := ExpandConstant('{src}\Prerequisites\MariaDB\*.msi');

  for I := 0 to 1 do
  begin
    if FindFirst(SearchPaths[I], FindRec) then
    begin
      try
        repeat
          if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
          begin
            if I = 0 then
              Result := ExpandConstant('{tmp}\MariaDB\' + FindRec.Name)
            else
              Result := ExpandConstant('{src}\Prerequisites\MariaDB\' + FindRec.Name);
            Break;
          end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;

    if Result <> '' then
      Break;
  end;
end;

function WaitForMariaDbService(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to 12 do
  begin
    if IsMariaDbInstalled() then
    begin
      Result := True;
      Exit;
    end;
    Sleep(2500);
  end;
end;

function EscapeMsiPropertyValue(const Value: String): String;
var
  I: Integer;
  Ch: Char;
begin
  Result := '';
  for I := 1 to Length(Value) do
  begin
    Ch := Value[I];
    if Ch = '"' then
      Result := Result + '\"'
    else
      Result := Result + Ch;
  end;
end;

function InstallBundledMariaDb(const RootPassword: String): Boolean;
var
  MsiPath: String;
  ResultCode: Integer;
  Params: String;
begin
  MsiPath := FindBundledMariaDbMsi();
  if MsiPath = '' then
  begin
    Result := False;
    Exit;
  end;

  AppendSetupLog('[MariaDB] Installing bundled MSI as OrderWebMariaDB service: ' + MsiPath);
  AppendSetupLog('[MariaDB] MSI log: ' + GetMariaDbInstallLogPath());
  Params :=
    '/i "' + MsiPath + '" ' +
    'SERVICENAME=OrderWebMariaDB ' +
    'PORT=3306 ' +
    'PASSWORD="' + EscapeMsiPropertyValue(RootPassword) + '" ' +
    'UTF8=1 ' +
    'STDCONFIG=1 ' +
    '/qn /norestart ' +
    '/l*v "' + GetMariaDbInstallLogPath() + '"';

  Result := Exec(ExpandConstant('{sys}\msiexec.exe'),
    Params,
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);

  if Result then
    Result := WaitForMariaDbService();

  if not Result then
    AppendSetupLog('[MariaDB] MSI install failed or service was not created. Exit code: ' + IntToStr(ResultCode));
end;

function EnsureMariaDbReady(const RootPassword: String): Boolean;
begin
  if not IsMariaDbInstalled() then
  begin
    if InstallBundledMariaDb(RootPassword) then
      AppendSetupLog('[MariaDB] Installed from bundled MSI.')
    else
    begin
      MsgBox(
        'MariaDB was not detected on this PC.' + #13#10#13#10 +
        'Install MariaDB Server first, or place a MariaDB MSI in:' + #13#10 +
        ExpandConstant('{src}\Prerequisites\MariaDB\') + #13#10#13#10 +
        'Then run this installer again.',
        mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if not StartMariaDbService() then
  begin
    MsgBox(
      'MariaDB is installed but the database service could not be started.' + #13#10 +
      'Start the MariaDB service manually, then run this installer again.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

function EnsureExistingMariaDbReady(): Boolean;
begin
  if not IsMariaDbInstalled() then
  begin
    MsgBox(
      'MariaDB was not detected on this PC.' + #13#10#13#10 +
      'This update installer expects MariaDB to already be installed on the mother terminal.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if not StartMariaDbService() then
  begin
    MsgBox(
      'MariaDB is installed but the database service could not be started.' + #13#10 +
      'Start the MariaDB service manually, then run this installer again.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

function EscapeCliPassword(const Value: String): String;
var
  I: Integer;
  Ch: Char;
begin
  Result := '';
  for I := 1 to Length(Value) do
  begin
    Ch := Value[I];
    if Ch = '"' then
      Result := Result + '\"'
    else if Ch = '\' then
      Result := Result + '\\'
    else
      Result := Result + Ch;
  end;
end;

function RunMotherInstallDatabase(const RootPassword, LanSubnet: String): Boolean;
var
  ResultCode: Integer;
  ExtraArgs, SafeLogArgs, Output: String;
begin
  ExtraArgs := '--root-user root --root-password "' + EscapeCliPassword(RootPassword) + '" --quiet';
  SafeLogArgs := '--root-user root --root-password "********" --quiet';
  if LanSubnet <> '' then
  begin
    ExtraArgs := ExtraArgs + ' --child-ips "' + EscapeCliPassword(LanSubnet) + '"';
    SafeLogArgs := SafeLogArgs + ' --child-ips "' + EscapeCliPassword(LanSubnet) + '"';
  end;

  ResultCode := RunDatabaseSetupLogged('install-mother', ExtraArgs, SafeLogArgs, Output);
  Result := ResultCode = 0;
  if not Result then
  begin
    MsgBox('Mother database installation failed.' + #13#10#13#10 +
      Output + #13#10#13#10 +
      'Log: ' + GetDatabaseSetupLogPath(), mbError, MB_OK);
  end;
end;

function RunVerifyDatabase(): Boolean;
begin
  Result := RunDatabaseSetupOrAbort('verify', '--quiet', 'Database verification failed.');
end;

function RunCheckVersionDatabase(): Boolean;
var
  ResultCode: Integer;
  Output: String;
begin
  ResultCode := RunDatabaseSetup('check-version', '--quiet', Output);
  if ResultCode = 0 then
    Result := True
  else if ResultCode = 8 then
  begin
    MsgBox(
      'This app requires a newer database schema on the mother terminal.' + #13#10#13#10 +
      Output + #13#10#13#10 +
      'Update the mother terminal first with OrderWeb.DatabaseSetup.exe migrate.',
      mbError, MB_OK);
    Result := False;
  end
  else
    Result := RunDatabaseSetupOrAbort('check-version', '--quiet', 'Database version check failed.');
end;

function RunMotherUpdateDatabase(): Boolean;
begin
  if not ConfigExists() then
  begin
    MsgBox(
      'No production database config was found at:' + #13#10 +
      GetProductionConfigPath() + #13#10#13#10 +
      'Run the mother installer first, or restore orderweb-database.json.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if not RunDatabaseSetupOrAbort('backup', '--quiet', 'Pre-update backup failed.') then
  begin
    Result := False;
    Exit;
  end;

  if not RunDatabaseSetupOrAbort('migrate', '--quiet', 'Database migration failed.') then
  begin
    Result := False;
    Exit;
  end;

  Result := RunVerifyDatabase() and RunCheckVersionDatabase();
end;

function CopyOptionalChildDatabaseConfig(const SourcePath: String): Boolean;
var
  TargetPath: String;
begin
  TargetPath := GetProductionConfigPath();
  ForceDirectories(ExtractFilePath(TargetPath));

  if not ApplyDatabaseConfigDirectoryAcl(RemoveBackslashUnlessRoot(ExtractFilePath(TargetPath))) then
  begin
    Result := False;
    Exit;
  end;

  if (SourcePath <> '') and FileExists(SourcePath) then
  begin
    AppendSetupLog('[Config] Copying child database config from ' + SourcePath);
    Result := CopyFile(SourcePath, TargetPath, False);
    if Result then
      Result := ApplyDatabaseConfigAcl(TargetPath)
    else
      MsgBox('Could not copy orderweb-database.json to ' + TargetPath, mbError, MB_OK);
  end
  else
    Result := True;
end;

function ChildConfigCheckIfPresent(): Boolean;
begin
  if not ConfigExists() then
  begin
    AppendSetupLog('[Child] No orderweb-database.json yet. Pairing will happen in the app.');
    Result := True;
    Exit;
  end;

  Result := RunCheckVersionDatabase();
end;
