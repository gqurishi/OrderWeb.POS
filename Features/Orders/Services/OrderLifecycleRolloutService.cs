using MySqlConnector;

namespace POS_in_NET.Services;

public sealed class OrderLifecycleRolloutConfig
{
    public bool SchemaMigrationReady { get; set; }
    public bool EnableLifecycleReads { get; set; }
    public bool EnableLifecycleWrites { get; set; }
    public bool EnableDraftSaveTable { get; set; }
    public bool EnableDraftSaveCollection { get; set; }
    public bool EnableDraftSaveDelivery { get; set; }
    public bool EnableResumePath { get; set; }
    public bool EnableSendDurability { get; set; }
    public bool EnablePaymentLines { get; set; }
    public bool EnableStrictFinalizeRules { get; set; }
    public bool LegacyFallbackPathsRemoved { get; set; }
    public bool ValidationWindowClosed { get; set; }

    public bool ShouldUseLegacyFallbackPaths => !(LegacyFallbackPathsRemoved && ValidationWindowClosed);

    public bool IsDraftSaveEnabledForOrderType(string orderType)
    {
        var normalized = (orderType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "table" => EnableDraftSaveTable,
            "pickup" or "collection" => EnableDraftSaveCollection,
            "delivery" => EnableDraftSaveDelivery,
            _ => false
        };
    }

    public static OrderLifecycleRolloutConfig CreateDefault()
    {
        return new OrderLifecycleRolloutConfig
        {
            SchemaMigrationReady = true,
            EnableLifecycleReads = true,
            EnableLifecycleWrites = false,
            EnableDraftSaveTable = false,
            EnableDraftSaveCollection = false,
            EnableDraftSaveDelivery = false,
            EnableResumePath = true,
            EnableSendDurability = false,
            EnablePaymentLines = false,
            EnableStrictFinalizeRules = false,
            LegacyFallbackPathsRemoved = false,
            ValidationWindowClosed = false
        };
    }
}

public sealed class OrderLifecycleRolloutService
{
    private readonly DatabaseService _databaseService;
    private readonly Dictionary<string, bool> _defaultFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["order.lifecycle.rollout.schema_migration_ready"] = true,
        ["order.lifecycle.rollout.lifecycle_reads_enabled"] = true,
        ["order.lifecycle.rollout.lifecycle_writes_enabled"] = false,
        ["order.lifecycle.rollout.draft_save_table_enabled"] = false,
        ["order.lifecycle.rollout.draft_save_collection_enabled"] = false,
        ["order.lifecycle.rollout.draft_save_delivery_enabled"] = false,
        ["order.lifecycle.rollout.resume_path_enabled"] = true,
        ["order.lifecycle.rollout.send_durability_enabled"] = false,
        ["order.lifecycle.rollout.payment_lines_enabled"] = false,
        ["order.lifecycle.rollout.strict_finalize_enabled"] = false,
        ["order.lifecycle.rollout.legacy_fallback_paths_removed"] = false,
        ["order.lifecycle.rollout.validation_window_closed"] = false
    };

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private OrderLifecycleRolloutConfig? _cachedConfig;
    private DateTime _cacheExpiresAt = DateTime.MinValue;

    public OrderLifecycleRolloutService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<OrderLifecycleRolloutConfig> GetConfigAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedConfig != null && DateTime.UtcNow < _cacheExpiresAt)
        {
            return _cachedConfig;
        }

        await _loadLock.WaitAsync();
        try
        {
            if (!forceRefresh && _cachedConfig != null && DateTime.UtcNow < _cacheExpiresAt)
            {
                return _cachedConfig;
            }

            var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _defaultFlags)
            {
                flags[entry.Key] = entry.Value;
            }

            using var connection = await _databaseService.GetConnectionAsync();

            await EnsureDefaultFlagsAsync(connection);

            var sql = @"
                SELECT setting_key, setting_value
                FROM settings
                WHERE setting_key LIKE 'order.lifecycle.rollout.%'";

            using var command = new MySqlCommand(sql, connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var key = reader["setting_key"]?.ToString();
                var value = reader["setting_value"]?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                flags[key] = ParseBool(value, flags.TryGetValue(key, out var defaultValue) ? defaultValue : false);
            }

            _cachedConfig = MapFlags(flags);
            _cacheExpiresAt = DateTime.UtcNow.AddSeconds(30);
            return _cachedConfig;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rollout config load warning: {ex.Message}");
            _cachedConfig = OrderLifecycleRolloutConfig.CreateDefault();
            _cacheExpiresAt = DateTime.UtcNow.AddSeconds(10);
            return _cachedConfig;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<bool> SetFlagAsync(string key, bool value)
    {
        if (string.IsNullOrWhiteSpace(key) || !_defaultFlags.ContainsKey(key))
        {
            return false;
        }

        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            const string upsertSql = @"
                INSERT INTO settings (setting_key, setting_value)
                VALUES (@key, @value)
                ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value), updated_at = CURRENT_TIMESTAMP";

            using var command = new MySqlCommand(upsertSql, connection);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value ? "true" : "false");
            await command.ExecuteNonQueryAsync();

            _cacheExpiresAt = DateTime.MinValue;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rollout config save warning: {ex.Message}");
            return false;
        }
    }

    private async Task EnsureDefaultFlagsAsync(MySqlConnection connection)
    {
        const string upsertSql = @"
            INSERT INTO settings (setting_key, setting_value)
            VALUES (@key, @value)
            ON DUPLICATE KEY UPDATE setting_key = setting_key";

        foreach (var entry in _defaultFlags)
        {
            using var command = new MySqlCommand(upsertSql, connection);
            command.Parameters.AddWithValue("@key", entry.Key);
            command.Parameters.AddWithValue("@value", entry.Value ? "true" : "false");
            await command.ExecuteNonQueryAsync();
        }
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsedBool))
        {
            return parsedBool;
        }

        if (int.TryParse(value, out var parsedInt))
        {
            return parsedInt != 0;
        }

        return value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("on", StringComparison.OrdinalIgnoreCase)
            ? true
            : defaultValue;
    }

    private static OrderLifecycleRolloutConfig MapFlags(IReadOnlyDictionary<string, bool> flags)
    {
        return new OrderLifecycleRolloutConfig
        {
            SchemaMigrationReady = Get(flags, "order.lifecycle.rollout.schema_migration_ready"),
            EnableLifecycleReads = Get(flags, "order.lifecycle.rollout.lifecycle_reads_enabled"),
            EnableLifecycleWrites = Get(flags, "order.lifecycle.rollout.lifecycle_writes_enabled"),
            EnableDraftSaveTable = Get(flags, "order.lifecycle.rollout.draft_save_table_enabled"),
            EnableDraftSaveCollection = Get(flags, "order.lifecycle.rollout.draft_save_collection_enabled"),
            EnableDraftSaveDelivery = Get(flags, "order.lifecycle.rollout.draft_save_delivery_enabled"),
            EnableResumePath = Get(flags, "order.lifecycle.rollout.resume_path_enabled"),
            EnableSendDurability = Get(flags, "order.lifecycle.rollout.send_durability_enabled"),
            EnablePaymentLines = Get(flags, "order.lifecycle.rollout.payment_lines_enabled"),
            EnableStrictFinalizeRules = Get(flags, "order.lifecycle.rollout.strict_finalize_enabled"),
            LegacyFallbackPathsRemoved = Get(flags, "order.lifecycle.rollout.legacy_fallback_paths_removed"),
            ValidationWindowClosed = Get(flags, "order.lifecycle.rollout.validation_window_closed")
        };
    }

    private static bool Get(IReadOnlyDictionary<string, bool> flags, string key)
    {
        return flags.TryGetValue(key, out var value) && value;
    }
}