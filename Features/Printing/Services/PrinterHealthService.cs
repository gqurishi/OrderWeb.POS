using POS_in_NET.Models;
using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Background service that monitors printer health status every 30 seconds.
/// Updates printer online/offline status in the database.
/// </summary>
public class PrinterHealthService : IDisposable
{
    private readonly NetworkPrinterDatabaseService _dbService;
    private readonly NetworkPrinterService _printerService;
    private Timer? _healthCheckTimer;
    private bool _isRunning = false;
    private readonly object _lock = new();
    
    // Configuration
    private const int HEALTH_CHECK_INTERVAL_MS = 30000; // 30 seconds
    private const int CONNECTION_TIMEOUT_MS = 3000; // 3 seconds for health check
    
    // Statistics
    public DateTime LastHealthCheck { get; private set; } = DateTime.MinValue;
    public int OnlinePrinters { get; private set; } = 0;
    public int OfflinePrinters { get; private set; } = 0;
    public int TotalPrinters { get; private set; } = 0;
    
    // Events
    public event EventHandler<PrinterStatusChangedEventArgs>? PrinterStatusChanged;
    public event EventHandler<HealthCheckCompletedEventArgs>? HealthCheckCompleted;

    public PrinterHealthService(
        NetworkPrinterDatabaseService dbService,
        NetworkPrinterService printerService)
    {
        _dbService = dbService;
        _printerService = printerService;
        Debug.WriteLine("🏥 PrinterHealthService initialized");
    }

    /// <summary>
    /// Start the background health monitoring
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                Debug.WriteLine("⚠️ PrinterHealthService already running");
                return;
            }

            _isRunning = true;
            
            // Run immediately, then every 30 seconds
            _healthCheckTimer = new Timer(
                async _ => await CheckAllPrintersAsync(),
                null,
                TimeSpan.Zero, // Start immediately
                TimeSpan.FromMilliseconds(HEALTH_CHECK_INTERVAL_MS)
            );
            
            Debug.WriteLine($"✅ PrinterHealthService STARTED - checking every {HEALTH_CHECK_INTERVAL_MS / 1000}s");
        }
    }

    /// <summary>
    /// Stop the background health monitoring
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _healthCheckTimer?.Dispose();
            _healthCheckTimer = null;
            _isRunning = false;
            
            Debug.WriteLine("🛑 PrinterHealthService STOPPED");
        }
    }

    /// <summary>
    /// Check health status of all printers
    /// </summary>
    public async Task CheckAllPrintersAsync()
    {
        try
        {
            Debug.WriteLine("🔍 Health check starting...");
            
            var printers = await _dbService.GetAllPrintersAsync();
            TotalPrinters = printers.Count;
            
            if (printers.Count == 0)
            {
                Debug.WriteLine("ℹ️ No printers configured");
                LastHealthCheck = DateTime.Now;
                return;
            }

            int online = 0;
            int offline = 0;
            var statusChanges = new List<(NetworkPrinter printer, bool wasOnline, bool isNowOnline)>();

            // Check each printer in parallel for speed
            var tasks = printers.Select(async printer =>
            {
                var wasOnline = printer.IsOnline;
                var isNowOnline = await CheckPrinterHealthAsync(printer);
                
                return (printer, wasOnline, isNowOnline);
            });

            var results = await Task.WhenAll(tasks);

            foreach (var (printer, wasOnline, isNowOnline) in results)
            {
                if (isNowOnline)
                    online++;
                else
                    offline++;

                // Update database if status changed
                if (wasOnline != isNowOnline)
                {
                    await _dbService.UpdatePrinterStatusAsync(printer.Id, isNowOnline);
                    statusChanges.Add((printer, wasOnline, isNowOnline));
                    
                    Debug.WriteLine($"📊 Printer '{printer.Name}' status: {(wasOnline ? "ONLINE" : "OFFLINE")} → {(isNowOnline ? "ONLINE" : "OFFLINE")}");
                    
                    // Raise event for status change
                    PrinterStatusChanged?.Invoke(this, new PrinterStatusChangedEventArgs
                    {
                        Printer = printer,
                        WasOnline = wasOnline,
                        IsNowOnline = isNowOnline
                    });
                }
            }

            OnlinePrinters = online;
            OfflinePrinters = offline;
            LastHealthCheck = DateTime.Now;
            
            Debug.WriteLine($"✅ Health check complete: {online} online, {offline} offline");
            
            // Raise completion event
            HealthCheckCompleted?.Invoke(this, new HealthCheckCompletedEventArgs
            {
                Timestamp = LastHealthCheck,
                TotalPrinters = TotalPrinters,
                OnlinePrinters = online,
                OfflinePrinters = offline,
                StatusChanges = statusChanges.Count
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Health check error: {ex.Message}");
        }
    }

    /// <summary>
    /// Check health of a single printer
    /// </summary>
    private async Task<bool> CheckPrinterHealthAsync(NetworkPrinter printer)
    {
        if (!printer.IsEnabled)
        {
            // Disabled printers are considered offline
            return false;
        }

        try
        {
            // Quick connection test
            var result = await _printerService.TestConnectionAsync(
                printer.IpAddress, 
                printer.Port
            );
            
            return result.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"⚠️ Health check failed for '{printer.Name}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Force an immediate health check (outside of schedule)
    /// </summary>
    public async Task ForceHealthCheckAsync()
    {
        Debug.WriteLine("🔄 Force health check requested");
        await CheckAllPrintersAsync();
    }

    /// <summary>
    /// Check a specific printer's health
    /// </summary>
    public async Task<bool> CheckSinglePrinterAsync(int printerId)
    {
        try
        {
            var printer = await _dbService.GetPrinterByIdAsync(printerId);
            if (printer == null)
            {
                return false;
            }

            var isOnline = await CheckPrinterHealthAsync(printer);
            
            if (printer.IsOnline != isOnline)
            {
                await _dbService.UpdatePrinterStatusAsync(printerId, isOnline);
            }
            
            return isOnline;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Single printer check error: {ex.Message}");
            return false;
        }
    }

    public bool IsRunning => _isRunning;

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Event args for printer status changes
/// </summary>
public class PrinterStatusChangedEventArgs : EventArgs
{
    public NetworkPrinter Printer { get; set; } = null!;
    public bool WasOnline { get; set; }
    public bool IsNowOnline { get; set; }
}

/// <summary>
/// Event args for health check completion
/// </summary>
public class HealthCheckCompletedEventArgs : EventArgs
{
    public DateTime Timestamp { get; set; }
    public int TotalPrinters { get; set; }
    public int OnlinePrinters { get; set; }
    public int OfflinePrinters { get; set; }
    public int StatusChanges { get; set; }
}
