using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;
using Microsoft.Maui.Controls.Shapes;
using MySqlConnector;
using Syncfusion.Maui.Calendar;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace POS_in_NET.Pages
{
    public partial class WebOrdersPage : ContentPage, INotifyPropertyChanged
    {
        private readonly DatabaseService? _databaseService;
        private readonly AuthenticationService? _authService;
        private readonly RoleAccessService _roleAccessService;
        
        private User? _currentUser;
        private Timer? _refreshTimer;
        private bool _isLoadingWebOrders;
        private bool _hasPendingWebOrdersRefresh;
        private bool _hasLoadedInitialData;
        private bool _isPageActive;
        private bool _isSubscribedToLiveOrderEvents;
        private bool _isBackgroundRecentSyncRunning;
        private bool _hasPendingForcedWebOrdersReload;
        private List<Order> _cachedLocalOrders = new();
        private CancellationTokenSource? _searchDebounceCts;
        private DateTime _lastWebOrdersRefreshAt = DateTime.MinValue;
        private DateTime _lastBackgroundRecentSyncAt = DateTime.MinValue;
        private static readonly TimeSpan MinRefreshGap = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(250);
        private static readonly bool EnableVerboseOrderDump = false;
        private static readonly TimeSpan BackgroundRecentSyncCooldown = TimeSpan.FromMinutes(1);
        
        // Pagination
        private int _currentPage = 0;
        private int _pageSize = 10;
        private int _totalPages = 0;
        private string _searchText = "";
        private bool _isOpeningSearchKeyboard;
        
        // Date Filter - Always defaults to TODAY
        private DateTime _selectedDate = DateTime.Today;
        private bool _showAllOrders = false; // Toggle to show all orders without date filter
        
        private ObservableCollection<WebOrderRow> _orders = new ObservableCollection<WebOrderRow>();
        public ObservableCollection<WebOrderRow> Orders
        {
            get => _orders;
            set
            {
                _orders = value;
                OnPropertyChanged();
            }
        }
        
        public new event PropertyChangedEventHandler? PropertyChanged;
        protected new virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public WebOrdersPage()
        {
            InitializeComponent();
            _databaseService = ServiceHelper.GetService<DatabaseService>();
            _authService = ServiceHelper.GetService<AuthenticationService>();
            _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
            
            // Set the page title in the TopBar
            TopBar.SetPageTitle("Web Orders");
            
            // Set BindingContext for data binding
            BindingContext = this;
            
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!_roleAccessService.IsManagerOrAdmin(_authService?.CurrentUser?.Role))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only Manager and Admin can access Web Orders.");
                    await Shell.Current.GoToAsync($"//{_roleAccessService.ResolveDashboardRoute(_authService?.CurrentUser?.Role)}");
                });
                return;
            }

            _isPageActive = true;
            SubscribeToLiveOrderEvents();
            StartStatusRefreshTimer();

            if (_hasLoadedInitialData)
            {
                await LoadWebOrdersAsync(forceLocalReload: false);
                _ = LoadWebOrdersAsync(forceLocalReload: true);
                StartBackgroundRecentSync("page-reopen");
            }
            else
            {
                await LoadPageAsync();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isPageActive = false;
            StopStatusRefreshTimer();
            UnsubscribeFromLiveOrderEvents();
            _searchDebounceCts?.Cancel();
            _searchDebounceCts?.Dispose();
            _searchDebounceCts = null;
        }

        private async Task LoadPageAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(" LoadPageAsync START");
                
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                // Get current user (optional - no redirect if not found)
                _currentUser = _authService?.GetCurrentUser();
                System.Diagnostics.Debug.WriteLine($" Current user: {_currentUser?.Username ?? "None"}");

                // Update UI for web orders display
                UpdateUIForUserRole();
                System.Diagnostics.Debug.WriteLine(" UpdateUIForUserRole completed");
                
                // DEBUG: Check WebSocket connection status
                var wsService = ServiceHelper.GetService<OrderWebWebSocketService>();
                if (wsService != null)
                {
                    var status = wsService.GetConnectionStatus();
                    System.Diagnostics.Debug.WriteLine($" WebSocket Status: {status}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(" WebSocket service is NULL!");
                }
                
                // Initialize date picker to TODAY
                _selectedDate = DateTime.Today;
                DateDisplayLabel.Text = _selectedDate.ToString("dd MMM yyyy");
                System.Diagnostics.Debug.WriteLine($" Date picker initialized to: {_selectedDate:MMM dd, yyyy}");
                
                // Load cached/local orders first so the page opens immediately.
                System.Diagnostics.Debug.WriteLine(" About to call LoadWebOrdersAsync...");
                await LoadWebOrdersAsync(forceLocalReload: true).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine(" LoadWebOrdersAsync completed");
                _hasLoadedInitialData = true;
                
                // Update connection status on main thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        UpdateConnectionStatus();
                        System.Diagnostics.Debug.WriteLine(" UpdateConnectionStatus completed");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($" Error in UpdateConnectionStatus: {ex.Message}");
                    }
                });

                StartBackgroundRecentSync("page-open");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error in LoadPageAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load page: {ex.Message}");
                });
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadingIndicator.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
                    System.Diagnostics.Debug.WriteLine(" LoadPageAsync COMPLETE");
                });
            }
        }

        private void SubscribeToLiveOrderEvents()
        {
            if (_isSubscribedToLiveOrderEvents)
            {
                return;
            }

            var wsService = ServiceHelper.GetService<OrderWebWebSocketService>();
            var cloudService = ServiceHelper.GetService<CloudOrderService>();
            if (cloudService != null && wsService != null)
            {
                cloudService.SetWebSocketService(wsService);
                wsService.SetCloudOrderService(cloudService);
            }

            if (wsService != null)
            {
                wsService.NewOrderReceived += OnWebSocketOrderReceived;
            }

            if (cloudService != null)
            {
                cloudService.OnOrdersUpdated += OnCloudOrdersUpdated;
            }

            var directDbService = ServiceHelper.GetService<OrderWebDirectDatabaseService>();
            if (directDbService != null)
            {
                directDbService.OnNewOrdersDetected += OnDirectDbOrdersDetected;
            }

            _isSubscribedToLiveOrderEvents = true;
        }

        private void UnsubscribeFromLiveOrderEvents()
        {
            if (!_isSubscribedToLiveOrderEvents)
            {
                return;
            }

            var wsService = ServiceHelper.GetService<OrderWebWebSocketService>();
            if (wsService != null)
            {
                wsService.NewOrderReceived -= OnWebSocketOrderReceived;
            }

            var cloudService = ServiceHelper.GetService<CloudOrderService>();
            if (cloudService != null)
            {
                cloudService.OnOrdersUpdated -= OnCloudOrdersUpdated;
            }

            var directDbService = ServiceHelper.GetService<OrderWebDirectDatabaseService>();
            if (directDbService != null)
            {
                directDbService.OnNewOrdersDetected -= OnDirectDbOrdersDetected;
            }

            _isSubscribedToLiveOrderEvents = false;
        }

        private void StartStatusRefreshTimer()
        {
            StopStatusRefreshTimer();
            _refreshTimer = new Timer(
                _ => MainThread.BeginInvokeOnMainThread(UpdateConnectionStatus),
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10));
        }

        private void StopStatusRefreshTimer()
        {
            _refreshTimer?.Dispose();
            _refreshTimer = null;
        }

        private void OnWebSocketOrderReceived(object? sender, OrderReceivedEventArgs args)
        {
            RefreshWebOrders();
        }

        private void OnCloudOrdersUpdated()
        {
            RefreshWebOrders();
        }

        private void OnDirectDbOrdersDetected()
        {
            RefreshWebOrders();
        }

        private void StartBackgroundRecentSync(string reason)
        {
            if (_isBackgroundRecentSyncRunning ||
                DateTime.UtcNow - _lastBackgroundRecentSyncAt < BackgroundRecentSyncCooldown)
            {
                return;
            }

            var cloudService = ServiceHelper.GetService<CloudOrderService>();
            if (cloudService == null)
            {
                return;
            }

            _isBackgroundRecentSyncRunning = true;
            _lastBackgroundRecentSyncAt = DateTime.UtcNow;

            _ = Task.Run(async () =>
            {
                try
                {
                    var syncResult = await cloudService.SyncLastSevenDaysAsync();
                    System.Diagnostics.Debug.WriteLine($"Web Orders background sync ({reason}): {syncResult.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Web Orders background sync failed: {ex.Message}");
                }
                finally
                {
                    _isBackgroundRecentSyncRunning = false;
                }
            });
        }

        private void UpdateUIForUserRole()
        {
            // Always show web orders - no authentication restriction
            AccessDeniedFrame.IsVisible = false;
            FiltersSection.IsVisible = true;
            OrdersSection.IsVisible = true;
            StatsSection.IsVisible = true; // This now includes Today's Orders + Filter + Search
        }

        private async Task LoadWebOrdersAsync(bool forceLocalReload = true)
        {
            if (_isLoadingWebOrders)
            {
                _hasPendingWebOrdersRefresh = true;
                _hasPendingForcedWebOrdersReload = _hasPendingForcedWebOrdersReload || forceLocalReload;
                return;
            }

            try
            {
                _isLoadingWebOrders = true;
                System.Diagnostics.Debug.WriteLine(" LoadWebOrdersAsync called");
                
                // Get orders from local database that came from cloud (web orders)
                var orderService = ServiceHelper.GetService<OrderService>();
                if (orderService == null)
                {
                    System.Diagnostics.Debug.WriteLine(" OrderService is null!");
                    return;
                }
                
                var allOrders = forceLocalReload || _cachedLocalOrders.Count == 0
                    ? await orderService.GetOrdersAsync().ConfigureAwait(false)
                    : _cachedLocalOrders.ToList();

                if (forceLocalReload || _cachedLocalOrders.Count == 0)
                {
                    _cachedLocalOrders = allOrders.ToList();
                }

                System.Diagnostics.Debug.WriteLine($" Total orders in database: {allOrders.Count}");
                
                if (EnableVerboseOrderDump)
                {
                    System.Diagnostics.Debug.WriteLine("========================================");
                    System.Diagnostics.Debug.WriteLine(" FULL DATABASE DUMP:");
                    foreach (var order in allOrders.OrderByDescending(o => o.CreatedAt).Take(20))
                    {
                        System.Diagnostics.Debug.WriteLine($"   ID: {order.Id} | Date: {order.CreatedAt:yyyy-MM-dd HH:mm:ss} | Customer: {order.CustomerName} | Source: {order.SourceChannel} | Sync: {order.SyncStatus}");
                    }
                    System.Diagnostics.Debug.WriteLine("========================================");
                }
                
                // DEBUG: Show all web order dates
                var webOrders = allOrders.Where(IsWebOrder).ToList();
                System.Diagnostics.Debug.WriteLine($" Total web orders: {webOrders.Count}");
                System.Diagnostics.Debug.WriteLine($" Total pending orders: {allOrders.Count(o => o.SyncStatus == Models.SyncStatus.Pending)}");
                System.Diagnostics.Debug.WriteLine($" Total failed orders: {allOrders.Count(o => o.SyncStatus == Models.SyncStatus.Failed)}");
                
                var orderDates = webOrders.GroupBy(o => o.CreatedAt.Date).OrderByDescending(g => g.Key).ToList();
                System.Diagnostics.Debug.WriteLine($" Web orders by date:");
                foreach (var dateGroup in orderDates.Take(10))
                {
                    System.Diagnostics.Debug.WriteLine($"   {dateGroup.Key:MMM dd, yyyy}: {dateGroup.Count()} orders");
                }
                
                System.Diagnostics.Debug.WriteLine($" Selected date filter: {_selectedDate:yyyy-MM-dd}");
                System.Diagnostics.Debug.WriteLine($" Looking for orders on: {_selectedDate.Date:yyyy-MM-dd}");
                System.Diagnostics.Debug.WriteLine($" Show all mode: {_showAllOrders}");
                
                //  FILTER BY SELECTED DATE (or show all if toggle enabled)
                List<Order> filteredWebOrders;
                var hasSearch = !string.IsNullOrWhiteSpace(_searchText);
                
                if (_showAllOrders || hasSearch)
                {
                    // Global search must cover all cached web orders, not only the selected date.
                    filteredWebOrders = allOrders.Where(IsWebOrder).ToList();
                    System.Diagnostics.Debug.WriteLine(hasSearch
                        ? $" Searching ALL web orders: {filteredWebOrders.Count}"
                        : $" Showing ALL orders: {filteredWebOrders.Count}");
                }
                else
                {
                    // Filter by selected date
                    filteredWebOrders = allOrders.Where(o => 
                        o.CreatedAt.Date == _selectedDate.Date && 
                        IsWebOrder(o)
                    ).ToList();
                    System.Diagnostics.Debug.WriteLine($" Orders for {_selectedDate:MMM dd, yyyy}: {filteredWebOrders.Count}");
                }
                
                // DEBUG: Show why orders might not match
                if (filteredWebOrders.Count == 0 && webOrders.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine(" No orders match selected date! Checking closest dates:");
                    var closestOrders = webOrders
                        .OrderBy(o => Math.Abs((o.CreatedAt.Date - _selectedDate.Date).TotalDays))
                        .Take(5);
                    foreach (var order in closestOrders)
                    {
                        System.Diagnostics.Debug.WriteLine($"   {order.CreatedAt:yyyy-MM-dd} | {order.CustomerName} | Days diff: {(order.CreatedAt.Date - _selectedDate.Date).TotalDays}");
                    }
                }
                
                // Apply search filter if exists
                if (hasSearch)
                {
                    filteredWebOrders = filteredWebOrders
                        .Where(o => MatchesWebOrderSearch(o, _searchText))
                        .ToList();
                    System.Diagnostics.Debug.WriteLine($" Search '{_searchText}' matched {filteredWebOrders.Count} web orders");
                }

                //  PAGINATION - Show 10 orders per page for speed
                var ordersToDisplay = filteredWebOrders
                    .OrderByDescending(o => o.CreatedAt)
                    .Skip(_currentPage * _pageSize)
                    .Take(_pageSize)
                    .ToList();
                var printStatuses = await LoadPrintStatusesAsync(ordersToDisplay).ConfigureAwait(false);
                
                _totalPages = (int)Math.Ceiling(filteredWebOrders.Count / (double)_pageSize);
                
                System.Diagnostics.Debug.WriteLine($" Displaying page {_currentPage + 1} of {_totalPages} ({ordersToDisplay.Count} orders)");
                
                // Update UI on main thread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        // Update Today's stats
                        TodayOrderCountLabel.Text = filteredWebOrders.Count.ToString();
                        
                        var pendingOrders = filteredWebOrders.Where(o => 
                            o.Status == OrderStatus.New || 
                            o.Status == OrderStatus.Kitchen || 
                            o.Status == OrderStatus.Preparing
                        ).ToList();
                        
                        // Update "Showing" label
                        if (!string.IsNullOrWhiteSpace(_searchText))
                        {
                            SelectedDateLabel.Text = $"Search: {_searchText.Trim()}";
                        }
                        else if (_selectedDate.Date == DateTime.Today)
                        {
                            SelectedDateLabel.Text = "Showing: Today";
                        }
                        else
                        {
                            SelectedDateLabel.Text = $"Showing: {_selectedDate:MMM dd, yyyy}";
                        }

                        // Update Orders collection for UI binding - PAGINATED.
                        // Replace the collection in one reset so MAUI's CollectionView does not process
                        // multiple insert notifications while measuring virtualized rows.
                        Orders = new ObservableCollection<WebOrderRow>(
                            ordersToDisplay.Select(order =>
                            {
                                printStatuses.TryGetValue(order.Id, out var printStatus);
                                return new WebOrderRow(order, printStatus ?? WebOrderPrintStatus.NotQueued());
                            }));
                        
                        System.Diagnostics.Debug.WriteLine($" Orders collection updated with {Orders.Count} items");
                        
                        // Update pagination info
                        UpdatePaginationUI();
                        
                        // Show/hide no orders message
                        NoOrdersFrame.IsVisible = filteredWebOrders.Count == 0;
                        
                        // Update connection status
                        var cloudService = ServiceHelper.GetService<CloudOrderService>();
                        var isPolling = cloudService?.IsPolling ?? false;
                        ConnectionStatusIndicator.Fill = isPolling ? Microsoft.Maui.Graphics.Colors.Green : Microsoft.Maui.Graphics.Colors.Orange;
                        ConnectionStatusLabel.Text = isPolling ? "Connected" : "Disconnected";
                        
                        System.Diagnostics.Debug.WriteLine(" UI updated successfully");
                    }
                    catch (Exception uiEx)
                    {
                        System.Diagnostics.Debug.WriteLine($" Error updating UI: {uiEx.Message}");
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading web orders: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load web orders: {ex.Message}");
                });
            }
            finally
            {
                _isLoadingWebOrders = false;
                _lastWebOrdersRefreshAt = DateTime.UtcNow;

                if (_hasPendingWebOrdersRefresh && _isPageActive)
                {
                    var forcePendingReload = _hasPendingForcedWebOrdersReload;
                    _hasPendingWebOrdersRefresh = false;
                    _hasPendingForcedWebOrdersReload = false;
                    _ = MainThread.InvokeOnMainThreadAsync(async () => await LoadWebOrdersAsync(forcePendingReload));
                }
                else if (!_isPageActive)
                {
                    _hasPendingWebOrdersRefresh = false;
                    _hasPendingForcedWebOrdersReload = false;
                }
            }
        }

        private static bool IsWebOrder(Order order)
        {
            return string.Equals(order.SourceChannel, "web", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesWebOrderSearch(Order order, string searchText)
        {
            var query = searchText.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return Contains(order.OrderNumber, query)
                || Contains(order.CloudOrderId, query)
                || Contains(order.OrderId, query)
                || Contains(order.CustomerName, query)
                || Contains(order.CustomerPhone, query);
        }

        private static bool Contains(string? value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<int, WebOrderPrintStatus>> LoadPrintStatusesAsync(List<Order> orders)
        {
            var result = orders.ToDictionary(o => o.Id, _ => WebOrderPrintStatus.NotQueued());

            if (_databaseService == null || orders.Count == 0)
            {
                return result;
            }

            var aliases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var order in orders)
            {
                AddOrderAlias(aliases, order.CloudOrderId, order.Id);
                AddOrderAlias(aliases, order.OrderId, order.Id);
                AddOrderAlias(aliases, order.OrderNumber, order.Id);
            }

            if (aliases.Count == 0)
            {
                return result;
            }

            try
            {
                await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var parameterNames = aliases.Keys.Select((_, index) => $"@orderId{index}").ToList();
                var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT q.order_id, q.job_type, q.status, q.retry_count, COALESCE(q.max_retries, 3) AS max_retries, q.error_message
                    FROM network_print_queue q
                    INNER JOIN (
                        SELECT order_id, job_type, MAX(id) AS latest_id
                        FROM network_print_queue
                        WHERE order_id IN ({string.Join(",", parameterNames)})
                          AND job_type IN ('online_receipt', 'takeaway_ticket')
                        GROUP BY order_id, job_type
                    ) latest ON latest.latest_id = q.id
                    ORDER BY q.order_id, q.job_type";

                var aliasList = aliases.Keys.ToList();
                for (var index = 0; index < aliasList.Count; index++)
                {
                    command.Parameters.AddWithValue(parameterNames[index], aliasList[index]);
                }

                var rowsByOrderId = new Dictionary<int, List<PrintJobStatusRow>>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var queueOrderId = reader["order_id"]?.ToString() ?? string.Empty;
                    if (!aliases.TryGetValue(queueOrderId, out var localOrderId))
                    {
                        continue;
                    }

                    if (!rowsByOrderId.TryGetValue(localOrderId, out var rows))
                    {
                        rows = new List<PrintJobStatusRow>();
                        rowsByOrderId[localOrderId] = rows;
                    }

                    rows.Add(new PrintJobStatusRow(
                        reader["job_type"]?.ToString() ?? "",
                        reader["status"]?.ToString() ?? "pending",
                        Convert.ToInt32(reader["retry_count"]),
                        Convert.ToInt32(reader["max_retries"]),
                        reader["error_message"] == DBNull.Value ? null : reader["error_message"]?.ToString()));
                }

                foreach (var (orderId, rows) in rowsByOrderId)
                {
                    result[orderId] = WebOrderPrintStatus.FromQueueRows(rows);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Failed to load web order print statuses: {ex.Message}");
            }

            return result;
        }

        private static void AddOrderAlias(Dictionary<string, int> aliases, string? value, int orderId)
        {
            if (!string.IsNullOrWhiteSpace(value) && !aliases.ContainsKey(value))
            {
                aliases[value] = orderId;
            }
        }

        private async Task ShowPlaceholderContent()
        {
            // For now, show placeholder content
            TodayOrderCountLabel.Text = "0";
            SelectedDateLabel.Text = "Showing: Today";
            
            NoOrdersFrame.IsVisible = true;
            
            await Task.CompletedTask;
        }

        private async void OnRefreshClicked(object sender, EventArgs e)
        {
            try
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
                
                // Local refresh only. Use "Sync Now" for an explicit cloud pull.
                await LoadWebOrdersAsync(forceLocalReload: true);
                UpdateConnectionStatus();
                
                if (_cachedLocalOrders.Count > 0)
                {
                    var webOrders = _cachedLocalOrders.Where(IsWebOrder).ToList();
                    var orderDates = webOrders.GroupBy(o => o.CreatedAt.Date).OrderByDescending(g => g.Key).Take(5);
                    
                    var datesSummary = string.Join("\n", orderDates.Select(g => $"- {g.Key:MMM dd}: {g.Count()} orders"));
                    
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Refresh Complete",
                        $"Loaded {webOrders.Count} cached web orders.\n\nRecent dates:\n{datesSummary}\n\nUse Sync Now to pull from OrderWeb.");
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Refresh Complete", "Cached web orders reloaded.");
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to refresh: {ex.Message}");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        private async void OnSearchClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Info", "Search functionality coming soon!");
        }

        private async void OnManualSyncClicked(object sender, EventArgs e)
        {
            try
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                // Get cloud order service
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                if (cloudService == null) 
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Cloud service not available");
                    return;
                }
                
                // Use the selected date from the date picker
                var targetDate = _selectedDate;
                System.Diagnostics.Debug.WriteLine($" Manual sync requested for date: {targetDate:yyyy-MM-dd}");
                
                // Fetch orders for the selected date
                var syncResult = await cloudService.SyncOrdersByDateAsync(targetDate);
                
                // Refresh the UI with new data
                await LoadWebOrdersAsync(forceLocalReload: true);

                if (syncResult.Success)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Complete", 
                        $"{syncResult.Message}\n\nOrders fetched from {targetDate:MMM dd, yyyy}");
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Failed", syncResult.Message);
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Sync failed: {ex.Message}");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        private void RefreshWebOrders()
        {
            if (!_isPageActive)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastWebOrdersRefreshAt) < MinRefreshGap)
            {
                _hasPendingWebOrdersRefresh = true;
                return;
            }

            // Refresh web orders data on main thread with live performance monitoring
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var refreshStart = DateTime.Now;
                    await LoadWebOrdersAsync(forceLocalReload: true);
                    var refreshDuration = (DateTime.Now - refreshStart).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($" UI SPEED: Orders refreshed in {refreshDuration:F0}ms");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($" Error refreshing web orders: {ex.Message}");
                }
            });
        }

        // Date Filter Handler
        private async Task ApplySelectedDateAsync(DateTime selectedDate)
        {
            try
            {
                _selectedDate = selectedDate.Date;
                _currentPage = 0; // Reset to first page when date changes
                _showAllOrders = false; // Switch back to date filtering mode
                DateDisplayLabel.Text = _selectedDate.ToString("dd MMM yyyy");
                ShowAllButton.Text = "Show all order";
                ShowAllButton.BackgroundColor = Color.FromArgb("#10B981");
                SelectedDateLabel.Text = _selectedDate.Date == DateTime.Today
                    ? "Showing: Today"
                    : $"Showing: {_selectedDate:MMM dd, yyyy}";
                
                System.Diagnostics.Debug.WriteLine($" Date filter changed to: {_selectedDate:MMM dd, yyyy}");
                
                // Refresh orders with new date filter
                await LoadWebOrdersAsync(forceLocalReload: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error changing date: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to change date: {ex.Message}");
            }
        }

        private async void OnOpenCalendarClicked(object sender, EventArgs e)
        {
            try
            {
                var modal = new ContentPage
                {
                    BackgroundColor = Color.FromArgb("#80000000")
                };

                var calendar = new SfCalendar
                {
                    SelectedDate = _selectedDate,
                    MinimumDate = new DateTime(2020, 1, 1),
                    MaximumDate = DateTime.Today,
                    SelectionMode = CalendarSelectionMode.Single,
                    HeightRequest = 360,
                    Background = Colors.White,
                    HeaderView = new CalendarHeaderView
                    {
                        Background = Color.FromArgb("#10B981"),
                        TextStyle = new CalendarTextStyle
                        {
                            TextColor = Colors.White,
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold
                        }
                    },
                    MonthView = new CalendarMonthView
                    {
                        Background = Colors.White,
                        HeaderView = new CalendarMonthHeaderView
                        {
                            Background = Color.FromArgb("#F3F4F6"),
                            TextStyle = new CalendarTextStyle
                            {
                                TextColor = Color.FromArgb("#6B7280"),
                                FontSize = 13,
                                FontAttributes = FontAttributes.Bold
                            }
                        },
                        TextStyle = new CalendarTextStyle
                        {
                            TextColor = Color.FromArgb("#1F2937"),
                            FontSize = 14
                        },
                        TodayTextStyle = new CalendarTextStyle
                        {
                            TextColor = Color.FromArgb("#10B981"),
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold
                        },
                        TrailingLeadingDatesTextStyle = new CalendarTextStyle
                        {
                            TextColor = Color.FromArgb("#D1D5DB"),
                            FontSize = 13
                        },
                        TodayBackground = Color.FromArgb("#D1FAE5")
                    },
                    SelectionBackground = Color.FromArgb("#10B981")
                };

                var frame = new Frame
                {
                    BackgroundColor = Colors.White,
                    Padding = 0,
                    CornerRadius = 16,
                    HasShadow = true,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    WidthRequest = 420
                };

                var mainLayout = new VerticalStackLayout { Spacing = 0 };

                var headerLayout = new VerticalStackLayout
                {
                    BackgroundColor = Color.FromArgb("#10B981"),
                    Padding = new Thickness(24, 20),
                    Spacing = 4
                };

                headerLayout.Children.Add(new Label
                {
                    Text = "Select Date",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White
                });

                headerLayout.Children.Add(new Label
                {
                    Text = "Choose a trading date",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#D1FAE5")
                });

                var contentLayout = new VerticalStackLayout
                {
                    Padding = new Thickness(16, 16),
                    Spacing = 0
                };
                contentLayout.Children.Add(calendar);

                var buttonLayout = new Grid
                {
                    Padding = new Thickness(24, 16, 24, 24),
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    ColumnSpacing = 12
                };

                var cancelBorder = new Border
                {
                    BackgroundColor = Colors.White,
                    StrokeThickness = 2,
                    Stroke = Color.FromArgb("#D1D5DB"),
                    Padding = new Thickness(0, 14),
                    StrokeShape = new RoundRectangle { CornerRadius = 8 }
                };
                cancelBorder.Content = new Label
                {
                    Text = "Cancel",
                    TextColor = Color.FromArgb("#6B7280"),
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };
                var cancelTap = new TapGestureRecognizer();
                cancelTap.Tapped += async (_, __) => await Navigation.PopModalAsync();
                cancelBorder.GestureRecognizers.Add(cancelTap);

                var applyBorder = new Border
                {
                    BackgroundColor = Color.FromArgb("#10B981"),
                    StrokeThickness = 0,
                    Padding = new Thickness(0, 14),
                    StrokeShape = new RoundRectangle { CornerRadius = 8 }
                };
                applyBorder.Content = new Label
                {
                    Text = "Apply",
                    TextColor = Colors.White,
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };
                var applyTap = new TapGestureRecognizer();
                applyTap.Tapped += async (_, __) =>
                {
                    if (calendar.SelectedDate.HasValue)
                    {
                        await ApplySelectedDateAsync(calendar.SelectedDate.Value);
                    }

                    await Navigation.PopModalAsync();
                };
                applyBorder.GestureRecognizers.Add(applyTap);

                buttonLayout.Children.Add(cancelBorder);
                Grid.SetColumn(cancelBorder, 0);
                buttonLayout.Children.Add(applyBorder);
                Grid.SetColumn(applyBorder, 1);

                mainLayout.Children.Add(headerLayout);
                mainLayout.Children.Add(contentLayout);
                mainLayout.Children.Add(buttonLayout);

                frame.Content = mainLayout;
                modal.Content = frame;

                await Navigation.PushModalAsync(modal);
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to open calendar: {ex.Message}");
            }
        }

        private async void OnSearchSectionTapped(object sender, TappedEventArgs e)
        {
            await OpenSearchKeyboardAsync();
        }

        private async void OnGlobalSearchEntryFocused(object sender, FocusEventArgs e)
        {
            if (!e.IsFocused)
            {
                return;
            }

            await OpenSearchKeyboardAsync();
        }

        private async Task OpenSearchKeyboardAsync()
        {
            if (_isOpeningSearchKeyboard)
            {
                return;
            }

            _isOpeningSearchKeyboard = true;
            try
            {
                GlobalSearchEntry?.Unfocus();

                var keyboard = new VirtualKeyboardDialog();
                keyboard.SetInitialText(_searchText);

                var result = await keyboard.ShowAsync(this);
                if (result == null)
                {
                    return;
                }

                var normalized = result.Trim();
                if (!string.Equals(GlobalSearchEntry?.Text, normalized, StringComparison.Ordinal))
                {
                    GlobalSearchEntry!.Text = normalized;
                }
                else
                {
                    _searchText = normalized;
                    _currentPage = 0;
                    await LoadWebOrdersAsync(forceLocalReload: false);
                }
            }
            finally
            {
                _isOpeningSearchKeyboard = false;
            }
        }

        // Order Action Button Handlers
        private async void OnViewDetailsClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Order order)
            {
                try
                {
                    var detailsPopup = new OrderDetailsPopup(order);
                    await Navigation.PushModalAsync(detailsPopup);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($" Error showing order details: {ex.Message}");
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to show order details");
                }
            }
        }

        private async void OnPrintReceiptClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Order order)
            {
                try
                {
                    LoadingIndicator.IsVisible = true;
                    LoadingIndicator.IsRunning = true;

                    var cloudService = ServiceHelper.GetService<CloudOrderService>();
                    if (cloudService == null)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Cloud order service not available");
                        return;
                    }

                    var result = await cloudService.QueueWebOrderPrintAsync(order);
                    if (result.Success)
                    {
                        await LoadWebOrdersAsync(forceLocalReload: true);
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Queued", result.Message);
                    }
                    else
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Print Queue Error", result.Message);
                    }
                }
                catch (Exception ex)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Print Error", $"Failed to print receipt: {ex.Message}");
                }
                finally
                {
                    LoadingIndicator.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
                }
            }
        }

        private async void OnTakePaymentClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Order order)
            {
                try
                {
                    await Navigation.PushAsync(new OrderPlacementPageSimple(order.OrderId), false);
                }
                catch (Exception ex)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Payment Error", $"Could not open payment screen: {ex.Message}");
                }
            }
        }

        private async void OnMarkCompleteClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Order order)
            {
                try
                {
                    if (OnlineOrderPaymentHelper.IsDeferredPaymentMethod(order.PaymentMethod)
                        && order.LocalLifecycleState != LocalLifecycleState.Paid)
                    {
                        await Navigation.PushAsync(new OrderPlacementPageSimple(order.OrderId), false);
                        return;
                    }

                    bool confirm = await DisplayAlert("Complete Order", 
                        $"Mark order {order.OrderNumber} as completed?", "Yes", "No");
                    
                    if (!confirm) return;

                    LoadingIndicator.IsVisible = true;
                    LoadingIndicator.IsRunning = true;

                    // Update order status
                    order.Status = OrderStatus.Completed;
                    order.CompletedTime = DateTime.Now;
                    order.UpdatedAt = DateTime.Now;

                    var orderService = ServiceHelper.GetService<OrderService>();
                    if (orderService == null)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Order service not available");
                        return;
                    }
                    var updated = await orderService.UpdateOrderStatusAsync(order.Id, OrderStatus.Completed);

                    if (updated)
                    {
                        order.LocalLifecycleState = LocalLifecycleState.Paid;
                        order.IsOpen = false;
                        order.PaymentStatus = PaymentStatus.Paid;
                        order.PaidAt = DateTime.Now;

                        await SendOrderWebCompletionAckAsync(order);

                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Order Completed", 
                            $"Order {order.OrderNumber} has been marked as completed!");
                        
                        // Refresh the orders list
                        await LoadWebOrdersAsync(forceLocalReload: true);
                    }
                    else
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to update order status.");
                    }
                }
                catch (Exception ex)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to complete order: {ex.Message}");
                }
                finally
                {
                    LoadingIndicator.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
                }
            }
        }

        private async Task SendOrderWebCompletionAckAsync(Order order)
        {
            if (!IsWebOrder(order))
            {
                return;
            }

            try
            {
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                if (cloudService == null)
                {
                    System.Diagnostics.Debug.WriteLine("OrderWeb completion ACK skipped: CloudOrderService unavailable");
                    return;
                }

                var user = _authService?.CurrentUser;
                var sentOrQueued = await cloudService.SendOrderSettlementAsync(
                    order,
                    status: "paid",
                    staffId: user?.Id.ToString(),
                    staffName: user?.Name ?? user?.Username,
                    notes: $"POS tender: {order.PaymentMethod}");

                System.Diagnostics.Debug.WriteLine(sentOrQueued
                    ? $"OrderWeb completion ACK sent/queued for {order.OrderId}"
                    : $"OrderWeb completion ACK not sent for {order.OrderId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrderWeb completion ACK warning: {ex.Message}");
            }
        }

        private async void OnSpeedTestClicked(object sender, EventArgs e)
        {
            try
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                // Ultra-fast speed testing using CloudOrderService
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                if (cloudService != null)
                {
                    // Run multiple speed tests
                    var testStart = DateTime.Now;
                    
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Speed Test", 
                        "Testing 2-second polling performance...\n\n" +
                        "• Testing API response time\n" +
                        "• Testing order processing speed\n" +
                        "• Testing UI refresh speed");

                    // Perform speed tests with ultra-fast polling
                    await cloudService.FetchOrdersAsync();
                    await Task.Delay(100); // Small delay to see performance
                    await cloudService.FetchOrdersAsync();
                    await Task.Delay(100);
                    await cloudService.FetchOrdersAsync();
                    
                    var testDuration = (DateTime.Now - testStart).TotalMilliseconds;
                    
                    // Generate performance status
                    var performanceStatus = $"Ultra-Fast Polling Performance:\n" +
                                          $"• Test Duration: {testDuration:F0}ms\n" +
                                          $"• Polling Interval: 2 seconds\n" +
                                          $"• Last Sync: {(cloudService.LastSyncTime != default ? cloudService.LastSyncTime.ToString("HH:mm:ss") : "Never")}\n" +
                                          $"• Status: {(cloudService.IsPolling ? "Active" : "Stopped")}";
                    
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Speed Test Results", 
                        $"Test completed in {testDuration:F0}ms\n\n{performanceStatus}\n\n" +
                        "Orders will appear within 2-4 seconds.\n" +
                        "15x faster than standard polling.");
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Speed Test Failed", 
                        "CloudOrderService not available");
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Speed Test Error", $"Test failed: {ex.Message}");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        // ===== PAGINATION METHODS =====
        
        private void UpdatePaginationUI()
        {
            if (PaginationLabel != null)
            {
                PaginationLabel.Text = _totalPages > 0 
                    ? $"Page {_currentPage + 1} of {_totalPages}" 
                    : "No orders";
                
                PrevPageButton.IsEnabled = _currentPage > 0;
                NextPageButton.IsEnabled = _currentPage < _totalPages - 1;
            }
        }

        private async void OnPreviousPageClicked(object sender, EventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                await LoadWebOrdersAsync(forceLocalReload: false);
            }
        }

        private async void OnNextPageClicked(object sender, EventArgs e)
        {
            if (_currentPage < _totalPages - 1)
            {
                _currentPage++;
                await LoadWebOrdersAsync(forceLocalReload: false);
            }
        }

        private async void OnGlobalSearchChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = e.NewTextValue ?? "";
            _currentPage = 0; // Reset to first page
            await DebounceWebOrderSearchAsync();
        }

        private async Task DebounceWebOrderSearchAsync()
        {
            _searchDebounceCts?.Cancel();
            _searchDebounceCts?.Dispose();
            var cts = new CancellationTokenSource();
            _searchDebounceCts = cts;

            try
            {
                await Task.Delay(SearchDebounceDelay, cts.Token);
                if (!cts.IsCancellationRequested && _isPageActive)
                {
                    await LoadWebOrdersAsync(forceLocalReload: false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// Manual Sync Now button - Force fetch latest orders from OrderWeb.net
        /// </summary>
        private async void OnSyncNowClicked(object sender, EventArgs e)
        {
            try
            {
                // Disable button during sync
                SyncNowButton.IsEnabled = false;
                SyncNowButton.Text = "Syncing...";
                ConnectionStatusLabel.Text = "Syncing...";
                ConnectionStatusIndicator.Fill = Colors.Orange;
                
                System.Diagnostics.Debug.WriteLine(" Manual sync initiated by user");
                
                // Get services
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                if (cloudService == null)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Services not available");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine(" Manual Sync: Syncing last 7 days from OrderWeb.net");
                var result = await cloudService.SyncLastSevenDaysAsync();
                
                System.Diagnostics.Debug.WriteLine($" Sync result: Success={result.Success}, OrdersFound={result.OrdersFound}, Message={result.Message}");
                
                // Refresh UI to show all orders
                System.Diagnostics.Debug.WriteLine(" Refreshing UI after sync...");
                await LoadWebOrdersAsync(forceLocalReload: true);
                
                // Update status
                UpdateConnectionStatus();
                
                if (result.Success)
                {
                    var message = result.OrdersFound > 0
                        ? $"Synced {result.OrdersFound} orders from OrderWeb.net\n{result.Message}"
                        : $"No new orders found.\n{result.Message}";

                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Complete", message);
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Failed", result.Message);
                }
                
                System.Diagnostics.Debug.WriteLine($" Manual sync complete: {result.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Manual sync failed: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Failed", ex.Message);
            }
            finally
            {
                // Re-enable button
                SyncNowButton.IsEnabled = true;
                SyncNowButton.Text = "Sync Now";
                UpdateConnectionStatus();
            }
        }
        
        /// <summary>
        /// Update connection status indicator based on WebSocket state
        /// </summary>
        private void UpdateConnectionStatus()
        {
            try
            {
                var wsService = ServiceHelper.GetService<OrderWebWebSocketService>();
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                
                if (wsService != null)
                {
                    if (wsService.IsConnected)
                    {
                        // WebSocket is connected - primary real-time mode
                        ConnectionStatusLabel.Text = "Connected";
                        ConnectionStatusIndicator.Fill = Colors.Green;
                        
                        // Show last message time if available
                        if (wsService.LastMessageTime.HasValue)
                        {
                            var elapsed = DateTime.Now - wsService.LastMessageTime.Value;
                            if (elapsed.TotalMinutes < 1)
                                LastSyncLabel.Text = "Last sync: Just now";
                            else if (elapsed.TotalMinutes < 60)
                                LastSyncLabel.Text = $"Last sync: {(int)elapsed.TotalMinutes}m ago";
                            else
                                LastSyncLabel.Text = $"Last sync: {(int)elapsed.TotalHours}h ago";
                        }
                        else
                        {
                            LastSyncLabel.Text = "Real-time updates active";
                        }
                    }
                    else
                    {
                        // WebSocket disconnected - backup polling mode
                        ConnectionStatusLabel.Text = "Backup Mode";
                        ConnectionStatusIndicator.Fill = Colors.Orange;
                        
                        // Show last sync from polling
                        var pollingService = cloudService;
                        if (pollingService != null)
                        {
                            var lastSync = pollingService.LastSyncTime;
                            if (lastSync != default)
                            {
                                var elapsed = DateTime.Now - lastSync;
                                LastSyncLabel.Text = $"Last check: {(int)elapsed.TotalSeconds}s ago";
                            }
                            else
                            {
                                LastSyncLabel.Text = "Checking every 60s";
                            }
                        }
                        else
                        {
                            LastSyncLabel.Text = "Checking every 60s";
                        }
                    }
                }
                else
                {
                    // No services available
                    ConnectionStatusLabel.Text = "Offline";
                    ConnectionStatusIndicator.Fill = Colors.Red;
                    LastSyncLabel.Text = "Click Sync Now";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating connection status: {ex.Message}");
            }
        }

        // Helper method to format payment method display
        private string FormatPaymentMethod(string? paymentMethod)
        {
            if (string.IsNullOrWhiteSpace(paymentMethod))
                return "Not specified";
            
            // Convert common payment method values to display format
            return paymentMethod.ToLower() switch
            {
                "cash" => "Cash",
                "card" => "Card",
                "credit_card" => "Credit Card",
                "debit_card" => "Debit Card",
                "voucher" => "Gift Card",        // OrderWeb.net uses "voucher" for gift cards
                "gift_card" => "Gift Card",
                "giftcard" => "Gift Card",
                "online" => "Online Payment",
                "cod" => "Cash on Delivery",
                _ => paymentMethod // Return original if not recognized
            };
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            // Get authentication service
            var authService = ServiceHelper.GetService<AuthenticationService>();
            if (authService != null)
            {
                await authService.LogoutAsync();
            }

            // Navigate to login page immediately
            await Shell.Current.GoToAsync("//login");
        }
        
        private async void OnShowAllClicked(object sender, EventArgs e)
        {
            // For dates outside the local 7-day cache window, fetch that day from web first.
            var cacheBoundaryDate = DateTime.Today.AddDays(-7);
            var isOlderThanCache = _selectedDate.Date < cacheBoundaryDate.Date;

            if (isOlderThanCache)
            {
                try
                {
                    LoadingIndicator.IsVisible = true;
                    LoadingIndicator.IsRunning = true;

                    var cloudService = ServiceHelper.GetService<CloudOrderService>();
                    if (cloudService == null)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Cloud service not available");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($" Older than cache window selected ({_selectedDate:yyyy-MM-dd}). Syncing selected day from web...");
                    var syncResult = await cloudService.SyncOrdersByDateAsync(_selectedDate);

                    // Keep selected-date mode after sync and refresh that day.
                    _showAllOrders = false;
                    ShowAllButton.Text = "Show all order";
                    ShowAllButton.BackgroundColor = Color.FromArgb("#10B981");
                    SelectedDateLabel.Text = _selectedDate.Date == DateTime.Today
                        ? "Showing: Today"
                        : $"Showing: {_selectedDate:MMM dd, yyyy}";

                    await LoadWebOrdersAsync(forceLocalReload: true);

                    if (!syncResult.Success)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Failed", syncResult.Message);
                    }
                }
                catch (Exception ex)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to sync selected date: {ex.Message}");
                }
                finally
                {
                    LoadingIndicator.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
                }

                return;
            }

            _showAllOrders = !_showAllOrders;
            
            if (_showAllOrders)
            {
                ShowAllButton.Text = "Back to date filter";
                ShowAllButton.BackgroundColor = Color.FromArgb("#F59E0B");
                SelectedDateLabel.Text = "Showing: All Orders";
            }
            else
            {
                ShowAllButton.Text = "Show all order";
                ShowAllButton.BackgroundColor = Color.FromArgb("#10B981");
                SelectedDateLabel.Text = $"Showing: {_selectedDate:MMM dd, yyyy}";
            }
            
            await LoadWebOrdersAsync(forceLocalReload: false);
        }

        public sealed record PrintJobStatusRow(string JobType, string Status, int RetryCount, int MaxRetries, string? ErrorMessage);

        public sealed class WebOrderPrintStatus
        {
            public string Code { get; init; } = "not_queued";
            public string? ErrorMessage { get; init; }

            public static WebOrderPrintStatus NotQueued() => new() { Code = "not_queued" };

            public static WebOrderPrintStatus FromQueueRows(IReadOnlyCollection<PrintJobStatusRow> rows)
            {
                if (rows.Count == 0)
                {
                    return NotQueued();
                }

                var receipt = rows.FirstOrDefault(r => string.Equals(r.JobType, "online_receipt", StringComparison.OrdinalIgnoreCase));
                var kitchen = rows.FirstOrDefault(r => string.Equals(r.JobType, "takeaway_ticket", StringComparison.OrdinalIgnoreCase));
                var requiredRows = new[] { receipt, kitchen }.Where(r => r != null).Cast<PrintJobStatusRow>().ToList();

                if (requiredRows.Any(r => string.Equals(r.Status, "printing", StringComparison.OrdinalIgnoreCase)))
                {
                    return new WebOrderPrintStatus { Code = "printing" };
                }

                var failed = requiredRows.FirstOrDefault(r =>
                    string.Equals(r.Status, "failed", StringComparison.OrdinalIgnoreCase) ||
                    r.RetryCount >= r.MaxRetries);
                if (failed != null)
                {
                    return new WebOrderPrintStatus { Code = "failed", ErrorMessage = failed.ErrorMessage };
                }

                if (receipt != null &&
                    kitchen != null &&
                    requiredRows.All(r => string.Equals(r.Status, "completed", StringComparison.OrdinalIgnoreCase)))
                {
                    return new WebOrderPrintStatus { Code = "printed" };
                }

                return new WebOrderPrintStatus { Code = "queued" };
            }
        }

        public sealed class WebOrderRow
        {
            public WebOrderRow(Order order, WebOrderPrintStatus printStatus)
            {
                Order = order;
                PrintStatus = printStatus;
            }

            public Order Order { get; }
            public WebOrderPrintStatus PrintStatus { get; }

            public string? OrderNumber => Order.OrderNumber;
            public DateTime CreatedAt => Order.CreatedAt;
            public string? CustomerName => Order.CustomerName;
            public string? CustomerPhone => Order.CustomerPhone;
            public string? PaymentMethod => Order.PaymentMethod;
            public string PaymentMethodDisplay
            {
                get
                {
                    var method = OnlineOrderPaymentHelper.GetDisplayMethod(Order.PaymentMethod);
                    return CanTakePayment ? $"{method} Due" : method;
                }
            }
            public Color PaymentMethodColor => OnlineOrderPaymentHelper.NormalizeMethod(Order.PaymentMethod) switch
            {
                "cash" => CanTakePayment ? Color.FromArgb("#B45309") : Color.FromArgb("#059669"),
                "card" or "online" => Color.FromArgb("#2563EB"),
                "gift_card" => Color.FromArgb("#7C3AED"),
                _ => Color.FromArgb("#4B5563")
            };
            public bool CanTakePayment =>
                Order.LocalLifecycleState != LocalLifecycleState.Paid &&
                Order.LocalLifecycleState != LocalLifecycleState.Voided &&
                OnlineOrderPaymentHelper.IsDeferredPaymentMethod(Order.PaymentMethod);
            public decimal TotalAmount => Order.TotalAmount;
            public string OrderTypeDisplay => string.IsNullOrWhiteSpace(Order.OrderType)
                ? "Online"
                : char.ToUpper(Order.OrderType[0]) + Order.OrderType[1..].Replace("_", " ");
            public string OrderStatusDisplay => Order.Status.ToString();
            public string PrintStatusText => PrintStatus.Code switch
            {
                "queued" => "Queued",
                "printing" => "Printing",
                "printed" => "Printed",
                "failed" => "Failed",
                _ => "Not queued"
            };
            public Color PrintStatusColor => PrintStatus.Code switch
            {
                "queued" => Color.FromArgb("#64748B"),
                "printing" => Color.FromArgb("#2563EB"),
                "printed" => Color.FromArgb("#059669"),
                "failed" => Color.FromArgb("#DC2626"),
                _ => Color.FromArgb("#94A3B8")
            };
        }
    }
}
