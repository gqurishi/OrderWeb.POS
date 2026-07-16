using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class PrinterRoutingSettings
{
    public bool UseAllJobsPrinter { get; init; }
    public int? AllJobsPrinterId { get; init; }
}

/// <summary>
/// Resolves the optional physical-printer override without changing dedicated assignments.
/// </summary>
public sealed class PrinterRoutingService
{
    private const string ModeKey = "print_routing_mode";
    private const string PrinterIdKey = "all_jobs_printer_id";
    private readonly DatabaseService _databaseService;
    private readonly NetworkPrinterDatabaseService _printerDatabaseService;

    public PrinterRoutingService(
        DatabaseService databaseService,
        NetworkPrinterDatabaseService printerDatabaseService)
    {
        _databaseService = databaseService;
        _printerDatabaseService = printerDatabaseService;
    }

    public async Task<PrinterRoutingSettings> GetSettingsAsync()
    {
        await EnsureSettingsTableAsync();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT setting_key, setting_value
            FROM printer_settings
            WHERE setting_key IN (@modeKey, @printerIdKey)";
        command.Parameters.AddWithValue("@modeKey", ModeKey);
        command.Parameters.AddWithValue("@printerIdKey", PrinterIdKey);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        var useAllJobs = values.TryGetValue(ModeKey, out var mode) &&
                         string.Equals(mode, "all_jobs", StringComparison.OrdinalIgnoreCase);
        var printerId = values.TryGetValue(PrinterIdKey, out var rawPrinterId) &&
                        int.TryParse(rawPrinterId, out var parsedPrinterId)
            ? parsedPrinterId
            : (int?)null;

        return new PrinterRoutingSettings
        {
            UseAllJobsPrinter = useAllJobs,
            AllJobsPrinterId = printerId
        };
    }

    public async Task SaveSettingsAsync(bool useAllJobsPrinter, int? allJobsPrinterId)
    {
        if (useAllJobsPrinter)
        {
            if (!allJobsPrinterId.HasValue)
            {
                throw new InvalidOperationException("Select an all-jobs printer before enabling this mode.");
            }

            var selectedPrinter = await _printerDatabaseService.GetPrinterByIdAsync(allJobsPrinterId.Value);
            if (selectedPrinter == null || !selectedPrinter.IsEnabled)
            {
                throw new InvalidOperationException("The selected all-jobs printer is unavailable or disabled.");
            }

            if (selectedPrinter.PrinterType == NetworkPrinterType.Label)
            {
                throw new InvalidOperationException("Label printers cannot be used for all jobs.");
            }
        }

        await EnsureSettingsTableAsync();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await UpsertSettingAsync(connection, transaction, ModeKey, useAllJobsPrinter ? "all_jobs" : "dedicated");
        await UpsertSettingAsync(connection, transaction, PrinterIdKey, allJobsPrinterId?.ToString() ?? string.Empty);
        await transaction.CommitAsync();
    }

    public async Task<NetworkPrinter?> GetAllJobsPrinterAsync()
    {
        var settings = await GetSettingsAsync();
        if (!settings.UseAllJobsPrinter || !settings.AllJobsPrinterId.HasValue)
        {
            return null;
        }

        var printer = await _printerDatabaseService.GetPrinterByIdAsync(settings.AllJobsPrinterId.Value);
        return printer is { IsEnabled: true } && printer.PrinterType != NetworkPrinterType.Label
            ? printer
            : null;
    }

    public async Task<NetworkPrinter?> ResolvePrinterAsync(params NetworkPrinterType[] dedicatedTypes)
    {
        var settings = await GetSettingsAsync();
        if (settings.UseAllJobsPrinter)
        {
            if (!settings.AllJobsPrinterId.HasValue)
            {
                return null;
            }

            var selectedPrinter = await _printerDatabaseService.GetPrinterByIdAsync(settings.AllJobsPrinterId.Value);
            return selectedPrinter is { IsEnabled: true } && selectedPrinter.PrinterType != NetworkPrinterType.Label
                ? selectedPrinter
                : null;
        }

        foreach (var type in dedicatedTypes)
        {
            var printer = (await _printerDatabaseService.GetPrintersByTypeAsync(type))
                .FirstOrDefault(candidate => candidate.IsEnabled);
            if (printer != null)
            {
                return printer;
            }
        }

        return null;
    }

    public async Task DisableIfSelectedAsync(int printerId)
    {
        var settings = await GetSettingsAsync();
        if (settings.AllJobsPrinterId == printerId)
        {
            await SaveSettingsAsync(false, null);
        }
    }

    private async Task EnsureSettingsTableAsync()
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS printer_settings (
                id INT PRIMARY KEY AUTO_INCREMENT,
                setting_key VARCHAR(100) NOT NULL UNIQUE,
                setting_value TEXT NULL,
                description VARCHAR(255) NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertSettingAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string key,
        string value)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO printer_settings (setting_key, setting_value)
            VALUES (@key, @value)
            ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync();
    }
}
