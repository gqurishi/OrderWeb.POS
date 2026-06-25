using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Background service that automatically runs database cleanup every day at 2am
/// Deletes cached OrderWeb.net orders to keep the database lightweight
/// </summary>
public class CleanupSchedulerService
{
    private readonly DatabaseCleanupService _cleanupService;
    private readonly DatabaseService _databaseService;
    private Timer? _cleanupTimer;
    private const int CHECK_INTERVAL_HOURS = 1; // Check every hour
    private const int CLEANUP_HOUR = 2; // 2am local time

    public bool IsRunning { get; private set; }

    public CleanupSchedulerService(DatabaseCleanupService cleanupService, DatabaseService databaseService)
    {
        _cleanupService = cleanupService;
        _databaseService = databaseService;
    }

    /// <summary>
    /// Start the automatic cleanup scheduler
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;

        Debug.WriteLine(" Starting DatabaseCleanup Scheduler...");

        // Wait 5 minutes after app start before first check
        var initialDelay = TimeSpan.FromMinutes(5);
        var checkInterval = TimeSpan.FromHours(CHECK_INTERVAL_HOURS);

        _cleanupTimer = new Timer(
            async _ => await CheckAndRunCleanupAsync(),
            null,
            initialDelay,
            checkInterval
        );

        IsRunning = true;
        Debug.WriteLine($" Cleanup Scheduler started (checks every {CHECK_INTERVAL_HOURS}h, cleans daily at {CLEANUP_HOUR}:00)");
    }

    /// <summary>
    /// Stop the cleanup scheduler
    /// </summary>
    public void Stop()
    {
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
        IsRunning = false;
        Debug.WriteLine(" Cleanup Scheduler stopped");
    }

    /// <summary>
    /// Check if cleanup is needed and run it
    /// </summary>
    private async Task CheckAndRunCleanupAsync()
    {
        try
        {
            // Get last cleanup timestamp
            var lastCleanupStr = Preferences.Get("LastCleanupDate", "");
            DateTime? lastCleanup = null;

            if (!string.IsNullOrEmpty(lastCleanupStr))
            {
                lastCleanup = DateTime.Parse(lastCleanupStr);
            }

            var now = DateTime.Now;
            var scheduledCleanupTime = now.Date.AddHours(CLEANUP_HOUR);
            var alreadyCleanedToday = lastCleanup.HasValue && lastCleanup.Value.Date == now.Date;
            var shouldRunCleanup = now >= scheduledCleanupTime && !alreadyCleanedToday;

            if (shouldRunCleanup)
            {
                Debug.WriteLine($"⏰ Scheduled cleanup time reached, running now...");
                Debug.WriteLine($"   Last cleanup: {lastCleanup?.ToString("yyyy-MM-dd HH:mm") ?? "Never"}");

                // Run cleanup in background
                await Task.Run(async () =>
                {
                    var result = await _cleanupService.RunCleanupAsync();
                    
                    if (result.Success)
                    {
                        Debug.WriteLine($" Automatic cleanup completed: {result.OrdersDeleted} web orders deleted");
                    }
                    else
                    {
                        Debug.WriteLine($" Automatic cleanup failed: {result.ErrorMessage}");
                    }
                });
            }
            else
            {
                if (lastCleanup.HasValue)
                {
                    var nextRun = now < scheduledCleanupTime ? scheduledCleanupTime : scheduledCleanupTime.AddDays(1);
                    var hoursUntilNext = (nextRun - now).TotalHours;
                    Debug.WriteLine($"Info: Cleanup check: Next cleanup in {hoursUntilNext:F1} hours at {nextRun:yyyy-MM-dd HH:mm}");
                }
                else
                {
                    Debug.WriteLine($"Info: Cleanup check: first scheduled cleanup will run at {scheduledCleanupTime:yyyy-MM-dd HH:mm}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error in cleanup scheduler: {ex.Message}");
        }
    }

    /// <summary>
    /// Manually trigger cleanup (for testing or user action)
    /// </summary>
    public async Task<CleanupResult> RunManualCleanupAsync()
    {
        Debug.WriteLine(" Manual cleanup triggered by user");
        return await _cleanupService.RunCleanupAsync();
    }

    /// <summary>
    /// Get cleanup statistics
    /// </summary>
    public async Task<CleanupStats> GetStatsAsync()
    {
        return await _cleanupService.GetCleanupStatsAsync();
    }

    /// <summary>
    /// Initialize database indexes on first run
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        try
        {
            // Check if indexes were already created
            var indexesCreated = Preferences.Get("DatabaseIndexesCreated", false);

            if (!indexesCreated)
            {
                Debug.WriteLine(" First run: Creating database indexes...");
                await _cleanupService.CreateIndexesAsync();
                Preferences.Set("DatabaseIndexesCreated", true);
                Debug.WriteLine(" Database indexes created for faster queries");
            }
            else
            {
                Debug.WriteLine("Info: Database indexes already exist");
            }
            
            // Ensure pending_acks table exists (needed for OrderWeb.net features)
            await EnsureOrderWebTablesExistAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error initializing database: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Ensure OrderWeb.net related tables exist
    /// </summary>
    private async Task EnsureOrderWebTablesExistAsync()
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            
            // Create pending_acks table if not exists
            using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = @"
                CREATE TABLE IF NOT EXISTS pending_acks (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    order_id VARCHAR(255) NOT NULL,
                    status VARCHAR(20) NOT NULL,
                    reason TEXT NULL,
                    printed_at DATETIME NULL,
                    device_id VARCHAR(50) NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    retry_count INT DEFAULT 0,
                    last_retry_at DATETIME NULL,
                    INDEX idx_pending_acks_created (created_at),
                    INDEX idx_pending_acks_retry (retry_count, created_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
            await cmd1.ExecuteNonQueryAsync();
            
            // Create heartbeat_log table if not exists
            using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = @"
                CREATE TABLE IF NOT EXISTS heartbeat_log (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    device_id VARCHAR(100) NOT NULL,
                    status VARCHAR(20) NOT NULL,
                    pending_acks_count INT DEFAULT 0,
                    pending_orders_count INT DEFAULT 0,
                    last_print_at DATETIME NULL,
                    sent_at DATETIME NOT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_heartbeat_device (device_id, sent_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
            await cmd2.ExecuteNonQueryAsync();
            
            // Create order_received_log table if not exists
            using var cmd3 = connection.CreateCommand();
            cmd3.CommandText = @"
                CREATE TABLE IF NOT EXISTS order_received_log (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    order_id VARCHAR(255) NOT NULL,
                    received_at DATETIME NOT NULL,
                    device_id VARCHAR(100) NULL,
                    status VARCHAR(20) DEFAULT 'received',
                    sent_to_cloud BOOLEAN DEFAULT FALSE,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_order_received (order_id, received_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
            await cmd3.ExecuteNonQueryAsync();
            
            Debug.WriteLine(" OrderWeb.net tables verified/created");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error creating OrderWeb tables: {ex.Message}");
        }
    }
}
