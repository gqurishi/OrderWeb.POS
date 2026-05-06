using System.Timers;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public class BackgroundSyncService : IDisposable
{
    private readonly OnlineOrderApiService _apiService;
    private readonly OrderService _orderService;
    private System.Timers.Timer? _syncTimer;
    private System.Timers.Timer? _statusTimer;
    private bool _isInitialized = false;
    private bool _isSyncing = false;

    public event EventHandler<SyncEventArgs>? SyncStatusChanged;
    public event EventHandler<OrderEventArgs>? NewOrderReceived;
    public event EventHandler<string>? StatusTransitionProcessed;

    public BackgroundSyncService()
    {
        _apiService = new OnlineOrderApiService();
        _orderService = new OrderService();
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            var apiInitialized = await _apiService.InitializeAsync();
            if (apiInitialized)
            {
                StartSyncTimer();
                StartStatusTransitionTimer();
                _isInitialized = true;
                
                OnSyncStatusChanged(new SyncEventArgs { Status = SyncStatus.Ready, Message = "Background sync initialized successfully" });
                
                return true;
            }
            else
            {
                OnSyncStatusChanged(new SyncEventArgs { Status = SyncStatus.NotConfigured, Message = "API not configured" });
                return false;
            }
        }
        catch (Exception ex)
        {
            OnSyncStatusChanged(new SyncEventArgs { Status = SyncStatus.Error, Message = $"Initialization failed: {ex.Message}" });
            return false;
        }
    }

    private void StartSyncTimer()
    {
        // Sync every minute (60,000 ms)
        _syncTimer = new System.Timers.Timer(60000);
        _syncTimer.Elapsed += async (s, e) => await PerformSyncAsync();
        _syncTimer.AutoReset = true;
        _syncTimer.Start();
        
        // Perform initial sync immediately
        Task.Run(async () => await PerformSyncAsync());
    }

    private void StartStatusTransitionTimer()
    {
        // Check for status transitions every 30 seconds
        _statusTimer = new System.Timers.Timer(30000);
        _statusTimer.Elapsed += async (s, e) => await ProcessStatusTransitionsAsync();
        _statusTimer.AutoReset = true;
        _statusTimer.Start();
    }

    private async Task PerformSyncAsync()
    {
        if (_isSyncing) return; // Prevent overlapping syncs
        
        _isSyncing = true;
        
        try
        {
            OnSyncStatusChanged(new SyncEventArgs { Status = SyncStatus.Syncing, Message = "Fetching new orders..." });

            var result = await _apiService.FetchNewOrdersAsync();
            
            if (result.Success)
            {
                var newOrdersCount = 0;
                
                foreach (var order in result.Orders)
                {
                    var saveResult = await _orderService.SaveOrderAsync(order);
                    if (saveResult.Success)
                    {
                        newOrdersCount++;
                        OnNewOrderReceived(new OrderEventArgs { Order = order });
                        
                        // Log the new order
                        System.Diagnostics.Debug.WriteLine($"New order received: {order.OrderId} - {order.CustomerName}");
                    }
                }
                
                var message = newOrdersCount > 0 
                    ? $"Sync completed. {newOrdersCount} new orders received."
                    : "Sync completed. No new orders.";
                
                OnSyncStatusChanged(new SyncEventArgs 
                { 
                    Status = SyncStatus.Success, 
                    Message = message,
                    NewOrdersCount = newOrdersCount,
                    LastSyncTime = DateTime.Now
                });
            }
            else
            {
                OnSyncStatusChanged(new SyncEventArgs 
                { 
                    Status = SyncStatus.Error, 
                    Message = $"Sync failed: {result.Message}"
                });
            }
        }
        catch (Exception ex)
        {
            OnSyncStatusChanged(new SyncEventArgs 
            { 
                Status = SyncStatus.Error, 
                Message = $"Sync error: {ex.Message}"
            });
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task ProcessStatusTransitionsAsync()
    {
        try
        {
            var result = await _orderService.ProcessAutomaticStatusTransitionsAsync();
            if (result.Success && result.Message.Contains("0") == false)
            {
                OnStatusTransitionProcessed(result.Message);
                System.Diagnostics.Debug.WriteLine($"Status transitions: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Status transition error: {ex.Message}");
        }
    }

    public async Task<bool> ManualSyncAsync()
    {
        if (!_isInitialized)
        {
            OnSyncStatusChanged(new SyncEventArgs { Status = SyncStatus.Error, Message = "Service not initialized" });
            return false;
        }

        if (_isSyncing)
        {
            OnSyncStatusChanged(new SyncEventArgs { Status = SyncStatus.Error, Message = "Sync already in progress" });
            return false;
        }

        await PerformSyncAsync();
        return true;
    }

    public void PauseSyncing()
    {
        _syncTimer?.Stop();
        OnSyncStatusChanged(new SyncEventArgs { Status = SyncStatus.Paused, Message = "Syncing paused" });
    }

    public void ResumeSyncing()
    {
        if (_isInitialized && _syncTimer != null)
        {
            _syncTimer.Start();
            OnSyncStatusChanged(new SyncEventArgs { Status = SyncStatus.Ready, Message = "Syncing resumed" });
        }
    }

    public bool IsSyncing => _isSyncing;
    public bool IsInitialized => _isInitialized;

    private void OnSyncStatusChanged(SyncEventArgs args)
    {
        SyncStatusChanged?.Invoke(this, args);
    }

    private void OnNewOrderReceived(OrderEventArgs args)
    {
        NewOrderReceived?.Invoke(this, args);
    }

    private void OnStatusTransitionProcessed(string message)
    {
        StatusTransitionProcessed?.Invoke(this, message);
    }

    public void Dispose()
    {
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        _statusTimer?.Stop();
        _statusTimer?.Dispose();
        _apiService?.Dispose();
    }
}

// Event argument classes
public class SyncEventArgs : EventArgs
{
    public SyncStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public int NewOrdersCount { get; set; }
    public DateTime? LastSyncTime { get; set; }
}

public class OrderEventArgs : EventArgs
{
    public Order Order { get; set; } = null!;
}

public enum SyncStatus
{
    NotConfigured,
    Ready,
    Syncing,
    Success,
    Error,
    Paused
}