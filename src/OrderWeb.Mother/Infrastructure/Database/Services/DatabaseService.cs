using MySqlConnector;
using System.Data;
using System.Linq;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Database service using MariaDB/MySQL ONLY
/// This application uses MariaDB as the primary database
/// Host is selected by terminal setup. Mother uses localhost; child terminals use the mother terminal IP.
/// </summary>
public class DatabaseService
{
    public DatabaseService()
    {
        // DO NOT initialize database in constructor - it blocks app startup!
        // Initialize will be called separately when needed
        // _ = InitializeDatabaseAsync(); // REMOVED - was blocking!
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = new MySqlConnection(GetConnectionString());
            
            // Add 5-second timeout
            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.OpenAsync(cts.Token);
            
            // Test with a simple query
            using var command = new MySqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync();
            
            return result != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Database connection failed: {ex.Message}");
            return false;
        }
    }

    public async Task<string> GetConnectionStatusAsync()
    {
        try
        {
            using var connection = new MySqlConnection(GetConnectionString());
            await connection.OpenAsync();
            
            using var command = new MySqlCommand("SELECT VERSION()", connection);
            var version = await command.ExecuteScalarAsync();
            
            return $"Connected to MariaDB/MySQL version: {version}";
        }
        catch (Exception ex)
        {
            return $"Connection failed: {ex.Message}";
        }
    }

    public async Task<MySqlConnection> GetConnectionAsync()
    {
        var connection = new MySqlConnection(GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    public async Task<bool> InitializeDatabaseAsync()
    {
        var result = await EnsureProductionSchemaAsync();
        return result.Success;
    }

    [Obsolete("Production database creation is handled by OrderWeb.DatabaseSetup.exe install-mother.")]
    public async Task<bool> CreateDatabaseIfNotExistsAsync()
    {
        AppDiagnostics.Log("CreateDatabaseIfNotExistsAsync is disabled. Use OrderWeb.DatabaseSetup.exe install-mother.");
        var result = await EnsureProductionSchemaAsync();
        return result.Success;
    }

    [Obsolete("Production schema is created by OrderWeb.DatabaseSetup.exe migrations.")]
    public async Task<bool> CreateTablesAsync()
    {
        AppDiagnostics.Log("CreateTablesAsync is disabled. Use OrderWeb.DatabaseSetup.exe migrate.");
        var result = await EnsureProductionSchemaAsync();
        return result.Success;
    }

    public async Task<(bool Success, string Message)> EnsureProductionSchemaAsync()
    {
        try
        {
            if (!await TestConnectionAsync())
            {
                return (false, "Cannot connect to the database. Check MariaDB is running and credentials are correct.");
            }

            if (!TerminalConfigurationService.IsConfigured)
            {
                return (false, "Terminal setup is not complete.");
            }

            await using var connection = new MySqlConnection(GetConnectionString());
            await connection.OpenAsync();

            if (TerminalConfigurationService.IsChildTerminal)
            {
                var schemaGate = await ChildSchemaVersionGateService.CheckAsync(connection);
                if (!schemaGate.IsCompatible)
                {
                    return (false, schemaGate.Message);
                }

                if (!await TableExistsAsync(connection, "users"))
                {
                    return (false,
                        "The mother terminal database schema is incomplete. On the mother PC, run OrderWeb.DatabaseSetup.exe verify.");
                }

                return (true, schemaGate.Message);
            }

            if (!await TableExistsAsync(connection, "users"))
            {
                return (false, "Database schema is incomplete. Run OrderWeb.DatabaseSetup.exe verify on the mother terminal.");
            }

            int? schemaVersion = null;
            if (await TableExistsAsync(connection, "app_schema_version"))
            {
                await using var versionCommand = new MySqlCommand(
                    "SELECT schema_version FROM app_schema_version WHERE id = 1 LIMIT 1",
                    connection);
                var versionValue = await versionCommand.ExecuteScalarAsync();
                if (versionValue != null && versionValue != DBNull.Value)
                {
                    schemaVersion = Convert.ToInt32(versionValue);
                }
            }

            return schemaVersion.HasValue
                ? (true, $"Production database connected in compatibility mode (schema {schemaVersion.Value}).")
                : (true, "Production database connected in compatibility mode (unversioned schema).");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("EnsureProductionSchema", ex);
            return (false, $"Database schema verification failed: {ex.Message}");
        }
    }

    private static async Task<bool> TableExistsAsync(MySqlConnection connection, string tableName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    public string GetConnectionString()
    {
        return TerminalConfigurationService.GetPosConnectionString();
    }

    public async Task UpdateOrderStatusAsync(int orderId, string status)
    {
        try
        {
            using var connection = await GetConnectionAsync();
            using var command = new MySqlCommand(
                @"UPDATE orders
                  SET status = @status,
                      updated_by_terminal_name = @updatedByTerminalName,
                      updated_by_terminal_at = NOW(),
                      updated_at = NOW()
                  WHERE id = @orderId", 
                connection);
            
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            command.Parameters.AddWithValue("@orderId", orderId);
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update order status: {ex.Message}");
            throw;
        }
    }

    private static string GetCurrentTerminalName()
    {
        try
        {
            return TerminalConfigurationService.GetConfiguration().TerminalName;
        }
        catch
        {
            return "Terminal";
        }
    }

    // Cloud Configuration Management
    public async Task<Dictionary<string, string>> GetCloudConfigAsync()
    {
        var config = new Dictionary<string, string>();
        try
        {
            using var connection = await GetConnectionAsync();
            using var command = new MySqlCommand("SELECT * FROM cloud_config LIMIT 1", connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                config["tenant_slug"] = reader.GetString(reader.GetOrdinal("tenant_slug"));
                config["restaurant_slug"] = TryGetOptionalString(reader, "restaurant_slug") ?? "";
                config["api_key"] = reader.GetString(reader.GetOrdinal("api_key"));
                var cloudUrl = reader.GetString(reader.GetOrdinal("cloud_url"));
                config["cloud_url"] = cloudUrl;
                config["api_base_url"] = TryGetOptionalString(reader, "api_base_url") ?? cloudUrl;
                config["is_enabled"] = reader.GetBoolean(reader.GetOrdinal("is_enabled")).ToString();
                config["polling_interval_seconds"] = reader.GetInt32(reader.GetOrdinal("polling_interval_seconds")).ToString();
                config["auto_print_enabled"] = reader.GetBoolean(reader.GetOrdinal("auto_print_enabled")).ToString();
                config["online_order_master_enabled"] = TryGetOptionalBoolean(reader, "online_order_master_enabled", true).ToString();
                config["online_order_master_terminal_name"] = TryGetOptionalString(reader, "online_order_master_terminal_name") ?? "";
            }

            static string? TryGetOptionalString(MySqlDataReader reader, string columnName)
            {
                try
                {
                    var ordinal = reader.GetOrdinal(columnName);
                    return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                }
                catch (IndexOutOfRangeException)
                {
                    return null;
                }
            }

            static bool TryGetOptionalBoolean(MySqlDataReader reader, string columnName, bool fallback)
            {
                try
                {
                    var ordinal = reader.GetOrdinal(columnName);
                    return reader.IsDBNull(ordinal) ? fallback : reader.GetBoolean(ordinal);
                }
                catch (IndexOutOfRangeException)
                {
                    return fallback;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get cloud config: {ex.Message}");
        }
        return config;
    }

    public async Task<bool> SaveCloudConfigAsync(string tenantSlug, string apiKey, string cloudUrl, 
        bool isEnabled, int pollingInterval, bool autoPrint)
    {
        return await SaveCloudConfigAsync(tenantSlug, apiKey, cloudUrl, isEnabled, pollingInterval, autoPrint,
            "", "", "", "", 0, "api_polling");
    }

    public async Task<bool> SaveCloudConfigAsync(string tenantSlug, string apiKey, string cloudUrl, 
        bool isEnabled, int pollingInterval, bool autoPrint, string dbHost, string dbName, 
        string dbUsername, string dbPassword, int dbPort, string connectionType)
    {
        try
        {
            using var connection = await GetConnectionAsync();
            
            // Check if config exists
            using var checkCommand = new MySqlCommand("SELECT COUNT(*) FROM cloud_config", connection);
            var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
            
            var apiBaseUrl = OrderWebApiClient.NormalizeApiBaseUrl(cloudUrl);
            var sql = count > 0 
                ? @"UPDATE cloud_config SET tenant_slug = @tenantSlug, api_key = @apiKey, 
                    api_base_url = @apiBaseUrl, cloud_url = @cloudUrl, is_enabled = @isEnabled,
                    polling_interval_seconds = @pollingInterval, auto_print_enabled = @autoPrint,
                    updated_at = CURRENT_TIMESTAMP"
                : @"INSERT INTO cloud_config (tenant_slug, api_key, api_base_url, cloud_url, is_enabled, 
                    polling_interval_seconds, auto_print_enabled) 
                    VALUES (@tenantSlug, @apiKey, @apiBaseUrl, @cloudUrl, @isEnabled, @pollingInterval, @autoPrint)";
            
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@tenantSlug", tenantSlug);
            command.Parameters.AddWithValue("@apiKey", apiKey);
            command.Parameters.AddWithValue("@apiBaseUrl", apiBaseUrl);
            command.Parameters.AddWithValue("@cloudUrl", cloudUrl);
            command.Parameters.AddWithValue("@isEnabled", isEnabled);
            command.Parameters.AddWithValue("@pollingInterval", pollingInterval);
            command.Parameters.AddWithValue("@autoPrint", autoPrint);
            
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save cloud config: {ex.Message}");
            return false;
        }
    }

    // NEW: Cloud Configuration CRUD methods for CloudConfiguration model
    public async Task<CloudConfiguration?> GetCloudConfigurationAsync()
    {
        try
        {
            using var connection = await GetConnectionAsync();
            using var command = new MySqlCommand("SELECT * FROM cloud_config LIMIT 1", connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                return new CloudConfiguration
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    ApiBaseUrl = reader.GetString(reader.GetOrdinal("api_base_url")),
                    RestApiBaseUrl = reader.GetString(reader.GetOrdinal("api_base_url")),
                    TenantSlug = reader.GetString(reader.GetOrdinal("tenant_slug")),
                    ApiKey = reader.GetString(reader.GetOrdinal("api_key")),
                    WebSocketUrl = reader.GetString(reader.GetOrdinal("websocket_url")),
                    ConnectionTimeout = reader.GetInt32(reader.GetOrdinal("connection_timeout")),
                    PollingIntervalSeconds = reader.GetInt32(reader.GetOrdinal("polling_interval_seconds")),
                    MaxRetryAttempts = reader.GetInt32(reader.GetOrdinal("max_retry_attempts")),
                    IsEnabled = reader.GetBoolean(reader.GetOrdinal("is_enabled")),
                    IsApiTested = reader.GetBoolean(reader.GetOrdinal("is_api_tested")),
                    IsWebSocketTested = reader.GetBoolean(reader.GetOrdinal("is_websocket_tested")),
                    ApiTestResult = reader.IsDBNull("api_test_result") ? null : reader.GetString(reader.GetOrdinal("api_test_result")),
                    WebSocketTestResult = reader.IsDBNull("websocket_test_result") ? null : reader.GetString(reader.GetOrdinal("websocket_test_result")),
                    LastApiTest = reader.IsDBNull("last_api_test") ? null : reader.GetDateTime(reader.GetOrdinal("last_api_test")),
                    LastWebSocketTest = reader.IsDBNull("last_websocket_test") ? null : reader.GetDateTime(reader.GetOrdinal("last_websocket_test")),
                    AutoPrintEnabled = reader.GetBoolean(reader.GetOrdinal("auto_print_enabled")),
                    NotificationsEnabled = reader.GetBoolean(reader.GetOrdinal("notifications_enabled")),
                    OnlineOrderMasterEnabled = GetOptionalBoolean(reader, "online_order_master_enabled", true),
                    OnlineOrderMasterTerminalName = GetOptionalString(reader, "online_order_master_terminal_name") ?? "",
                    LastSync = reader.IsDBNull("last_sync") ? null : reader.GetDateTime(reader.GetOrdinal("last_sync")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                };
            }
            
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get cloud configuration: {ex.Message}");
            return null;
        }
    }

    private static string? GetOptionalString(MySqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static bool GetOptionalBoolean(MySqlDataReader reader, string columnName, bool fallback)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? fallback : reader.GetBoolean(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return fallback;
        }
    }

    private static async Task<string?> GetExistingCloudConfigValueAsync(MySqlConnection connection, string columnName)
    {
        try
        {
            using var command = new MySqlCommand($"SELECT {columnName} FROM cloud_config ORDER BY id LIMIT 1", connection);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveCloudConfigurationAsync(CloudConfiguration config)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($" SaveCloudConfigurationAsync starting...");
            using var connection = await GetConnectionAsync();
            System.Diagnostics.Debug.WriteLine($" Database connection established");
            
            // Check if config exists
            using var checkCommand = new MySqlCommand("SELECT COUNT(*) FROM cloud_config", connection);
            var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
            System.Diagnostics.Debug.WriteLine($" Existing config count: {count}");
            
            // Use RestApiBaseUrl if available, fallback to ApiBaseUrl
            var apiBaseUrl = !string.IsNullOrEmpty(config.RestApiBaseUrl) 
                ? config.RestApiBaseUrl 
                : config.ApiBaseUrl;
            apiBaseUrl = OrderWebApiClient.NormalizeApiBaseUrl(apiBaseUrl);

            var existingMasterTerminalName = await GetExistingCloudConfigValueAsync(connection, "online_order_master_terminal_name");
            var onlineOrderMasterTerminalName = !string.IsNullOrWhiteSpace(config.OnlineOrderMasterTerminalName)
                ? config.OnlineOrderMasterTerminalName.Trim()
                : (!string.IsNullOrWhiteSpace(existingMasterTerminalName)
                    ? existingMasterTerminalName
                    : TerminalConfigurationService.GetConfiguration().TerminalName);
            
            var sql = count > 0 
                ? @"UPDATE cloud_config SET 
                    api_base_url = @apiBaseUrl,
                    tenant_slug = @tenantSlug,
                    api_key = @apiKey,
                    websocket_url = @websocketUrl,
                    connection_timeout = @connectionTimeout,
                    polling_interval_seconds = @pollingInterval,
                    max_retry_attempts = @maxRetryAttempts,
                    is_enabled = @isEnabled,
                    is_api_tested = @isApiTested,
                    is_websocket_tested = @isWebSocketTested,
                    api_test_result = @apiTestResult,
                    websocket_test_result = @websocketTestResult,
                    last_api_test = @lastApiTest,
                    last_websocket_test = @lastWebSocketTest,
                    auto_print_enabled = @autoPrint,
                    notifications_enabled = @notificationsEnabled,
                    online_order_master_enabled = @onlineOrderMasterEnabled,
                    online_order_master_terminal_name = @onlineOrderMasterTerminalName,
                    updated_at = CURRENT_TIMESTAMP
                    WHERE id = (SELECT MIN(id) FROM (SELECT id FROM cloud_config) as temp)"
                : @"INSERT INTO cloud_config (
                    api_base_url, tenant_slug, api_key, websocket_url,
                    connection_timeout, polling_interval_seconds, max_retry_attempts,
                    is_enabled, is_api_tested, is_websocket_tested,
                    api_test_result, websocket_test_result,
                    last_api_test, last_websocket_test,
                    auto_print_enabled, notifications_enabled,
                    online_order_master_enabled, online_order_master_terminal_name
                    ) VALUES (
                    @apiBaseUrl, @tenantSlug, @apiKey, @websocketUrl,
                    @connectionTimeout, @pollingInterval, @maxRetryAttempts,
                    @isEnabled, @isApiTested, @isWebSocketTested,
                    @apiTestResult, @websocketTestResult,
                    @lastApiTest, @lastWebSocketTest,
                    @autoPrint, @notificationsEnabled,
                    @onlineOrderMasterEnabled, @onlineOrderMasterTerminalName
                    )";
            
            System.Diagnostics.Debug.WriteLine($" SQL: {(count > 0 ? "UPDATE" : "INSERT")}");
            
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@apiBaseUrl", apiBaseUrl);
            command.Parameters.AddWithValue("@tenantSlug", config.TenantSlug);
            command.Parameters.AddWithValue("@apiKey", config.ApiKey);
            command.Parameters.AddWithValue("@websocketUrl", config.WebSocketUrl ?? "");
            command.Parameters.AddWithValue("@connectionTimeout", config.ConnectionTimeout);
            command.Parameters.AddWithValue("@pollingInterval", config.PollingIntervalSeconds);
            command.Parameters.AddWithValue("@maxRetryAttempts", config.MaxRetryAttempts);
            command.Parameters.AddWithValue("@isEnabled", config.IsEnabled);
            command.Parameters.AddWithValue("@isApiTested", config.IsApiTested);
            command.Parameters.AddWithValue("@isWebSocketTested", config.IsWebSocketTested);
            command.Parameters.AddWithValue("@apiTestResult", (object?)config.ApiTestResult ?? DBNull.Value);
            command.Parameters.AddWithValue("@websocketTestResult", (object?)config.WebSocketTestResult ?? DBNull.Value);
            command.Parameters.AddWithValue("@lastApiTest", (object?)config.LastApiTest ?? DBNull.Value);
            command.Parameters.AddWithValue("@lastWebSocketTest", (object?)config.LastWebSocketTest ?? DBNull.Value);
            command.Parameters.AddWithValue("@autoPrint", config.AutoPrintEnabled);
            command.Parameters.AddWithValue("@notificationsEnabled", config.NotificationsEnabled);
            command.Parameters.AddWithValue("@onlineOrderMasterEnabled", config.OnlineOrderMasterEnabled);
            command.Parameters.AddWithValue("@onlineOrderMasterTerminalName", onlineOrderMasterTerminalName);
            
            System.Diagnostics.Debug.WriteLine($" Executing database command...");
            var rowsAffected = await command.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($" Rows affected: {rowsAffected}");
            
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Failed to save cloud configuration: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<bool> UpdateConnectionTestResultsAsync(bool isApiTested, string apiResult, bool isWebSocketTested, string websocketResult)
    {
        try
        {
            using var connection = await GetConnectionAsync();
            using var command = new MySqlCommand(@"
                UPDATE cloud_config SET 
                    is_api_tested = @isApiTested,
                    api_test_result = @apiResult,
                    last_api_test = @lastApiTest,
                    is_websocket_tested = @isWebSocketTested,
                    websocket_test_result = @websocketResult,
                    last_websocket_test = @lastWebSocketTest,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = 1", connection);
            
            command.Parameters.AddWithValue("@isApiTested", isApiTested);
            command.Parameters.AddWithValue("@apiResult", apiResult);
            command.Parameters.AddWithValue("@lastApiTest", DateTime.UtcNow);
            command.Parameters.AddWithValue("@isWebSocketTested", isWebSocketTested);
            command.Parameters.AddWithValue("@websocketResult", websocketResult);
            command.Parameters.AddWithValue("@lastWebSocketTest", DateTime.UtcNow);
            
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update test results: {ex.Message}");
            return false;
        }
    }
}
