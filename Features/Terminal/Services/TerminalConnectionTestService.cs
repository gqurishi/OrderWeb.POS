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
        if (config.Mode == TerminalMode.Child)
        {
            try
            {
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
                };

                using var response = await httpClient.GetAsync(
                    $"http://{config.DatabaseHost}:{config.MotherApiPort}/health");

                return response.IsSuccessStatusCode
                    ? new TerminalConnectionTestResult(
                        true,
                        "Connected",
                        $"Connected to Mother API at {config.DatabaseHost}:{config.MotherApiPort}.")
                    : new TerminalConnectionTestResult(
                        false,
                        "Mother API Unavailable",
                        $"Mother API returned {(int)response.StatusCode}. Check Mother POS and pairing setup.");
            }
            catch (Exception ex)
            {
                return new TerminalConnectionTestResult(
                    false,
                    "Mother API Offline",
                    $"Cannot connect to Mother API at {config.DatabaseHost}:{config.MotherApiPort}. {ex.Message}");
            }
        }

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

            return new TerminalConnectionTestResult(true, "Connected", "Connected to Local Mother Database");
        }
        catch (Exception ex)
        {
            return new TerminalConnectionTestResult(
                false,
                "Local Database Offline",
                $"Cannot connect to the local database. {ex.Message}");
        }
    }
}
