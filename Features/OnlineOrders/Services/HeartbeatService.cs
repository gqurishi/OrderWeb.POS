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
    private Timer? _heartbeatTimer;
    private string? _deviceId;
    private bool _isRunning = false;

    public bool IsRunning => _isRunning;
    
    public HeartbeatService(DatabaseService databaseService, OrderWebApiClient orderWebApiClient)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _databaseService = databaseService;
        _orderWebApiClient = orderWebApiClient;
        
        System.Diagnostics.Debug.WriteLine(" HeartbeatService initialized");
    }
    
    /// <summary>
    /// Start sending heartbeats every 30 seconds
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
        {
            System.Diagnostics.Debug.WriteLine(" Heartbeat already running");
            return;
        }
        
        _deviceId = await _orderWebApiClient.GetDeviceIdAsync();
        _isRunning = true;
        
        System.Diagnostics.Debug.WriteLine(" Starting heartbeat service (30s interval)");
        
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
        System.Diagnostics.Debug.WriteLine(" Heartbeat service stopped");
    }
    
    /// <summary>
    /// Send single heartbeat to OrderWeb.net
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        try
        {
            var roleCheck = await _orderWebApiClient.CanRunCloudJobsAsync();
            if (!roleCheck.Allowed)
            {
                return;
            }

            var config = await _orderWebApiClient.GetConfigAsync();
            if (config == null)
            {
                return; // Silently skip if not configured
            }

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
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($" Heartbeat failed: {response.StatusCode}");
                await LogHeartbeatAsync(stats, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Heartbeat error: {ex.Message}");
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
