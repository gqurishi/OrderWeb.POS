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
    private readonly OrderWebApiClient _orderWebApiClient;
    private string? _deviceId;
    private bool _isRunning = false;
    private DateTime _lastHeartbeatAt = DateTime.MinValue;
    private string _lastHeartbeatStatus = "Not started";

    public bool IsRunning => _isRunning;
    public DateTime LastHeartbeatAt => _lastHeartbeatAt;
    public string LastHeartbeatStatus => _lastHeartbeatStatus;
    
    public HeartbeatService(DatabaseService databaseService, OrderWebApiClient orderWebApiClient)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _databaseService = databaseService;
        _orderWebApiClient = orderWebApiClient;
        
        System.Diagnostics.Debug.WriteLine(" HeartbeatService initialized");
    }
    
    /// <summary>
    /// Mark heartbeat as active and send one heartbeat. The BackgroundSyncManager owns the repeat interval.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        if (_isRunning)
        {
            System.Diagnostics.Debug.WriteLine(" Heartbeat already running");
            return true;
        }
        
        _deviceId = await _orderWebApiClient.GetDeviceIdAsync();
        _isRunning = true;
        
        System.Diagnostics.Debug.WriteLine(" Starting heartbeat service (managed by background sync)");
        return await SendHeartbeatAsync();
    }
    
    /// <summary>
    /// Stop heartbeat service
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        System.Diagnostics.Debug.WriteLine(" Heartbeat service stopped");
    }
    
    /// <summary>
    /// Send single heartbeat to OrderWeb.net
    /// </summary>
    public async Task<bool> SendHeartbeatAsync()
    {
        try
        {
            var roleCheck = await _orderWebApiClient.CanRunCloudJobsAsync();
            if (!roleCheck.Allowed)
            {
                _lastHeartbeatStatus = roleCheck.Reason;
                return true;
            }

            var config = await _orderWebApiClient.GetConfigAsync();
            if (config == null)
            {
                _lastHeartbeatStatus = "OrderWeb API is not configured or disabled.";
                return true; // Silently skip if not configured
            }

            _deviceId ??= await _orderWebApiClient.GetDeviceIdAsync();

            var url = OrderWebApiClient.BuildUrl(config, "/pos/heartbeat");
            
            // Get current stats
            var stats = await GetHeartbeatStatsAsync();
            
            var payload = new
            {
                tenant = config.TenantSlug,
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
            
            _orderWebApiClient.ApplyAuthHeaders(request, config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($" Heartbeat sent: {stats.PendingOrders} orders, {stats.PendingAcks} ACKs");
                await LogHeartbeatAsync(stats, true);
                _lastHeartbeatAt = DateTime.Now;
                _lastHeartbeatStatus = "Online";
                return true;
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($" Heartbeat failed: {response.StatusCode}");
                await LogHeartbeatAsync(stats, false);
                _lastHeartbeatAt = DateTime.Now;
                _lastHeartbeatStatus = $"HTTP {(int)response.StatusCode}: {errorBody}";
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Heartbeat error: {ex.Message}");
            _lastHeartbeatAt = DateTime.Now;
            _lastHeartbeatStatus = ex.Message;
            return false;
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
            
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM network_print_queue WHERE status IN ('pending', 'processing', 'printing')";
                var result = await cmd.ExecuteScalarAsync();
                stats.PendingOrders = result != null ? Convert.ToInt32(result) : 0;
            }
            catch
            {
                stats.PendingOrders = 0;
            }

            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT MAX(COALESCE(completed_at, printed_at)) FROM network_print_queue WHERE status = 'completed'";
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    stats.LastPrintAt = Convert.ToDateTime(result);
                }
            }
            catch
            {
                // Ignore missing print queue table on older databases.
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error getting heartbeat stats: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($" Failed to log heartbeat: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
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
