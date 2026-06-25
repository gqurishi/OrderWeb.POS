using System.Text.Json;
using MySqlConnector;

namespace POS_in_NET.Services;

public sealed record TerminalSyncEvent(
    long Id,
    AppDataChangeKind Kind,
    string EntityType,
    string? EntityId,
    string? SourceTerminalName,
    string? OrderNumber,
    DateTime CreatedAt);

public static class TerminalEventSyncService
{
    private const int DefaultEventLimit = 100;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    public static async Task EnsureSchemaAsync(MySqlConnection connection)
    {
        if (_schemaEnsured)
        {
            return;
        }

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured)
            {
                return;
            }

            const string sql = @"
                CREATE TABLE IF NOT EXISTS terminal_events (
                    id BIGINT AUTO_INCREMENT PRIMARY KEY,
                    event_kind VARCHAR(40) NOT NULL,
                    entity_type VARCHAR(40) NOT NULL,
                    entity_id VARCHAR(120) NULL,
                    order_number VARCHAR(80) NULL,
                    source_terminal_name VARCHAR(120) NULL,
                    payload_json LONGTEXT NULL,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_terminal_events_id_kind (id, event_kind),
                    INDEX idx_terminal_events_created (created_at),
                    INDEX idx_terminal_events_entity (entity_type, entity_id),
                    INDEX idx_terminal_events_source (source_terminal_name)
                ) ENGINE=InnoDB";

            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public static async Task<long> GetLatestEventIdAsync(MySqlConnection connection)
    {
        await EnsureSchemaAsync(connection);

        await using var command = new MySqlCommand("SELECT COALESCE(MAX(id), 0) FROM terminal_events", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public static async Task<List<TerminalSyncEvent>> GetEventsAfterAsync(
        MySqlConnection connection,
        long lastSeenEventId,
        int limit = DefaultEventLimit)
    {
        await EnsureSchemaAsync(connection);

        const string sql = @"
            SELECT id, event_kind, entity_type, entity_id, order_number, source_terminal_name, created_at
            FROM terminal_events
            WHERE id > @lastSeenEventId
            ORDER BY id ASC
            LIMIT @limit";

        var events = new List<TerminalSyncEvent>();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lastSeenEventId", lastSeenEventId);
        command.Parameters.AddWithValue("@limit", Math.Max(1, limit));

        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new TerminalSyncEvent(
                reader.GetInt64("id"),
                ParseKind(reader["event_kind"]?.ToString()),
                reader["entity_type"]?.ToString() ?? "unknown",
                reader.IsDBNull(reader.GetOrdinal("entity_id")) ? null : reader.GetString("entity_id"),
                reader.IsDBNull(reader.GetOrdinal("source_terminal_name")) ? null : reader.GetString("source_terminal_name"),
                reader.IsDBNull(reader.GetOrdinal("order_number")) ? null : reader.GetString("order_number"),
                reader.GetDateTime("created_at")));
        }

        return events;
    }

    public static async Task PublishAsync(
        MySqlConnection connection,
        AppDataChangeKind kind,
        string entityType,
        string? entityId = null,
        string? orderNumber = null,
        object? payload = null)
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            return;
        }

        await EnsureSchemaAsync(connection);

        const string sql = @"
            INSERT INTO terminal_events
                (event_kind, entity_type, entity_id, order_number, source_terminal_name, payload_json, created_at)
            VALUES
                (@eventKind, @entityType, @entityId, @orderNumber, @sourceTerminalName, @payloadJson, NOW())";

        var terminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@eventKind", ToDbKind(kind));
        command.Parameters.AddWithValue("@entityType", string.IsNullOrWhiteSpace(entityType) ? "unknown" : entityType.Trim());
        command.Parameters.AddWithValue("@entityId", string.IsNullOrWhiteSpace(entityId) ? DBNull.Value : entityId.Trim());
        command.Parameters.AddWithValue("@orderNumber", string.IsNullOrWhiteSpace(orderNumber) ? DBNull.Value : orderNumber.Trim());
        command.Parameters.AddWithValue("@sourceTerminalName", string.IsNullOrWhiteSpace(terminalName) ? DBNull.Value : terminalName.Trim());
        command.Parameters.AddWithValue("@payloadJson", payload == null ? DBNull.Value : JsonSerializer.Serialize(payload));
        await command.ExecuteNonQueryAsync();
    }

    public static async Task PublishAsync(
        AppDataChangeKind kind,
        string entityType,
        string? entityId = null,
        string? orderNumber = null,
        object? payload = null)
    {
        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString(
            connectionTimeoutSeconds: 2,
            defaultCommandTimeoutSeconds: 3));
        await connection.OpenAsync();
        await PublishAsync(connection, kind, entityType, entityId, orderNumber, payload);
    }

    public static async Task PruneOldEventsAsync(MySqlConnection connection, int retentionDays = 7)
    {
        await EnsureSchemaAsync(connection);

        await using var command = new MySqlCommand(@"
            DELETE FROM terminal_events
            WHERE created_at < DATE_SUB(NOW(), INTERVAL @retentionDays DAY)", connection);
        command.Parameters.AddWithValue("@retentionDays", Math.Max(1, retentionDays));
        await command.ExecuteNonQueryAsync();
    }

    private static string ToDbKind(AppDataChangeKind kind) => kind switch
    {
        AppDataChangeKind.Orders => "orders",
        AppDataChangeKind.TableLayout => "table_layout",
        AppDataChangeKind.All => "all",
        _ => "manual"
    };

    private static AppDataChangeKind ParseKind(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "orders" => AppDataChangeKind.Orders,
        "table_layout" => AppDataChangeKind.TableLayout,
        "all" => AppDataChangeKind.All,
        _ => AppDataChangeKind.Manual
    };
}
