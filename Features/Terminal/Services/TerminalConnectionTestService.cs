using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed record TerminalConnectionTestResult(bool Success, string Title, string Message);

public static class TerminalConnectionTestService
{
    public static async Task<TerminalConnectionTestResult> TestAsync(int timeoutSeconds = 5)
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            return new TerminalConnectionTestResult(false, "Terminal Not Setup", "Please complete terminal setup first.");
        }

        var config = TerminalConfigurationService.GetConfiguration();

        try
        {
            await using var connection = new MySqlConnection(
                TerminalConfigurationService.GetPosConnectionString(
                    connectionTimeoutSeconds: timeoutSeconds,
                    pooled: false));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            await connection.OpenAsync(cts.Token);

            await using var command = new MySqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cts.Token);

            if (config.Mode == TerminalMode.Child)
            {
                var schemaGate = await ChildSchemaVersionGateService.CheckAsync(connection, cts.Token);
                if (!schemaGate.IsCompatible)
                {
                    return new TerminalConnectionTestResult(
                        false,
                        "Mother Database Outdated",
                        schemaGate.Message);
                }

                return new TerminalConnectionTestResult(
                    true,
                    "Connected",
                    $"Connected to mother terminal ({config.DatabaseHost}). Database schema version {schemaGate.CurrentSchemaVersion}.");
            }

            return new TerminalConnectionTestResult(true, "Connected", "Connected to Local Mother Database");
        }
        catch (Exception ex)
        {
            return config.Mode == TerminalMode.Child
                ? new TerminalConnectionTestResult(
                    false,
                    "Mother Terminal Offline",
                    $"Cannot connect to Mother Terminal at {config.DatabaseHost}. Check LAN cable/Wi-Fi, IP address, firewall, and MariaDB.")
                : new TerminalConnectionTestResult(
                    false,
                    "Local Database Offline",
                    $"Cannot connect to the local database. {ex.Message}");
        }
    }
}
