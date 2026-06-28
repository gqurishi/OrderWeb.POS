using MySqlConnector;
using OrderWeb.DatabaseSetup.Models;

namespace OrderWeb.DatabaseSetup.Services;

public sealed class SchemaVerifier
{
    private readonly string _migrationsPath;
    private readonly MigrationEngine _migrationEngine;

    public SchemaVerifier(string migrationsPath, string appVersion)
    {
        _migrationsPath = migrationsPath;
        _migrationEngine = new MigrationEngine(migrationsPath, appVersion);
    }

    public async Task<VerifyResult> VerifyAsync(DatabaseConfig config, CancellationToken cancellationToken = default)
    {
        var objectResult = await VerifyObjectsAsync(config, cancellationToken);
        if (!objectResult.IsSuccess)
        {
            return objectResult;
        }

        var historyResult = await VerifyMigrationHistoryAsync(config, cancellationToken);
        if (!historyResult.IsSuccess)
        {
            return historyResult;
        }

        return VerifyResult.Success("Database schema and migration history verification passed.");
    }

    public async Task<VerifyResult> VerifyObjectsAsync(DatabaseConfig config, CancellationToken cancellationToken = default)
    {
        var verifyScriptPath = Path.Combine(_migrationsPath, "VERIFY_REQUIRED_SCHEMA.sql");
        if (!File.Exists(verifyScriptPath))
        {
            verifyScriptPath = Path.Combine(AppContext.BaseDirectory, "Migrations", "VERIFY_REQUIRED_SCHEMA.sql");
        }

        if (!File.Exists(verifyScriptPath))
        {
            return VerifyResult.Failed($"Verification script not found: {verifyScriptPath}");
        }

        var sql = await File.ReadAllTextAsync(verifyScriptPath, cancellationToken);
        await using var connection = new MySqlConnection(ConfigStore.BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);

        var missingTables = new List<string>();
        var missingViews = new List<string>();

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        do
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var columnName = reader.GetName(0);
                var value = reader.GetString(0);
                if (columnName.Equals("missing_table", StringComparison.OrdinalIgnoreCase))
                {
                    missingTables.Add(value);
                }
                else if (columnName.Equals("missing_view", StringComparison.OrdinalIgnoreCase))
                {
                    missingViews.Add(value);
                }
            }
        } while (await reader.NextResultAsync(cancellationToken));

        if (missingTables.Count == 0 && missingViews.Count == 0)
        {
            return VerifyResult.Success("Required tables and views exist.");
        }

        var details = new List<string>();
        if (missingTables.Count > 0)
        {
            details.Add($"missing tables: {string.Join(", ", missingTables)}");
        }

        if (missingViews.Count > 0)
        {
            details.Add($"missing views: {string.Join(", ", missingViews)}");
        }

        return VerifyResult.Failed(string.Join("; ", details), missingTables, missingViews);
    }

    public async Task<VerifyResult> VerifyMigrationHistoryAsync(DatabaseConfig config, CancellationToken cancellationToken = default)
    {
        var status = await _migrationEngine.GetHistoryStatusAsync(config, cancellationToken);
        if (status.IsComplete)
        {
            return VerifyResult.Success("All bundled migrations are recorded in migration_history.");
        }

        var message = status.MissingMigrationIds.Count == 0
            ? $"Applied schema version {status.AppliedSchemaVersion} is below bundled version {status.BundledSchemaVersion}."
            : $"Missing successful migrations: {string.Join(", ", status.MissingMigrationIds)}";

        return VerifyResult.Failed(message, missingMigrations: status.MissingMigrationIds);
    }
}

public sealed class VerifyResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> MissingTables { get; init; } = [];
    public IReadOnlyList<string> MissingViews { get; init; } = [];
    public IReadOnlyList<string> MissingMigrations { get; init; } = [];

    public static VerifyResult Success(string message) =>
        new() { IsSuccess = true, Message = message };

    public static VerifyResult Failed(
        string message,
        IReadOnlyList<string>? missingTables = null,
        IReadOnlyList<string>? missingViews = null,
        IReadOnlyList<string>? missingMigrations = null) =>
        new()
        {
            IsSuccess = false,
            Message = message,
            MissingTables = missingTables ?? [],
            MissingViews = missingViews ?? [],
            MissingMigrations = missingMigrations ?? []
        };
}
