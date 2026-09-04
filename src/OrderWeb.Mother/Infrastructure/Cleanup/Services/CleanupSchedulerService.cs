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
    private readonly OrderService _orderService;
    private Timer? _cleanupTimer;
    private const int CHECK_INTERVAL_HOURS = 1; // Check every hour
    private const int CLEANUP_HOUR = 2; // 2am local time

    public bool IsRunning { get; private set; }

    public CleanupSchedulerService(
        DatabaseCleanupService cleanupService,
        DatabaseService databaseService,
        OrderService orderService)
    {
        _cleanupService = cleanupService;
        _databaseService = databaseService;
        _orderService = orderService;
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
    public async Task CheckAndRunCleanupAsync()
    {
        try
        {
            // Repair only stale, provably empty legacy table rows. This runs on
            // every idle cleanup check rather than waiting for the nightly purge.
            await _orderService.QuarantineCorruptedEmptyTableOrdersAsync();

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
    /// Verifies production schema on startup. DDL is owned by OrderWeb.DatabaseSetup.exe.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        try
        {
            if (!TerminalConfigurationService.IsConfigured)
            {
                return;
            }

            var result = await _databaseService.EnsureProductionSchemaAsync();
            if (!result.Success)
            {
                Debug.WriteLine($"Production schema check: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Production schema check error: {ex.Message}");
        }
    }
}
