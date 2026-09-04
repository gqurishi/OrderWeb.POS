using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Layouts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages
{
    public partial class VisualTablePage : ContentPage
    {
        private readonly FloorService _floorService;
        private readonly RestaurantTableService _tableService;
        private readonly TableSessionService _sessionService;
        private readonly OrderService _orderService;
        private readonly AuthenticationService _authService;
        private readonly RoleAccessService _roleAccessService;
        private readonly InactivityService _inactivityService;
        private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
        private List<Floor> _floors = new();
        private Floor? _currentFloor;
        private Dictionary<int, Border> _tableViews = new();
        private Dictionary<int, RestaurantTable> _currentTablesById = new();
        private bool _isAdmin;
        private bool _hasUnsavedChanges = false;
        private bool _isSubscribedToRefreshEvents;
        private bool _isTableSelectionInProgress;
        private bool _isOpeningTableOrder;
        private bool _isLoadingFloorsAndTables;
        private bool _hasBackfilledTableSessions;
        private DateTime _lastSuccessfulLayoutLoadAt = DateTime.MinValue;
        private DateTime _lastSuccessfulTableStateRefreshAt = DateTime.MinValue;
        private DateTime? _lastSyncAt;
        private IDispatcherTimer? _autoRefreshTimer;
        private IDispatcherTimer? _basicUserIdleTimer;
        private DateTime _basicUserLastActivityAt = DateTime.Now;
        private bool _isBasicUserIdleNavigating;
        private const int GRID_SIZE = 20; // 20px snap grid
        private static readonly TimeSpan BasicUserIdleTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan TableStateRefreshMinGap = TimeSpan.FromMilliseconds(1200);
        private const string SelectedFloorPreferenceKey = "visual_layout_selected_floor_id";

        public VisualTablePage()
        {
            InitializeComponent();
            
            TopBar.SetPageTitle("Restaurant Layout");
            
            _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
            _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
            _inactivityService = ServiceHelper.GetService<InactivityService>() ?? new InactivityService(_authService, _roleAccessService);
            _floorService = new FloorService();
            _tableService = new RestaurantTableService();
            _sessionService = new TableSessionService();
            _orderService = new OrderService();
            _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
                ?? new OrderServiceAvailabilityService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(), _authService);
            
            // Set up numeric keyboard event
            NumericKeyboard.NumberConfirmed += OnNumericKeyboardConfirmed;

            UpdateLastSyncLabel(false);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            var serviceSettings = await _orderServiceAvailabilityService.GetAsync(forceRefresh: true);
            if (!serviceSettings.TableEnabled)
            {
                await AppAlertService.ShowAlertAsync("Table Service Unavailable", "Table service is disabled by the Administrator.");
                await NavigationCoordinator.Shared.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
                return;
            }

            if (_authService.CurrentUser?.Role == UserRole.User)
            {
                _inactivityService.Start();
                _inactivityService.ResetActivity();
                _inactivityService.TrackPage(this);
                StartBasicUserIdleWatchdog();
            }

            _isAdmin = _roleAccessService.IsAdmin(_authService.CurrentUser?.Role);

            // Do not auto-reset table/session state on page load.
            // Active orders/sessions must persist so colors stay accurate
            // (Green=idle, Amber=active, Red=problem).

            if (!_hasBackfilledTableSessions)
            {
                _hasBackfilledTableSessions = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var backfillResult = await _sessionService.BackfillOpenTableSessionsAsync();
                        if (backfillResult.repairedCount > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[VisualTable] {backfillResult.message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VisualTable] Backfill skipped: {ex.Message}");
                    }
                });
            }

            SubscribeToRefreshEvents();
            StartAutoRefreshPolling();
            var shouldShowLoader = !_floors.Any() || _lastSuccessfulLayoutLoadAt == DateTime.MinValue;
            await LoadFloorsAndTables(showLoading: shouldShowLoader, loadingMessage: "Loading layout...");
            UpdateAdminToolsVisibility();
        }

        protected override void OnDisappearing()
        {
            StopBasicUserIdleWatchdog();
            StopAutoRefreshPolling();
            UnsubscribeFromRefreshEvents();
            base.OnDisappearing();
        }

        private void StartBasicUserIdleWatchdog()
        {
            ResetBasicUserIdle();

            if (_basicUserIdleTimer != null)
            {
                return;
            }

            _basicUserIdleTimer = Dispatcher.CreateTimer();
            _basicUserIdleTimer.Interval = TimeSpan.FromSeconds(1);
            _basicUserIdleTimer.Tick += OnBasicUserIdleTick;
            _basicUserIdleTimer.Start();
        }

        private void StopBasicUserIdleWatchdog()
        {
            if (_basicUserIdleTimer == null)
            {
                return;
            }

            _basicUserIdleTimer.Stop();
            _basicUserIdleTimer.Tick -= OnBasicUserIdleTick;
            _basicUserIdleTimer = null;
            _isBasicUserIdleNavigating = false;
        }

        private void ResetBasicUserIdle()
        {
            _basicUserLastActivityAt = DateTime.Now;
            _inactivityService.ResetActivity();
        }

        private async void OnBasicUserIdleTick(object? sender, EventArgs e)
        {
            if (_isBasicUserIdleNavigating || _authService.CurrentUser?.Role != UserRole.User)
            {
                return;
            }

            if (LoadingOverlay.IsVisible || CoverPopupOverlay.IsVisible || !NumericKeyboard.InputTransparent)
            {
                ResetBasicUserIdle();
                return;
            }

            if (DateTime.Now - _basicUserLastActivityAt < BasicUserIdleTimeout)
            {
                return;
            }

            _isBasicUserIdleNavigating = true;
            try
            {
                await _inactivityService.ReturnToDashboardForIdleAsync();
            }
            catch (Exception ex)
            {
                _isBasicUserIdleNavigating = false;
                System.Diagnostics.Debug.WriteLine($"[VisualTable] Basic user idle dashboard return failed: {ex.Message}");
            }
        }

        private void SubscribeToRefreshEvents()
        {
            if (_isSubscribedToRefreshEvents)
            {
                return;
            }

            AppDataRefreshService.DataChanged += OnAppDataChanged;
            _isSubscribedToRefreshEvents = true;
        }

        private void UnsubscribeFromRefreshEvents()
        {
            if (!_isSubscribedToRefreshEvents)
            {
                return;
            }

            AppDataRefreshService.DataChanged -= OnAppDataChanged;
            _isSubscribedToRefreshEvents = false;
        }

        private async void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (e.IsFromCurrentTerminal)
            {
                return;
            }

            if (e.HasKind(AppDataChangeKind.TableLayout))
            {
                await OnMainThreadRefreshLayoutAsync();
                return;
            }

            if (e.HasKind(AppDataChangeKind.Orders))
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ToastNotification.ShowAsync("Live update", e.ToastMessage, NotificationType.Info, 1400);
                });
                await RefreshCurrentFloorTableStatesAsync();
            }
        }

        private async Task OnMainThreadRefreshLayoutAsync()
        {
            if ((DateTime.UtcNow - _lastSuccessfulLayoutLoadAt).TotalMilliseconds < 1200)
            {
                return;
            }

            await LoadFloorsAndTables(showLoading: false);
            UpdateAdminToolsVisibility();
        }

        private void StartAutoRefreshPolling()
        {
            if (_autoRefreshTimer != null)
            {
                return;
            }

            _autoRefreshTimer = Dispatcher.CreateTimer();
            // Change events are primary; this slow poll only recovers a missed event.
            _autoRefreshTimer.Interval = TimeSpan.FromMinutes(5);
            _autoRefreshTimer.Tick += OnAutoRefreshTick;
            _autoRefreshTimer.Start();
        }

        private void StopAutoRefreshPolling()
        {
            if (_autoRefreshTimer == null)
            {
                return;
            }

            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= OnAutoRefreshTick;
            _autoRefreshTimer = null;
        }

        private async void OnAutoRefreshTick(object? sender, EventArgs e)
        {
            if (_isLoadingFloorsAndTables)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastSuccessfulTableStateRefreshAt) < TableStateRefreshMinGap)
            {
                return;
            }

            await RefreshCurrentFloorTableStatesAsync();
        }

        private void UpdateAdminToolsVisibility()
        {
            if (!_isAdmin)
            {
                UnsavedChangesBar.IsVisible = false;
            }

            FloorManagementButton.IsVisible = _isAdmin;
            TableManagementButton.IsVisible = _isAdmin;
            SetBackgroundButton.IsVisible = _isAdmin;
            SaveLayoutButton.IsVisible = _isAdmin;
            // Show remove button only if there's a background image
            UpdateRemoveBackgroundButtonVisibility();
        }
        
        private void UpdateRemoveBackgroundButtonVisibility()
        {
            RemoveBackgroundButton.IsVisible = _isAdmin && _currentFloor != null && !string.IsNullOrEmpty(_currentFloor.BackgroundImage);
        }

        private async Task LoadFloorsAndTables(bool showLoading = true, string? loadingMessage = null)
        {
            if (_isLoadingFloorsAndTables)
            {
                return;
            }

            _isLoadingFloorsAndTables = true;
            if (showLoading)
            {
                SetLoadingState(true, loadingMessage ?? "Refreshing layout...");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("=== LoadFloorsAndTables Started ===");
                var previousFloorId = _currentFloor?.Id;
                var persistedFloorId = Preferences.Get(SelectedFloorPreferenceKey, -1);
                
                _floors = await _floorService.GetAllFloorsAsync();
                
                System.Diagnostics.Debug.WriteLine($"Floors returned: {_floors.Count}");
                
                if (_floors.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No floors found - showing empty state");
                    ShowNoFloorsMessage();
                    await ToastNotification.ShowAsync("Info", "No floors found. Please add floors in Table Management.", NotificationType.Info);
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Creating floor tabs for {_floors.Count} floors");
                CreateFloorTabs();

                var selectedFloorId = previousFloorId ?? (persistedFloorId > 0 ? persistedFloorId : (int?)null);
                var selectedFloor = selectedFloorId.HasValue
                    ? _floors.FirstOrDefault(f => f.Id == selectedFloorId.Value) ?? _floors.First()
                    : _floors.First();

                System.Diagnostics.Debug.WriteLine($"Selecting floor: {selectedFloor.Name}");
                await SelectFloor(selectedFloor, showLoading: false, showWarnings: showLoading);
                
                System.Diagnostics.Debug.WriteLine("=== LoadFloorsAndTables Completed ===");
                                _lastSuccessfulLayoutLoadAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! Error loading floors: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
                await ToastNotification.ShowAsync("Error", $"Could not load floors: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                _isLoadingFloorsAndTables = false;
                if (showLoading)
                {
                    SetLoadingState(false);
                }
            }
        }

        private void CreateFloorTabs()
        {
            FloorTabsLayout.Children.Clear();
            
            foreach (var floor in _floors)
            {
                var tabBorder = new Border
                {
                    BackgroundColor = Color.FromArgb("#F3F4F6"),
                    Stroke = Color.FromArgb("#E5E7EB"),
                    StrokeThickness = 1,
                    Padding = new Thickness(16, 8),
                    MinimumHeightRequest = 44,
                    VerticalOptions = LayoutOptions.Center,
                    BindingContext = floor.Id,
                    StrokeShape = new RoundRectangle { CornerRadius = 12 }
                };

                var tabLabel = new Label
                {
                    Text = $"{floor.Name} ({floor.TableCount})",
                    FontSize = 14,
                    FontFamily = "OpenSansSemibold",
                    TextColor = Color.FromArgb("#374151"),
                    VerticalOptions = LayoutOptions.Center
                };

                tabBorder.Content = tabLabel;
                
                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += async (s, e) =>
                {
                    ResetBasicUserIdle();
                    await SelectFloor(floor);
                };
                tabBorder.GestureRecognizers.Add(tapGesture);
                
                FloorTabsLayout.Children.Add(tabBorder);
            }
        }

        private async Task SelectFloor(Floor floor, bool showLoading = true, bool showWarnings = true)
        {
            if (showLoading)
            {
                SetLoadingState(true, $"Loading {floor.Name}...");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SelectFloor: {floor.Name} (ID: {floor.Id}) ===");
                
                _currentFloor = floor;
                Preferences.Set(SelectedFloorPreferenceKey, floor.Id);
                UpdateFloorTabAppearance();
                
                var backgroundPath = await _floorService.ResolveFloorBackgroundImageAsync(floor);
                SetCanvasBackground(backgroundPath);
                
                // Update remove button visibility based on current floor's background
                UpdateRemoveBackgroundButtonVisibility();
                
                // Load tables for this floor
                System.Diagnostics.Debug.WriteLine($"Loading tables for floor {floor.Id}...");
                var (tables, usedFallback) = await LoadTablesForFloorWithFallbackAsync(floor.Id, showWarnings);
                System.Diagnostics.Debug.WriteLine($"Tables loaded: {tables.Count}");
                
                ClearTableViews();
                _currentTablesById = tables.ToDictionary(table => table.Id);
                
                if (tables.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No tables on this floor - showing empty state");
                    EmptyStateView.IsVisible = true;
                }
                else
                {
                    EmptyStateView.IsVisible = false;
                    System.Diagnostics.Debug.WriteLine("Creating table views...");
                    CreateTableViews(tables);
                    System.Diagnostics.Debug.WriteLine($"Table views created: {_tableViews.Count}");
                }

                _lastSyncAt = DateTime.Now;
                UpdateLastSyncLabel(usedFallback);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! Error selecting floor: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
                await ToastNotification.ShowAsync("Error", $"Could not load tables: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                if (showLoading)
                {
                    SetLoadingState(false);
                }
            }
        }

        private async Task<(List<RestaurantTable> tables, bool usedFallback)> LoadTablesForFloorWithFallbackAsync(int floorId, bool showWarnings = true)
        {
            try
            {
                var sessionTables = await _sessionService.GetTablesWithSessionsAsync(floorId);
                if (sessionTables.Count > 0)
                {
                    return (sessionTables, false);
                }

                var plainTables = await _tableService.GetTablesByFloorAsync(floorId);
                if (plainTables.Count > 0)
                {
                    if (showWarnings)
                    {
                        await ToastNotification.ShowAsync("Warning", "Using fallback table load. Session data is temporarily unavailable.", NotificationType.Warning);
                    }
                    return (plainTables, true);
                }

                return (sessionTables, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Session table load failed, using fallback: {ex.Message}");
                var plainTables = await _tableService.GetTablesByFloorAsync(floorId);
                if (showWarnings)
                {
                    await ToastNotification.ShowAsync("Warning", "Fallback mode active for table layout loading.", NotificationType.Warning);
                }
                return (plainTables, true);
            }
        }

        private async Task RefreshCurrentFloorTableStatesAsync()
        {
            if (_currentFloor == null || _isLoadingFloorsAndTables)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastSuccessfulTableStateRefreshAt) < TableStateRefreshMinGap)
            {
                return;
            }

            try
            {
                var (tables, usedFallback) = await LoadTablesForFloorWithFallbackAsync(_currentFloor.Id, showWarnings: false);
                var incomingIds = tables.Select(table => table.Id).ToHashSet();
                var canPatchInPlace = _tableViews.Count == tables.Count
                    && _tableViews.Keys.All(incomingIds.Contains);

                if (!canPatchInPlace)
                {
                    await SelectFloor(_currentFloor, showLoading: false, showWarnings: false);
                    _lastSuccessfulTableStateRefreshAt = DateTime.UtcNow;
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    foreach (var table in tables)
                    {
                        if (_tableViews.TryGetValue(table.Id, out var tableView))
                        {
                            UpdateTableViewState(tableView, table);
                            _currentTablesById[table.Id] = table;
                        }
                    }

                    _lastSyncAt = DateTime.Now;
                    UpdateLastSyncLabel(usedFallback);
                });

                _lastSuccessfulTableStateRefreshAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualTable] Table state refresh skipped: {ex.Message}");
            }
        }

        private void UpdateTableViewState(Border tableView, RestaurantTable table)
        {
            var (bgColor, borderColor, textColor) = GetTableColors(table);
            tableView.BindingContext = table;
            tableView.BackgroundColor = bgColor;
            tableView.Stroke = borderColor;

            if (tableView.Content is not VerticalStackLayout stack)
            {
                return;
            }

            foreach (var child in stack.Children)
            {
                if (child is Label label)
                {
                    label.Text = table.TableNumber;
                    label.TextColor = textColor;
                }
                else if (child is Ellipse statusDot)
                {
                    statusDot.Fill = borderColor;
                }
            }
        }

        private void SetLoadingState(bool isLoading, string? message = null)
        {
            LoadingOverlay.IsVisible = isLoading;
            LoadingIndicator.IsRunning = isLoading;

            if (!string.IsNullOrWhiteSpace(message))
            {
                LoadingText.Text = message;
            }
            else if (!isLoading)
            {
                LoadingText.Text = "Loading layout...";
            }
        }

        private void UpdateLastSyncLabel(bool usedFallback)
        {
            if (!_lastSyncAt.HasValue)
            {
                LastSyncLabel.Text = "Not synced yet";
                LastSyncLabel.TextColor = Color.FromArgb("#6B7280");
                return;
            }

            LastSyncLabel.Text = usedFallback
                ? $"Fallback sync {_lastSyncAt.Value:HH:mm:ss}"
                : $"Synced {_lastSyncAt.Value:HH:mm:ss}";

            LastSyncLabel.TextColor = usedFallback
                ? Color.FromArgb("#B45309")
                : Color.FromArgb("#047857");
        }

        private void UpdateFloorTabAppearance()
        {
            if (_currentFloor == null) return;
            
            foreach (var child in FloorTabsLayout.Children.OfType<Border>())
            {
                var label = child.Content as Label;
                var isSelected = child.BindingContext is int floorId && floorId == _currentFloor.Id;

                if (label != null && isSelected)
                {
                    child.BackgroundColor = Color.FromArgb("#3B82F6");
                    child.Stroke = Colors.Transparent;
                    label.TextColor = Colors.White;
                }
                else
                {
                    child.BackgroundColor = Color.FromArgb("#F3F4F6");
                    child.Stroke = Color.FromArgb("#E5E7EB");
                    if (label != null) label.TextColor = Color.FromArgb("#374151");
                }
            }
        }

        private void ClearTableViews()
        {
            foreach (var tableView in _tableViews.Values)
            {
                TableCanvas.Children.Remove(tableView);
            }
            _tableViews.Clear();
        }

        private void CreateTableViews(List<RestaurantTable> tables)
        {
            int defaultX = 40;
            int defaultY = 40;
            int index = 0;
            
            foreach (var table in tables)
            {
                var tableView = CreateTableView(table);
                
                // Use saved position or calculate default grid position
                int posX = table.PositionX > 0 ? table.PositionX : defaultX + (index % 6) * 140;
                int posY = table.PositionY > 0 ? table.PositionY : defaultY + (index / 6) * 140;
                
                AbsoluteLayout.SetLayoutBounds(tableView, new Rect(posX, posY, 120, 120));
                AbsoluteLayout.SetLayoutFlags(tableView, AbsoluteLayoutFlags.None);
                
                TableCanvas.Children.Add(tableView);
                _tableViews[table.Id] = tableView;
                index++;
            }
        }

        private Border CreateTableView(RestaurantTable table)
        {
            // Get colors based on status
            var (bgColor, borderColor, textColor) = GetTableColors(table);
            
            var tableBorder = new Border
            {
                BackgroundColor = bgColor,
                Stroke = borderColor,
                StrokeThickness = 2,
                Padding = new Thickness(8),
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Shadow = new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(2, 2),
                    Radius = 8,
                    Opacity = 0.15f
                }
            };

            // Content
            var contentStack = new VerticalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            // Table image
            var tableImage = new Image
            {
                Source = table.TableDesignIcon,
                WidthRequest = 50,
                HeightRequest = 50,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center
            };

            // Table number
            var tableNumber = new Label
            {
                Text = table.TableNumber,
                FontSize = 16,
                FontFamily = "OpenSansSemibold",
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                HorizontalOptions = LayoutOptions.Center
            };

            // Status indicator
            var statusDot = new Ellipse
            {
                WidthRequest = 10,
                HeightRequest = 10,
                Fill = borderColor,
                HorizontalOptions = LayoutOptions.Center
            };

            contentStack.Children.Add(tableImage);
            contentStack.Children.Add(tableNumber);
            contentStack.Children.Add(statusDot);

            tableBorder.Content = contentStack;

            // Store table reference
            tableBorder.BindingContext = table;

            // Add drag gesture for admin FIRST (higher priority)
            if (_isAdmin)
            {
                var panGesture = new PanGestureRecognizer();
                panGesture.PanUpdated += (s, e) => OnTableDrag(table, tableBorder, e);
                tableBorder.GestureRecognizers.Add(panGesture);
            }

            // Add tap gesture for selection
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                ResetBasicUserIdle();
                var currentTable = tableBorder.BindingContext as RestaurantTable ?? table;
                await SelectTableAsync(currentTable, tableBorder);
            };
            tableBorder.GestureRecognizers.Add(tapGesture);

            return tableBorder;
        }

        private (Color bg, Color border, Color text) GetTableColors(RestaurantTable table)
        {
            var session = table.CurrentSession;
            var hasActiveSession = session != null;
            var hasProblemState = session?.LinkedOrderIsStaleDraft == true
                || table.Status == TableStatus.Reserved
                || session?.Status == TableSessionStatus.Cleaning;

            if (hasProblemState)
            {
                return (Color.FromArgb("#FEE2E2"), Color.FromArgb("#EF4444"), Color.FromArgb("#991B1B"));
            }

            if (hasActiveSession)
            {
                return (Color.FromArgb("#FEF3C7"), Color.FromArgb("#F59E0B"), Color.FromArgb("#92400E"));
            }

            return (Color.FromArgb("#D1FAE5"), Color.FromArgb("#10B981"), Color.FromArgb("#065F46"));
        }

        private double _startX, _startY;
        private double _dragStartX, _dragStartY;
        
        private void OnTableDrag(RestaurantTable table, Border tableView, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    var bounds = AbsoluteLayout.GetLayoutBounds(tableView);
                    _startX = bounds.X;
                    _startY = bounds.Y;
                    _dragStartX = bounds.X;
                    _dragStartY = bounds.Y;
                    tableView.Scale = 1.05;
                    tableView.Opacity = 0.8;
                    System.Diagnostics.Debug.WriteLine($"Drag started at ({_startX}, {_startY})");
                    break;

                case GestureStatus.Running:
                    var newX = _startX + e.TotalX;
                    var newY = _startY + e.TotalY;
                    
                    // Get actual canvas dimensions
                    double canvasWidth = TableCanvas.Width > 0 ? TableCanvas.Width : 1200;
                    double canvasHeight = TableCanvas.Height > 0 ? TableCanvas.Height : 800;
                    
                    // Keep within canvas bounds
                    newX = Math.Max(0, Math.Min(newX, canvasWidth - 120));
                    newY = Math.Max(0, Math.Min(newY, canvasHeight - 120));
                    
                    AbsoluteLayout.SetLayoutBounds(tableView, new Rect(newX, newY, 120, 120));
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    tableView.Scale = 1.0;
                    tableView.Opacity = 1.0;
                    
                    // Snap to grid
                    var finalBounds = AbsoluteLayout.GetLayoutBounds(tableView);
                    int snappedX = (int)(Math.Round(finalBounds.X / GRID_SIZE) * GRID_SIZE);
                    int snappedY = (int)(Math.Round(finalBounds.Y / GRID_SIZE) * GRID_SIZE);
                    
                    AbsoluteLayout.SetLayoutBounds(tableView, new Rect(snappedX, snappedY, 120, 120));
                    
                    // Update table position
                    table.PositionX = snappedX;
                    table.PositionY = snappedY;
                    
                    System.Diagnostics.Debug.WriteLine($"Drag completed: ({_dragStartX}, {_dragStartY}) -> ({snappedX}, {snappedY})");
                    
                    // Only mark as changed if position actually changed
                    if (Math.Abs(_dragStartX - snappedX) > 1 || Math.Abs(_dragStartY - snappedY) > 1)
                    {
                        SetUnsavedChanges(true);
                        ShowLayoutStatus("Saving layout...", "info");
                        // Auto-save
                        _ = AutoSaveTablePositionAsync(table.Id, snappedX, snappedY);
                    }
                    break;
            }
        }

        private void SetUnsavedChanges(bool hasChanges)
        {
            if (!_isAdmin)
            {
                _hasUnsavedChanges = false;
                UnsavedChangesBar.IsVisible = false;
                return;
            }

            _hasUnsavedChanges = hasChanges;
            if (hasChanges)
            {
                ShowLayoutStatus("You have unsaved changes", "warning");
            }
            else
            {
                UnsavedChangesBar.IsVisible = false;
            }
        }

        private void ShowLayoutStatus(string message, string tone, bool autoHide = false)
        {
            if (!_isAdmin)
            {
                return;
            }

            LayoutStatusLabel.Text = message;

            switch (tone)
            {
                case "success":
                    UnsavedChangesBar.BackgroundColor = Color.FromArgb("#DCFCE7");
                    LayoutStatusLabel.TextColor = Color.FromArgb("#166534");
                    break;
                case "error":
                    UnsavedChangesBar.BackgroundColor = Color.FromArgb("#FEE2E2");
                    LayoutStatusLabel.TextColor = Color.FromArgb("#B91C1C");
                    break;
                case "info":
                    UnsavedChangesBar.BackgroundColor = Color.FromArgb("#DBEAFE");
                    LayoutStatusLabel.TextColor = Color.FromArgb("#1D4ED8");
                    break;
                default:
                    UnsavedChangesBar.BackgroundColor = Color.FromArgb("#FEF3C7");
                    LayoutStatusLabel.TextColor = Color.FromArgb("#92400E");
                    break;
            }

            UnsavedChangesBar.IsVisible = true;

            if (autoHide)
            {
                _ = AutoHideLayoutStatusAsync(1800);
            }
        }

        private async Task AutoHideLayoutStatusAsync(int milliseconds)
        {
            await Task.Delay(milliseconds);
            if (!_hasUnsavedChanges)
            {
                UnsavedChangesBar.IsVisible = false;
            }
        }

        private async Task AutoSaveTablePositionAsync(int tableId, int x, int y)
        {
            try
            {
                var success = await _tableService.UpdateTablePositionAsync(tableId, x, y);
                if (success)
                {
                    SetUnsavedChanges(false);
                    ShowLayoutStatus($"Saved at {DateTime.Now:HH:mm:ss}", "success", true);
                    System.Diagnostics.Debug.WriteLine($"Auto-saved table {tableId} position to ({x}, {y})");
                }
                else
                {
                    SetUnsavedChanges(true);
                    ShowLayoutStatus("Auto-save failed. Tap Save Layout.", "error");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-save failed: {ex.Message}");
                SetUnsavedChanges(true);
                ShowLayoutStatus("Auto-save failed. Tap Save Layout.", "error");
            }
        }

        private RestaurantTable? _popupTable;
        private Border? _popupTableView;

        private async Task SelectTableAsync(RestaurantTable table, Border tableView)
        {
            if (_isTableSelectionInProgress)
            {
                return;
            }

            _isTableSelectionInProgress = true;
            try
            {
                var hasKnownSession = table.CurrentSession != null || table.CurrentSessionId.HasValue;
                var hasCachedOpenOrder = ActiveTableOrderCacheService.TryGetOpenOrderByTableNumber(table.TableNumber, out _);
                var isLikelyFreshTable = table.Status == TableStatus.Available && !hasKnownSession && !hasCachedOpenOrder;

                if (isLikelyFreshTable)
                {
                    ShowCoverPopup(table, tableView);
                    return;
                }

                var context = await ResolveTableOrderContextAsync(table);
                var shouldOpenExistingFlow = context.SessionId.HasValue
                    || !string.IsNullOrWhiteSpace(context.ExistingOrderId)
                    || table.Status == TableStatus.Occupied;

                if (shouldOpenExistingFlow)
                {
                    await NavigateToTableOrderAsync(table, context.EffectiveCoverCount, context.SessionId, context.ExistingOrderId);
                    return;
                }

                ShowCoverPopup(table, tableView);
            }
            finally
            {
                _isTableSelectionInProgress = false;
            }
        }

        private void ShowCoverPopup(RestaurantTable table, Border tableView)
        {
            _popupTable = table;
            _popupTableView = tableView;
            
            tableView.BackgroundColor = Color.FromArgb("#3B82F6");
            tableView.Stroke = Color.FromArgb("#1D4ED8");
            
            CoverPopupTitle.Text = $"Table {table.TableNumber}";
            _customCoverValue = 0;
            CustomCoverLabel.Text = "Other number...";
            CustomCoverLabel.TextColor = Color.FromArgb("#9CA3AF");
            CoverPopupOverlay.IsVisible = true;
        }

        private async Task<(int? SessionId, string? ExistingOrderId, int EffectiveCoverCount)> ResolveTableOrderContextAsync(RestaurantTable table)
        {
            var sessionId = table.CurrentSession?.Id ?? table.CurrentSessionId;
            string? existingOrderId = null;
            var effectiveCoverCount = table.CurrentSession?.PartySize > 0
                ? table.CurrentSession.PartySize
                : Math.Max(table.Capacity, 1);

            if (!sessionId.HasValue)
            {
                var activeSession = await _sessionService.GetActiveSessionByTableIdAsync(table.Id);
                if (activeSession != null)
                {
                    sessionId = activeSession.Id;
                    effectiveCoverCount = activeSession.PartySize > 0 ? activeSession.PartySize : effectiveCoverCount;
                }
            }

            var candidateOrderIds = new List<string?>
            {
                table.CurrentSession?.LinkedOrderId,
                table.CurrentSession?.CurrentOrderId
            };

            if (sessionId.HasValue && ActiveTableOrderCacheService.TryGetOpenOrderBySessionId(sessionId.Value, out var cachedBySession))
            {
                candidateOrderIds.Add(cachedBySession);
            }

            if (ActiveTableOrderCacheService.TryGetOpenOrderByTableNumber(table.TableNumber, out var cachedByTable))
            {
                candidateOrderIds.Add(cachedByTable);
            }

            foreach (var candidateOrderId in candidateOrderIds.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var candidate = await _orderService.GetOrderByExternalIdAsync(candidateOrderId!);
                if (IsUsableTableOrder(candidate, table.TableNumber, sessionId))
                {
                    existingOrderId = candidate!.OrderId;
                    break;
                }

                ActiveTableOrderCacheService.Remove(candidateOrderId, sessionId, table.TableNumber);
                AppDiagnostics.Log($"[VisualTable] Ignored stale order reference '{candidateOrderId}' for table {table.TableNumber}.");
            }

            if (sessionId.HasValue && string.IsNullOrWhiteSpace(existingOrderId))
            {
                var sessionOrder = await _orderService.GetOpenOrderByTableSessionIdAsync(sessionId.Value);
                if (IsUsableTableOrder(sessionOrder, table.TableNumber, sessionId))
                {
                    existingOrderId = sessionOrder!.OrderId;
                    ActiveTableOrderCacheService.Upsert(
                        sessionOrder.OrderId,
                        sessionOrder.TableSessionId,
                        table.TableNumber,
                        sessionOrder.UpdatedAt == default ? DateTime.Now : sessionOrder.UpdatedAt,
                        sessionOrder.IsOpen);
                }
            }

            if (string.IsNullOrWhiteSpace(existingOrderId))
            {
                var recoveredOrder = await _orderService.GetLatestOpenTableOrderByTableNumberAsync(table.TableNumber);
                if (IsUsableTableOrder(recoveredOrder, table.TableNumber, sessionId))
                {
                    existingOrderId = recoveredOrder!.OrderId;
                    ActiveTableOrderCacheService.Upsert(
                        recoveredOrder.OrderId,
                        recoveredOrder.TableSessionId,
                        table.TableNumber,
                        recoveredOrder.UpdatedAt == default ? DateTime.Now : recoveredOrder.UpdatedAt,
                        recoveredOrder.IsOpen);
                }
            }

            return (sessionId, existingOrderId, Math.Max(effectiveCoverCount, 1));
        }

        private static bool IsUsableTableOrder(Order? order, string tableNumber, int? sessionId)
        {
            if (order == null || !order.IsOpen || order.LocalLifecycleState is LocalLifecycleState.Paid or LocalLifecycleState.Voided)
            {
                return false;
            }

            if (!string.Equals(order.OrderType, "table", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(order.CustomerName?.Trim(), $"Table {tableNumber.Trim()}", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !sessionId.HasValue || !order.TableSessionId.HasValue || order.TableSessionId.Value == sessionId.Value;
        }
        
        private void OnCloseCoverPopup(object sender, EventArgs e)
        {
            ResetBasicUserIdle();
            CloseCoverPopup();
        }
        
        private void CloseCoverPopup()
        {
            CoverPopupOverlay.IsVisible = false;
            
            // Reset table appearance
            if (_popupTable != null && _popupTableView != null)
            {
                var (bg, border, _) = GetTableColors(_popupTable);
                _popupTableView.BackgroundColor = bg;
                _popupTableView.Stroke = border;
            }
            
            _popupTable = null;
            _popupTableView = null;
        }
        
        private async void OnCoverSelected(object sender, EventArgs e)
        {
            ResetBasicUserIdle();

            if (sender is Button button && _popupTable != null)
            {
                if (int.TryParse(button.Text, out int coverCount))
                {
                    await ProcessTableSelection(coverCount);
                }
            }
        }
        
        private int _customCoverValue = 0;
        
        private void OnCustomCoverTapped(object sender, EventArgs e)
        {
            ResetBasicUserIdle();

            // Show numeric keyboard
            NumericKeyboard.Show();
        }
        
        private async void OnNumericKeyboardConfirmed(object? sender, int number)
        {
            ResetBasicUserIdle();

            _customCoverValue = number;
            CustomCoverLabel.Text = number.ToString();
            CustomCoverLabel.TextColor = Color.FromArgb("#1F2937");
            
            // Automatically proceed with selection
            if (_popupTable != null)
            {
                await ProcessTableSelection(number);
            }
        }
        
        private async void OnCustomCoverSubmit(object sender, EventArgs e)
        {
            ResetBasicUserIdle();

            if (_popupTable == null) return;
            
            if (_customCoverValue <= 0)
            {
                // Show numeric keyboard if no value entered
                NumericKeyboard.Show();
                return;
            }
            
            await ProcessTableSelection(_customCoverValue);
        }
        
        private async Task ProcessTableSelection(int coverCount)
        {
            if (_popupTable == null || _isOpeningTableOrder || coverCount <= 0) return;

            _isOpeningTableOrder = true;

            var selectedTable = _popupTable;
            
            // Close popup first
            CoverPopupOverlay.IsVisible = false;
            
            System.Diagnostics.Debug.WriteLine($"Table {selectedTable.TableNumber} selected with {coverCount} covers");
            
            // Reset table view
            if (_popupTableView != null)
            {
                var (bg, border, _) = GetTableColors(_popupTable);
                _popupTableView.BackgroundColor = bg;
                _popupTableView.Stroke = border;
            }
            
            _popupTable = null;
            _popupTableView = null;

            SetLoadingState(true, $"Opening table {selectedTable.TableNumber}...");
            try
            {
                var context = await ResolveTableOrderContextAsync(selectedTable);
                var effectiveCoverCount = context.SessionId.HasValue
                    ? context.EffectiveCoverCount
                    : coverCount;
                await NavigateToTableOrderAsync(selectedTable, effectiveCoverCount, context.SessionId, context.ExistingOrderId);
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log($"[VisualTable] Failed to open table {selectedTable.TableNumber}: {ex}");
                await ToastNotification.ShowAsync("Could not open table", "Please tap the table and try again.", NotificationType.Error, 3000);
            }
            finally
            {
                SetLoadingState(false);
                _isOpeningTableOrder = false;
            }
        }

        private async Task NavigateToTableOrderAsync(RestaurantTable table, int requestedCoverCount, int? sessionId, string? existingOrderId)
        {
            var createdNewSession = false;
            if (sessionId.HasValue && !string.IsNullOrWhiteSpace(existingOrderId))
            {
                var linkResult = await _sessionService.LinkOrderToSessionAsync(sessionId.Value, existingOrderId);
                if (!linkResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[VisualTable] Session link warning: {linkResult.message}");
                }

                var fastOrderPage = new OrderPlacementPageSimple(
                    table.TableNumber,
                    Math.Max(requestedCoverCount, 1),
                    "Current User",
                    1,
                    sessionId,
                    existingOrderId);
                await NavigationCoordinator.Shared.PushTemporaryPageAsync(fastOrderPage, animated: false);
                return;
            }

            if (!sessionId.HasValue)
            {
                var openResult = await _sessionService.OpenTableWithSessionAsync(table.Id, Math.Max(requestedCoverCount, 1));
                if (!openResult.success || !openResult.sessionId.HasValue)
                {
                    await ToastNotification.ShowAsync("Table Busy", openResult.message, NotificationType.Warning, 2500);
                    return;
                }

                sessionId = openResult.sessionId.Value;
                createdNewSession = !string.Equals(openResult.message, "Existing active session resumed", StringComparison.OrdinalIgnoreCase);
            }

            if (sessionId.HasValue && !string.IsNullOrWhiteSpace(existingOrderId))
            {
                var linkResult = await _sessionService.LinkOrderToSessionAsync(sessionId.Value, existingOrderId);
                if (!linkResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[VisualTable] Session link warning: {linkResult.message}");
                }
            }

            var effectiveCoverCount = table.CurrentSession?.PartySize > 0
                ? table.CurrentSession.PartySize
                : Math.Max(requestedCoverCount, 1);

            var orderPage = new OrderPlacementPageSimple(table.TableNumber, effectiveCoverCount, "Current User", 1, sessionId, existingOrderId);
            try
            {
                await NavigationCoordinator.Shared.PushTemporaryPageAsync(orderPage, animated: false);
            }
            catch
            {
                if (createdNewSession && sessionId.HasValue)
                {
                    await _sessionService.CloseSessionForOrderAsync(sessionId.Value, "open_navigation_failed", "system");
                }

                throw;
            }
        }

        private void ShowNoFloorsMessage()
        {
            EmptyStateView.IsVisible = true;
        }

        // Event Handlers
        private async void OnSetBackgroundClicked(object sender, EventArgs e)
        {
            if (!_isAdmin)
            {
                await ToastNotification.ShowAsync("Access Denied", "Only admin can update floor background.", NotificationType.Error);
                return;
            }

            if (_currentFloor == null) return;

            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select Floor Background Image",
                    FileTypes = FilePickerFileType.Images
                });

                if (result != null)
                {
                    using var sourceStream = await result.OpenReadAsync();
                    using var memoryStream = new MemoryStream();
                    await sourceStream.CopyToAsync(memoryStream);

                    var localPath = await _floorService.SaveFloorBackgroundImageAsync(
                        _currentFloor.Id,
                        result.FileName,
                        GetMimeType(result.FileName),
                        memoryStream.ToArray());

                    if (string.IsNullOrWhiteSpace(localPath))
                    {
                        await ToastNotification.ShowAsync("Error", "Could not save background to database", NotificationType.Error);
                        return;
                    }
                    
                    // Update UI
                    _currentFloor.BackgroundImage = localPath;
                    SetCanvasBackground(localPath);
                    UpdateRemoveBackgroundButtonVisibility();

                    await ToastNotification.ShowAsync("Success", "Background image updated", NotificationType.Success);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting background: {ex.Message}");
                await ToastNotification.ShowAsync("Error", "Could not set background image", NotificationType.Error);
            }
        }

        private async void OnSaveLayoutClicked(object sender, EventArgs e)
        {
            if (!_isAdmin)
            {
                await ToastNotification.ShowAsync("Access Denied", "Only admin can save table layout changes.", NotificationType.Error);
                return;
            }

            try
            {
                int savedCount = 0;
                foreach (var kvp in _tableViews)
                {
                    var bounds = AbsoluteLayout.GetLayoutBounds(kvp.Value);
                    var success = await _tableService.UpdateTablePositionAsync(kvp.Key, (int)bounds.X, (int)bounds.Y);
                    if (success) savedCount++;
                }

                SetUnsavedChanges(false);
                ShowLayoutStatus($"Layout saved ({savedCount} tables)", "success", true);
                await ToastNotification.ShowAsync("Success", $"Layout saved ({savedCount} tables)", NotificationType.Success);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving layout: {ex.Message}");
                await ToastNotification.ShowAsync("Error", "Could not save layout", NotificationType.Error);
            }
        }

        private async void OnRemoveBackgroundClicked(object sender, EventArgs e)
        {
            if (!_isAdmin)
            {
                await ToastNotification.ShowAsync("Access Denied", "Only admin can remove floor background.", NotificationType.Error);
                return;
            }

            if (_currentFloor == null) return;

            try
            {
                // Remove from database
                var updateSuccess = await _floorService.RemoveFloorBackgroundImageAsync(_currentFloor.Id);
                if (!updateSuccess)
                {
                    await ToastNotification.ShowAsync("Error", "Could not remove background from database", NotificationType.Error);
                    return;
                }
                
                // Update UI
                _currentFloor.BackgroundImage = string.Empty;
                SetCanvasBackground(null);
                UpdateRemoveBackgroundButtonVisibility();

                await ToastNotification.ShowAsync("Success", "Background image removed", NotificationType.Success);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing background: {ex.Message}");
                await ToastNotification.ShowAsync("Error", "Could not remove background image", NotificationType.Error);
            }
        }

        private async void OnGoToTableManagementClicked(object sender, EventArgs e)
        {
            if (!_isAdmin)
            {
                await ToastNotification.ShowAsync("Access Denied", "Only admin can access table management.", NotificationType.Error);
                return;
            }

            await NavigationCoordinator.Shared.NavigateShellAsync("table", source: sender as VisualElement);
        }

        private void SetCanvasBackground(string? imagePath)
        {
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                System.Diagnostics.Debug.WriteLine($"Setting background: {imagePath}");
                CanvasBackgroundImage.Source = imagePath;
                CanvasBackgroundImageBlur.Source = imagePath;
                CanvasBackgroundImage.IsVisible = true;
                CanvasBackgroundImageBlur.IsVisible = true;
                return;
            }

            System.Diagnostics.Debug.WriteLine("No background image");
            CanvasBackgroundImage.Source = null;
            CanvasBackgroundImageBlur.Source = null;
            CanvasBackgroundImage.IsVisible = false;
            CanvasBackgroundImageBlur.IsVisible = false;
        }

        private static string GetMimeType(string fileName)
        {
            return System.IO.Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
        }

        private async void OnGoToFloorManagementClicked(object sender, EventArgs e)
        {
            if (!_isAdmin)
            {
                await ToastNotification.ShowAsync("Access Denied", "Only admin can access floor management.", NotificationType.Error);
                return;
            }

            await NavigationCoordinator.Shared.NavigateShellAsync("floor", source: sender as VisualElement);
        }
    }
}
