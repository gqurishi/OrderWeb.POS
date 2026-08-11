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
    private static TerminalEventsSchema? _schema;

    public static async Task EnsureSchemaAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
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

            var columns = await GetTerminalEventColumnsAsync(connection);
            await AddColumnIfMissingAsync(connection, columns, "event_kind", "VARCHAR(40) NULL");
            await AddColumnIfMissingAsync(connection, columns, "entity_type", "VARCHAR(40) NULL");
            await AddColumnIfMissingAsync(connection, columns, "entity_id", "VARCHAR(120) NULL");
            await AddColumnIfMissingAsync(connection, columns, "order_number", "VARCHAR(80) NULL");
            await AddColumnIfMissingAsync(connection, columns, "source_terminal_name", "VARCHAR(120) NULL");
            await AddColumnIfMissingAsync(connection, columns, "payload_json", "LONGTEXT NULL");
            await AddColumnIfMissingAsync(connection, columns, "created_at", "DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP");
            await BackfillTerminalEventCompatibilityColumnsAsync(connection, columns);

            _schema = ReadTerminalEventsSchema(columns);
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
            var entityType = reader["entity_type"]?.ToString();
            events.Add(new TerminalSyncEvent(
                reader.GetInt64("id"),
                ParseKind(reader["event_kind"]?.ToString()),
                string.IsNullOrWhiteSpace(entityType) ? "unknown" : entityType,
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

        var schema = _schema ?? TerminalEventsSchema.Modern;
        var eventKind = ToDbKind(kind);
        var entityTypeValue = string.IsNullOrWhiteSpace(entityType) ? "unknown" : entityType.Trim();
        object entityIdValue = string.IsNullOrWhiteSpace(entityId) ? DBNull.Value : entityId.Trim();
        object orderNumberValue = string.IsNullOrWhiteSpace(orderNumber) ? DBNull.Value : orderNumber.Trim();
        var terminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
        object terminalNameValue = string.IsNullOrWhiteSpace(terminalName) ? DBNull.Value : terminalName.Trim();
        object payloadJsonValue = payload == null ? DBNull.Value : JsonSerializer.Serialize(payload);

        var columns = new List<string>
        {
            "event_kind",
            "entity_type",
            "entity_id",
            "order_number",
            "source_terminal_name",
            "payload_json",
            "created_at"
        };
        var values = new List<string>
        {
            "@eventKind",
            "@entityType",
            "@entityId",
            "@orderNumber",
            "@sourceTerminalName",
            "@payloadJson",
            "NOW()"
        };

        if (schema.HasEventId)
        {
            columns.Add("event_id");
            values.Add("@eventId");
        }

        if (schema.HasTerminalName)
        {
            columns.Add("terminal_name");
            values.Add("@sourceTerminalName");
        }

        if (schema.HasChangeKind)
        {
            columns.Add("change_kind");
            values.Add("@eventKind");
        }

        if (schema.HasEntityName)
        {
            columns.Add("entity_name");
            values.Add("@entityType");
        }

        var sql = $@"
            INSERT INTO terminal_events
                ({string.Join(", ", columns)})
            VALUES
                ({string.Join(", ", values)})";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@eventKind", eventKind);
        command.Parameters.AddWithValue("@entityType", entityTypeValue);
        command.Parameters.AddWithValue("@entityId", entityIdValue);
        command.Parameters.AddWithValue("@orderNumber", orderNumberValue);
        command.Parameters.AddWithValue("@sourceTerminalName", terminalNameValue);
        command.Parameters.AddWithValue("@payloadJson", payloadJsonValue);
        command.Parameters.AddWithValue("@eventId", Guid.NewGuid().ToString());
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
        AppDataChangeKind.Reservations => "reservations",
        AppDataChangeKind.Settings => "settings",
        AppDataChangeKind.Printers => "printers",
        AppDataChangeKind.All => "all",
        _ => "manual"
    };

    private static AppDataChangeKind ParseKind(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "orders" => AppDataChangeKind.Orders,
        "table_layout" => AppDataChangeKind.TableLayout,
        "tables" => AppDataChangeKind.TableLayout,
        "reservations" => AppDataChangeKind.Reservations,
        "settings" => AppDataChangeKind.Settings,
        "printers" => AppDataChangeKind.Printers,
        "all" => AppDataChangeKind.All,
        _ => AppDataChangeKind.Manual
    };

    private static async Task<HashSet<string>> GetTerminalEventColumnsAsync(MySqlConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string sql = @"
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'terminal_events'";

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString("COLUMN_NAME"));
        }

        return columns;
    }

    private static async Task AddColumnIfMissingAsync(
        MySqlConnection connection,
        HashSet<string> columns,
        string columnName,
        string definition)
    {
        if (columns.Contains(columnName))
        {
            return;
        }

        try
        {
            await using var command = new MySqlCommand($"ALTER TABLE terminal_events ADD COLUMN {columnName} {definition}", connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.Number == 1060)
        {
            // Another terminal upgraded the schema first.
        }

        columns.Add(columnName);
    }

    private static async Task BackfillTerminalEventCompatibilityColumnsAsync(
        MySqlConnection connection,
        HashSet<string> columns)
    {
        if (columns.Contains("change_kind"))
        {
            await ExecuteCompatibilitySqlAsync(connection, @"
                UPDATE terminal_events
                SET event_kind = change_kind
                WHERE (event_kind IS NULL OR event_kind = '')
                  AND change_kind IS NOT NULL");
        }

        if (columns.Contains("entity_name"))
        {
            await ExecuteCompatibilitySqlAsync(connection, @"
                UPDATE terminal_events
                SET entity_type = entity_name
                WHERE (entity_type IS NULL OR entity_type = '')
                  AND entity_name IS NOT NULL");
        }

        if (columns.Contains("terminal_name"))
        {
            await ExecuteCompatibilitySqlAsync(connection, @"
                UPDATE terminal_events
                SET source_terminal_name = terminal_name
                WHERE (source_terminal_name IS NULL OR source_terminal_name = '')
                  AND terminal_name IS NOT NULL");
        }

        await ExecuteCompatibilitySqlAsync(connection, @"
            UPDATE terminal_events
            SET entity_type = 'unknown'
            WHERE entity_type IS NULL OR entity_type = ''");
    }

    private static async Task ExecuteCompatibilitySqlAsync(MySqlConnection connection, string sql)
    {
        try
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LiveUpdate] terminal_events compatibility warning: {ex.Message}");
        }
    }

    private static TerminalEventsSchema ReadTerminalEventsSchema(HashSet<string> columns)
    {
        return new TerminalEventsSchema(
            columns.Contains("event_id"),
            columns.Contains("terminal_name"),
            columns.Contains("change_kind"),
            columns.Contains("entity_name"));
    }

    private sealed record TerminalEventsSchema(
        bool HasEventId,
        bool HasTerminalName,
        bool HasChangeKind,
        bool HasEntityName)
    {
        public static TerminalEventsSchema Modern { get; } = new(false, false, false, false);
    }
}
