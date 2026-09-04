using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MySqlConnector;
using OrderWeb.DatabaseSetup.Models;

namespace OrderWeb.DatabaseSetup.Services;

public sealed partial class MigrationEngine
{
    private static readonly Regex MigrationFilePattern = MigrationFileRegex();

    private readonly string _migrationsPath;
    private readonly string _appVersion;
    private IReadOnlyList<MigrationFile>? _cachedMigrations;

    public MigrationEngine(string migrationsPath, string appVersion)
    {
        _migrationsPath = migrationsPath;
        _appVersion = appVersion;
    }

    public int GetBundledSchemaVersion() =>
        DiscoverMigrationFiles().Count == 0 ? 0 : DiscoverMigrationFiles().Max(file => file.Version);

    public IReadOnlyList<MigrationFile> DiscoverMigrationFiles()
    {
        if (_cachedMigrations != null)
        {
            return _cachedMigrations;
        }

        if (!Directory.Exists(_migrationsPath))
        {
            throw new DirectoryNotFoundException($"Migrations folder not found: {_migrationsPath}");
        }

        _cachedMigrations = Directory.GetFiles(_migrationsPath, "*.sql")
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var match = MigrationFilePattern.Match(fileName);
                if (!match.Success)
                {
                    return null;
                }

                var migrationId = Path.GetFileNameWithoutExtension(fileName);
                return new MigrationFile(
                    int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    migrationId,
                    fileName,
                    path);
            })
            .Where(file => file != null)
            .Cast<MigrationFile>()
            .OrderBy(file => file.Version)
            .ToList();

        return _cachedMigrations;
    }

    public async Task<MigrationRunResult> RunPendingAsync(
        DatabaseConfig config,
        bool skipBackup,
        CancellationToken cancellationToken = default)
    {
        var migrations = DiscoverMigrationFiles();
        if (migrations.Count == 0)
        {
            return MigrationRunResult.Failed("No numbered migration files were found.");
        }

        var connectionString = ConfigStore.BuildConnectionString(config);
        await SqlExecutor.TestConnectionAsync(connectionString, cancellationToken);

        if (!skipBackup && await DatabaseHasAnyUserTablesAsync(config, cancellationToken))
        {
            var backupService = new BackupService(_appVersion);
            var backupResult = await backupService.CreateBackupAsync(config, "pre_migrate", cancellationToken);
            if (!backupResult.IsSuccess)
            {
                return MigrationRunResult.Failed($"Pre-migration backup failed: {backupResult.Message}");
            }
        }

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var appliedRecords = await GetAppliedMigrationRecordsAsync(connection, cancellationToken);
        var executed = new List<string>();
        var skipped = new List<string>();

        foreach (var migration in migrations)
        {
            var sql = await File.ReadAllTextAsync(migration.FullPath, cancellationToken);
            var checksum = ComputeSha256(sql);

            if (appliedRecords.TryGetValue(migration.Id, out var existing))
            {
                if (existing.Success)
                {
                    if (!string.IsNullOrWhiteSpace(existing.Checksum) &&
                        !string.Equals(existing.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        return MigrationRunResult.Failed(
                            $"Migration {migration.Id} was already applied with a different checksum. Create a new numbered migration instead of editing released SQL.",
                            executed,
                            skipped);
                    }

                    skipped.Add(migration.Id);
                    continue;
                }
            }

            var historyId = await BeginMigrationHistoryAsync(connection, migration, checksum, cancellationToken);

            try
            {
                await SqlExecutor.ExecuteScriptAsync(connection, sql, cancellationToken);
                await RecordMigrationSuccessAsync(connection, migration, checksum, cancellationToken);
                executed.Add(migration.Id);
            }
            catch (Exception ex)
            {
                await CompleteMigrationHistoryAsync(connection, historyId, success: false, errorMessage: ex.Message, cancellationToken);
                return MigrationRunResult.Failed($"Migration {migration.Id} failed: {ex.Message}", executed, skipped);
            }
        }

        var appliedVersion = await GetAppliedSchemaVersionAsync(connection, cancellationToken);
        return MigrationRunResult.Success(executed, skipped, appliedVersion);
    }

    public async Task<MigrationHistoryStatus> GetHistoryStatusAsync(
        DatabaseConfig config,
        CancellationToken cancellationToken = default)
    {
        var migrations = DiscoverMigrationFiles();
        await using var connection = new MySqlConnection(ConfigStore.BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        var appliedRecords = await GetAppliedMigrationRecordsAsync(connection, cancellationToken);
        var missing = migrations
            .Where(m => !appliedRecords.TryGetValue(m.Id, out var record) || !record.Success)
            .Select(m => m.Id)
            .ToList();

        var applied = migrations
            .Where(m => appliedRecords.TryGetValue(m.Id, out var record) && record.Success)
            .Select(m => m.Id)
            .ToList();

        return new MigrationHistoryStatus
        {
            BundledSchemaVersion = GetBundledSchemaVersion(),
            AppliedSchemaVersion = await GetAppliedSchemaVersionAsync(connection, cancellationToken),
            RecordedSchemaVersion = await GetCurrentSchemaVersionAsync(config, cancellationToken),
            ExpectedMigrationIds = migrations.Select(m => m.Id).ToList(),
            AppliedMigrationIds = applied,
            MissingMigrationIds = missing
        };
    }

    public async Task UpdateSchemaVersionAsync(
        DatabaseConfig config,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(ConfigStore.BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        if (!await SqlExecutor.TableExistsAsync(connection, "app_schema_version", cancellationToken))
        {
            throw new InvalidOperationException("app_schema_version table does not exist. Run migration 001 first.");
        }

        const string sql = @"
            UPDATE app_schema_version
            SET schema_version = @schemaVersion,
                app_version = @appVersion,
                database_name = @databaseName,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = 1";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schemaVersion", schemaVersion);
        command.Parameters.AddWithValue("@appVersion", _appVersion);
        command.Parameters.AddWithValue("@databaseName", config.DatabaseName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SyncSchemaVersionFromHistoryAsync(DatabaseConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(ConfigStore.BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        var appliedVersion = await GetAppliedSchemaVersionAsync(connection, cancellationToken);
        await UpdateSchemaVersionAsync(config, appliedVersion, cancellationToken);
    }

    public async Task<int?> GetCurrentSchemaVersionAsync(DatabaseConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(ConfigStore.BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        if (!await SqlExecutor.TableExistsAsync(connection, "app_schema_version", cancellationToken))
        {
            return null;
        }

        await using var command = new MySqlCommand("SELECT schema_version FROM app_schema_version WHERE id = 1 LIMIT 1", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    public async Task<int> GetAppliedSchemaVersionAsync(MySqlConnection connection, CancellationToken cancellationToken = default)
    {
        var migrations = DiscoverMigrationFiles();
        var appliedRecords = await GetAppliedMigrationRecordsAsync(connection, cancellationToken);
        return migrations
            .Where(m => appliedRecords.TryGetValue(m.Id, out var record) && record.Success)
            .Select(m => m.Version)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static async Task<bool> DatabaseHasAnyUserTablesAsync(DatabaseConfig config, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ConfigStore.BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @databaseName
              AND TABLE_TYPE = 'BASE TABLE'";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@databaseName", config.DatabaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<Dictionary<string, AppliedMigrationRecord>> GetAppliedMigrationRecordsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        var applied = new Dictionary<string, AppliedMigrationRecord>(StringComparer.OrdinalIgnoreCase);
        if (!await SqlExecutor.TableExistsAsync(connection, "migration_history", cancellationToken))
        {
            return applied;
        }

        const string sql = @"
            SELECT migration_id, checksum_sha256, success
            FROM migration_history";

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var migrationId = reader.GetString("migration_id");
            applied[migrationId] = new AppliedMigrationRecord(
                migrationId,
                reader.IsDBNull(reader.GetOrdinal("checksum_sha256")) ? null : reader.GetString("checksum_sha256"),
                Convert.ToInt32(reader["success"]) == 1);
        }

        return applied;
    }

    private async Task<int> BeginMigrationHistoryAsync(
        MySqlConnection connection,
        MigrationFile migration,
        string checksum,
        CancellationToken cancellationToken)
    {
        if (!await SqlExecutor.TableExistsAsync(connection, "migration_history", cancellationToken))
        {
            return 0;
        }

        const string sql = @"
            INSERT INTO migration_history
                (migration_id, migration_name, checksum_sha256, started_at, success, app_version, executed_by)
            VALUES
                (@migrationId, @migrationName, @checksum, CURRENT_TIMESTAMP, 0, @appVersion, @executedBy)
            ON DUPLICATE KEY UPDATE
                migration_name = VALUES(migration_name),
                checksum_sha256 = VALUES(checksum_sha256),
                started_at = CURRENT_TIMESTAMP,
                finished_at = NULL,
                success = 0,
                error_message = NULL,
                app_version = VALUES(app_version),
                executed_by = VALUES(executed_by)";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@migrationId", migration.Id);
        command.Parameters.AddWithValue("@migrationName", migration.FileName);
        command.Parameters.AddWithValue("@checksum", checksum);
        command.Parameters.AddWithValue("@appVersion", _appVersion);
        command.Parameters.AddWithValue("@executedBy", "OrderWeb.DatabaseSetup");
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var idCommand = new MySqlCommand("SELECT id FROM migration_history WHERE migration_id = @migrationId LIMIT 1", connection);
        idCommand.Parameters.AddWithValue("@migrationId", migration.Id);
        return Convert.ToInt32(await idCommand.ExecuteScalarAsync(cancellationToken));
    }

    private async Task RecordMigrationSuccessAsync(
        MySqlConnection connection,
        MigrationFile migration,
        string checksum,
        CancellationToken cancellationToken)
    {
        if (!await SqlExecutor.TableExistsAsync(connection, "migration_history", cancellationToken))
        {
            return;
        }

        const string sql = @"
            INSERT INTO migration_history
                (migration_id, migration_name, checksum_sha256, started_at, finished_at, success, app_version, executed_by)
            VALUES
                (@migrationId, @migrationName, @checksum, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1, @appVersion, @executedBy)
            ON DUPLICATE KEY UPDATE
                migration_name = VALUES(migration_name),
                checksum_sha256 = VALUES(checksum_sha256),
                finished_at = CURRENT_TIMESTAMP,
                success = 1,
                error_message = NULL,
                app_version = VALUES(app_version),
                executed_by = VALUES(executed_by)";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@migrationId", migration.Id);
        command.Parameters.AddWithValue("@migrationName", migration.FileName);
        command.Parameters.AddWithValue("@checksum", checksum);
        command.Parameters.AddWithValue("@appVersion", _appVersion);
        command.Parameters.AddWithValue("@executedBy", "OrderWeb.DatabaseSetup");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CompleteMigrationHistoryAsync(
        MySqlConnection connection,
        int historyId,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        if (historyId <= 0 || !await SqlExecutor.TableExistsAsync(connection, "migration_history", cancellationToken))
        {
            return;
        }

        const string sql = @"
            UPDATE migration_history
            SET finished_at = CURRENT_TIMESTAMP,
                success = @success,
                error_message = @errorMessage
            WHERE id = @id";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@success", success ? 1 : 0);
        command.Parameters.AddWithValue("@errorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", historyId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    [GeneratedRegex(@"^(?<version>\d{3})_(?<name>.+)\.sql$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MigrationFileRegex();
}

public sealed record MigrationFile(int Version, string Id, string FileName, string FullPath);

public sealed record AppliedMigrationRecord(string MigrationId, string? Checksum, bool Success);

public sealed class MigrationHistoryStatus
{
    public int BundledSchemaVersion { get; init; }
    public int AppliedSchemaVersion { get; init; }
    public int? RecordedSchemaVersion { get; init; }
    public IReadOnlyList<string> ExpectedMigrationIds { get; init; } = [];
    public IReadOnlyList<string> AppliedMigrationIds { get; init; } = [];
    public IReadOnlyList<string> MissingMigrationIds { get; init; } = [];

    public bool IsComplete => MissingMigrationIds.Count == 0 && AppliedSchemaVersion >= BundledSchemaVersion;
}

public sealed class MigrationRunResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> ExecutedMigrationIds { get; init; } = [];
    public IReadOnlyList<string> SkippedMigrationIds { get; init; } = [];
    public int LatestSchemaVersion { get; init; }

    public static MigrationRunResult Success(IReadOnlyList<string> executed, IReadOnlyList<string> skipped, int latestSchemaVersion)
    {
        var message = executed.Count == 0
            ? skipped.Count == 0
                ? "No migrations found to apply."
                : $"Database is already up to date. Skipped {skipped.Count} migration(s)."
            : skipped.Count == 0
                ? $"Applied {executed.Count} migration(s)."
                : $"Applied {executed.Count} migration(s), skipped {skipped.Count} already-applied migration(s).";

        return new MigrationRunResult
        {
            IsSuccess = true,
            Message = message,
            ExecutedMigrationIds = executed,
            SkippedMigrationIds = skipped,
            LatestSchemaVersion = latestSchemaVersion
        };
    }

    public static MigrationRunResult Failed(
        string message,
        IReadOnlyList<string>? executed = null,
        IReadOnlyList<string>? skipped = null) =>
        new()
        {
            IsSuccess = false,
            Message = message,
            ExecutedMigrationIds = executed ?? [],
            SkippedMigrationIds = skipped ?? []
        };
}
