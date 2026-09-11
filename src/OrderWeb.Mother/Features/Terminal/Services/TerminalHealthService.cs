using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class TerminalHealthService : IDisposable
{
    public const int OnlineHeartbeatWindowSeconds = 60;

    private readonly DatabaseService _databaseService;
    private readonly object _syncRoot = new();
    private Timer? _timer;
    private bool _isRunning;
    private bool _isUpdating;

    public TerminalHealthService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public DateTime LastHeartbeatAt { get; private set; } = DateTime.MinValue;
    public string LastHeartbeatStatus { get; private set; } = "Not started";
    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _timer = new Timer(
                async _ => await UpdateCurrentTerminalAsync(),
                null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(30));
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            _timer?.Dispose();
            _timer = null;
            _isRunning = false;
        }
    }

    public async Task UpdateCurrentTerminalAsync()
    {
        if (_isUpdating || !TerminalConfigurationService.IsConfigured)
        {
            return;
        }

        _isUpdating = true;
        try
        {
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString(
                connectionTimeoutSeconds: 3,
                defaultCommandTimeoutSeconds: 4));
            await connection.OpenAsync();
            await EnsureTableAsync(connection);

            var config = TerminalConfigurationService.GetConfiguration();
            const string sql = @"
                INSERT INTO terminal_health
                    (terminal_name, terminal_mode, database_host, app_version, last_seen_at, last_status, last_error)
                VALUES
                    (@terminalName, @terminalMode, @databaseHost, @appVersion, NOW(), 'online', '')
                ON DUPLICATE KEY UPDATE
                    terminal_mode = VALUES(terminal_mode),
                    database_host = VALUES(database_host),
                    app_version = VALUES(app_version),
                    last_seen_at = NOW(),
                    last_status = 'online',
                    last_error = '',
                    updated_at = NOW()";

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@terminalName", config.TerminalName);
            command.Parameters.AddWithValue("@terminalMode", config.Mode.ToString());
            command.Parameters.AddWithValue("@databaseHost", config.DatabaseHost);
            command.Parameters.AddWithValue("@appVersion", AppInfo.Current.VersionString);
            await command.ExecuteNonQueryAsync();

            LastHeartbeatAt = DateTime.Now;
            LastHeartbeatStatus = "Online";
            TerminalConnectionStateService.ReportConnected();
        }
        catch (Exception ex)
        {
            LastHeartbeatAt = DateTime.Now;
            LastHeartbeatStatus = ex.Message;

            if (TerminalConfigurationService.IsChildTerminal)
            {
                TerminalConnectionStateService.ReportDisconnected("Mother terminal disconnected. Check the mother terminal, network, and MariaDB.");
            }

            System.Diagnostics.Debug.WriteLine($"Terminal health heartbeat failed: {ex.Message}");
        }
        finally
        {
            _isUpdating = false;
        }
    }

    public async Task<List<TerminalHealthStatus>> GetTerminalStatusesAsync()
    {
        var rows = new List<TerminalHealthStatus>();

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        await TerminalPairingService.EnsureTableAsync(connection);
        await TerminalPairingService.EnsurePermanentPairingCodesAsync(connection);
        await EnsureClientPairingColumnsAsync(connection);

        // Existing pending codes keep working; expiry is no longer used.
        await using (var clearExpiry = new MySqlCommand(
            "UPDATE terminal_pairings SET pairing_expires_at = NULL WHERE paired_at IS NULL AND pairing_expires_at IS NOT NULL",
            connection))
        {
            await clearExpiry.ExecuteNonQueryAsync();
        }

        const string sql = @"
            SELECT terminal_name, terminal_mode, database_host, app_version, last_seen_at, last_status, last_error
            FROM terminal_health
            ORDER BY terminal_mode = 'Mother' DESC, terminal_name";

        await using (var command = new MySqlCommand(sql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add(new TerminalHealthStatus
                {
                    TerminalName = reader.GetString("terminal_name"),
                    Mode = reader.GetString("terminal_mode"),
                    DatabaseHost = reader.GetString("database_host"),
                    AppVersion = reader.IsDBNull(reader.GetOrdinal("app_version")) ? string.Empty : reader.GetString("app_version"),
                    LastSeenAt = reader.GetDateTime("last_seen_at"),
                    LastStatus = reader.GetString("last_status"),
                    LastError = reader.IsDBNull(reader.GetOrdinal("last_error")) ? string.Empty : reader.GetString("last_error")
                });
            }
        }

        const string pairingsSql = @"
            SELECT terminal_name, terminal_id, device_type, platform, device_name, app_version,
                   pairing_code, pairing_expires_at, paired_at, disabled_at, enabled,
                   last_seen_at, last_ip_address, last_sync_event_id, revoked_at, current_user_id,
                   websocket_status, websocket_connected_at, websocket_disconnected_at, websocket_last_message_at
            FROM terminal_pairings
            ORDER BY terminal_name";

        await using var pairingsCommand = new MySqlCommand(pairingsSql, connection);
        await using var pairingsReader = await pairingsCommand.ExecuteReaderAsync();
        while (await pairingsReader.ReadAsync())
        {
            var terminalName = pairingsReader.GetString("terminal_name");
            var existing = rows.FirstOrDefault(status =>
                string.Equals(status.TerminalName, terminalName, StringComparison.OrdinalIgnoreCase));

            var disabledAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("disabled_at"))
                ? (DateTime?)null
                : pairingsReader.GetDateTime("disabled_at");
            var pairedAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("paired_at"))
                ? (DateTime?)null
                : pairingsReader.GetDateTime("paired_at");
            var pairingCode = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("pairing_code"))
                ? string.Empty
                : pairingsReader.GetString("pairing_code");
            var pairingLastSeenAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("last_seen_at"))
                ? (DateTime?)null
                : pairingsReader.GetDateTime("last_seen_at");
            var enabled = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("enabled")) || pairingsReader.GetBoolean("enabled");
            var revokedAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("revoked_at"))
                ? (DateTime?)null
                : pairingsReader.GetDateTime("revoked_at");

            var isFreshHeartbeat = pairingLastSeenAt.HasValue &&
                DateTime.Now - pairingLastSeenAt.Value <= TimeSpan.FromSeconds(OnlineHeartbeatWindowSeconds);
            var pairingStatus = disabledAt.HasValue || !enabled
                ? "Disabled"
                : revokedAt.HasValue
                    ? "Revoked"
                : pairedAt.HasValue
                    ? isFreshHeartbeat ? "Online" : "Offline"
                    : "Pending";

            var target = existing ?? new TerminalHealthStatus
            {
                TerminalName = terminalName,
                Mode = "Child",
                DatabaseHost = TerminalNetworkInfoService.GetBestLocalIpAddress(),
                LastSeenAt = DateTime.MinValue
            };

            target.PairingStatus = pairingStatus;
            target.PairingCode = pairingCode;
            target.PairingExpiresAt = null;
            target.PairedAt = pairedAt;
            target.IsDisabled = disabledAt.HasValue || !enabled;
            target.RevokedAt = revokedAt;
            target.TerminalId = ReadString(pairingsReader, "terminal_id");
            target.DeviceType = ReadString(pairingsReader, "device_type");
            target.Platform = ReadString(pairingsReader, "platform");
            target.DeviceName = ReadString(pairingsReader, "device_name");
            target.AppVersion = string.IsNullOrWhiteSpace(target.AppVersion)
                ? ReadString(pairingsReader, "app_version")
                : target.AppVersion;
            target.LastIpAddress = ReadString(pairingsReader, "last_ip_address");
            target.LastSyncEventId = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("last_sync_event_id"))
                ? 0
                : pairingsReader.GetInt64("last_sync_event_id");
            target.CurrentUserId = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("current_user_id"))
                ? null
                : pairingsReader.GetInt32("current_user_id");
            target.WebSocketStatus = ReadString(pairingsReader, "websocket_status");
            target.WebSocketConnectedAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("websocket_connected_at"))
                ? null
                : pairingsReader.GetDateTime("websocket_connected_at");
            target.WebSocketDisconnectedAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("websocket_disconnected_at"))
                ? null
                : pairingsReader.GetDateTime("websocket_disconnected_at");
            target.WebSocketLastMessageAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("websocket_last_message_at"))
                ? null
                : pairingsReader.GetDateTime("websocket_last_message_at");

            if (pairingLastSeenAt.HasValue && (target.LastSeenAt <= DateTime.MinValue.AddDays(1) || pairingLastSeenAt > target.LastSeenAt))
            {
                target.LastSeenAt = pairingLastSeenAt.Value;
            }

            if (string.IsNullOrWhiteSpace(target.LastStatus))
            {
                target.LastStatus = pairingStatus;
            }

            if (existing == null)
            {
                rows.Add(target);
            }
        }

        rows = rows
            .OrderByDescending(status => string.Equals(status.Mode, "Mother", StringComparison.OrdinalIgnoreCase))
            .ThenBy(status => status.TerminalName)
            .ToList();

        // Always show the live Mother LAN IP so Client reconnect works after DHCP / IP change.
        var motherLanIp = TerminalNetworkInfoService.GetBestLocalIpAddress();
        foreach (var row in rows)
        {
            if (row.IsMother ||
                row.IsPendingPairing ||
                row.IsExpiredPairing ||
                string.IsNullOrWhiteSpace(row.DatabaseHost) ||
                string.Equals(row.DatabaseHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(row.DatabaseHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                row.DatabaseHost = motherLanIp;
            }
        }

        return rows;
    }

    public async Task<(bool Success, string Message)> DisableTerminalAsync(string terminalName)
    {
        return await UpdateClientTerminalAsync(
            terminalName,
            "UPDATE terminal_pairings SET enabled = FALSE, disabled_at = NOW(), client_status = 'disabled', updated_at = NOW() WHERE terminal_name = @terminalName",
            "disabled");
    }

    public async Task<(bool Success, string Message)> RevokeTerminalTokenAsync(string terminalName)
    {
        return await UpdateClientTerminalAsync(
            terminalName,
            "UPDATE terminal_pairings SET token_hash = NULL, revoked_at = NOW(), client_status = 'revoked', updated_at = NOW() WHERE terminal_name = @terminalName",
            "revoked");
    }

    public async Task<(bool Success, string Message)> ForceLogoutTerminalAsync(string terminalName)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return (false, "Client sessions can be managed from the mother terminal only.");
        }

        var cleanName = (terminalName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            return (false, "Terminal name is missing.");
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientPairingColumnsAsync(connection);

        const string sql = @"
            UPDATE client_user_sessions s
            INNER JOIN terminal_pairings p ON p.terminal_id = s.terminal_id
            SET s.revoked_at = NOW()
            WHERE p.terminal_name = @terminalName
              AND s.revoked_at IS NULL";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@terminalName", cleanName);
        var rows = await command.ExecuteNonQueryAsync();
        return (true, rows > 0 ? $"{cleanName} has been logged out." : $"{cleanName} has no active Client session.");
    }

    public async Task<(bool Success, string Message)> RenameTerminalAsync(string terminalName, string newTerminalName)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return (false, "Terminals can be renamed from the mother terminal only.");
        }

        var cleanName = (terminalName ?? string.Empty).Trim();
        var cleanNewName = (newTerminalName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanName) || string.IsNullOrWhiteSpace(cleanNewName))
        {
            return (false, "Terminal name is missing.");
        }

        if (string.Equals(cleanName, cleanNewName, StringComparison.OrdinalIgnoreCase))
        {
            return (true, "Terminal name is unchanged.");
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        await TerminalPairingService.EnsureTableAsync(connection);

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var pairingCommand = new MySqlCommand(
                "UPDATE terminal_pairings SET terminal_name = @newTerminalName, updated_at = NOW() WHERE terminal_name = @terminalName",
                connection,
                transaction))
            {
                pairingCommand.Parameters.AddWithValue("@terminalName", cleanName);
                pairingCommand.Parameters.AddWithValue("@newTerminalName", cleanNewName);
                await pairingCommand.ExecuteNonQueryAsync();
            }

            await using (var healthCommand = new MySqlCommand(
                "UPDATE terminal_health SET terminal_name = @newTerminalName, updated_at = NOW() WHERE terminal_name = @terminalName AND terminal_mode <> 'Mother'",
                connection,
                transaction))
            {
                healthCommand.Parameters.AddWithValue("@terminalName", cleanName);
                healthCommand.Parameters.AddWithValue("@newTerminalName", cleanNewName);
                await healthCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return (true, $"{cleanName} renamed to {cleanNewName}.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Success, string Message)> DeleteTerminalAsync(string terminalName)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return (false, "Terminals can be deleted from the mother terminal only.");
        }

        var cleanName = (terminalName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            return (false, "Terminal name is missing.");
        }

        var currentConfig = TerminalConfigurationService.GetConfiguration();
        if (string.Equals(cleanName, currentConfig.TerminalName, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "You cannot delete this terminal while it is in use.");
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureTableAsync(connection);
        await TerminalPairingService.EnsureTableAsync(connection);

        const string statusSql = @"
            SELECT terminal_mode, last_seen_at
            FROM terminal_health
            WHERE terminal_name = @terminalName
            LIMIT 1";

        await using (var statusCommand = new MySqlCommand(statusSql, connection))
        {
            statusCommand.Parameters.AddWithValue("@terminalName", cleanName);
            await using var reader = await statusCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var mode = reader.GetString("terminal_mode");
                if (string.Equals(mode, "Mother", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Mother terminals cannot be deleted from Terminal Health.");
                }

                var lastSeenAt = reader.GetDateTime("last_seen_at");
                if (DateTime.Now - lastSeenAt <= TimeSpan.FromSeconds(OnlineHeartbeatWindowSeconds))
                {
                    return (false, "This terminal is online. Disconnect it first, then refresh and delete.");
                }
            }
        }

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var deletedRows = 0;

            const string deleteHealthSql = "DELETE FROM terminal_health WHERE terminal_name = @terminalName AND terminal_mode <> 'Mother'";
            await using (var deleteHealthCommand = new MySqlCommand(deleteHealthSql, connection, transaction))
            {
                deleteHealthCommand.Parameters.AddWithValue("@terminalName", cleanName);
                deletedRows += await deleteHealthCommand.ExecuteNonQueryAsync();
            }

            const string deletePairingSql = "DELETE FROM terminal_pairings WHERE terminal_name = @terminalName";
            await using (var deletePairingCommand = new MySqlCommand(deletePairingSql, connection, transaction))
            {
                deletePairingCommand.Parameters.AddWithValue("@terminalName", cleanName);
                deletedRows += await deletePairingCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            return deletedRows > 0
                ? (true, $"{cleanName} has been removed from Terminal Health.")
                : (false, "No matching Client POS terminal was found.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public static async Task EnsureTableAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
        const string sql = @"
            CREATE TABLE IF NOT EXISTS terminal_health (
                terminal_name VARCHAR(120) NOT NULL PRIMARY KEY,
                terminal_mode ENUM('Mother', 'Child') NOT NULL,
                database_host VARCHAR(255) NOT NULL,
                app_version VARCHAR(40) NULL,
                last_seen_at DATETIME NOT NULL,
                last_status VARCHAR(40) NOT NULL DEFAULT 'online',
                last_error TEXT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_terminal_health_seen (last_seen_at),
                INDEX idx_terminal_health_mode (terminal_mode)
            ) ENGINE=InnoDB";

        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(bool Success, string Message)> UpdateClientTerminalAsync(
        string terminalName,
        string sql,
        string action)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return (false, "Client terminals can be managed from the mother terminal only.");
        }

        var cleanName = (terminalName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            return (false, "Terminal name is missing.");
        }

        var currentConfig = TerminalConfigurationService.GetConfiguration();
        if (string.Equals(cleanName, currentConfig.TerminalName, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "This Mother terminal cannot be managed as a Client terminal.");
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientPairingColumnsAsync(connection);

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@terminalName", cleanName);
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0
            ? (true, $"{cleanName} has been {action}.")
            : (false, "No matching Client POS terminal was found.");
    }

    private static async Task EnsureClientPairingColumnsAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;

        await TerminalPairingService.EnsureTableAsync(connection);

        var statements = new[]
        {
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS terminal_id VARCHAR(64) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS device_type VARCHAR(80) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS platform VARCHAR(80) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS device_name VARCHAR(160) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS app_version VARCHAR(40) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS last_seen_at DATETIME NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS current_user_id INT NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS enabled BOOLEAN NOT NULL DEFAULT TRUE",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS last_ip_address VARCHAR(45) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS last_sync_event_id BIGINT NOT NULL DEFAULT 0",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS revoked_at DATETIME NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS websocket_status VARCHAR(40) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS websocket_connected_at DATETIME NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS websocket_disconnected_at DATETIME NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS websocket_last_message_at DATETIME NULL",
            @"CREATE TABLE IF NOT EXISTS client_user_sessions (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                terminal_id VARCHAR(64) NOT NULL,
                user_id INT NOT NULL,
                session_token_hash VARCHAR(128) NOT NULL,
                expires_at DATETIME NOT NULL,
                revoked_at DATETIME NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_seen_at DATETIME NULL,
                INDEX idx_client_user_sessions_terminal (terminal_id),
                INDEX idx_client_user_sessions_user (user_id),
                INDEX idx_client_user_sessions_token (session_token_hash),
                INDEX idx_client_user_sessions_expiry (expires_at)
            ) ENGINE=InnoDB"
        };

        foreach (var statement in statements)
        {
            await using var command = new MySqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string ReadString(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    public void Dispose()
    {
        Stop();
    }
}
