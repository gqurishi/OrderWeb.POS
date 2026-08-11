using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed record SchemaVersionGateResult(
    bool IsCompatible,
    int? CurrentSchemaVersion,
    int RequiredSchemaVersion,
    string Message);

/// <summary>
/// Reads schema metadata for diagnostics. Version differences are intentionally
/// non-blocking so mixed app/database versions can continue to connect.
/// </summary>
public static class ChildSchemaVersionGateService
{
    public static int RequiredSchemaVersion => PosDatabaseDefaults.RequiredSchemaVersion;

    public static async Task<SchemaVersionGateResult> CheckAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (!await TableExistsAsync(connection, "app_schema_version", cancellationToken))
        {
            return Compatible(
                null,
                "Connected in compatibility mode (database version is not recorded).");
        }

        var current = await ReadSchemaVersionAsync(connection, cancellationToken);
        if (current == null)
        {
            return Compatible(
                null,
                "Connected in compatibility mode (database version is not recorded).");
        }

        if (current.Value < RequiredSchemaVersion)
        {
            return Compatible(
                current,
                $"Connected in compatibility mode to database schema version {current.Value}; app schema version is {RequiredSchemaVersion}.");
        }

        return Compatible(
            current,
            $"Mother database schema version {current.Value} is compatible with this app.");
    }

    public static async Task<SchemaVersionGateResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            return Incompatible(null, "Terminal setup is not complete.");
        }

        try
        {
            await using var connection = new MySqlConnection(
                TerminalConfigurationService.GetPosConnectionString(
                    connectionTimeoutSeconds: 5,
                    pooled: false));
            await connection.OpenAsync(cancellationToken);
            return await CheckAsync(connection, cancellationToken);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("SchemaVersionGate", ex);
            return Incompatible(null, $"Could not read mother database schema version: {ex.Message}");
        }
    }

    private static async Task<int?> ReadSchemaVersionAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            "SELECT schema_version FROM app_schema_version WHERE id = 1 LIMIT 1",
            connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static async Task<bool> TableExistsAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static SchemaVersionGateResult Compatible(int? current, string message) =>
        new(true, current, RequiredSchemaVersion, message);

    private static SchemaVersionGateResult Incompatible(int? current, string message) =>
        new(false, current, RequiredSchemaVersion, message);
}
