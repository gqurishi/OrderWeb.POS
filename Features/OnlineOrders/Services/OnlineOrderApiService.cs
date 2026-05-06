using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using POS_in_NET.Models;
using POS_in_NET.Models.Api;
using MySqlConnector;

namespace POS_in_NET.Services;

public class OnlineOrderApiService
{
    private readonly HttpClient _httpClient;
    private readonly DatabaseService _databaseService;
    private ApiConfiguration? _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public OnlineOrderApiService()
    {
        _httpClient = new HttpClient();
        _databaseService = new DatabaseService();
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        
        // Set default timeout
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            _config = await LoadApiConfigurationAsync();
            if (_config != null)
            {
                ConfigureHttpClient();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API initialization failed: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        try
        {
            if (_config == null)
            {
                return (false, "API not configured. Please set up API configuration first.");
            }

            // Test endpoint - you can customize this based on your API
            var response = await _httpClient.GetAsync("api/test");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return (true, $"Connection successful. Response: {content}");
            }
            else
            {
                return (false, $"Connection failed. Status: {response.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timeout. Check your internet connection and API endpoint.");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<(bool Success, List<Order> Orders, string Message)> FetchNewOrdersAsync()
    {
        try
        {
            if (_config == null)
            {
                return (false, new List<Order>(), "API not configured");
            }

            // Get last sync time to only fetch new orders
            var lastSync = await GetLastSyncTimeAsync();
            var url = $"api/orders/new?since={lastSync:yyyy-MM-ddTHH:mm:ss}";
            
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<ApiResponse<OrdersResponse>>(content, _jsonOptions);
                
                if (apiResponse?.Success == true && apiResponse.Data != null)
                {
                    var orders = apiResponse.Data.Orders.Select(apiOrder => apiOrder.ToOrder()).ToList();
                    
                    // Update last sync time
                    await UpdateLastSyncTimeAsync(DateTime.Now);
                    
                    return (true, orders, $"Fetched {orders.Count} new orders");
                }
                else
                {
                    return (false, new List<Order>(), apiResponse?.Message ?? "Unknown error");
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return (false, new List<Order>(), $"API error: {response.StatusCode} - {errorContent}");
            }
        }
        catch (HttpRequestException ex)
        {
            return (false, new List<Order>(), $"Network error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return (false, new List<Order>(), $"Data parsing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, new List<Order>(), $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UpdateOrderStatusAsync(string orderId, OrderStatus status, string? notes = null)
    {
        try
        {
            if (_config == null)
            {
                return (false, "API not configured");
            }

            var statusUpdate = new OrderStatusUpdate
            {
                OrderId = orderId,
                Status = status.ToString().ToLower(),
                UpdateTime = DateTime.Now,
                Notes = notes,
                UpdatedBy = "POS_System" // You can get current user info here
            };

            var json = JsonSerializer.Serialize(statusUpdate, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("api/orders/status", content);
            
            if (response.IsSuccessStatusCode)
            {
                return (true, "Order status updated successfully");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return (false, $"Status update failed: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Status update error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> SaveApiConfigurationAsync(ApiConfiguration config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            
            using var connection = new MySqlConnection("Server=localhost;Database=Pos-net;Uid=root;Pwd=root;Port=3306;");
            await connection.OpenAsync();

            var query = @"INSERT INTO settings (setting_key, setting_value) 
                         VALUES ('api_configuration', @config) 
                         ON DUPLICATE KEY UPDATE setting_value = @config";
            
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@config", json);
            
            await command.ExecuteNonQueryAsync();
            
            _config = config;
            ConfigureHttpClient();
            
            return (true, "API configuration saved successfully");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to save configuration: {ex.Message}");
        }
    }

    private async Task<ApiConfiguration?> LoadApiConfigurationAsync()
    {
        try
        {
            using var connection = new MySqlConnection("Server=localhost;Database=Pos-net;Uid=root;Pwd=root;Port=3306;");
            await connection.OpenAsync();

            var query = "SELECT setting_value FROM settings WHERE setting_key = 'api_configuration'";
            using var command = new MySqlCommand(query, connection);
            
            var result = await command.ExecuteScalarAsync();
            if (result != null)
            {
                var json = result.ToString();
                return JsonSerializer.Deserialize<ApiConfiguration>(json!, _jsonOptions);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load API configuration: {ex.Message}");
            return null;
        }
    }

    private void ConfigureHttpClient()
    {
        if (_config == null) return;

        _httpClient.BaseAddress = new Uri(_config.BaseUrl);
        _httpClient.DefaultRequestHeaders.Clear();
        
        // Add API authentication headers
        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _config.ApiKey);
        }
        
        if (!string.IsNullOrEmpty(_config.ApiSecret))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Secret", _config.ApiSecret);
        }
        
        if (!string.IsNullOrEmpty(_config.RestaurantId))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Restaurant-Id", _config.RestaurantId);
        }
        
        if (!string.IsNullOrEmpty(_config.LocationId))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Location-Id", _config.LocationId);
        }

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "POS-System/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    private async Task<DateTime> GetLastSyncTimeAsync()
    {
        try
        {
            using var connection = new MySqlConnection("Server=localhost;Database=Pos-net;Uid=root;Pwd=root;Port=3306;");
            await connection.OpenAsync();

            var query = "SELECT setting_value FROM settings WHERE setting_key = 'last_sync_time'";
            using var command = new MySqlCommand(query, connection);
            
            var result = await command.ExecuteScalarAsync();
            if (result != null && DateTime.TryParse(result.ToString(), out var lastSync))
            {
                return lastSync;
            }
            
            // If no last sync time, return 24 hours ago
            return DateTime.Now.AddHours(-24);
        }
        catch
        {
            return DateTime.Now.AddHours(-24);
        }
    }

    private async Task UpdateLastSyncTimeAsync(DateTime syncTime)
    {
        try
        {
            using var connection = new MySqlConnection("Server=localhost;Database=Pos-net;Uid=root;Pwd=root;Port=3306;");
            await connection.OpenAsync();

            var query = @"INSERT INTO settings (setting_key, setting_value) 
                         VALUES ('last_sync_time', @syncTime) 
                         ON DUPLICATE KEY UPDATE setting_value = @syncTime";
            
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@syncTime", syncTime.ToString("yyyy-MM-dd HH:mm:ss"));
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update sync time: {ex.Message}");
        }
    }

    public async Task<ApiConfiguration?> GetCurrentConfigurationAsync()
    {
        if (_config == null)
        {
            _config = await LoadApiConfigurationAsync();
        }
        return _config;
    }

    public bool IsConfigured => _config != null && !string.IsNullOrEmpty(_config.BaseUrl);

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}