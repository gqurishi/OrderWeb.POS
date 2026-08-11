using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class TerminalHealthService : IDisposable
{
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

        const string sql = @"
            SELECT terminal_name, terminal_mode, database_host, last_seen_at, last_status, last_error
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
                    LastSeenAt = reader.GetDateTime("last_seen_at"),
                    LastStatus = reader.GetString("last_status"),
                    LastError = reader.IsDBNull(reader.GetOrdinal("last_error")) ? string.Empty : reader.GetString("last_error")
                });
            }
        }

        const string pairingsSql = @"
            SELECT terminal_name, pairing_code, pairing_expires_at, paired_at, disabled_at
            FROM terminal_pairings
            ORDER BY terminal_name";

        await using var pairingsCommand = new MySqlCommand(pairingsSql, connection);
        await using var pairingsReader = await pairingsCommand.ExecuteReaderAsync();
        while (await pairingsReader.ReadAsync())
        {
            var terminalName = pairingsReader.GetString("terminal_name");
            var existing = rows.FirstOrDefault(status =>
                string.Equals(status.TerminalName, terminalName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                continue;
            }

            var disabledAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("disabled_at"))
                ? (DateTime?)null
                : pairingsReader.GetDateTime("disabled_at");
            var pairedAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("paired_at"))
                ? (DateTime?)null
                : pairingsReader.GetDateTime("paired_at");
            var expiresAt = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("pairing_expires_at"))
                ? (DateTime?)null
                : pairingsReader.GetDateTime("pairing_expires_at");
            var pairingCode = pairingsReader.IsDBNull(pairingsReader.GetOrdinal("pairing_code"))
                ? string.Empty
                : pairingsReader.GetString("pairing_code");

            var pairingStatus = disabledAt.HasValue
                ? "Disabled"
                : pairedAt.HasValue
                    ? "Offline"
                    : expiresAt.HasValue && expiresAt.Value <= DateTime.Now
                        ? "Expired"
                        : "Pending";

            rows.Add(new TerminalHealthStatus
            {
                TerminalName = terminalName,
                Mode = "Child",
                DatabaseHost = TerminalNetworkInfoService.GetBestLocalIpAddress(),
                LastSeenAt = DateTime.MinValue,
                LastStatus = pairingStatus,
                PairingStatus = pairingStatus,
                PairingCode = pairingCode,
                PairingExpiresAt = expiresAt,
                PairedAt = pairedAt,
                IsDisabled = disabledAt.HasValue
            });
        }

        rows = rows
            .OrderByDescending(status => string.Equals(status.Mode, "Mother", StringComparison.OrdinalIgnoreCase))
            .ThenBy(status => status.TerminalName)
            .ToList();

        return rows;
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
                if (DateTime.Now - lastSeenAt <= TimeSpan.FromSeconds(45))
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
                : (false, "No matching child terminal was found.");
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

    public void Dispose()
    {
        Stop();
    }
}
