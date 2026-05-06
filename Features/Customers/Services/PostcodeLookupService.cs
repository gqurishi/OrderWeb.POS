using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Main orchestrator for postcode lookup functionality
/// Manages provider selection and delegates to appropriate service
/// </summary>
public class PostcodeLookupService
{
    private readonly string _connectionString;
    private PostcodeLookupSettings? _cachedSettings;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private const int CACHE_MINUTES = 5;

    public PostcodeLookupService(DatabaseService databaseService)
    {
        // Get connection string from DatabaseService
        var host = "localhost";
        var user = "root";
        var password = "root";
        var database = "Pos-net";
        var port = "3306";
        _connectionString = $"Server={host};Database={database};Uid={user};Pwd={password};Port={port};Connection Timeout=5;";
    }

    /// <summary>
    /// Look up addresses for a postcode using configured provider
    /// </summary>
    public async Task<List<AddressResult>> LookupPostcodeAsync(string postcode)
    {
        var settings = await GetSettingsAsync();
        var service = CreateService(settings);

        if (service == null)
            throw new InvalidOperationException("No address lookup provider configured");

        try
        {
            var results = await service.LookupPostcodeAsync(postcode);

            // Update usage statistics
            await IncrementUsageAsync();

            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PostcodeLookup] Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Test connection to active provider
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        var settings = await GetSettingsAsync();
        var service = CreateService(settings);

        if (service == null)
            throw new InvalidOperationException("No address lookup provider configured");

        return await service.TestConnectionAsync();
    }

    /// <summary>
    /// Get current settings from database
    /// </summary>
    public async Task<PostcodeLookupSettings> GetSettingsAsync()
    {
        // Return cached settings if still valid
        if (_cachedSettings != null && DateTime.Now < _cacheExpiry)
            return _cachedSettings;

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = "SELECT * FROM postcode_lookup_settings ORDER BY id DESC LIMIT 1";
        using var command = new MySqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            _cachedSettings = new PostcodeLookupSettings
            {
                Id = reader.GetInt32("id"),
                Provider = reader.GetString("provider"),
                MapboxApiToken = reader.GetString("mapbox_api_token"),
                MapboxEnabled = reader.GetBoolean("mapbox_enabled"),
                CustomApiUrl = reader.GetString("custom_api_url"),
                CustomAuthToken = reader.GetString("custom_auth_token"),
                CustomEnabled = reader.GetBoolean("custom_enabled"),
                TotalLookups = reader.GetInt32("total_lookups"),
                LastUsed = reader.IsDBNull(reader.GetOrdinal("last_used")) 
                    ? null 
                    : reader.GetDateTime("last_used"),
                CreatedAt = reader.GetDateTime("created_at"),
                UpdatedAt = reader.GetDateTime("updated_at")
            };

            _cacheExpiry = DateTime.Now.AddMinutes(CACHE_MINUTES);
            return _cachedSettings;
        }

        // Return default settings if none found
        return new PostcodeLookupSettings
        {
            Provider = "Mapbox",
            MapboxEnabled = true
        };
    }

    /// <summary>
    /// Save settings to database
    /// </summary>
    public async Task<bool> SaveSettingsAsync(PostcodeLookupSettings settings)
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // First, check if any settings exist
        var checkQuery = "SELECT COUNT(*) FROM postcode_lookup_settings";
        using var checkCommand = new MySqlCommand(checkQuery, connection);
        var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

        string query;
        if (count == 0)
        {
            // Insert first record
            query = @"
                INSERT INTO postcode_lookup_settings 
                (provider, mapbox_api_token, mapbox_enabled, 
                 custom_api_url, custom_auth_token, custom_enabled, updated_at)
                VALUES 
                (@provider, @mapboxToken, @mapboxEnabled, 
                 @customUrl, @customToken, @customEnabled, NOW())";
        }
        else
        {
            // Update the latest record
            query = @"
                UPDATE postcode_lookup_settings 
                SET provider = @provider,
                    mapbox_api_token = @mapboxToken,
                    mapbox_enabled = @mapboxEnabled,
                    custom_api_url = @customUrl,
                    custom_auth_token = @customToken,
                    custom_enabled = @customEnabled,
                    updated_at = NOW()
                WHERE id = (SELECT id FROM (SELECT id FROM postcode_lookup_settings ORDER BY id DESC LIMIT 1) AS tmp)";
        }

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@provider", settings.Provider);
        command.Parameters.AddWithValue("@mapboxToken", settings.MapboxApiToken ?? "");
        command.Parameters.AddWithValue("@mapboxEnabled", settings.MapboxEnabled);
        command.Parameters.AddWithValue("@customUrl", settings.CustomApiUrl ?? "");
        command.Parameters.AddWithValue("@customToken", settings.CustomAuthToken ?? "");
        command.Parameters.AddWithValue("@customEnabled", settings.CustomEnabled);

        var result = await command.ExecuteNonQueryAsync();

        // Clear cache
        _cachedSettings = null;
        _cacheExpiry = DateTime.MinValue;

        return result > 0;
    }

    /// <summary>
    /// Create appropriate service based on provider settings
    /// </summary>
    private IAddressLookupService? CreateService(PostcodeLookupSettings settings)
    {
        System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] CreateService called");
        System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Provider: {settings.Provider}");
        System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] MapboxEnabled: {settings.MapboxEnabled}");
        System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] TokenEmpty: {string.IsNullOrEmpty(settings.MapboxApiToken)}");
        System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] TokenLength: {settings.MapboxApiToken?.Length ?? 0}");
        
        IAddressLookupService? service = settings.Provider switch
        {
            "Mapbox" when settings.MapboxEnabled && !string.IsNullOrEmpty(settings.MapboxApiToken) 
                => new MapboxAddressService(settings.MapboxApiToken),
            
            "Custom" when settings.CustomEnabled && !string.IsNullOrEmpty(settings.CustomApiUrl) 
                => new CustomPAFService(settings.CustomApiUrl, settings.CustomAuthToken),
            
            _ => null
        };
        
        System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Service created: {service?.GetType().Name ?? "NULL"}");
        return service;
    }

    /// <summary>
    /// Increment usage counter
    /// </summary>
    private async Task IncrementUsageAsync()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                UPDATE postcode_lookup_settings 
                SET total_lookups = total_lookups + 1,
                    last_used = NOW()
                WHERE id = (SELECT id FROM (SELECT id FROM postcode_lookup_settings ORDER BY id DESC LIMIT 1) AS tmp)";

            using var command = new MySqlCommand(query, connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // Don't fail the lookup if statistics update fails
            Console.WriteLine($"[PostcodeLookup] Failed to update stats: {ex.Message}");
        }
    }
}
