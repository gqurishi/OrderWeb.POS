using MySqlConnector;
using OrderWeb.Contracts.Access;

namespace POS_in_NET.Services;

/// <summary>
/// Per-Client-terminal feature grants stored on Mother. Web Orders is never persisted.
/// Reservations are on by default so Client dashboards match Mother.
/// </summary>
public sealed class ClientTerminalAccessService
{
    private const string PolicyMarkerTerminalId = "__orderweb_policy__";
    private const string ReservationsDefaultMigrationKey = "reservations_default_v2";
    private static int _reservationsDefaultMigrated; // 0 = unknown, 1 = done

    private readonly DatabaseService _databaseService;

    public ClientTerminalAccessService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public static async Task EnsureTableAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged)
        {
            return;
        }

        await using var command = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS client_terminal_access (
                terminal_id VARCHAR(64) NOT NULL,
                feature_key VARCHAR(80) NOT NULL,
                is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (terminal_id, feature_key),
                INDEX idx_client_terminal_access_terminal (terminal_id)
            ) ENGINE=InnoDB", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlySet<string>> GetGrantedFeaturesAsync(
        string? terminalId,
        CancellationToken cancellationToken = default)
    {
        terminalId = (terminalId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(terminalId))
        {
            return ClientAccessPolicy.DefaultGrantedFeatures;
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection);
        await MigrateReservationsDefaultOnAsync(connection, cancellationToken);

        var stored = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using (var command = new MySqlCommand(
            "SELECT feature_key, is_enabled FROM client_terminal_access WHERE terminal_id = @terminalId",
            connection))
        {
            command.Parameters.AddWithValue("@terminalId", terminalId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(key) ||
                    string.Equals(key, ReservationsDefaultMigrationKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                stored[key] = !reader.IsDBNull(1) && Convert.ToBoolean(reader.GetValue(1));
            }
        }

        if (stored.Count == 0)
        {
            await SeedDefaultsAsync(connection, terminalId, cancellationToken);
            return ClientAccessPolicy.DefaultGrantedFeatures;
        }

        return ClientAccessPolicy.FilterFeatures(stored.Where(pair => pair.Value).Select(pair => pair.Key));
    }

    public async Task<bool> HasFeatureAsync(string? terminalId, string feature, CancellationToken cancellationToken = default)
    {
        if (!ClientAccessPolicy.IsFeatureGrantable(feature))
        {
            return false;
        }

        var granted = await GetGrantedFeaturesAsync(terminalId, cancellationToken);
        return granted.Contains(feature);
    }

    public async Task SaveGrantedFeaturesAsync(
        string terminalId,
        IEnumerable<string> features,
        CancellationToken cancellationToken = default)
    {
        terminalId = (terminalId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(terminalId))
        {
            throw new ArgumentException("A Client terminal ID is required.", nameof(terminalId));
        }

        var enabled = ClientAccessPolicy.FilterFeatures(features);
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var (key, _) in ClientAccessPolicy.EditableFeatures)
            {
                await using var command = new MySqlCommand(@"
                    INSERT INTO client_terminal_access (terminal_id, feature_key, is_enabled, updated_at)
                    VALUES (@terminalId, @featureKey, @enabled, NOW())
                    ON DUPLICATE KEY UPDATE is_enabled = @enabled, updated_at = NOW()",
                    connection,
                    transaction);
                command.Parameters.AddWithValue("@terminalId", terminalId);
                command.Parameters.AddWithValue("@featureKey", key);
                command.Parameters.AddWithValue("@enabled", enabled.Contains(key));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task SeedDefaultsAsync(
        MySqlConnection connection,
        string terminalId,
        CancellationToken cancellationToken)
    {
        foreach (var (key, _) in ClientAccessPolicy.EditableFeatures)
        {
            await using var command = new MySqlCommand(@"
                INSERT INTO client_terminal_access (terminal_id, feature_key, is_enabled, updated_at)
                VALUES (@terminalId, @featureKey, @enabled, NOW())
                ON DUPLICATE KEY UPDATE feature_key = feature_key",
                connection);
            command.Parameters.AddWithValue("@terminalId", terminalId);
            command.Parameters.AddWithValue("@featureKey", key);
            command.Parameters.AddWithValue("@enabled", ClientAccessPolicy.DefaultGrantedFeatures.Contains(key));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// One-time: enable Reservations for every Client terminal that still has the
    /// old product default (off). After this runs, Terminal Health remains the
    /// place to turn Reservations off for a specific Client.
    /// </summary>
    private static async Task MigrateReservationsDefaultOnAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _reservationsDefaultMigrated) == 1)
        {
            return;
        }

        if (!ClientAccessPolicy.DefaultGrantedFeatures.Contains(OrderWeb.Contracts.Features.PosFeatureKeys.Reservations))
        {
            Volatile.Write(ref _reservationsDefaultMigrated, 1);
            return;
        }

        await using (var existsCommand = new MySqlCommand(@"
            SELECT 1
            FROM client_terminal_access
            WHERE terminal_id = @terminalId AND feature_key = @featureKey
            LIMIT 1", connection))
        {
            existsCommand.Parameters.AddWithValue("@terminalId", PolicyMarkerTerminalId);
            existsCommand.Parameters.AddWithValue("@featureKey", ReservationsDefaultMigrationKey);
            var existing = await existsCommand.ExecuteScalarAsync(cancellationToken);
            if (existing is not null)
            {
                Volatile.Write(ref _reservationsDefaultMigrated, 1);
                return;
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var marker = new MySqlCommand(@"
                INSERT INTO client_terminal_access (terminal_id, feature_key, is_enabled, updated_at)
                VALUES (@terminalId, @featureKey, TRUE, NOW())",
                connection,
                transaction))
            {
                marker.Parameters.AddWithValue("@terminalId", PolicyMarkerTerminalId);
                marker.Parameters.AddWithValue("@featureKey", ReservationsDefaultMigrationKey);
                try
                {
                    await marker.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (MySqlException ex) when (ex.Number is 1062)
                {
                    // Another process already claimed the migration.
                    await transaction.RollbackAsync(cancellationToken);
                    Volatile.Write(ref _reservationsDefaultMigrated, 1);
                    return;
                }
            }

            await using (var upgrade = new MySqlCommand(@"
                UPDATE client_terminal_access
                SET is_enabled = TRUE, updated_at = NOW()
                WHERE feature_key = @featureKey
                  AND terminal_id <> @policyTerminal
                  AND is_enabled = FALSE",
                connection,
                transaction))
            {
                upgrade.Parameters.AddWithValue("@featureKey", OrderWeb.Contracts.Features.PosFeatureKeys.Reservations);
                upgrade.Parameters.AddWithValue("@policyTerminal", PolicyMarkerTerminalId);
                await upgrade.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            Volatile.Write(ref _reservationsDefaultMigrated, 1);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
