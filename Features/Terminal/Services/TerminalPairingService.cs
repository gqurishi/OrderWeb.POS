using System.Security.Cryptography;
using MySqlConnector;

namespace POS_in_NET.Services;

public sealed record TerminalPairingResult(bool Success, string Message, string? PairingCode = null, DateTime? ExpiresAt = null);

public static class TerminalPairingService
{
    private const int PairingCodeMin = 100000;
    private const int PairingCodeMaxExclusive = 1000000;

    public static async Task<TerminalPairingResult> CreateChildPairingAsync(string terminalName, int expiryMinutes = 30)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return new TerminalPairingResult(false, "Child terminal pairing can be created on the mother terminal only.");
        }

        var cleanName = NormalizeTerminalName(terminalName);
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            return new TerminalPairingResult(false, "Enter a terminal name.");
        }

        var code = GeneratePairingCode();
        var expiresAt = DateTime.Now.AddMinutes(Math.Max(5, expiryMinutes));

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureTableAsync(connection);

        const string sql = @"
            INSERT INTO terminal_pairings
                (terminal_name, terminal_mode, pairing_code, pairing_expires_at, paired_at, disabled_at, created_at, updated_at)
            VALUES
                (@terminalName, 'Child', @pairingCode, @expiresAt, NULL, NULL, NOW(), NOW())
            ON DUPLICATE KEY UPDATE
                pairing_code = VALUES(pairing_code),
                pairing_expires_at = VALUES(pairing_expires_at),
                paired_at = NULL,
                disabled_at = NULL,
                updated_at = NOW()";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@terminalName", cleanName);
        command.Parameters.AddWithValue("@pairingCode", code);
        command.Parameters.AddWithValue("@expiresAt", expiresAt);
        await command.ExecuteNonQueryAsync();

        return new TerminalPairingResult(true, $"Pairing code created for {cleanName}.", code, expiresAt);
    }

    public static async Task<TerminalPairingResult> ValidateAndActivateChildAsync(
        string terminalName,
        string pairingCode,
        MySqlConnection? openConnection = null)
    {
        var cleanName = NormalizeTerminalName(terminalName);
        var cleanCode = NormalizePairingCode(pairingCode);
        if (string.IsNullOrWhiteSpace(cleanName) || string.IsNullOrWhiteSpace(cleanCode))
        {
            return new TerminalPairingResult(false, "Terminal name and pairing code are required.");
        }

        var ownsConnection = openConnection == null;
        await using var createdConnection = ownsConnection
            ? new MySqlConnection(TerminalConfigurationService.GetPosConnectionString(connectionTimeoutSeconds: 5, pooled: false))
            : null;
        var connection = openConnection ?? createdConnection!;
        if (ownsConnection)
        {
            await connection.OpenAsync();
        }

        await EnsureTableAsync(connection);

        const string selectSql = @"
            SELECT pairing_code, pairing_expires_at, paired_at, disabled_at
            FROM terminal_pairings
            WHERE terminal_name = @terminalName
            LIMIT 1";

        await using (var selectCommand = new MySqlCommand(selectSql, connection))
        {
            selectCommand.Parameters.AddWithValue("@terminalName", cleanName);
            await using var reader = (MySqlDataReader)await selectCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return new TerminalPairingResult(false, "This terminal has not been added on the mother terminal.");
            }

            if (!reader.IsDBNull(reader.GetOrdinal("disabled_at")))
            {
                return new TerminalPairingResult(false, "This terminal is disabled on the mother terminal.");
            }

            if (!reader.IsDBNull(reader.GetOrdinal("paired_at")))
            {
                return new TerminalPairingResult(false, "This pairing code has already been used. Create a new code on the mother terminal.");
            }

            var storedCode = reader.IsDBNull(reader.GetOrdinal("pairing_code"))
                ? string.Empty
                : reader.GetString("pairing_code");
            if (!string.Equals(storedCode, cleanCode, StringComparison.Ordinal))
            {
                return new TerminalPairingResult(false, "Pairing code is incorrect.");
            }

            var expiresAt = reader.IsDBNull(reader.GetOrdinal("pairing_expires_at"))
                ? DateTime.MinValue
                : reader.GetDateTime("pairing_expires_at");
            if (expiresAt <= DateTime.Now)
            {
                return new TerminalPairingResult(false, "Pairing code has expired. Create a new code on the mother terminal.");
            }
        }

        const string activateSql = @"
            UPDATE terminal_pairings
            SET paired_at = NOW(),
                pairing_code = NULL,
                pairing_expires_at = NULL,
                updated_at = NOW()
            WHERE terminal_name = @terminalName
              AND pairing_code = @pairingCode
              AND paired_at IS NULL
              AND disabled_at IS NULL";

        await using var activateCommand = new MySqlCommand(activateSql, connection);
        activateCommand.Parameters.AddWithValue("@terminalName", cleanName);
        activateCommand.Parameters.AddWithValue("@pairingCode", cleanCode);
        var rows = await activateCommand.ExecuteNonQueryAsync();
        return rows > 0
            ? new TerminalPairingResult(true, "Child terminal paired successfully.")
            : new TerminalPairingResult(false, "Could not activate pairing. Create a new code and try again.");
    }

    public static async Task EnsureTableAsync(MySqlConnection connection)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS terminal_pairings (
                terminal_name VARCHAR(120) NOT NULL PRIMARY KEY,
                terminal_mode ENUM('Child') NOT NULL DEFAULT 'Child',
                pairing_code VARCHAR(12) NULL,
                pairing_expires_at DATETIME NULL,
                paired_at DATETIME NULL,
                disabled_at DATETIME NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_terminal_pairings_code (pairing_code),
                INDEX idx_terminal_pairings_expiry (pairing_expires_at),
                INDEX idx_terminal_pairings_paired (paired_at),
                INDEX idx_terminal_pairings_disabled (disabled_at)
            ) ENGINE=InnoDB";

        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string GeneratePairingCode()
    {
        return RandomNumberGenerator.GetInt32(PairingCodeMin, PairingCodeMaxExclusive).ToString();
    }

    private static string NormalizeTerminalName(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string NormalizePairingCode(string? value)
    {
        return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
    }
}
