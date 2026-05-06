using System;
using System.Text.Json;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Represents a queued operation to be sent when connection is restored
    /// </summary>
    public class OfflineQueueItem
    {
        public int Id { get; set; }
        
        // Operation details
        public string OperationType { get; set; } = string.Empty; // order_status, print_ack, gift_card, loyalty, custom
        public string Endpoint { get; set; } = string.Empty; // API endpoint to call
        public string HttpMethod { get; set; } = "POST"; // GET, POST, PUT, DELETE, PATCH
        
        // Request payload
        public string Payload { get; set; } = string.Empty; // JSON payload as string
        public string? Headers { get; set; } // Custom headers as JSON string (optional)
        
        // Queue metadata
        public int Priority { get; set; } = 5; // 1=highest, 10=lowest
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ScheduledAt { get; set; } // When to attempt sending (for delayed operations)
        
        // Retry logic
        public QueueStatus Status { get; set; } = QueueStatus.Pending;
        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;
        public DateTime? LastAttemptAt { get; set; }
        public string? LastError { get; set; }
        
        // Success tracking
        public DateTime? SentAt { get; set; }
        public int? ResponseStatus { get; set; } // HTTP status code
        public string? ResponseBody { get; set; }
        
        // Helper method to deserialize payload
        public T? GetPayloadAs<T>()
        {
            try
            {
                return JsonSerializer.Deserialize<T>(Payload);
            }
            catch
            {
                return default;
            }
        }
        
        // Helper method to get custom headers
        public Dictionary<string, string>? GetHeaders()
        {
            if (string.IsNullOrEmpty(Headers))
                return null;
                
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(Headers);
            }
            catch
            {
                return null;
            }
        }
    }
    
    /// <summary>
    /// Status of queued operation
    /// </summary>
    public enum QueueStatus
    {
        Pending,    // Waiting to be sent
        Processing, // Currently being sent
        Sent,       // Successfully sent and confirmed
        Failed,     // Failed after max retries
        Cancelled   // Cancelled by user/system
    }
    
    /// <summary>
    /// Statistics about the offline queue
    /// </summary>
    public class OfflineQueueStats
    {
        public int PendingCount { get; set; }
        public int ProcessingCount { get; set; }
        public int FailedCount { get; set; }
        public int SentTodayCount { get; set; }
        public DateTime? OldestPendingItem { get; set; }
        public int TotalRetries { get; set; }
    }
}
