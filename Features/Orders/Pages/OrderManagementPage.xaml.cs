using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class OrderManagementPage : ContentPage, INotifyPropertyChanged
{
    private readonly OrderService _orderService;
    private readonly OnlineOrderApiService _apiService;
    private readonly BackgroundSyncService _syncService;
    private System.Timers.Timer? _refreshTimer;
    private bool _isSubscribedToRefreshEvents;
    
    private ObservableCollection<OrderDisplayModel> _orders = new();
    private List<Order> _allOrders = new();
    
    public ObservableCollection<OrderDisplayModel> Orders
    {
        get => _orders;
        set => SetProperty(ref _orders, value);
    }

    public ICommand SendToKitchenCommand { get; }
    public ICommand MarkReadyCommand { get; }
    public ICommand MarkDeliveringCommand { get; }
    public ICommand CompleteOrderCommand { get; }

    public OrderManagementPage()
    {
        InitializeComponent();
        
        // Set the page title in the TopBar
        TopBar.SetPageTitle("Order Tracking");
        
        _orderService = new OrderService();
        _apiService = new OnlineOrderApiService();
        _syncService = new BackgroundSyncService();
        
        // Initialize commands
        SendToKitchenCommand = new Command<OrderDisplayModel>(async order => await SendToKitchen(order));
        MarkReadyCommand = new Command<OrderDisplayModel>(async order => await MarkReady(order));
        MarkDeliveringCommand = new Command<OrderDisplayModel>(async order => await MarkDelivering(order));
        CompleteOrderCommand = new Command<OrderDisplayModel>(async order => await CompleteOrder(order));
        
        BindingContext = this;
        
        // Set default filter
        StatusFilterPicker.SelectedIndex = 0;
        
        // Start refresh timer (every 30 seconds)
        StartRefreshTimer();
        
        // Subscribe to sync service events
        _syncService.SyncStatusChanged += OnSyncStatusChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SubscribeToRefreshEvents();
        await LoadOrders();
        await CheckApiStatus();
    }

    private void SubscribeToRefreshEvents()
    {
        if (_isSubscribedToRefreshEvents)
        {
            return;
        }

        AppDataRefreshService.RefreshRequested += OnRefreshRequested;
        _isSubscribedToRefreshEvents = true;
    }

    private void UnsubscribeFromRefreshEvents()
    {
        if (!_isSubscribedToRefreshEvents)
        {
            return;
        }

        AppDataRefreshService.RefreshRequested -= OnRefreshRequested;
        _isSubscribedToRefreshEvents = false;
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        await LoadOrders();
        UpdateLastSyncTime();
        await CheckApiStatus();
    }

    private void StartRefreshTimer()
    {
        _refreshTimer = new System.Timers.Timer(30000); // 30 seconds
        _refreshTimer.Elapsed += async (sender, e) =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await LoadOrders();
                UpdateLastSyncTime();
            });
        };
        _refreshTimer.Start();
    }

    private async void OnSyncStatusChanged(object? sender, SyncEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await LoadOrders();
            UpdateLastSyncTime();
        });
    }

    private void UpdateLastSyncTime()
    {
        LastSyncLabel.Text = $"Last sync: {DateTime.Now:HH:mm:ss}";
    }

    private async Task LoadOrders()
    {
        try
        {
            _allOrders = await _orderService.GetOrdersAsync();
            ApplyStatusFilter();
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load orders: {ex.Message}");
        }
    }

    private void ApplyStatusFilter()
    {
        var filteredOrders = _allOrders;
        
        // Apply status filter based on picker selection
        if (StatusFilterPicker.SelectedIndex > 0)
        {
            var selectedStatus = StatusFilterPicker.SelectedIndex switch
            {
                1 => OrderStatus.New,
                2 => OrderStatus.Kitchen,
                3 => OrderStatus.Preparing,
                4 => OrderStatus.Ready,
                5 => OrderStatus.Delivering,
                6 => OrderStatus.Completed,
                _ => OrderStatus.New
            };
            
            filteredOrders = _allOrders.Where(o => o.Status == selectedStatus).ToList();
        }
        
        // Convert to display models and sort by creation time (newest first)
        var displayOrders = filteredOrders
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderDisplayModel(o))
            .ToList();
        
        Orders.Clear();
        foreach (var order in displayOrders)
        {
            Orders.Add(order);
        }
    }

    private async Task SendToKitchen(OrderDisplayModel order)
    {
        try
        {
            var success = await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Kitchen);
            if (success)
            {
                await LoadOrders();
            }
            else
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to update order status");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to send to kitchen: {ex.Message}");
        }
    }

    private async Task MarkReady(OrderDisplayModel order)
    {
        try
        {
            var success = await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Ready);
            if (success)
            {
                await LoadOrders();
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Order {order.OrderId} marked as ready");
            }
            else
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to update order status");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to mark ready: {ex.Message}");
        }
    }

    private async Task MarkDelivering(OrderDisplayModel order)
    {
        try
        {
            var success = await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Delivering);
            if (success)
            {
                await LoadOrders();
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Order {order.OrderId} out for delivery");
            }
            else
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to update order status");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to mark delivering: {ex.Message}");
        }
    }

    private async Task CompleteOrder(OrderDisplayModel order)
    {
        try
        {
            var success = await _orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Completed);
            if (success)
            {
                await LoadOrders();
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Order {order.OrderId} completed");
            }
            else
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to update order status");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to complete order: {ex.Message}");
        }
    }

    private async Task CheckApiStatus()
    {
        try
        {
            var config = await _apiService.GetCurrentConfigurationAsync();
            if (config == null)
            {
                ConnectionStatusLabel.Text = "API Status: Not configured";
                ConnectionStatusLabel.TextColor = Colors.Orange;
                return;
            }

            var (success, message) = await _apiService.TestConnectionAsync();
            ConnectionStatusLabel.Text = success ? "API Status: Connected" : "API Status: Error";
            ConnectionStatusLabel.TextColor = success ? Colors.Green : Colors.Red;
        }
        catch
        {
            ConnectionStatusLabel.Text = "API Status: Error";
            ConnectionStatusLabel.TextColor = Colors.Red;
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await LoadOrders();
        UpdateLastSyncTime();
        await CheckApiStatus();
    }

    private void OnStatusFilterChanged(object sender, EventArgs e)
    {
        ApplyStatusFilter();
    }

    private async void OnApiSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//apiconfig");
    }

    private async void OnTestConnectionClicked(object sender, EventArgs e)
    {
        try
        {
            ConnectionStatusLabel.Text = "API Status: Testing...";
            ConnectionStatusLabel.TextColor = Colors.Blue;

            var (success, message) = await _apiService.TestConnectionAsync();
            
            ConnectionStatusLabel.Text = success ? "API Status: Connected" : "API Status: Error";
            ConnectionStatusLabel.TextColor = success ? Colors.Green : Colors.Red;
            
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Connection Test", message);
        }
        catch (Exception ex)
        {
            ConnectionStatusLabel.Text = "API Status: Error";
            ConnectionStatusLabel.TextColor = Colors.Red;
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Connection test failed: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        UnsubscribeFromRefreshEvents();
        base.OnDisappearing();
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _syncService.SyncStatusChanged -= OnSyncStatusChanged;
    }

    private bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;

        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected new void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

// Display model for orders with UI-specific properties
public class OrderDisplayModel
{
    private readonly Order _order;

    public OrderDisplayModel(Order order)
    {
        _order = order;
    }

    public int Id => _order.Id;
    public string OrderId => _order.OrderId;
    public string CustomerName => _order.CustomerName;
    public string CustomerPhone => _order.CustomerPhone ?? "No phone";
    public string CustomerAddress => _order.CustomerAddress ?? "No address";
    public decimal TotalAmount => _order.TotalAmount;
    public OrderStatus Status => _order.Status;
    public List<OrderItem> Items => _order.Items;

    public string TimeDisplay
    {
        get
        {
            var timeSpan = DateTime.Now - _order.CreatedAt;
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays}d ago";
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours}h ago";
            if (timeSpan.TotalMinutes >= 1)
                return $"{(int)timeSpan.TotalMinutes}m ago";
            return "Just now";
        }
    }

    public string StatusColor
    {
        get
        {
            return _order.Status switch
            {
                OrderStatus.New => "#F44336",      // Red
                OrderStatus.Kitchen => "#FF9800",  // Orange  
                OrderStatus.Preparing => "#FFC107", // Amber
                OrderStatus.Ready => "#4CAF50",     // Green
                OrderStatus.Delivering => "#2196F3", // Blue
                OrderStatus.Completed => "#9E9E9E", // Grey
                _ => "#666666"
            };
        }
    }

    // Button visibility properties
    public bool CanSendToKitchen => _order.Status == OrderStatus.New;
    public bool CanMarkReady => _order.Status == OrderStatus.Preparing;
    public bool CanMarkDelivering => _order.Status == OrderStatus.Ready;
    public bool CanComplete => _order.Status == OrderStatus.Delivering;
}