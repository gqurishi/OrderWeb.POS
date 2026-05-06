using POS_in_NET.Models;
using MySqlConnector;
using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Print job model for the queue
/// </summary>
public class NetworkPrintJob
{
    public int Id { get; set; }
    public int PrinterId { get; set; }
    public string PrinterName { get; set; } = "";
    public string JobType { get; set; } = "receipt"; // receipt, kitchen, bar, test
    public byte[] PrintData { get; set; } = Array.Empty<byte>();
    public string? OrderId { get; set; }
    public string Status { get; set; } = "pending"; // pending, printing, completed, failed
    public int RetryCount { get; set; } = 0;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? PrintedAt { get; set; }
    public DateTime? LastAttempt { get; set; }
}

/// <summary>
/// Queue statistics
/// </summary>
public class NetworkPrintQueueStats
{
    public int PendingJobs { get; set; }
    public int PrintingJobs { get; set; }
    public int CompletedToday { get; set; }
    public int FailedToday { get; set; }
    public DateTime LastProcessed { get; set; }
}

/// <summary>
/// Background service that processes the print queue.
/// Retries failed jobs automatically with exponential backoff.
/// </summary>
public class NetworkPrintQueueService : IDisposable
{
    private readonly NetworkPrinterDatabaseService _dbService;
    private readonly NetworkPrinterService _printerService;
    private readonly DatabaseService _databaseService;
    private Timer? _processTimer;
    private bool _isRunning = false;
    private bool _isProcessing = false;
    private readonly object _lock = new();
    
    // Configuration
    private const int PROCESS_INTERVAL_MS = 5000; // Process queue every 5 seconds
    private const int MAX_RETRIES = 5;
    private const int BASE_RETRY_DELAY_SECONDS = 30; // Exponential backoff base
    
    // Statistics
    public DateTime LastProcessed { get; private set; } = DateTime.MinValue;
    public int JobsProcessedToday { get; private set; } = 0;
    public int JobsFailedToday { get; private set; } = 0;
    
    // Events
    public event EventHandler<PrintJobCompletedEventArgs>? JobCompleted;
    public event EventHandler<PrintJobFailedEventArgs>? JobFailed;

    public NetworkPrintQueueService(
        NetworkPrinterDatabaseService dbService,
        NetworkPrinterService printerService,
        DatabaseService databaseService)
    {
        _dbService = dbService;
        _printerService = printerService;
        _databaseService = databaseService;
        Debug.WriteLine("📋 NetworkPrintQueueService initialized");
    }

    /// <summary>
    /// Ensure the print_queue table exists
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS network_print_queue (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    printer_id INT NOT NULL,
                    job_type VARCHAR(50) NOT NULL DEFAULT 'receipt',
                    print_data LONGBLOB NOT NULL,
                    order_id VARCHAR(100),
                    status ENUM('pending', 'printing', 'completed', 'failed') DEFAULT 'pending',
                    retry_count INT DEFAULT 0,
                    error_message TEXT,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    printed_at TIMESTAMP NULL,
                    last_attempt TIMESTAMP NULL,
                    INDEX idx_status (status),
                    INDEX idx_printer (printer_id),
                    INDEX idx_created (created_at)
                )";
            
            await command.ExecuteNonQueryAsync();
            Debug.WriteLine("✅ network_print_queue table ready");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error creating print_queue table: {ex.Message}");
        }
    }

    /// <summary>
    /// Start the background queue processor
    /// </summary>
    public async Task StartAsync()
    {
        await EnsureTableExistsAsync();
        
        lock (_lock)
        {
            if (_isRunning)
            {
                Debug.WriteLine("⚠️ NetworkPrintQueueService already running");
                return;
            }

            _isRunning = true;
            
            // Process queue every 5 seconds
            _processTimer = new Timer(
                async _ => await ProcessQueueAsync(),
                null,
                TimeSpan.FromSeconds(2), // Start after 2 seconds
                TimeSpan.FromMilliseconds(PROCESS_INTERVAL_MS)
            );
            
            Debug.WriteLine($"✅ NetworkPrintQueueService STARTED - processing every {PROCESS_INTERVAL_MS / 1000}s");
        }
    }

    /// <summary>
    /// Stop the background queue processor
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _processTimer?.Dispose();
            _processTimer = null;
            _isRunning = false;
            
            Debug.WriteLine("🛑 NetworkPrintQueueService STOPPED");
        }
    }

    /// <summary>
    /// Add a print job to the queue
    /// </summary>
    public async Task<int> EnqueueAsync(int printerId, byte[] printData, string jobType = "receipt", string? orderId = null)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                INSERT INTO network_print_queue (printer_id, job_type, print_data, order_id, status)
                VALUES (@printerId, @jobType, @printData, @orderId, 'pending');
                SELECT LAST_INSERT_ID();";
            
            command.Parameters.AddWithValue("@printerId", printerId);
            command.Parameters.AddWithValue("@jobType", jobType);
            command.Parameters.AddWithValue("@printData", printData);
            command.Parameters.AddWithValue("@orderId", orderId ?? (object)DBNull.Value);
            
            var result = await command.ExecuteScalarAsync();
            var jobId = Convert.ToInt32(result);
            
            Debug.WriteLine($"📥 Print job #{jobId} enqueued for printer #{printerId}");
            return jobId;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error enqueueing print job: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Process pending jobs in the queue
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        // Prevent overlapping processing
        if (_isProcessing)
        {
            return;
        }

        _isProcessing = true;
        
        try
        {
            // Get pending jobs that are ready to retry
            var jobs = await GetPendingJobsAsync();
            
            if (jobs.Count == 0)
            {
                return;
            }

            Debug.WriteLine($"📋 Processing {jobs.Count} pending print job(s)...");

            foreach (var job in jobs)
            {
                await ProcessJobAsync(job);
            }

            LastProcessed = DateTime.Now;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Queue processing error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// Get pending jobs ready for processing
    /// </summary>
    private async Task<List<NetworkPrintJob>> GetPendingJobsAsync()
    {
        var jobs = new List<NetworkPrintJob>();
        
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            // Get pending jobs, respecting retry delay
            command.CommandText = @"
                SELECT pq.id, pq.printer_id, pq.job_type, pq.print_data, pq.order_id, 
                       pq.status, pq.retry_count, pq.error_message, pq.created_at, 
                       pq.printed_at, pq.last_attempt, p.printer_name
                FROM network_print_queue pq
                JOIN printers p ON pq.printer_id = p.id
                WHERE pq.status IN ('pending', 'failed')
                  AND pq.retry_count < @maxRetries
                  AND p.is_enabled = 1
                  AND (pq.last_attempt IS NULL 
                       OR pq.last_attempt < DATE_SUB(NOW(), INTERVAL POWER(2, pq.retry_count) * @baseDelay SECOND))
                ORDER BY pq.created_at ASC
                LIMIT 10";
            
            command.Parameters.AddWithValue("@maxRetries", MAX_RETRIES);
            command.Parameters.AddWithValue("@baseDelay", BASE_RETRY_DELAY_SECONDS);
            
            using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                jobs.Add(new NetworkPrintJob
                {
                    Id = reader.GetInt32("id"),
                    PrinterId = reader.GetInt32("printer_id"),
                    PrinterName = reader.IsDBNull(reader.GetOrdinal("printer_name")) ? "Unknown" : reader.GetString("printer_name"),
                    JobType = reader.GetString("job_type"),
                    PrintData = (byte[])reader["print_data"],
                    OrderId = reader.IsDBNull(reader.GetOrdinal("order_id")) ? null : reader.GetString("order_id"),
                    Status = reader.GetString("status"),
                    RetryCount = reader.GetInt32("retry_count"),
                    ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString("error_message"),
                    CreatedAt = reader.GetDateTime("created_at"),
                    PrintedAt = reader.IsDBNull(reader.GetOrdinal("printed_at")) ? null : reader.GetDateTime("printed_at"),
                    LastAttempt = reader.IsDBNull(reader.GetOrdinal("last_attempt")) ? null : reader.GetDateTime("last_attempt")
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error getting pending jobs: {ex.Message}");
        }
        
        return jobs;
    }

    /// <summary>
    /// Process a single print job
    /// </summary>
    private async Task ProcessJobAsync(NetworkPrintJob job)
    {
        try
        {
            Debug.WriteLine($"🖨️ Processing job #{job.Id} for '{job.PrinterName}'...");
            
            // Mark as printing
            await UpdateJobStatusAsync(job.Id, "printing");
            
            // Get printer
            var printer = await _dbService.GetPrinterByIdAsync(job.PrinterId);
            if (printer == null)
            {
                await FailJobAsync(job.Id, "Printer not found");
                return;
            }

            if (!printer.IsOnline)
            {
                await RetryJobAsync(job.Id, "Printer is offline");
                return;
            }

            // Send to printer
            var success = await _printerService.SendToPrinterAsync(printer, job.PrintData);
            
            if (success)
            {
                await CompleteJobAsync(job.Id);
                JobsProcessedToday++;
                
                Debug.WriteLine($"✅ Job #{job.Id} printed successfully");
                
                JobCompleted?.Invoke(this, new PrintJobCompletedEventArgs
                {
                    JobId = job.Id,
                    PrinterName = job.PrinterName,
                    OrderId = job.OrderId
                });
            }
            else
            {
                await RetryJobAsync(job.Id, "Send failed");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Job #{job.Id} error: {ex.Message}");
            await RetryJobAsync(job.Id, ex.Message);
        }
    }

    /// <summary>
    /// Update job status
    /// </summary>
    private async Task UpdateJobStatusAsync(int jobId, string status)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = @status, last_attempt = NOW()
                WHERE id = @id";
            
            command.Parameters.AddWithValue("@id", jobId);
            command.Parameters.AddWithValue("@status", status);
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error updating job status: {ex.Message}");
        }
    }

    /// <summary>
    /// Mark job as completed
    /// </summary>
    private async Task CompleteJobAsync(int jobId)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = 'completed', printed_at = NOW(), last_attempt = NOW()
                WHERE id = @id";
            
            command.Parameters.AddWithValue("@id", jobId);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error completing job: {ex.Message}");
        }
    }

    /// <summary>
    /// Mark job for retry
    /// </summary>
    private async Task RetryJobAsync(int jobId, string errorMessage)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = 'failed', 
                    retry_count = retry_count + 1,
                    error_message = @error,
                    last_attempt = NOW()
                WHERE id = @id";
            
            command.Parameters.AddWithValue("@id", jobId);
            command.Parameters.AddWithValue("@error", errorMessage);
            
            await command.ExecuteNonQueryAsync();
            
            Debug.WriteLine($"🔄 Job #{jobId} will retry: {errorMessage}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error retrying job: {ex.Message}");
        }
    }

    /// <summary>
    /// Mark job as permanently failed
    /// </summary>
    private async Task FailJobAsync(int jobId, string errorMessage)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = 'failed', 
                    retry_count = @maxRetries,
                    error_message = @error,
                    last_attempt = NOW()
                WHERE id = @id";
            
            command.Parameters.AddWithValue("@id", jobId);
            command.Parameters.AddWithValue("@maxRetries", MAX_RETRIES);
            command.Parameters.AddWithValue("@error", errorMessage);
            
            await command.ExecuteNonQueryAsync();
            
            JobsFailedToday++;
            
            Debug.WriteLine($"❌ Job #{jobId} permanently failed: {errorMessage}");
            
            JobFailed?.Invoke(this, new PrintJobFailedEventArgs
            {
                JobId = jobId,
                ErrorMessage = errorMessage
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error failing job: {ex.Message}");
        }
    }

    /// <summary>
    /// Get queue statistics
    /// </summary>
    public async Task<NetworkPrintQueueStats> GetStatsAsync()
    {
        var stats = new NetworkPrintQueueStats
        {
            LastProcessed = LastProcessed
        };
        
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                SELECT 
                    SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END) as pending,
                    SUM(CASE WHEN status = 'printing' THEN 1 ELSE 0 END) as printing,
                    SUM(CASE WHEN status = 'completed' AND DATE(printed_at) = CURDATE() THEN 1 ELSE 0 END) as completed_today,
                    SUM(CASE WHEN status = 'failed' AND retry_count >= @maxRetries AND DATE(last_attempt) = CURDATE() THEN 1 ELSE 0 END) as failed_today
                FROM network_print_queue";
            
            command.Parameters.AddWithValue("@maxRetries", MAX_RETRIES);
            
            using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                stats.PendingJobs = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                stats.PrintingJobs = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                stats.CompletedToday = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                stats.FailedToday = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error getting queue stats: {ex.Message}");
        }
        
        return stats;
    }

    /// <summary>
    /// Retry all failed jobs manually
    /// </summary>
    public async Task<int> RetryAllFailedJobsAsync()
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = 'pending', retry_count = 0, error_message = NULL
                WHERE status = 'failed' AND retry_count >= @maxRetries";
            
            command.Parameters.AddWithValue("@maxRetries", MAX_RETRIES);
            
            var affected = await command.ExecuteNonQueryAsync();
            
            Debug.WriteLine($"🔄 Reset {affected} failed jobs for retry");
            return affected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error retrying failed jobs: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Clear completed jobs older than specified days
    /// </summary>
    public async Task<int> ClearOldJobsAsync(int daysOld = 7)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                DELETE FROM network_print_queue 
                WHERE status = 'completed' 
                  AND printed_at < DATE_SUB(NOW(), INTERVAL @days DAY)";
            
            command.Parameters.AddWithValue("@days", daysOld);
            
            var affected = await command.ExecuteNonQueryAsync();
            
            Debug.WriteLine($"🧹 Cleared {affected} old completed jobs");
            return affected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error clearing old jobs: {ex.Message}");
            return 0;
        }
    }

    public bool IsRunning => _isRunning;

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Event args for completed print jobs
/// </summary>
public class PrintJobCompletedEventArgs : EventArgs
{
    public int JobId { get; set; }
    public string PrinterName { get; set; } = "";
    public string? OrderId { get; set; }
}

/// <summary>
/// Event args for failed print jobs
/// </summary>
public class PrintJobFailedEventArgs : EventArgs
{
    public int JobId { get; set; }
    public string ErrorMessage { get; set; } = "";
}
