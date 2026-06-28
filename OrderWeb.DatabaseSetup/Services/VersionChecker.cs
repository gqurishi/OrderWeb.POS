using System.Text.Json;
using OrderWeb.DatabaseSetup.Models;

namespace OrderWeb.DatabaseSetup.Services;

public sealed class VersionChecker
{
    private readonly MigrationEngine _migrationEngine;

    public VersionChecker(MigrationEngine migrationEngine)
    {
        _migrationEngine = migrationEngine;
    }

    public async Task<VersionCheckResult> CheckAsync(
        DatabaseConfig config,
        int? requiredSchemaVersion,
        CancellationToken cancellationToken = default)
    {
        var required = requiredSchemaVersion ?? _migrationEngine.GetBundledSchemaVersion();
        var status = await _migrationEngine.GetHistoryStatusAsync(config, cancellationToken);
        var current = status.RecordedSchemaVersion ?? status.AppliedSchemaVersion;

        if (status.AppliedSchemaVersion == 0 && status.RecordedSchemaVersion == null)
        {
            return VersionCheckResult.Mismatch(
                required,
                0,
                status,
                "app_schema_version is missing or no migrations have been applied.");
        }

        if (status.MissingMigrationIds.Count > 0)
        {
            return VersionCheckResult.Mismatch(
                required,
                current,
                status,
                $"Missing successful migrations: {string.Join(", ", status.MissingMigrationIds)}");
        }

        if (current < required || status.AppliedSchemaVersion < required)
        {
            return VersionCheckResult.Mismatch(
                required,
                Math.Max(current, status.AppliedSchemaVersion),
                status,
                $"Database schema {Math.Max(current, status.AppliedSchemaVersion)} is older than required {required}.");
        }

        return VersionCheckResult.Ok(required, current, status);
    }

    public static void WriteResult(VersionCheckResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false }));
            return;
        }

        Console.WriteLine($"REQUIRED={result.RequiredSchemaVersion}");
        Console.WriteLine($"CURRENT={result.CurrentSchemaVersion}");
        Console.WriteLine($"APPLIED={result.AppliedSchemaVersion}");
        Console.WriteLine($"BUNDLED={result.BundledSchemaVersion}");
        Console.WriteLine($"MIGRATIONS_APPLIED={result.AppliedMigrationCount}");
        Console.WriteLine($"MIGRATIONS_EXPECTED={result.ExpectedMigrationCount}");
    }
}

public sealed class VersionCheckResult
{
    public bool IsCompatible { get; init; }
    public int RequiredSchemaVersion { get; init; }
    public int CurrentSchemaVersion { get; init; }
    public int AppliedSchemaVersion { get; init; }
    public int BundledSchemaVersion { get; init; }
    public int AppliedMigrationCount { get; init; }
    public int ExpectedMigrationCount { get; init; }
    public IReadOnlyList<string> MissingMigrationIds { get; init; } = [];
    public string Message { get; init; } = string.Empty;

    public static VersionCheckResult Ok(int required, int current, MigrationHistoryStatus status) =>
        new()
        {
            IsCompatible = true,
            RequiredSchemaVersion = required,
            CurrentSchemaVersion = current,
            AppliedSchemaVersion = status.AppliedSchemaVersion,
            BundledSchemaVersion = status.BundledSchemaVersion,
            AppliedMigrationCount = status.AppliedMigrationIds.Count,
            ExpectedMigrationCount = status.ExpectedMigrationIds.Count,
            MissingMigrationIds = status.MissingMigrationIds,
            Message = "Database schema version is compatible."
        };

    public static VersionCheckResult Mismatch(int required, int current, MigrationHistoryStatus status, string message) =>
        new()
        {
            IsCompatible = false,
            RequiredSchemaVersion = required,
            CurrentSchemaVersion = current,
            AppliedSchemaVersion = status.AppliedSchemaVersion,
            BundledSchemaVersion = status.BundledSchemaVersion,
            AppliedMigrationCount = status.AppliedMigrationIds.Count,
            ExpectedMigrationCount = status.ExpectedMigrationIds.Count,
            MissingMigrationIds = status.MissingMigrationIds,
            Message = message
        };
}
