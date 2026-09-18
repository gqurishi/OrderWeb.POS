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
    /// <summary>pending(=queued) → printing(=sending) → completed(=printed) | failed | needs_attention</summary>
    public string Status { get; set; } = PrintJobLifecycle.DbPending;
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? PrintedAt { get; set; }
    public DateTime? LastAttempt { get; set; }

    public string LifecycleStatus => PrintJobLifecycle.FromQueueStatus(Status);

    public string StaffCategory => PrintJobLifecycle.FormatStaffCategory(JobType);

    public string OrderDisplay =>
        string.IsNullOrWhiteSpace(OrderId) ? "—" : OrderId.Trim();

    public string TimeDisplay =>
        (LastAttempt ?? CreatedAt).ToString("dd/MM HH:mm");

    public string StatusDisplay =>
        string.Equals(Status, PrintJobLifecycle.DbNeedsAttention, StringComparison.OrdinalIgnoreCase)
            ? "Needs attention"
            : "Failed";

    public string TitleDisplay => $"#{OrderDisplay} · {StaffCategory}";

    public string MetaDisplay => $"{PrinterName} · {TimeDisplay}";
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

public sealed class PrintQueueManagementSnapshot
{
    public int PendingJobs { get; init; }
    public int FailedJobs { get; init; }
    public int NeedsAttentionJobs { get; init; }
    public int PrintingJobs { get; init; }
    public int PreviousDayJobs { get; init; }
    public DateTime? OldestWaitingJob { get; init; }
    public DateTime? LastCancellationAt { get; init; }
    public int LastCancellationCount { get; init; }
    public int WaitingJobs => PendingJobs + FailedJobs + NeedsAttentionJobs;
}

public sealed class PrintQueueCancellationResult
{
    public int CancelledJobs { get; init; }
    public DateTime Cutoff { get; init; }
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
    private readonly BackgroundSyncManager? _backgroundSyncManager;
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
        DatabaseService databaseService,
        BackgroundSyncManager? backgroundSyncManager = null)
    {
        _dbService = dbService;
        _printerService = printerService;
        _databaseService = databaseService;
        _backgroundSyncManager = backgroundSyncManager;
        Debug.WriteLine(" NetworkPrintQueueService initialized");
    }

    /// <summary>
    /// Ensure the print_queue table exists
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
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
                    max_retries INT DEFAULT 5,
                    error_message TEXT,
                    created_by_terminal_name VARCHAR(120) NULL,
                    claimed_by_terminal_name VARCHAR(120) NULL,
                    claimed_at TIMESTAMP NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    started_at TIMESTAMP NULL,
                    completed_at TIMESTAMP NULL,
                    printed_at TIMESTAMP NULL,
                    last_attempt TIMESTAMP NULL,
                    INDEX idx_status (status),
                    INDEX idx_printer (printer_id),
                    INDEX idx_created (created_at)
                )";
            
            await command.ExecuteNonQueryAsync();
            await MigrateQueueTableAsync(connection);
            Debug.WriteLine(" network_print_queue table ready");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error creating print_queue table: {ex.Message}");
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
                Debug.WriteLine(" NetworkPrintQueueService already running");
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
            
            Debug.WriteLine($" NetworkPrintQueueService STARTED - processing every {PROCESS_INTERVAL_MS / 1000}s");
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
            
            Debug.WriteLine(" NetworkPrintQueueService STOPPED");
        }
    }

    /// <summary>
    /// Add a print job to the queue. Returns job id when <see cref="PrintJobLifecycle.Queued"/> —
    /// not physical <see cref="PrintJobLifecycle.Printed"/> (that happens after SendToPrinter succeeds).
    /// </summary>
    public async Task<int> EnqueueAsync(int printerId, byte[] printData, string jobType = "receipt", string? orderId = null)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                INSERT INTO network_print_queue (printer_id, job_type, print_data, order_id, status, created_by_terminal_name)
                VALUES (@printerId, @jobType, @printData, @orderId, 'pending', @createdByTerminalName);
                SELECT LAST_INSERT_ID();";
            
            command.Parameters.AddWithValue("@printerId", printerId);
            command.Parameters.AddWithValue("@jobType", jobType);
            command.Parameters.AddWithValue("@printData", printData);
            command.Parameters.AddWithValue("@orderId", orderId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@createdByTerminalName", GetCurrentTerminalName());
            
            var result = await command.ExecuteScalarAsync();
            var jobId = Convert.ToInt32(result);
            
            Debug.WriteLine($" Print job #{jobId} enqueued for printer #{printerId}");
            _backgroundSyncManager?.RequestRunSoon("network-print-queue");
            return jobId;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error enqueueing print job: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Process pending jobs in the queue
    /// </summary>
    public async Task ProcessQueueAsync()
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

            Debug.WriteLine($" Processing {jobs.Count} pending print job(s)...");

            foreach (var job in jobs)
            {
                await ProcessJobAsync(job);
            }

            LastProcessed = DateTime.Now;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Queue processing error: {ex.Message}");
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
                       pq.printed_at, pq.last_attempt, p.name AS printer_name
                FROM network_print_queue pq
                JOIN network_printers p ON pq.printer_id = p.id
                WHERE pq.status IN ('pending', 'failed')
                  AND pq.retry_count < COALESCE(pq.max_retries, @maxRetries)
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
            Debug.WriteLine($" Error getting pending jobs: {ex.Message}");
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
            Debug.WriteLine($" Processing job #{job.Id} for '{job.PrinterName}'...");
            
            if (!await TryClaimJobAsync(job))
            {
                Debug.WriteLine($"Info: Job #{job.Id} already claimed by another terminal");
                return;
            }
            
            var printer = await _dbService.GetPrinterByIdAsync(job.PrinterId);
            if (printer == null)
            {
                PrintReliabilitySupportLog.Write(
                    job.Id, job.PrinterName, job.PrinterId, job.RetryCount + 1,
                    "failed_permanent", "Printer not found", job.OrderId, job.JobType);
                await FailJobAsync(job, "Printer not found");
                return;
            }

            // Phase 5: probe paper/ready when ESC/POS status is supported.
            string? statusDetail = null;
            if (NetworkPrinterService.SupportsEscPosRealtimeStatus(printer))
            {
                var status = await _printerService.GetPrinterStatusAsync(printer);
                statusDetail = status.StatusProbeSucceeded
                    ? (status.PaperStatusKnown
                        ? $"probe=ok paper={(status.HasPaper ? "yes" : "OUT")} cover={(status.CoverOpen ? "open" : "ok")} err={(status.HasError ? "yes" : "no")}"
                        : "probe=ok paper=unknown")
                    : "probe=none (honest target)";

                if (!status.IsOnline)
                {
                    PrintReliabilitySupportLog.Write(
                        job.Id, printer.Name, printer.Id, job.RetryCount + 1,
                        "retry", status.NotReadyReason ?? "Printer offline", job.OrderId, job.JobType, statusDetail);
                    await RetryJobAsync(job, status.NotReadyReason ?? "Printer offline");
                    return;
                }

                if (!status.IsReadyForPrint)
                {
                    var reason = status.NotReadyReason ?? "Printer not ready";
                    PrintReliabilitySupportLog.Write(
                        job.Id, printer.Name, printer.Id, job.RetryCount + 1,
                        "retry", reason, job.OrderId, job.JobType, statusDetail);
                    await RetryJobAsync(job, reason);
                    return;
                }
            }
            else if (!printer.IsOnline)
            {
                PrintReliabilitySupportLog.Write(
                    job.Id, printer.Name, printer.Id, job.RetryCount + 1,
                    "retry", "Printer is offline", job.OrderId, job.JobType, "label/no-DLE");
                await RetryJobAsync(job, "Printer is offline");
                return;
            }
            else
            {
                statusDetail = "probe=skipped (label/honest)";
            }

            var success = await _printerService.SendToPrinterAsync(printer, job.PrintData);
            
            if (success)
            {
                PrintReliabilitySupportLog.Write(
                    job.Id, printer.Name, printer.Id, job.RetryCount + 1,
                    statusDetail?.Contains("probe=ok", StringComparison.Ordinal) == true
                        ? "printed_status_ok"
                        : "printed_best_effort",
                    null, job.OrderId, job.JobType, statusDetail);

                await CompleteJobAsync(job.Id);
                JobsProcessedToday++;
                
                Debug.WriteLine($" Job #{job.Id} printed successfully");
                
                JobCompleted?.Invoke(this, new PrintJobCompletedEventArgs
                {
                    JobId = job.Id,
                    PrinterName = job.PrinterName,
                    OrderId = job.OrderId,
                    JobType = job.JobType
                });
            }
            else
            {
                PrintReliabilitySupportLog.Write(
                    job.Id, printer.Name, printer.Id, job.RetryCount + 1,
                    "retry", "Send failed", job.OrderId, job.JobType, statusDetail);
                await RetryJobAsync(job, "Send failed");
            }
        }
        catch (Exception ex)
        {
            PrintReliabilitySupportLog.Write(
                job.Id, job.PrinterName, job.PrinterId, job.RetryCount + 1,
                "retry", ex.Message, job.OrderId, job.JobType);
            Debug.WriteLine($" Job #{job.Id} error: {ex.Message}");
            await RetryJobAsync(job, ex.Message);
        }
    }

    private async Task<bool> TryClaimJobAsync(NetworkPrintJob job)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = 'printing',
                    started_at = COALESCE(started_at, NOW()),
                    last_attempt = NOW(),
                    claimed_by_terminal_name = @claimedByTerminalName,
                    claimed_at = NOW()
                WHERE id = @id
                  AND status IN ('pending', 'failed')
                  AND retry_count < COALESCE(max_retries, @maxRetries)";
            
            command.Parameters.AddWithValue("@id", job.Id);
            command.Parameters.AddWithValue("@claimedByTerminalName", GetCurrentTerminalName());
            command.Parameters.AddWithValue("@maxRetries", MAX_RETRIES);
            
            return await command.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error claiming job: {ex.Message}");
            return false;
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
                SET status = 'completed', completed_at = NOW(), printed_at = NOW(), last_attempt = NOW()
                WHERE id = @id";
            
            command.Parameters.AddWithValue("@id", jobId);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error completing job: {ex.Message}");
        }
    }

    /// <summary>
    /// Mark job for retry
    /// </summary>
    private async Task RetryJobAsync(NetworkPrintJob job, string errorMessage)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = CASE
                        WHEN retry_count + 1 >= COALESCE(max_retries, @maxRetries)
                            THEN @needsAttention
                        ELSE @pending
                    END,
                    retry_count = retry_count + 1,
                    error_message = @error,
                    last_attempt = NOW()
                WHERE id = @id";
            
            command.Parameters.AddWithValue("@id", job.Id);
            command.Parameters.AddWithValue("@error", errorMessage);
            command.Parameters.AddWithValue("@maxRetries", MAX_RETRIES);
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.DbNeedsAttention);
            command.Parameters.AddWithValue("@pending", PrintJobLifecycle.DbPending);
            
            await command.ExecuteNonQueryAsync();

            var finalFailure = await IsPermanentlyFailedAsync(connection, job.Id);
            
            if (finalFailure)
            {
                JobsFailedToday++;
                Debug.WriteLine($" Job #{job.Id} permanently failed: {errorMessage}");
                PrintReliabilitySupportLog.Write(
                    job.Id, job.PrinterName, job.PrinterId, job.RetryCount + 1,
                    "needs_attention", errorMessage, job.OrderId, job.JobType);

                JobFailed?.Invoke(this, new PrintJobFailedEventArgs
                {
                    JobId = job.Id,
                    PrinterName = job.PrinterName,
                    OrderId = job.OrderId,
                    JobType = job.JobType,
                    ErrorMessage = errorMessage
                });
            }
            else
            {
                Debug.WriteLine($" Job #{job.Id} will retry: {errorMessage}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error retrying job: {ex.Message}");
        }
    }

    /// <summary>
    /// Mark job as permanently failed
    /// </summary>
    private async Task FailJobAsync(NetworkPrintJob job, string errorMessage)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = @needsAttention, 
                    retry_count = @maxRetries,
                    error_message = @error,
                    last_attempt = NOW()
                WHERE id = @id";
            
            command.Parameters.AddWithValue("@id", job.Id);
            command.Parameters.AddWithValue("@maxRetries", MAX_RETRIES);
            command.Parameters.AddWithValue("@error", errorMessage);
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.DbNeedsAttention);
            
            await command.ExecuteNonQueryAsync();
            
            JobsFailedToday++;
            
            Debug.WriteLine($" Job #{job.Id} permanently failed: {errorMessage}");
            
            JobFailed?.Invoke(this, new PrintJobFailedEventArgs
            {
                JobId = job.Id,
                PrinterName = job.PrinterName,
                OrderId = job.OrderId,
                JobType = job.JobType,
                ErrorMessage = errorMessage
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error failing job: {ex.Message}");
        }
    }

    private static async Task<bool> IsPermanentlyFailedAsync(MySqlConnection connection, int jobId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT status IN (@failed, @needsAttention) AND retry_count >= COALESCE(max_retries, @maxRetries)
            FROM network_print_queue
            WHERE id = @id";
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@maxRetries", MAX_RETRIES);
        command.Parameters.AddWithValue("@failed", "failed");
        command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.DbNeedsAttention);

        var result = await command.ExecuteScalarAsync();
        return result != null && Convert.ToBoolean(result);
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
                    SUM(CASE WHEN status IN ('failed', 'needs_attention') AND DATE(COALESCE(last_attempt, created_at)) = CURDATE() THEN 1 ELSE 0 END) as failed_today
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
            Debug.WriteLine($" Error getting queue stats: {ex.Message}");
        }
        
        return stats;
    }

    public async Task<PrintQueueManagementSnapshot> GetManagementSnapshotAsync(int? printerId = null)
    {
        await EnsureTableExistsAsync();

        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END) AS pending_count,
                SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END) AS failed_count,
                SUM(CASE WHEN status = 'needs_attention' THEN 1 ELSE 0 END) AS needs_attention_count,
                SUM(CASE WHEN status = 'printing' THEN 1 ELSE 0 END) AS printing_count,
                SUM(CASE WHEN status IN ('pending', 'failed', 'needs_attention') AND created_at < CURDATE() THEN 1 ELSE 0 END) AS previous_day_count,
                MIN(CASE WHEN status IN ('pending', 'failed', 'needs_attention', 'printing') THEN created_at END) AS oldest_waiting
            FROM network_print_queue
            WHERE (@printerId IS NULL OR printer_id = @printerId);

            SELECT cancelled_at, job_count
            FROM print_queue_cancellation_audit
            WHERE (@printerId IS NULL OR printer_id = @printerId OR printer_id IS NULL)
            ORDER BY cancelled_at DESC
            LIMIT 1;";
        command.Parameters.AddWithValue("@printerId", printerId ?? (object)DBNull.Value);

        using var reader = await command.ExecuteReaderAsync();
        var pending = 0;
        var failed = 0;
        var needsAttention = 0;
        var printing = 0;
        var previousDay = 0;
        DateTime? oldest = null;
        DateTime? lastCancellationAt = null;
        var lastCancellationCount = 0;

        if (await reader.ReadAsync())
        {
            pending = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
            failed = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            needsAttention = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
            printing = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
            previousDay = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));
            oldest = reader.IsDBNull(5) ? null : reader.GetDateTime(5);
        }

        if (await reader.NextResultAsync() && await reader.ReadAsync())
        {
            lastCancellationAt = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
            lastCancellationCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        }

        return new PrintQueueManagementSnapshot
        {
            PendingJobs = pending,
            FailedJobs = failed,
            NeedsAttentionJobs = needsAttention,
            PrintingJobs = printing,
            PreviousDayJobs = previousDay,
            OldestWaitingJob = oldest,
            LastCancellationAt = lastCancellationAt,
            LastCancellationCount = lastCancellationCount
        };
    }

    /// <summary>
    /// Phase 1 visibility: jobs that are not physically printed (queued / sending / failed / needs attention).
    /// </summary>
    public async Task<IReadOnlyList<NetworkPrintJob>> GetNotPrintedVisibleJobsAsync(int limit = 50)
    {
        await EnsureTableExistsAsync();
        var jobs = new List<NetworkPrintJob>();
        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT pq.id, pq.printer_id, pq.job_type, pq.print_data, pq.order_id,
                   pq.status, pq.retry_count, pq.error_message, pq.created_at,
                   pq.printed_at, pq.last_attempt, p.name AS printer_name
            FROM network_print_queue pq
            JOIN network_printers p ON pq.printer_id = p.id
            WHERE pq.status IN ('pending', 'printing', 'failed', 'needs_attention')
            ORDER BY
                CASE pq.status
                    WHEN 'needs_attention' THEN 0
                    WHEN 'failed' THEN 1
                    WHEN 'printing' THEN 2
                    ELSE 3
                END,
                pq.created_at ASC
            LIMIT @limit";
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 200));
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

        return jobs;
    }

    public async Task<int> CountNeedsAttentionAsync()
    {
        await EnsureTableExistsAsync();
        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM network_print_queue
            WHERE status = @needsAttention";
        command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.DbNeedsAttention);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    }

    /// <summary>
    /// Phase 3 staff list: failed + needs_attention only (not pending backoff).
    /// </summary>
    public async Task<IReadOnlyList<NetworkPrintJob>> GetStaffAttentionJobsAsync(int? printerId = null, int limit = 80)
    {
        await EnsureTableExistsAsync();
        var jobs = new List<NetworkPrintJob>();
        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT pq.id, pq.printer_id, pq.job_type, pq.print_data, pq.order_id,
                   pq.status, pq.retry_count, pq.error_message, pq.created_at,
                   pq.printed_at, pq.last_attempt, p.name AS printer_name
            FROM network_print_queue pq
            JOIN network_printers p ON pq.printer_id = p.id
            WHERE pq.status IN ('failed', 'needs_attention')
              AND (@printerId IS NULL OR pq.printer_id = @printerId)
            ORDER BY
                CASE pq.status WHEN 'needs_attention' THEN 0 ELSE 1 END,
                COALESCE(pq.last_attempt, pq.created_at) DESC
            LIMIT @limit";
        command.Parameters.AddWithValue("@printerId", printerId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 200));
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

        return jobs;
    }

    /// <summary>Re-queue one failed / needs_attention job (same ticket, no second copy).</summary>
    public async Task<bool> RetryJobByIdAsync(int jobId)
    {
        if (jobId <= 0)
        {
            return false;
        }

        try
        {
            await EnsureTableExistsAsync();
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE network_print_queue
                SET status = @pending,
                    retry_count = 0,
                    error_message = NULL,
                    last_attempt = NULL,
                    claimed_by_terminal_name = NULL,
                    claimed_at = NULL
                WHERE id = @id
                  AND status IN ('failed', @needsAttention)";
            command.Parameters.AddWithValue("@id", jobId);
            command.Parameters.AddWithValue("@pending", PrintJobLifecycle.DbPending);
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.DbNeedsAttention);
            var affected = await command.ExecuteNonQueryAsync();
            if (affected > 0)
            {
                _backgroundSyncManager?.RequestRunSoon("network-print-queue");
            }

            return affected > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Print reliability] RetryJobById failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enqueue a fresh copy of the ticket (may duplicate on paper), then dismiss the original.
    /// </summary>
    public async Task<(bool Ok, int? NewJobId, string Message)> ReprintJobByIdAsync(
        int jobId,
        int staffUserId,
        string staffName)
    {
        if (jobId <= 0)
        {
            return (false, null, "Invalid job.");
        }

        try
        {
            await EnsureTableExistsAsync();
            using var connection = await _databaseService.GetConnectionAsync();
            NetworkPrintJob? source = null;
            using (var select = connection.CreateCommand())
            {
                select.CommandText = @"
                    SELECT id, printer_id, job_type, print_data, order_id, status
                    FROM network_print_queue
                    WHERE id = @id
                      AND status IN ('failed', 'needs_attention')
                    LIMIT 1";
                select.Parameters.AddWithValue("@id", jobId);
                using var reader = await select.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    source = new NetworkPrintJob
                    {
                        Id = reader.GetInt32("id"),
                        PrinterId = reader.GetInt32("printer_id"),
                        JobType = reader.GetString("job_type"),
                        PrintData = (byte[])reader["print_data"],
                        OrderId = reader.IsDBNull(reader.GetOrdinal("order_id")) ? null : reader.GetString("order_id"),
                        Status = reader.GetString("status")
                    };
                }
            }

            if (source == null || source.PrintData.Length == 0)
            {
                return (false, null, "Job not found or has no print data.");
            }

            var newId = await EnqueueAsync(source.PrinterId, source.PrintData, source.JobType, source.OrderId);
            var dismissed = await DismissJobByIdAsync(
                jobId,
                staffUserId,
                staffName,
                $"Staff reprint → new job #{newId} (may duplicate)");
            if (!dismissed)
            {
                return (true, newId, $"Reprinted as job #{newId}, but the original stay listed — dismiss it if needed.");
            }

            return (true, newId, $"Reprinted as job #{newId}. Original dismissed.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Print reliability] ReprintJobById failed: {ex.Message}");
            return (false, null, ex.Message);
        }
    }

    /// <summary>Dismiss (cancel) one failed / needs_attention job — reason required.</summary>
    public async Task<bool> DismissJobByIdAsync(
        int jobId,
        int staffUserId,
        string staffName,
        string reason)
    {
        if (jobId <= 0 || string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        try
        {
            await EnsureTableExistsAsync();
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE network_print_queue
                SET status = 'cancelled',
                    cancelled_at = NOW(),
                    cancelled_by_user_id = @userId,
                    cancelled_by_name = @userName,
                    cancellation_reason = @reason,
                    error_message = @reason,
                    claimed_by_terminal_name = NULL,
                    claimed_at = NULL
                WHERE id = @id
                  AND status IN ('failed', 'needs_attention', 'pending')";
            command.Parameters.AddWithValue("@id", jobId);
            command.Parameters.AddWithValue("@userId", staffUserId);
            command.Parameters.AddWithValue("@userName", string.IsNullOrWhiteSpace(staffName) ? "Staff" : staffName);
            command.Parameters.AddWithValue("@reason", reason.Trim());
            return await command.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Print reliability] DismissJobById failed: {ex.Message}");
            return false;
        }
    }

    public async Task<PrintQueueCancellationResult> CancelWaitingJobsAsync(
        DateTime cutoff,
        int? printerId,
        int cancelledByUserId,
        string cancelledByName,
        string reason,
        string scope)
    {
        await EnsureTableExistsAsync();

        var cancelledOnlineJobs = new List<(int JobId, string OrderId, string JobType, string PrinterName)>();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = @"
                SELECT q.id, q.order_id, q.job_type, p.name
                FROM network_print_queue q
                JOIN network_printers p ON p.id = q.printer_id
                WHERE q.status IN ('pending', 'failed', 'needs_attention')
                  AND q.created_at <= @cutoff
                  AND (@printerId IS NULL OR q.printer_id = @printerId)
                FOR UPDATE";
            selectCommand.Parameters.AddWithValue("@cutoff", cutoff);
            selectCommand.Parameters.AddWithValue("@printerId", printerId ?? (object)DBNull.Value);

            await using var reader = await selectCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var orderId = reader.IsDBNull(1) ? null : reader.GetString(1);
                var jobType = reader.GetString(2);
                if (!string.IsNullOrWhiteSpace(orderId) && IsOnlineOrderJob(jobType))
                {
                    cancelledOnlineJobs.Add((reader.GetInt32(0), orderId, jobType, reader.GetString(3)));
                }
            }
        }

        int cancelledCount;
        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = @"
                UPDATE network_print_queue
                SET status = 'cancelled',
                    cancelled_at = NOW(),
                    cancelled_by_user_id = @userId,
                    cancelled_by_name = @userName,
                    cancellation_reason = @reason,
                    error_message = @reason,
                    claimed_by_terminal_name = NULL,
                    claimed_at = NULL
                WHERE status IN ('pending', 'failed', 'needs_attention')
                  AND created_at <= @cutoff
                  AND (@printerId IS NULL OR printer_id = @printerId)";
            updateCommand.Parameters.AddWithValue("@userId", cancelledByUserId);
            updateCommand.Parameters.AddWithValue("@userName", cancelledByName);
            updateCommand.Parameters.AddWithValue("@reason", reason);
            updateCommand.Parameters.AddWithValue("@cutoff", cutoff);
            updateCommand.Parameters.AddWithValue("@printerId", printerId ?? (object)DBNull.Value);
            cancelledCount = await updateCommand.ExecuteNonQueryAsync();
        }

        await using (var auditCommand = connection.CreateCommand())
        {
            auditCommand.Transaction = transaction;
            auditCommand.CommandText = @"
                INSERT INTO print_queue_cancellation_audit
                    (cutoff_at, scope, printer_id, job_count, cancelled_by_user_id, cancelled_by_name, reason)
                VALUES
                    (@cutoff, @scope, @printerId, @jobCount, @userId, @userName, @reason)";
            auditCommand.Parameters.AddWithValue("@cutoff", cutoff);
            auditCommand.Parameters.AddWithValue("@scope", scope);
            auditCommand.Parameters.AddWithValue("@printerId", printerId ?? (object)DBNull.Value);
            auditCommand.Parameters.AddWithValue("@jobCount", cancelledCount);
            auditCommand.Parameters.AddWithValue("@userId", cancelledByUserId);
            auditCommand.Parameters.AddWithValue("@userName", cancelledByName);
            auditCommand.Parameters.AddWithValue("@reason", reason);
            await auditCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        foreach (var job in cancelledOnlineJobs
                     .GroupBy(item => item.OrderId, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            JobFailed?.Invoke(this, new PrintJobFailedEventArgs
            {
                JobId = job.JobId,
                PrinterName = job.PrinterName,
                OrderId = job.OrderId,
                JobType = job.JobType,
                ErrorMessage = reason
            });
        }

        Debug.WriteLine($" Cancelled {cancelledCount} queued print job(s) through {cutoff:O}");
        return new PrintQueueCancellationResult
        {
            CancelledJobs = cancelledCount,
            Cutoff = cutoff
        };
    }

    /// <summary>
    /// Phase 2: printer came back (paper change / online) — clear backoff and re-queue
    /// failed + needs_attention + pending jobs for <paramref name="printerId"/> only.
    /// </summary>
    public async Task<int> FlushWaitingJobsForPrinterAsync(int printerId)
    {
        if (printerId <= 0)
        {
            return 0;
        }

        try
        {
            await EnsureTableExistsAsync();
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE network_print_queue
                SET status = @pending,
                    last_attempt = NULL,
                    retry_count = CASE
                        WHEN status = @needsAttention THEN 0
                        ELSE retry_count
                    END,
                    error_message = CASE
                        WHEN status = @needsAttention THEN NULL
                        ELSE error_message
                    END
                WHERE printer_id = @printerId
                  AND status IN (@pending, @failed, @needsAttention)";
            command.Parameters.AddWithValue("@printerId", printerId);
            command.Parameters.AddWithValue("@pending", PrintJobLifecycle.DbPending);
            command.Parameters.AddWithValue("@failed", "failed");
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.DbNeedsAttention);

            var affected = await command.ExecuteNonQueryAsync();
            if (affected > 0)
            {
                Debug.WriteLine($"[Print reliability] Printer #{printerId} ready — flushed {affected} waiting job(s)");
                _backgroundSyncManager?.RequestRunSoon("network-print-queue");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessQueueAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Print reliability] Flush process error: {ex.Message}");
                    }
                });
            }

            return affected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Print reliability] FlushWaitingJobsForPrinter failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Retry all failed jobs manually
    /// </summary>
    public async Task<int> RetryAllFailedJobsAsync(int? printerId = null)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                UPDATE network_print_queue 
                SET status = @pending, retry_count = 0, error_message = NULL, last_attempt = NULL
                WHERE status IN (@failed, @needsAttention)
                  AND (@printerId IS NULL OR printer_id = @printerId)";
            
            command.Parameters.AddWithValue("@printerId", printerId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@pending", PrintJobLifecycle.DbPending);
            command.Parameters.AddWithValue("@failed", "failed");
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.DbNeedsAttention);
            
            var affected = await command.ExecuteNonQueryAsync();
            if (affected > 0)
            {
                _backgroundSyncManager?.RequestRunSoon("network-print-queue");
            }
            
            Debug.WriteLine($" Reset {affected} failed jobs for retry");
            return affected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error retrying failed jobs: {ex.Message}");
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
            
            Debug.WriteLine($" Cleared {affected} old completed jobs");
            return affected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error clearing old jobs: {ex.Message}");
            return 0;
        }
    }

    public bool IsRunning => _isRunning;

    private static bool IsOnlineOrderJob(string jobType) =>
        string.Equals(jobType, "online_receipt", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(jobType, "takeaway_ticket", StringComparison.OrdinalIgnoreCase);

    private static async Task MigrateQueueTableAsync(MySqlConnection connection)
    {
        if (!await ColumnExistsAsync(connection, "network_print_queue", "max_retries"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN max_retries INT DEFAULT 5 AFTER retry_count");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "started_at"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN started_at TIMESTAMP NULL AFTER created_at");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "completed_at"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN completed_at TIMESTAMP NULL AFTER started_at");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "printed_at"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN printed_at TIMESTAMP NULL AFTER completed_at");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "last_attempt"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN last_attempt TIMESTAMP NULL AFTER printed_at");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "created_by_terminal_name"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN created_by_terminal_name VARCHAR(120) NULL AFTER error_message");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "claimed_by_terminal_name"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN claimed_by_terminal_name VARCHAR(120) NULL AFTER created_by_terminal_name");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "claimed_at"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN claimed_at TIMESTAMP NULL AFTER claimed_by_terminal_name");
        }

        // Phase 1–2: allow needs_attention (ENUM may reject it on older DBs).
        try
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                MODIFY COLUMN status VARCHAR(32) NOT NULL DEFAULT 'pending'");
        }
        catch
        {
            // Column already VARCHAR or alter not permitted under migration-managed mode.
        }
    }

    private static async Task<bool> ColumnExistsAsync(MySqlConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName
              AND COLUMN_NAME = @columnName";
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task ExecuteNonQueryAsync(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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
    public string JobType { get; set; } = "";
}

/// <summary>
/// Event args for failed print jobs
/// </summary>
public class PrintJobFailedEventArgs : EventArgs
{
    public int JobId { get; set; }
    public string PrinterName { get; set; } = "";
    public string? OrderId { get; set; }
    public string JobType { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}
