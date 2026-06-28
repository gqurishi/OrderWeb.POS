using OrderWeb.DatabaseSetup.Models;
using OrderWeb.DatabaseSetup.Services;

namespace OrderWeb.DatabaseSetup;

public sealed class CommandRunner
{
    private readonly string _appVersion;
    private readonly CommandLineOptions _options;

    public CommandRunner(string appVersion, CommandLineOptions options)
    {
        _appVersion = appVersion;
        _options = options;
    }

    public async Task<int> RunAsync(string command, CancellationToken cancellationToken = default)
    {
        return command.ToLowerInvariant() switch
        {
            "install-mother" => await InstallMotherAsync(cancellationToken),
            "migrate" => await MigrateAsync(cancellationToken),
            "verify" => await VerifyAsync(cancellationToken),
            "backup" => await BackupAsync(cancellationToken),
            "restore" => await RestoreAsync(cancellationToken),
            "check-version" => await CheckVersionAsync(cancellationToken),
            "migration-status" => await MigrationStatusAsync(cancellationToken),
            _ => InvalidCommand(command)
        };
    }

    private async Task<int> InstallMotherAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.RootUser) || string.IsNullOrWhiteSpace(_options.RootPassword))
        {
            WriteError("install-mother requires --root-user and --root-password (or ORDERWEB_ROOT_USER / ORDERWEB_ROOT_PASSWORD).");
            return ExitCodes.InvalidArguments;
        }

        var migrationsPath = ConfigStore.ResolveMigrationsPath(_options.MigrationsPath);
        var migrationEngine = new MigrationEngine(migrationsPath, _appVersion);
        var verifier = new SchemaVerifier(migrationsPath, _appVersion);

        WriteInfo("Provisioning MariaDB database and application user...");
        var provision = await MotherInstaller.ProvisionAsync(
            _options.RootUser,
            _options.RootPassword,
            _options.LanSubnet,
            _options.DatabasePassword,
            cancellationToken);

        if (!provision.IsSuccess || provision.Config == null)
        {
            WriteError(provision.Message);
            return ExitCodes.ConnectionFailed;
        }

        ProductionConfigSaveResult saveResult;
        try
        {
            var configPath = _options.ConfigPath ?? ConfigStore.ResolveProductionConfigPath();
            saveResult = ConfigStore.SaveProductionConfig(provision.Config, configPath);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return ExitCodes.ConfigError;
        }

        WriteInfo($"Saved database config to {saveResult.ConfigPath}");

        WriteInfo("Running production migrations...");
        var migrationResult = await migrationEngine.RunPendingAsync(provision.Config, skipBackup: true, cancellationToken);
        if (!migrationResult.IsSuccess)
        {
            WriteError(migrationResult.Message);
            return ExitCodes.MigrationFailed;
        }

        WriteInfo(migrationResult.Message);
        LogMigrationDetails(migrationResult);

        WriteInfo("Verifying database schema...");
        var verifyResult = await verifier.VerifyAsync(provision.Config, cancellationToken);
        if (!verifyResult.IsSuccess)
        {
            WriteError(verifyResult.Message);
            return ExitCodes.VerifyFailed;
        }

        await migrationEngine.SyncSchemaVersionFromHistoryAsync(provision.Config, cancellationToken);

        var manifestPath = ConfigStore.ResolveInstallManifestPath();
        ConfigStore.WriteInstallManifest(new InstallManifest
        {
            AppVersion = _appVersion,
            DatabaseName = provision.Config.DatabaseName,
            DatabaseUser = provision.Config.DatabaseUser,
            DatabaseHost = provision.Config.DatabaseHost,
            DatabasePort = provision.Config.DatabasePort,
            ConfigPath = saveResult.ConfigPath,
            SchemaVersion = migrationResult.LatestSchemaVersion,
            InstalledAtUtc = DateTime.UtcNow
        });

        if (_options.Json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                success = true,
                configPath = saveResult.ConfigPath,
                manifestPath,
                schemaVersion = migrationResult.LatestSchemaVersion,
                databaseName = provision.Config.DatabaseName,
                databaseUser = provision.Config.DatabaseUser,
                passwordGenerated = provision.PasswordGenerated
            }));
        }
        else
        {
            WriteInfo($"Install manifest written to {manifestPath}");
            WriteInfo($"Mother database ready. Schema version {migrationResult.LatestSchemaVersion}.");
            Console.WriteLine($"CONFIG_PATH={saveResult.ConfigPath}");
            Console.WriteLine($"MANIFEST_PATH={manifestPath}");
        }

        return ExitCodes.Success;
    }

    private async Task<int> MigrateAsync(CancellationToken cancellationToken)
    {
        DatabaseConfig config;
        try
        {
            config = ConfigStore.Load(_options.ConfigPath);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return ExitCodes.ConfigError;
        }

        var migrationsPath = ConfigStore.ResolveMigrationsPath(_options.MigrationsPath);
        var migrationEngine = new MigrationEngine(migrationsPath, _appVersion);
        var verifier = new SchemaVerifier(migrationsPath, _appVersion);

        var migrationResult = await migrationEngine.RunPendingAsync(config, _options.SkipBackup, cancellationToken);
        if (!migrationResult.IsSuccess)
        {
            WriteError(migrationResult.Message);
            return ExitCodes.MigrationFailed;
        }

        WriteInfo(migrationResult.Message);
        LogMigrationDetails(migrationResult);

        var verifyResult = await verifier.VerifyAsync(config, cancellationToken);
        if (!verifyResult.IsSuccess)
        {
            WriteError(verifyResult.Message);
            return ExitCodes.VerifyFailed;
        }

        await migrationEngine.SyncSchemaVersionFromHistoryAsync(config, cancellationToken);
        WriteInfo($"Migration complete. Schema version {migrationResult.LatestSchemaVersion}.");
        return ExitCodes.Success;
    }

    private async Task<int> VerifyAsync(CancellationToken cancellationToken)
    {
        DatabaseConfig config;
        try
        {
            config = ConfigStore.Load(_options.ConfigPath);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return ExitCodes.ConfigError;
        }

        var migrationsPath = ConfigStore.ResolveMigrationsPath(_options.MigrationsPath);
        var verifier = new SchemaVerifier(migrationsPath, _appVersion);
        var result = await verifier.VerifyAsync(config, cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(result.Message);
            return ExitCodes.VerifyFailed;
        }

        WriteInfo(result.Message);
        return ExitCodes.Success;
    }

    private async Task<int> BackupAsync(CancellationToken cancellationToken)
    {
        DatabaseConfig config;
        try
        {
            config = ConfigStore.Load(_options.ConfigPath);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return ExitCodes.ConfigError;
        }

        var migrationsPath = ConfigStore.ResolveMigrationsPath(_options.MigrationsPath);
        var migrationEngine = new MigrationEngine(migrationsPath, _appVersion);
        var currentVersion = await migrationEngine.GetCurrentSchemaVersionAsync(config, cancellationToken) ?? 0;

        var backupService = new BackupService(_appVersion);
        var result = await backupService.CreateBackupAsync(
            config,
            "manual",
            cancellationToken,
            _options.OutputPath,
            currentVersion);

        if (!result.IsSuccess)
        {
            WriteError(result.Message);
            return ExitCodes.BackupFailed;
        }

        WriteInfo(result.Message);
        return ExitCodes.Success;
    }

    private async Task<int> RestoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
        {
            WriteError("restore requires --input <backup-file>.");
            return ExitCodes.InvalidArguments;
        }

        DatabaseConfig config;
        try
        {
            config = ConfigStore.Load(_options.ConfigPath);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return ExitCodes.ConfigError;
        }

        var backupService = new BackupService(_appVersion);
        var result = await backupService.RestoreAsync(config, _options.InputPath, cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(result.Message);
            return ExitCodes.RestoreFailed;
        }

        WriteInfo(result.Message);
        return ExitCodes.Success;
    }

    private async Task<int> CheckVersionAsync(CancellationToken cancellationToken)
    {
        DatabaseConfig config;
        try
        {
            config = ConfigStore.Load(_options.ConfigPath);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return ExitCodes.ConfigError;
        }

        var migrationsPath = ConfigStore.ResolveMigrationsPath(_options.MigrationsPath);
        var migrationEngine = new MigrationEngine(migrationsPath, _appVersion);
        var checker = new VersionChecker(migrationEngine);
        var result = await checker.CheckAsync(config, _options.RequiredSchemaVersion, cancellationToken);
        VersionChecker.WriteResult(result, _options.Json);

        if (!result.IsCompatible)
        {
            if (!_options.Json)
            {
                WriteError(result.Message);
            }

            return ExitCodes.VersionMismatch;
        }

        if (!_options.Json)
        {
            WriteInfo(result.Message);
        }

        return ExitCodes.Success;
    }

    private async Task<int> MigrationStatusAsync(CancellationToken cancellationToken)
    {
        DatabaseConfig config;
        try
        {
            config = ConfigStore.Load(_options.ConfigPath);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return ExitCodes.ConfigError;
        }

        var migrationsPath = ConfigStore.ResolveMigrationsPath(_options.MigrationsPath);
        var migrationEngine = new MigrationEngine(migrationsPath, _appVersion);
        var status = await migrationEngine.GetHistoryStatusAsync(config, cancellationToken);

        if (_options.Json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(status));
        }
        else
        {
            Console.WriteLine($"BUNDLED={status.BundledSchemaVersion}");
            Console.WriteLine($"APPLIED={status.AppliedSchemaVersion}");
            Console.WriteLine($"RECORDED={status.RecordedSchemaVersion ?? 0}");
            Console.WriteLine($"EXPECTED_MIGRATIONS={status.ExpectedMigrationIds.Count}");
            Console.WriteLine($"APPLIED_MIGRATIONS={status.AppliedMigrationIds.Count}");
            if (status.MissingMigrationIds.Count > 0)
            {
                Console.WriteLine($"MISSING={string.Join(",", status.MissingMigrationIds)}");
            }
        }

        return status.IsComplete ? ExitCodes.Success : ExitCodes.VersionMismatch;
    }

    private void LogMigrationDetails(MigrationRunResult result)
    {
        if (_options.Quiet)
        {
            return;
        }

        foreach (var migrationId in result.ExecutedMigrationIds)
        {
            Console.WriteLine($"  applied: {migrationId}");
        }

        foreach (var migrationId in result.SkippedMigrationIds)
        {
            Console.WriteLine($"  skipped: {migrationId}");
        }
    }

    private int InvalidCommand(string command)
    {
        WriteError($"Unknown command '{command}'.");
        PrintHelp();
        return ExitCodes.InvalidArguments;
    }

    private void WriteInfo(string message)
    {
        if (!_options.Quiet)
        {
            Console.WriteLine(message);
        }
    }

    private void WriteError(string message)
    {
        Console.Error.WriteLine(message);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            OrderWeb.DatabaseSetup.exe — OrderWeb POS database installer helper

            Commands:
              install-mother     Create DB/user, write config, run migrations, verify
              migrate            Apply pending migrations, verify, sync schema version
              verify             Verify required tables/views and migration_history
              backup             Create .orderwebbackup package
              restore            Restore from .orderwebbackup package
              check-version      Compare DB schema version with bundled/required version
              migration-status   Show applied vs expected migrations

            Common options:
              --config-path <path>       Path to orderweb-database.json
              --migrations-path <path>   Folder containing 001_*.sql migrations
              --quiet                    Minimal console output
              --json                     Machine-readable output (check-version, migration-status)

            install-mother options:
              --root-user <user>         MariaDB admin user (default env ORDERWEB_ROOT_USER)
              --root-password <pwd>      MariaDB admin password (default env ORDERWEB_ROOT_PASSWORD)
              --database-password <pwd>  App user password (default: random; env ORDERWEB_APP_PASSWORD)
              --lan-subnet <pattern>     Child LAN grant host pattern (default 192.168.%)
              Writes orderweb-database.json to ProgramData\OrderWebPOS on Windows.

            migrate options:
              --skip-backup              Skip automatic pre-migration backup

            backup options:
              --output <path>            Backup file path (.orderwebbackup)

            restore options:
              --input <path>             Backup file path (.orderwebbackup)

            check-version options:
              --required <number>        Required schema version (default: bundled latest)

            Exit codes:
              0 success, 1 invalid args, 2 config error, 3 connection failed,
              4 migration failed, 5 verify failed, 6 backup failed, 7 restore failed,
              8 version mismatch, 99 unexpected error
            """);
    }
}
