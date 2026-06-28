using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed record SchemaVersionGateResult(
    bool IsCompatible,
    int? CurrentSchemaVersion,
    int RequiredSchemaVersion,
    string Message);

/// <summary>
/// Child terminals connect to the mother database and must not run migrations.
/// This gate reads app_schema_version on connect and blocks when the mother DB is behind.
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
            return Incompatible(
                null,
                "The mother terminal database is not installed. On the mother PC, run OrderWeb.DatabaseSetup.exe install-mother.");
        }

        var current = await ReadSchemaVersionAsync(connection, cancellationToken);
        if (current == null)
        {
            return Incompatible(
                null,
                "The mother terminal database has no schema version recorded. On the mother PC, run OrderWeb.DatabaseSetup.exe migrate.");
        }

        if (current.Value < RequiredSchemaVersion)
        {
            return Incompatible(
                current,
                $"This app requires database schema version {RequiredSchemaVersion}, but the mother terminal database is on version {current.Value}. " +
                "Update the mother terminal first: run OrderWeb.DatabaseSetup.exe migrate on the mother PC, then restart this terminal.");
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

        if (!TerminalConfigurationService.IsChildTerminal)
        {
            return Compatible(null, "Schema version gate applies to child terminals only.");
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
            AppDiagnostics.LogFatal("ChildSchemaVersionGate", ex);
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
