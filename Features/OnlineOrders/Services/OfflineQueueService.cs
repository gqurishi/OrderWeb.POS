using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services
{
    /// <summary>
    /// Manages offline queue for API operations when connection is lost
    /// Automatically processes queue when connection is restored
    /// </summary>
    public class OfflineQueueService
    {
        private readonly DatabaseService _db;
        private readonly HttpClient _httpClient;
        private Timer? _processingTimer;
        private bool _isProcessing = false;
        private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);
        
        public event EventHandler<QueueProcessedEventArgs>? QueueProcessed;
        
        public OfflineQueueService(DatabaseService dbService)
        {
            _db = dbService;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
        
        /// <summary>
        /// Start automatic queue processing (every 30 seconds when online)
        /// </summary>
        public void StartAutoProcessing()
        {
            StopAutoProcessing();
            _processingTimer = new Timer(async _ => await ProcessQueueAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
            System.Diagnostics.Debug.WriteLine("🔄 Offline queue auto-processing started");
        }
        
        /// <summary>
        /// Stop automatic queue processing
        /// </summary>
        public void StopAutoProcessing()
        {
            _processingTimer?.Dispose();
            _processingTimer = null;
            System.Diagnostics.Debug.WriteLine("⏸️ Offline queue auto-processing stopped");
        }
        
        /// <summary>
        /// Add operation to offline queue
        /// </summary>
        public async Task<bool> EnqueueAsync(
            string operationType,
            string endpoint,
            object payload,
            string httpMethod = "POST",
            int priority = 5,
            Dictionary<string, string>? customHeaders = null,
            DateTime? scheduledAt = null)
        {
            try
            {
                var payloadJson = JsonSerializer.Serialize(payload);
                var headersJson = customHeaders != null ? JsonSerializer.Serialize(customHeaders) : null;
                
                using var conn = await _db.GetConnectionAsync();
                
                var cmd = new MySqlCommand(@"
                    INSERT INTO offline_queue 
                    (operation_type, endpoint, http_method, payload, headers, priority, scheduled_at)
                    VALUES 
                    (@operation_type, @endpoint, @http_method, @payload, @headers, @priority, @scheduled_at)", conn);
                
                cmd.Parameters.AddWithValue("@operation_type", operationType);
                cmd.Parameters.AddWithValue("@endpoint", endpoint);
                cmd.Parameters.AddWithValue("@http_method", httpMethod);
                cmd.Parameters.AddWithValue("@payload", payloadJson);
                cmd.Parameters.AddWithValue("@headers", headersJson ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@priority", priority);
                cmd.Parameters.AddWithValue("@scheduled_at", scheduledAt ?? (object)DBNull.Value);
                
                await cmd.ExecuteNonQueryAsync();
                
                System.Diagnostics.Debug.WriteLine($"📥 Queued operation: {operationType} -> {endpoint}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to enqueue operation: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Process pending queue items (send to server)
        /// </summary>
        public async Task<(int Sent, int Failed)> ProcessQueueAsync()
        {
            // Prevent concurrent processing
            if (!await _processingLock.WaitAsync(0))
            {
                System.Diagnostics.Debug.WriteLine("⏭️ Queue processing already in progress, skipping");
                return (0, 0);
            }
            
            try
            {
                _isProcessing = true;
                var sent = 0;
                var failed = 0;
                
                // Get pending items (ordered by priority then created_at)
                var items = await GetPendingItemsAsync();
                
                if (items.Count == 0)
                    return (0, 0);
                
                System.Diagnostics.Debug.WriteLine($"🔄 Processing {items.Count} queued operations...");
                
                foreach (var item in items)
                {
                    // Check if scheduled for future
                    if (item.ScheduledAt.HasValue && item.ScheduledAt.Value > DateTime.Now)
                    {
                        System.Diagnostics.Debug.WriteLine($"⏰ Skipping {item.OperationType} (scheduled for {item.ScheduledAt.Value})");
                        continue;
                    }
                    
                    // Mark as processing
                    await UpdateStatusAsync(item.Id, QueueStatus.Processing);
                    
                    try
                    {
                        // Send to server
                        var result = await SendItemAsync(item);
                        
                        if (result.Success)
                        {
                            // Mark as sent
                            await MarkAsSentAsync(item.Id, result.StatusCode, result.ResponseBody);
                            sent++;
                            System.Diagnostics.Debug.WriteLine($"✅ Sent: {item.OperationType} -> {item.Endpoint}");
                        }
                        else
                        {
                            // Increment retry count
                            var newRetryCount = item.RetryCount + 1;
                            
                            if (newRetryCount >= item.MaxRetries)
                            {
                                // Max retries reached - mark as failed
                                await MarkAsFailedAsync(item.Id, result.Error);
                                failed++;
                                System.Diagnostics.Debug.WriteLine($"❌ Failed (max retries): {item.OperationType} - {result.Error}");
                            }
                            else
                            {
                                // Update retry count and error
                                await UpdateRetryAsync(item.Id, newRetryCount, result.Error);
                                System.Diagnostics.Debug.WriteLine($"⚠️ Retry {newRetryCount}/{item.MaxRetries}: {item.OperationType} - {result.Error}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Exception processing {item.OperationType}: {ex.Message}");
                        await UpdateRetryAsync(item.Id, item.RetryCount + 1, ex.Message);
                    }
                }
                
                if (sent > 0 || failed > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Queue processed: {sent} sent, {failed} failed");
                    QueueProcessed?.Invoke(this, new QueueProcessedEventArgs { SentCount = sent, FailedCount = failed });
                }
                
                return (sent, failed);
            }
            finally
            {
                _isProcessing = false;
                _processingLock.Release();
            }
        }
        
        /// <summary>
        /// Send a queue item to the server
        /// </summary>
        private async Task<SendResult> SendItemAsync(OfflineQueueItem item)
        {
            try
            {
                // Prepare request
                var request = new HttpRequestMessage(
                    new HttpMethod(item.HttpMethod),
                    item.Endpoint);
                
                // Add custom headers
                var headers = item.GetHeaders();
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
                
                // Add payload for POST/PUT/PATCH
                if (item.HttpMethod != "GET" && item.HttpMethod != "DELETE")
                {
                    request.Content = new StringContent(item.Payload, Encoding.UTF8, "application/json");
                }
                
                // Send request
                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    return new SendResult 
                    { 
                        Success = true, 
                        StatusCode = (int)response.StatusCode,
                        ResponseBody = responseBody
                    };
                }
                else
                {
                    return new SendResult 
                    { 
                        Success = false, 
                        Error = $"HTTP {(int)response.StatusCode}: {responseBody}",
                        StatusCode = (int)response.StatusCode
                    };
                }
            }
            catch (Exception ex)
            {
                return new SendResult 
                { 
                    Success = false, 
                    Error = ex.Message 
                };
            }
        }
        
        /// <summary>
        /// Get pending items from queue
        /// </summary>
        private async Task<List<OfflineQueueItem>> GetPendingItemsAsync()
        {
            var items = new List<OfflineQueueItem>();
            
            try
            {
                using var conn = await _db.GetConnectionAsync();
                
                var cmd = new MySqlCommand(@"
                    SELECT * FROM offline_queue 
                    WHERE status = 'pending' 
                    ORDER BY priority ASC, created_at ASC 
                    LIMIT 50", conn);
                
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    items.Add(new OfflineQueueItem
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("id")),
                        OperationType = reader.GetString(reader.GetOrdinal("operation_type")),
                        Endpoint = reader.GetString(reader.GetOrdinal("endpoint")),
                        HttpMethod = reader.GetString(reader.GetOrdinal("http_method")),
                        Payload = reader.GetString(reader.GetOrdinal("payload")),
                        Headers = reader.IsDBNull(reader.GetOrdinal("headers")) ? null : reader.GetString(reader.GetOrdinal("headers")),
                        Priority = reader.GetInt32(reader.GetOrdinal("priority")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                        ScheduledAt = reader.IsDBNull(reader.GetOrdinal("scheduled_at")) ? null : reader.GetDateTime(reader.GetOrdinal("scheduled_at")),
                        Status = Enum.Parse<QueueStatus>(reader.GetString(reader.GetOrdinal("status")), true),
                        RetryCount = reader.GetInt32(reader.GetOrdinal("retry_count")),
                        MaxRetries = reader.GetInt32(reader.GetOrdinal("max_retries"))
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to get pending items: {ex.Message}");
            }
            
            return items;
        }
        
        /// <summary>
        /// Update queue item status
        /// </summary>
        private async Task UpdateStatusAsync(int id, QueueStatus status)
        {
            try
            {
                using var conn = await _db.GetConnectionAsync();
                
                var cmd = new MySqlCommand(@"
                    UPDATE offline_queue 
                    SET status = @status 
                    WHERE id = @id", conn);
                
                cmd.Parameters.AddWithValue("@status", status.ToString().ToLower());
                cmd.Parameters.AddWithValue("@id", id);
                
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to update status: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Mark item as successfully sent
        /// </summary>
        private async Task MarkAsSentAsync(int id, int statusCode, string? responseBody)
        {
            try
            {
                using var conn = await _db.GetConnectionAsync();
                
                var cmd = new MySqlCommand(@"
                    UPDATE offline_queue 
                    SET status = 'sent', 
                        sent_at = NOW(),
                        response_status = @status_code,
                        response_body = @response_body
                    WHERE id = @id", conn);
                
                cmd.Parameters.AddWithValue("@status_code", statusCode);
                cmd.Parameters.AddWithValue("@response_body", responseBody ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@id", id);
                
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to mark as sent: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Mark item as failed (after max retries)
        /// </summary>
        private async Task MarkAsFailedAsync(int id, string? error)
        {
            try
            {
                using var conn = await _db.GetConnectionAsync();
                
                var cmd = new MySqlCommand(@"
                    UPDATE offline_queue 
                    SET status = 'failed', 
                        last_error = @error,
                        last_attempt_at = NOW()
                    WHERE id = @id", conn);
                
                cmd.Parameters.AddWithValue("@error", error ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@id", id);
                
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to mark as failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update retry count and error
        /// </summary>
        private async Task UpdateRetryAsync(int id, int retryCount, string? error)
        {
            try
            {
                using var conn = await _db.GetConnectionAsync();
                
                var cmd = new MySqlCommand(@"
                    UPDATE offline_queue 
                    SET retry_count = @retry_count,
                        last_error = @error,
                        last_attempt_at = NOW(),
                        status = 'pending'
                    WHERE id = @id", conn);
                
                cmd.Parameters.AddWithValue("@retry_count", retryCount);
                cmd.Parameters.AddWithValue("@error", error ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@id", id);
                
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to update retry: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get queue statistics
        /// </summary>
        public async Task<OfflineQueueStats> GetStatsAsync()
        {
            var stats = new OfflineQueueStats();
            
            try
            {
                using var conn = await _db.GetConnectionAsync();
                
                var cmd = new MySqlCommand(@"
                    SELECT 
                        SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END) as pending_count,
                        SUM(CASE WHEN status = 'processing' THEN 1 ELSE 0 END) as processing_count,
                        SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END) as failed_count,
                        SUM(CASE WHEN status = 'sent' AND DATE(sent_at) = CURDATE() THEN 1 ELSE 0 END) as sent_today_count,
                        MIN(CASE WHEN status = 'pending' THEN created_at END) as oldest_pending,
                        SUM(retry_count) as total_retries
                    FROM offline_queue", conn);
                
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    stats.PendingCount = reader.IsDBNull(reader.GetOrdinal("pending_count")) ? 0 : reader.GetInt32(reader.GetOrdinal("pending_count"));
                    stats.ProcessingCount = reader.IsDBNull(reader.GetOrdinal("processing_count")) ? 0 : reader.GetInt32(reader.GetOrdinal("processing_count"));
                    stats.FailedCount = reader.IsDBNull(reader.GetOrdinal("failed_count")) ? 0 : reader.GetInt32(reader.GetOrdinal("failed_count"));
                    stats.SentTodayCount = reader.IsDBNull(reader.GetOrdinal("sent_today_count")) ? 0 : reader.GetInt32(reader.GetOrdinal("sent_today_count"));
                    stats.OldestPendingItem = reader.IsDBNull(reader.GetOrdinal("oldest_pending")) ? null : reader.GetDateTime(reader.GetOrdinal("oldest_pending"));
                    stats.TotalRetries = reader.IsDBNull(reader.GetOrdinal("total_retries")) ? 0 : Convert.ToInt32(reader.GetDecimal(reader.GetOrdinal("total_retries")));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to get stats: {ex.Message}");
            }
            
            return stats;
        }
        
        /// <summary>
        /// Clear old sent items (keep last 7 days)
        /// </summary>
        public async Task<int> CleanupOldItemsAsync(int daysToKeep = 7)
        {
            try
            {
                using var conn = await _db.GetConnectionAsync();
                
                var cmd = new MySqlCommand(@"
                    DELETE FROM offline_queue 
                    WHERE status = 'sent' 
                    AND sent_at < DATE_SUB(NOW(), INTERVAL @days DAY)", conn);
                
                cmd.Parameters.AddWithValue("@days", daysToKeep);
                
                var deleted = await cmd.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"🗑️ Cleaned up {deleted} old queue items");
                return deleted;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to cleanup: {ex.Message}");
                return 0;
            }
        }
    }
    
    /// <summary>
    /// Result of sending a queue item
    /// </summary>
    internal class SendResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string? ResponseBody { get; set; }
        public string? Error { get; set; }
    }
    
    /// <summary>
    /// Event args for queue processed event
    /// </summary>
    public class QueueProcessedEventArgs : EventArgs
    {
        public int SentCount { get; set; }
        public int FailedCount { get; set; }
    }
}
