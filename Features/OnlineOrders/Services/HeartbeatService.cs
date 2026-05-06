using System.Text.Json;
using System.Text;

namespace POS_in_NET.Services;

/// <summary>
/// Sends heartbeat signals to OrderWeb.net every 30 seconds
/// Allows OrderWeb.net to detect if POS goes offline
/// </summary>
public class HeartbeatService
{
    private readonly HttpClient _httpClient;
    private readonly DatabaseService _databaseService;
    private Timer? _heartbeatTimer;
    private string? _deviceId;
    private bool _isRunning = false;
    
    public HeartbeatService(DatabaseService databaseService)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _databaseService = databaseService;
        
        System.Diagnostics.Debug.WriteLine("💓 HeartbeatService initialized");
    }
    
    /// <summary>
    /// Start sending heartbeats every 30 seconds
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
        {
            System.Diagnostics.Debug.WriteLine("⚠️ Heartbeat already running");
            return;
        }
        
        _deviceId = await GetDeviceIdAsync();
        _isRunning = true;
        
        System.Diagnostics.Debug.WriteLine("💓 Starting heartbeat service (30s interval)");
        
        // Send immediate heartbeat
        _ = Task.Run(async () => await SendHeartbeatAsync());
        
        // Start timer for recurring heartbeats
        _heartbeatTimer = new Timer(
            async _ => await SendHeartbeatAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30)
        );
    }
    
    /// <summary>
    /// Stop heartbeat service
    /// </summary>
    public void Stop()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _isRunning = false;
        System.Diagnostics.Debug.WriteLine("💓 Heartbeat service stopped");
    }
    
    /// <summary>
    /// Send single heartbeat to OrderWeb.net
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            var apiKey = config.GetValueOrDefault("api_key", "");
            var cloudUrl = config.GetValueOrDefault("cloud_url", "https://orderweb.net/api");
            
            if (string.IsNullOrEmpty(tenantSlug) || string.IsNullOrEmpty(apiKey))
            {
                return; // Silently skip if not configured
            }

            var url = $"{cloudUrl}/pos/heartbeat";
            
            // Get current stats
            var stats = await GetHeartbeatStatsAsync();
            
            var payload = new
            {
                tenant = tenantSlug,
                device_id = _deviceId,
                status = "online",
                pending_acks_count = stats.PendingAcks,
                pending_orders_count = stats.PendingOrders,
                last_print_at = stats.LastPrintAt?.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"💓 Heartbeat sent: {stats.PendingOrders} orders, {stats.PendingAcks} ACKs");
                await LogHeartbeatAsync(stats, true);
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"❌ Heartbeat failed: {response.StatusCode}");
                await LogHeartbeatAsync(stats, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Heartbeat error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get device ID from database or generate new one
    /// </summary>
    private async Task<string> GetDeviceIdAsync()
    {
        if (!string.IsNullOrEmpty(_deviceId))
            return _deviceId;
            
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = "SELECT value FROM cloud_config WHERE `key` = 'device_id'";
            var result = await command.ExecuteScalarAsync();
            
            if (result != null && !string.IsNullOrEmpty(result.ToString()))
            {
                _deviceId = result.ToString();
            }
            else
            {
                _deviceId = $"POS_{Environment.MachineName}_{Guid.NewGuid().ToString().Substring(0, 8)}";
                
                command.CommandText = @"INSERT INTO cloud_config (`key`, value) VALUES ('device_id', @deviceId)
                                       ON DUPLICATE KEY UPDATE value = @deviceId";
                command.Parameters.AddWithValue("@deviceId", _deviceId);
                await command.ExecuteNonQueryAsync();
            }
            
            return _deviceId!;
        }
        catch
        {
            return $"POS_{Environment.MachineName}";
        }
    }
    
    /// <summary>
    /// Get current heartbeat statistics
    /// </summary>
    private async Task<HeartbeatStats> GetHeartbeatStatsAsync()
    {
        var stats = new HeartbeatStats();
        
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            
            // Get pending ACKs count (check if table exists first)
            try
            {
                using var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = @"
                    SELECT COUNT(*) FROM information_schema.tables 
                    WHERE table_schema = DATABASE() AND table_name = 'pending_acks'";
                var tableExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
                
                if (tableExists)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM pending_acks WHERE retry_count < 10";
                    var result = await cmd.ExecuteScalarAsync();
                    stats.PendingAcks = result != null ? Convert.ToInt32(result) : 0;
                }
                else
                {
                    stats.PendingAcks = 0;
                }
            }
            catch
            {
                stats.PendingAcks = 0;
            }
            
            // Get pending orders count
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM print_queue WHERE status IN ('pending', 'printing')";
                var result = await cmd.ExecuteScalarAsync();
                stats.PendingOrders = result != null ? Convert.ToInt32(result) : 0;
            }
            
            // Get last print time
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT MAX(completed_at) FROM print_queue WHERE status = 'success'";
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    stats.LastPrintAt = Convert.ToDateTime(result);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error getting heartbeat stats: {ex.Message}");
        }
        
        return stats;
    }
    
    /// <summary>
    /// Log heartbeat to database
    /// </summary>
    private async Task LogHeartbeatAsync(HeartbeatStats stats, bool success)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                INSERT INTO heartbeat_log (device_id, status, pending_acks_count, pending_orders_count, last_print_at, sent_at) 
                VALUES (@deviceId, @status, @acks, @orders, @lastPrint, @sentAt)";
            
            command.Parameters.AddWithValue("@deviceId", _deviceId);
            command.Parameters.AddWithValue("@status", success ? "online" : "error");
            command.Parameters.AddWithValue("@acks", stats.PendingAcks);
            command.Parameters.AddWithValue("@orders", stats.PendingOrders);
            command.Parameters.AddWithValue("@lastPrint", stats.LastPrintAt.HasValue ? (object)stats.LastPrintAt.Value : DBNull.Value);
            command.Parameters.AddWithValue("@sentAt", DateTime.UtcNow);
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Failed to log heartbeat: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        Stop();
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Heartbeat statistics model
/// </summary>
public class HeartbeatStats
{
    public int PendingAcks { get; set; }
    public int PendingOrders { get; set; }
    public DateTime? LastPrintAt { get; set; }
}
