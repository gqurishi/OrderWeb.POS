using POS_in_NET.Models;

namespace POS_in_NET.Services;

public enum PrintOwnershipMode
{
    Direct,
    MotherQueue,
    NamedQueueTerminal,
    AnyQueueTerminal
}

public sealed record PrintingPolicy(
    PrintOwnershipMode OwnershipMode,
    string QueueOwnerTerminalName,
    bool DirectPrintingEnabled,
    string Description);

public sealed record PrintQueueOwnershipCheck(bool Allowed, string Reason);

public class PrintingPolicyService
{
    private const string OwnershipKey = "printing.ownership.mode";
    private const string QueueOwnerKey = "printing.queue.owner_terminal_name";
    private const string DirectEnabledKey = "printing.direct.enabled";
    private const string ConfigScopeKey = "printing.config.scope";

    private readonly DatabaseService _databaseService;

    public PrintingPolicyService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task EnsureDefaultsAsync()
    {
        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO settings (setting_key, setting_value)
            VALUES
                (@configScopeKey, 'shared_database'),
                (@ownershipKey, 'direct'),
                (@queueOwnerKey, 'mother'),
                (@directEnabledKey, 'true')
            ON DUPLICATE KEY UPDATE setting_key = setting_key";
        command.Parameters.AddWithValue("@configScopeKey", ConfigScopeKey);
        command.Parameters.AddWithValue("@ownershipKey", OwnershipKey);
        command.Parameters.AddWithValue("@queueOwnerKey", QueueOwnerKey);
        command.Parameters.AddWithValue("@directEnabledKey", DirectEnabledKey);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<PrintingPolicy> GetPolicyAsync()
    {
        await EnsureDefaultsAsync();

        var values = await GetSettingsAsync(OwnershipKey, QueueOwnerKey, DirectEnabledKey);
        var modeValue = values.GetValueOrDefault(OwnershipKey, "direct");
        var ownerValue = values.GetValueOrDefault(QueueOwnerKey, "mother");
        var directEnabled = ParseBoolean(values.GetValueOrDefault(DirectEnabledKey, "true"), true);

        var ownershipMode = ParseOwnershipMode(modeValue);
        var description = ownershipMode switch
        {
            PrintOwnershipMode.Direct => "Any terminal can print directly to shared network printers.",
            PrintOwnershipMode.MotherQueue => "Mother terminal owns the shared print queue.",
            PrintOwnershipMode.NamedQueueTerminal => $"Terminal '{ownerValue}' owns the shared print queue.",
            PrintOwnershipMode.AnyQueueTerminal => "Any terminal may process the shared print queue.",
            _ => "Any terminal can print directly to shared network printers."
        };

        return new PrintingPolicy(ownershipMode, ownerValue, directEnabled, description);
    }

    public async Task<PrintQueueOwnershipCheck> CanProcessSharedQueueAsync()
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            return new PrintQueueOwnershipCheck(false, "Terminal setup is not complete.");
        }

        var policy = await GetPolicyAsync();
        var terminalConfig = TerminalConfigurationService.GetConfiguration();

        return policy.OwnershipMode switch
        {
            PrintOwnershipMode.Direct => terminalConfig.IsMother
                ? new PrintQueueOwnershipCheck(true, "Direct print mode: mother processes fallback/online print queue.")
                : new PrintQueueOwnershipCheck(false, "Direct print mode: child terminals print directly; shared queue is mother-only."),

            PrintOwnershipMode.MotherQueue => terminalConfig.IsMother
                ? new PrintQueueOwnershipCheck(true, "Mother terminal owns print queue.")
                : new PrintQueueOwnershipCheck(false, "Child terminal: print queue is owned by the mother terminal."),

            PrintOwnershipMode.NamedQueueTerminal => string.Equals(
                    policy.QueueOwnerTerminalName.Trim(),
                    terminalConfig.TerminalName.Trim(),
                    StringComparison.OrdinalIgnoreCase)
                ? new PrintQueueOwnershipCheck(true, $"This terminal owns print queue: {terminalConfig.TerminalName}.")
                : new PrintQueueOwnershipCheck(false, $"Print queue owner is {policy.QueueOwnerTerminalName}."),

            PrintOwnershipMode.AnyQueueTerminal => new PrintQueueOwnershipCheck(true, "Any terminal may process print queue."),

            _ => new PrintQueueOwnershipCheck(false, "Unknown print ownership mode.")
        };
    }

    private async Task<Dictionary<string, string>> GetSettingsAsync(params string[] keys)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT setting_key, setting_value
            FROM settings
            WHERE setting_key IN ({string.Join(",", keys.Select((_, index) => $"@key{index}"))})";

        for (var index = 0; index < keys.Length; index++)
        {
            command.Parameters.AddWithValue($"@key{index}", keys[index]);
        }

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = reader["setting_key"]?.ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            values[key] = reader["setting_value"]?.ToString() ?? string.Empty;
        }

        return values;
    }

    private static PrintOwnershipMode ParseOwnershipMode(string? value)
    {
        return (value ?? "direct").Trim().ToLowerInvariant() switch
        {
            "mother_queue" or "motherqueue" or "mother" => PrintOwnershipMode.MotherQueue,
            "named_queue_terminal" or "namedqueueterminal" or "named_terminal" or "terminal" => PrintOwnershipMode.NamedQueueTerminal,
            "any_queue_terminal" or "anyqueueterminal" or "any" => PrintOwnershipMode.AnyQueueTerminal,
            _ => PrintOwnershipMode.Direct
        };
    }

    private static bool ParseBoolean(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
