using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// OrderWeb UK address lookup configuration and orchestration.
/// </summary>
public class PostcodeLookupService
{
    private PostcodeLookupSettings? _cachedSettings;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private const int CacheMinutes = 5;

    public PostcodeLookupService(DatabaseService databaseService)
    {
        _ = databaseService;
    }

    public async Task<List<AddressResult>> LookupPostcodeAsync(string postcode)
    {
        var settings = await GetSettingsAsync();
        var service = CreateService(settings)
            ?? throw new InvalidOperationException("OrderWeb address lookup is not configured. Add the owp_ API key in Settings → OrderWeb.");

        try
        {
            var results = await service.LookupPostcodeAsync(postcode);
            await IncrementUsageAsync();
            return results;
        }
        catch (AddressLookupException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Error: {ex.Message}");
            throw;
        }
    }

    public Task<bool> TestConnectionAsync()
    {
        return TestConnectionAsync(null);
    }

    public async Task<bool> TestConnectionAsync(string? apiKeyOverride, string? baseUrlOverride = null)
    {
        var settings = await GetSettingsAsync();
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride) ? settings.OrderWebAddressApiKey : apiKeyOverride.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(baseUrlOverride) ? settings.OrderWebBaseUrl : baseUrlOverride.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OrderWeb address API key is required.");
        }

        var service = new OrderWebAddressLookupService(apiKey, baseUrl);
        return await service.TestConnectionAsync();
    }

    public async Task<PostcodeLookupSettings> GetSettingsAsync()
    {
        if (_cachedSettings != null && DateTime.Now < _cacheExpiry)
        {
            return _cachedSettings;
        }

        await EnsureSettingsTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        var query = @"
            SELECT id, provider, orderweb_address_api_key, orderweb_base_url, orderweb_address_enabled,
                   total_lookups, last_used, created_at, updated_at
            FROM postcode_lookup_settings
            ORDER BY id DESC
            LIMIT 1";

        using var command = new MySqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var orderWebKey = ReadString(reader, "orderweb_address_api_key");

            _cachedSettings = new PostcodeLookupSettings
            {
                Id = reader.GetInt32("id"),
                Provider = ReadString(reader, "provider", "OrderWeb"),
                OrderWebAddressApiKey = orderWebKey,
                OrderWebBaseUrl = ReadString(reader, "orderweb_base_url", OrderWebAddressLookupService.DefaultBaseUrl),
                OrderWebAddressEnabled = reader.IsDBNull(reader.GetOrdinal("orderweb_address_enabled"))
                    || reader.GetBoolean("orderweb_address_enabled"),
                TotalLookups = reader.GetInt32("total_lookups"),
                LastUsed = reader.IsDBNull(reader.GetOrdinal("last_used")) ? null : reader.GetDateTime("last_used"),
                CreatedAt = reader.GetDateTime("created_at"),
                UpdatedAt = reader.GetDateTime("updated_at")
            };

            _cacheExpiry = DateTime.Now.AddMinutes(CacheMinutes);
            return _cachedSettings;
        }

        return new PostcodeLookupSettings();
    }

    public async Task<bool> SaveSettingsAsync(PostcodeLookupSettings settings)
    {
        await EnsureSettingsTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        using var checkCommand = new MySqlCommand("SELECT COUNT(*) FROM postcode_lookup_settings", connection);
        var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

        var query = count == 0
            ? @"
                INSERT INTO postcode_lookup_settings
                (provider, orderweb_address_api_key, orderweb_base_url, orderweb_address_enabled, updated_at)
                VALUES
                (@provider, @apiKey, @baseUrl, @enabled, NOW())"
            : @"
                UPDATE postcode_lookup_settings
                SET provider = @provider,
                    orderweb_address_api_key = @apiKey,
                    orderweb_base_url = @baseUrl,
                    orderweb_address_enabled = @enabled,
                    updated_at = NOW()
                WHERE id = (SELECT id FROM (SELECT id FROM postcode_lookup_settings ORDER BY id DESC LIMIT 1) AS tmp)";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@provider", "OrderWeb");
        command.Parameters.AddWithValue("@apiKey", settings.OrderWebAddressApiKey ?? string.Empty);
        command.Parameters.AddWithValue("@baseUrl", string.IsNullOrWhiteSpace(settings.OrderWebBaseUrl)
            ? OrderWebAddressLookupService.DefaultBaseUrl
            : settings.OrderWebBaseUrl.Trim());
        command.Parameters.AddWithValue("@enabled", settings.OrderWebAddressEnabled);

        var result = await command.ExecuteNonQueryAsync();
        _cachedSettings = null;
        _cacheExpiry = DateTime.MinValue;
        return result > 0;
    }

    private IAddressLookupService? CreateService(PostcodeLookupSettings settings)
    {
        if (!settings.OrderWebAddressEnabled || string.IsNullOrWhiteSpace(settings.OrderWebAddressApiKey))
        {
            return null;
        }

        return new OrderWebAddressLookupService(settings.OrderWebAddressApiKey, settings.OrderWebBaseUrl);
    }

    public async Task EnsureSettingsTableAsync()
    {
        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        const string createSql = @"
            CREATE TABLE IF NOT EXISTS postcode_lookup_settings (
                id INT AUTO_INCREMENT PRIMARY KEY,
                provider VARCHAR(50) DEFAULT 'OrderWeb',
                orderweb_address_api_key VARCHAR(500) DEFAULT '',
                orderweb_base_url VARCHAR(500) DEFAULT 'https://orderweb.net',
                orderweb_address_enabled BOOLEAN DEFAULT TRUE,
                total_lookups INT DEFAULT 0,
                last_used DATETIME NULL,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_provider (provider),
                INDEX idx_last_used (last_used)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        using (var createCommand = new MySqlCommand(createSql, connection))
        {
            await createCommand.ExecuteNonQueryAsync();
        }

        await TryAddColumnAsync(connection, "orderweb_address_api_key", "VARCHAR(500) DEFAULT ''");
        await TryAddColumnAsync(connection, "orderweb_base_url", "VARCHAR(500) DEFAULT 'https://orderweb.net'");
        await TryAddColumnAsync(connection, "orderweb_address_enabled", "BOOLEAN DEFAULT TRUE");

        using var seedCommand = new MySqlCommand(
            "INSERT INTO postcode_lookup_settings (provider, orderweb_address_enabled) SELECT 'OrderWeb', TRUE FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM postcode_lookup_settings LIMIT 1)",
            connection);
        await seedCommand.ExecuteNonQueryAsync();
    }

    private static async Task TryAddColumnAsync(MySqlConnection connection, string columnName, string definition)
    {
        try
        {
            using var command = new MySqlCommand($"ALTER TABLE postcode_lookup_settings ADD COLUMN {columnName} {definition}", connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.Number == 1060)
        {
            // Duplicate column — already migrated.
        }
    }

    private static string ReadString(MySqlDataReader reader, string column, string fallback = "")
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
    }

    private async Task IncrementUsageAsync()
    {
        try
        {
            using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            const string query = @"
                UPDATE postcode_lookup_settings
                SET total_lookups = total_lookups + 1,
                    last_used = NOW()
                WHERE id = (SELECT id FROM (SELECT id FROM postcode_lookup_settings ORDER BY id DESC LIMIT 1) AS tmp)";

            using var command = new MySqlCommand(query, connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Failed to update stats: {ex.Message}");
        }
    }
}
