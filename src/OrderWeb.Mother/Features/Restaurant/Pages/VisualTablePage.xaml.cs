using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Views;
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

            WireSharedTablesView();
            UpdateLastSyncLabel(false);
        }

        private void WireSharedTablesView()
        {
            // Reparent admin buttons into SharedUI toolbar (SharedUI owns the Live sync chip).
            AdminToolsHolder.Content = null;
            TablesView.AdminToolsContent = AdminToolsHost;

            TablesView.TableSelected += async (_, args) =>
            {
                ResetBasicUserIdle();
                if (!int.TryParse(args.Table.Id, out var tableId))
                {
                    return;
                }

                if (!_currentTablesById.TryGetValue(tableId, out var table))
                {
                    return;
                }

                await SelectTableAsync(table);
            };

            TablesView.FloorSelected += async (_, floorId) =>
            {
                ResetBasicUserIdle();
                if (!int.TryParse(floorId, out var id))
                {
                    return;
                }

                var floor = _floors.FirstOrDefault(f => f.Id == id);
                if (floor == null || _currentFloor?.Id == floor.Id)
                {
                    return;
                }

                await SelectFloor(floor, showLoading: false);
            };

            TablesView.TableMoved += (_, args) =>
            {
                ResetBasicUserIdle();
                if (!_isAdmin || !int.TryParse(args.TableId, out var tableId))
                {
                    return;
                }

                if (_currentTablesById.TryGetValue(tableId, out var table))
                {
                    table.PositionX = (int)args.X;
                    table.PositionY = (int)args.Y;
                }

                SetUnsavedChanges(true);
                ShowLayoutStatus("Saving layout...", "info");
                _ = AutoSaveTablePositionAsync(tableId, (int)args.X, (int)args.Y);
            };

            TablesView.EmptyActionRequested += OnGoToTableManagementClicked;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            var serviceSettings = await _orderServiceAvailabilityService.GetAsync(
                forceRefresh: !PosLayoutCache.IsWarm);
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
            TablesView.LayoutEditEnabled = _isAdmin;
            TablesView.EmptyActionText = _isAdmin ? "Go to Table Management" : string.Empty;

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

            // Phase 1: paint silently when layout was warmed after login (or page already has floors).
            var shouldShowLoader = !PosLayoutCache.IsWarm
                && (!_floors.Any() || _lastSuccessfulLayoutLoadAt == DateTime.MinValue);
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
            _basicUserIdleTimer.Interval = TimeSpan.FromSeconds(5);
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

            if (LoadingOverlay.IsLoading || CoverPopupOverlay.IsVisible || !NumericKeyboard.InputTransparent)
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
                PosLayoutCache.Invalidate();
                await OnMainThreadRefreshLayoutAsync();
                return;
            }

            if (e.HasKind(AppDataChangeKind.Orders))
            {
                // Phase 2: patch in place — no "Live update" toast noise.
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
                
                var floorService = _floorService;
                _floors = await Task.Run(async () => await floorService.GetAllFloorsAsync()).ConfigureAwait(true);
                if (_floors.Count > 0)
                {
                    PosLayoutCache.SetFloors(_floors);
                }
                
                System.Diagnostics.Debug.WriteLine($"Floors returned: {_floors.Count}");
                
                if (_floors.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No floors found - showing empty state");
                    ShowNoFloorsMessage();
                    await ToastNotification.ShowAsync("Info", "No floors found. Please add floors in Table Management.", NotificationType.Info);
                    return;
                }

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

        private async Task SelectFloor(Floor floor, bool showLoading = true, bool showWarnings = true)
        {
            var cachedTables = PosLayoutCache.TryGetTables(floor.Id)?.ToList();
            var paintedFromCache = cachedTables != null;

            // Only cold-miss (no cache) may show a loader.
            if (showLoading && !paintedFromCache)
            {
                SetLoadingState(true, $"Loading {floor.Name}...");
            }
            else
            {
                showLoading = false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SelectFloor: {floor.Name} (ID: {floor.Id}) ===");

                _currentFloor = floor;
                Preferences.Set(SelectedFloorPreferenceKey, floor.Id);

                // Instant paint from warm cache so the floor feels live.
                if (paintedFromCache)
                {
                    PublishSharedLayout(cachedTables!, usedFallback: false);
                    TablesView.SetSyncState(RestaurantSyncMode.Updating);
                }

                var floorService = _floorService;
                var backgroundPath = await Task.Run(async () => await floorService.ResolveFloorBackgroundImageAsync(floor))
                    .ConfigureAwait(true);
                SetCanvasBackground(backgroundPath);
                UpdateRemoveBackgroundButtonVisibility();

                System.Diagnostics.Debug.WriteLine($"Loading tables for floor {floor.Id}...");
                var floorId = floor.Id;
                var (tables, usedFallback) = await Task.Run(async () =>
                        await LoadTablesForFloorWithFallbackAsync(floorId, showWarnings: false))
                    .ConfigureAwait(true);
                if (usedFallback && showWarnings)
                {
                    await ToastNotification.ShowAsync(
                        "Warning",
                        "Using fallback table load. Session data is temporarily unavailable.",
                        NotificationType.Warning);
                }

                System.Diagnostics.Debug.WriteLine($"Tables loaded: {tables.Count}");
                PosLayoutCache.SetTables(floor.Id, tables);

                PublishSharedLayout(tables, usedFallback);
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

        private void PublishSharedLayout(List<RestaurantTable> tables, bool usedFallback = false)
        {
            _currentTablesById = tables.ToDictionary(table => table.Id);

            if (_currentFloor != null)
            {
                _currentFloor.TableCount = tables.Count;
            }

            var version = DateTime.UtcNow.Ticks.ToString();
            var floorDtos = _floors
                .Select((floor, index) =>
                {
                    var count = floor.Id == _currentFloor?.Id
                        ? tables.Count
                        : floor.TableCount;
                    return new FloorDto(
                        floor.Id.ToString(),
                        floor.Name,
                        index,
                        string.IsNullOrWhiteSpace(floor.BackgroundImage) ? null : floor.BackgroundImage,
                        TableCount: count);
                })
                .ToList();

            var tableDtos = tables.Select(MapToRestaurantTableDto).ToList();
            var preferredFloorId = _currentFloor?.Id.ToString();

            TablesView.Bind(
                new FloorSnapshotDto(version, floorDtos),
                new TableSnapshotDto(version, tableDtos),
                preferredFloorId);

            _lastSyncAt = DateTime.Now;
            UpdateLastSyncLabel(usedFallback);
        }

        private static RestaurantTableDto MapToRestaurantTableDto(RestaurantTable table)
        {
            var session = table.CurrentSession;
            var isProblem = session?.LinkedOrderIsStaleDraft == true
                || table.Status == TableStatus.Reserved
                || session?.Status == TableSessionStatus.Cleaning;
            var hasActiveSession = session != null;
            var openOrderId = session?.LinkedOrderId
                ?? session?.CurrentOrderId
                ?? (ActiveTableOrderCacheService.TryGetOpenOrderByTableNumber(table.TableNumber, out var cached)
                    ? cached
                    : null);

            return new RestaurantTableDto(
                table.Id.ToString(),
                table.FloorId.ToString(),
                table.TableNumber,
                table.Capacity,
                table.Status.ToString(),
                table.PositionX,
                table.PositionY,
                OpenOrderId: openOrderId,
                GuestCount: session?.PartySize ?? 0,
                SessionStatus: session?.Status.ToString(),
                Icon: table.TableDesignIcon,
                IsProblem: isProblem,
                HasActiveSession: hasActiveSession);
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
            if (_currentFloor == null || _isLoadingFloorsAndTables || _isTableSelectionInProgress)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastSuccessfulTableStateRefreshAt) < TableStateRefreshMinGap)
            {
                return;
            }

            try
            {
                var floorId = _currentFloor.Id;
                var (tables, usedFallback) = await Task.Run(async () =>
                        await LoadTablesForFloorWithFallbackAsync(floorId, showWarnings: false))
                    .ConfigureAwait(true);

                if (_isTableSelectionInProgress || _currentFloor?.Id != floorId)
                {
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_isTableSelectionInProgress)
                    {
                        return;
                    }

                    PosLayoutCache.SetTables(floorId, tables);
                    PublishSharedLayout(tables, usedFallback);
                });

                _lastSuccessfulTableStateRefreshAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualTable] Table state refresh skipped: {ex.Message}");
            }
        }

        private void SetLoadingState(bool isLoading, string? message = null)
        {
            LoadingOverlay.Message = string.IsNullOrWhiteSpace(message)
                ? "Cooking up your data…"
                : message;
            LoadingOverlay.IsLoading = isLoading;
            TablesView.IsLoading = isLoading;
        }

        private void UpdateLastSyncLabel(bool usedFallback)
        {
            if (!_lastSyncAt.HasValue)
            {
                TablesView.SetSyncState(RestaurantSyncMode.NotSynced);
                return;
            }

            TablesView.SetSyncState(
                usedFallback ? RestaurantSyncMode.Fallback : RestaurantSyncMode.Live,
                _lastSyncAt);
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

        private async Task SelectTableAsync(RestaurantTable table)
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
                    ShowCoverPopup(table);
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

                ShowCoverPopup(table);
            }
            finally
            {
                _isTableSelectionInProgress = false;
            }
        }

        private void ShowCoverPopup(RestaurantTable table)
        {
            _popupTable = table;
            TablesView.SetHighlightedTable(table.Id.ToString());
            
            CoverPopupTitle.Text = $"Table {table.TableNumber}";
            _customCoverValue = 0;
            CustomCoverLabel.Text = "Other number...";
            CustomCoverLabel.TextColor = Color.FromArgb("#9CA3AF");
            CoverPopupOverlay.InputTransparent = false;
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
            CoverPopupOverlay.InputTransparent = true;
            NumericKeyboard.Hide();
            TablesView.ClearHighlightedTable();
            _popupTable = null;
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
            CoverPopupOverlay.InputTransparent = true;
            TablesView.ClearHighlightedTable();
            
            System.Diagnostics.Debug.WriteLine($"Table {selectedTable.TableNumber} selected with {coverCount} covers");
            
            _popupTable = null;

            var showOpenLoader = !OrderPlacementPageSimple.IsMenuCacheWarm;
            if (showOpenLoader)
            {
                SetLoadingState(true, $"Opening table {selectedTable.TableNumber}...");
            }

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
                if (showOpenLoader)
                {
                    SetLoadingState(false);
                }
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
            _currentTablesById.Clear();
            TablesView.Bind(
                new FloorSnapshotDto(DateTime.UtcNow.Ticks.ToString(), Array.Empty<FloorDto>()),
                new TableSnapshotDto(DateTime.UtcNow.Ticks.ToString(), Array.Empty<RestaurantTableDto>()));
            TablesView.SetSyncState(RestaurantSyncMode.NotSynced);
            TablesView.FloorBackground = null;
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
                foreach (var kvp in TablesView.GetTablePositions())
                {
                    if (!int.TryParse(kvp.Key, out var tableId))
                    {
                        continue;
                    }

                    var success = await _tableService.UpdateTablePositionAsync(tableId, (int)kvp.Value.X, (int)kvp.Value.Y);
                    if (success)
                    {
                        savedCount++;
                        if (_currentTablesById.TryGetValue(tableId, out var table))
                        {
                            table.PositionX = (int)kvp.Value.X;
                            table.PositionY = (int)kvp.Value.Y;
                        }
                    }
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
                TablesView.FloorBackground = ImageSource.FromFile(imagePath);
                return;
            }

            System.Diagnostics.Debug.WriteLine("No background image");
            TablesView.FloorBackground = null;
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
