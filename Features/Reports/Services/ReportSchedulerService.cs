using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Background service that automatically generates daily, weekly, and monthly reports
/// Runs on schedule:
/// - Daily: Every day at 11:50 PM (for previous day report)
/// - Weekly: Every Sunday at 11:50 PM
/// - Monthly: 1st of month at 11:50 PM
/// Reports are stored in database for historical access (1+ years)
/// </summary>
public class ReportSchedulerService
{
    private readonly ReportGenerationService _reportService;
    private Timer? _reportTimer;
    private const int CHECK_INTERVAL_HOURS = 1; // Check every hour
    private const int REPORT_HOUR = 23; // 11 PM
    private const int REPORT_MINUTE = 50; // 50 minutes

    public bool IsRunning { get; private set; }

    public ReportSchedulerService(ReportGenerationService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// Start the automatic report generation scheduler
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;

        Debug.WriteLine("🚀 Starting Report Scheduler...");

        // Wait 30 seconds after app start before first check
        var initialDelay = TimeSpan.FromSeconds(30);
        var checkInterval = TimeSpan.FromHours(CHECK_INTERVAL_HOURS);

        _reportTimer = new Timer(
            async _ => await CheckAndGenerateReportsAsync(),
            null,
            initialDelay,
            checkInterval
        );

        IsRunning = true;
        Debug.WriteLine($"✅ Report Scheduler started (checks every {CHECK_INTERVAL_HOURS}h, generates daily at {REPORT_HOUR}:{REPORT_MINUTE})");
    }

    /// <summary>
    /// Stop the report scheduler
    /// </summary>
    public void Stop()
    {
        _reportTimer?.Dispose();
        _reportTimer = null;
        IsRunning = false;
        Debug.WriteLine("🛑 Report Scheduler stopped");
    }

    /// <summary>
    /// Check if it's time to generate reports and run them
    /// </summary>
    private async Task CheckAndGenerateReportsAsync()
    {
        try
        {
            var now = DateTime.Now;
            var scheduledReportTime = now.Date.AddHours(REPORT_HOUR).AddMinutes(REPORT_MINUTE);

            // Check if we've already run today
            var lastReportStr = Preferences.Get("LastReportGenerationDate", "");
            DateTime? lastReport = null;

            if (!string.IsNullOrEmpty(lastReportStr))
            {
                lastReport = DateTime.Parse(lastReportStr);
            }

            var alreadyRanToday = lastReport.HasValue && lastReport.Value.Date == now.Date;
            var shouldGenerateReports = now >= scheduledReportTime && !alreadyRanToday;

            if (!shouldGenerateReports)
            {
                return;
            }

            Debug.WriteLine($"⏱️ Report generation time reached ({REPORT_HOUR}:{REPORT_MINUTE}). Generating reports...");
            await GenerateAllReportsAsync(now);

            // Store last generation time
            Preferences.Set("LastReportGenerationDate", DateTime.Now.ToString("o"));
            Debug.WriteLine("✅ Report generation completed and stored in database");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Report generation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Generate all report types: daily, weekly, monthly
    /// </summary>
    private async Task GenerateAllReportsAsync(DateTime now)
    {
        try
        {
            // Generate DAILY report for YESTERDAY (so today's report has complete yesterday's data)
            var yesterday = now.Date.AddDays(-1);
            await _reportService.GenerateDailyReportAsync(yesterday);
            Debug.WriteLine($"✅ Daily report generated for {yesterday:yyyy-MM-dd}");

            // Generate WEEKLY report if today is Sunday (weekly reports are Mon-Sun)
            if (now.DayOfWeek == DayOfWeek.Sunday)
            {
                var weekStart = now.Date.AddDays(-(int)now.DayOfWeek + 1); // Get Monday
                await _reportService.GenerateWeeklyReportAsync(weekStart);
                Debug.WriteLine($"✅ Weekly report generated for week starting {weekStart:yyyy-MM-dd}");
            }

            // Generate MONTHLY report if today is 1st of month (for previous month)
            if (now.Day == 1)
            {
                var previousMonthDate = now.AddMonths(-1);
                await _reportService.GenerateMonthlyReportAsync(previousMonthDate.Year, previousMonthDate.Month);
                Debug.WriteLine($"✅ Monthly report generated for {previousMonthDate:yyyy-MM}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error during report generation: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// Manually trigger a report generation (for testing)
    /// </summary>
    public async Task GenerateDailyReportManuallyAsync(DateTime date)
    {
        try
        {
            Debug.WriteLine($"📋 Manually generating daily report for {date:yyyy-MM-dd}...");
            await _reportService.GenerateDailyReportAsync(date);
            Debug.WriteLine($"✅ Manual daily report generated successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Manual report generation failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Manually trigger a weekly report generation (for testing)
    /// </summary>
    public async Task GenerateWeeklyReportManuallyAsync(DateTime weekStart)
    {
        try
        {
            Debug.WriteLine($"📋 Manually generating weekly report for week starting {weekStart:yyyy-MM-dd}...");
            await _reportService.GenerateWeeklyReportAsync(weekStart);
            Debug.WriteLine($"✅ Manual weekly report generated successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Manual report generation failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Manually trigger a monthly report generation (for testing)
    /// </summary>
    public async Task GenerateMonthlyReportManuallyAsync(int year, int month)
    {
        try
        {
            Debug.WriteLine($"📋 Manually generating monthly report for {year:0000}-{month:00}...");
            await _reportService.GenerateMonthlyReportAsync(year, month);
            Debug.WriteLine($"✅ Manual monthly report generated successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Manual report generation failed: {ex.Message}");
            throw;
        }
    }
}
