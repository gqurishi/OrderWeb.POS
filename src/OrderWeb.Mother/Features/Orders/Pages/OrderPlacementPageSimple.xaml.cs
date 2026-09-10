using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using OrderWeb.SharedUI.Controls.OrderPlace;
using OrderWeb.SharedUI.Hosting;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace POS_in_NET.Pages
{
    public partial class OrderPlacementPageSimple : ContentPage, INavigationCommitParticipant
    {
        // Services
        private MenuItemService _menuItemService;
        private MenuCategoryService _categoryService;
        private MealDealService _mealDealService;
        private TastingMenuService _tastingMenuService;
        private OrderService _orderService;
        private TableSessionService _tableSessionService;
        private OrderRoutingPrintService _orderRoutingPrintService;
        private KitchenOrderRevisionService _kitchenRevisionService;
        private OrderNumberService _orderNumberService;
        private OrderLifecycleRolloutService _orderLifecycleRolloutService;
        private CashDrawerService _cashDrawerService;
        private DiscountAuditService _discountAuditService;
        private LoyaltyService _loyaltyService;
        private RoleAccessService _roleAccessService;
        private AuthenticationService _authService;
        private InactivityService _inactivityService;
        private RestaurantTableService _restaurantTableService;
        private DatabaseService _databaseService;
        private TableServiceChargeSettingsService _tableServiceChargeSettingsService;
        private TableServiceChargeOrderAuditService _tableServiceChargeOrderAuditService;
        private NavigationCoordinator _navigationCoordinator = NavigationCoordinator.Shared;
        private OrderLifecycleRolloutConfig _rolloutConfig = OrderLifecycleRolloutConfig.CreateDefault();

        // SharedUI Order Place shell (same as Client)
        private readonly OrderPlaceShellView _orderPlaceShell = new();
        private readonly OrderPlaceSessionState _orderPlaceSession = new();
        private MotherOrderPlaceHost? _orderPlaceHost;
        private List<OrderPlaceProductItem> _visibleProducts = new();
        private List<MenuCategory> _visibleSubcategories = new();
        private readonly List<MenuCategory> _shellTopCategories = new();
        private string? _shellSelectedCategoryId;
        private string? _shellSelectedSubcategoryId;

        // Model State
        private TableOrder _currentOrder = new();
        private List<MenuCategory> _allCategories = new();
        private List<MenuCategory> _topLevelCategories = new();
        private List<FoodMenuItem> _allMenuItems = new();
        private List<FoodMenuItem> _filteredItems = new();
        private List<MealDeal> _activeMealDeals = new();
        private List<TastingMenu> _activeTastingMenus = new();

        // UI State
        private MenuCategory? _selectedCategory;
        private MenuCategory? _selectedSubCategory;
        private FoodMenuItem? _modifierPopupItem;
        private bool _isCourseFireInProgress;

        // Session/Order State  
        private string? _pendingOrderId;
        private int? _tableSessionId;
        private string _persistentOrderNumber = string.Empty;
        private string _orderSourceChannel = "local";
        private string? _orderCloudOrderId;
        private string? _requestedPaymentMethod;
        private DateTime _lastSavedAt = DateTime.MinValue;
        private User? _currentUser;

        // Delivery/Collection/Table State
        private bool _isDeliveryOrder = false;
        private bool _isCollectionOrder = false;
        private int _deliveryCustomerId;
        private string _deliveryCustomerName = string.Empty;
        private string _deliveryCustomerPhone = string.Empty;
        private string _deliveryCustomerAddress = string.Empty;
        private int _collectionCustomerId;
        private string _collectionCustomerName = string.Empty;
        private string _collectionCustomerPhone = string.Empty;

        // Draft/Persistence State
        private bool _draftDirty = false;
        private bool _hasLoadedPersistentOrder = false;
        private bool _isLoadingPersistentOrder = false;
        private bool _isFinalizingOrder = false;
        private bool _isExplicitOrderCommit = false;
        private bool _isUltraFastSendInProgress = false;
        private bool _isPrintInProgress = false;
        private Task? _initialLoadTask;
        private bool _isRolloutConfigLoaded = false;
        private bool _hasShownConcurrencyConflict = false;
        private bool _isSubscribedToLiveUpdates = false;
        private CancellationTokenSource _draftSaveDelayCts = new();
        private readonly SemaphoreSlim _draftSaveLock = new(1, 1);
        private readonly SemaphoreSlim _orderNumberLock = new(1, 1);
        private IDisposable? _idleDraftSaveRegistration;

        // Menu Caching
        private static List<MenuCategory>? _cachedCategories;
        private static List<FoodMenuItem>? _cachedMenuItems;
        private static List<MealDeal>? _cachedMealDeals;
        private static List<TastingMenu>? _cachedTastingMenus;
        private static DateTime _menuCacheUpdatedAt = DateTime.MinValue;
        private static readonly TimeSpan MenuCacheTtl = TimeSpan.FromMinutes(30);
        private readonly Dictionary<string, List<MenuItemQuickNote>> _quickNotesCache = new();
        private readonly Dictionary<string, OrderPlaceLineRow> _orderLineRows = new(StringComparer.Ordinal);

        // Feature Flags (Runtime)
        private bool EnableInstantSendMode = true;
        private bool EnableStartupReadinessChecks = false;

        // Readiness Checks
        private DateTime _lastReadinessCheckAt = DateTime.MinValue;
        private (List<string> Critical, List<string> Info)? _cachedReadinessIssues;

        /// <summary>SharedUI session used by <see cref="MotherOrderPlaceHost"/>.</summary>
        internal OrderPlaceSessionState OrderPlaceSession => _orderPlaceSession;

        /// <summary>Raised when shell should rebind (same as Client host).</summary>
        internal event EventHandler? OrderPlaceStateChanged;

        /// <summary>SharedUI shell entry — OnAppearing already loads; republish session.</summary>
        internal async Task OrderPlaceInitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_initialLoadTask != null)
            {
                await _initialLoadTask;
            }

            PublishOrderPlaceSession();
        }

        internal Task OrderPlaceSelectCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var category = _shellTopCategories.FirstOrDefault(c =>
                string.Equals(c.Id, categoryId, StringComparison.Ordinal));
            if (category != null)
            {
                SelectCategory(category);
            }

            return Task.CompletedTask;
        }

        internal Task OrderPlaceSelectSubcategoryAsync(string? subcategoryId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(subcategoryId))
            {
                return Task.CompletedTask;
            }

            var subcategory = _visibleSubcategories.FirstOrDefault(c =>
                string.Equals(c.Id, subcategoryId, StringComparison.Ordinal))
                ?? _allCategories.FirstOrDefault(c =>
                    string.Equals(c.Id, subcategoryId, StringComparison.Ordinal));
            if (subcategory != null)
            {
                SelectSubCategory(subcategory);
            }

            return Task.CompletedTask;
        }

        internal async Task OrderPlaceAddProductAsync(string productId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(productId))
            {
                return;
            }

            if (productId.StartsWith("md:", StringComparison.OrdinalIgnoreCase))
            {
                var dealId = productId["md:".Length..];
                var deal = _activeMealDeals.FirstOrDefault(d =>
                    string.Equals(d.Id, dealId, StringComparison.OrdinalIgnoreCase));
                if (deal != null)
                {
                    await OnMealDealTappedAsync(deal);
                }

                return;
            }

            if (productId.StartsWith("tm:", StringComparison.OrdinalIgnoreCase))
            {
                var menuId = productId["tm:".Length..];
                var menu = _activeTastingMenus.FirstOrDefault(m =>
                    string.Equals(m.Id, menuId, StringComparison.OrdinalIgnoreCase));
                if (menu != null)
                {
                    await OnTastingMenuTappedAsync(menu);
                }

                return;
            }

            var item = _allMenuItems.FirstOrDefault(i =>
                string.Equals(i.Id, productId, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                await OnMenuItemTappedAsync(item);
            }
        }

        internal async Task OrderPlaceSetLineQuantityAsync(string lineId, int quantity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = _currentOrder.Items.FirstOrDefault(i =>
                string.Equals(i.Id, lineId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                return;
            }

            await ApplyOrderLineQuantityAsync(item, IsTastingMenuOrderItem(item), quantity);
        }

        internal async Task OrderPlaceEditLineNoteAsync(string lineId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = _currentOrder.Items.FirstOrDefault(i =>
                string.Equals(i.Id, lineId, StringComparison.OrdinalIgnoreCase));
            if (item == null || IsMealDealOrderItem(item))
            {
                return;
            }

            await ShowNoteDialog(item);
        }

        internal async Task OrderPlaceTrailingLineActionAsync(string lineId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = _currentOrder.Items.FirstOrDefault(i =>
                string.Equals(i.Id, lineId, StringComparison.OrdinalIgnoreCase));
            if (item == null || !IsTastingMenuOrderItem(item))
            {
                return;
            }

            await ShowTastingCourseProgressAsync(item);
        }

        internal Task OrderPlaceOrderNotesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnNotesClicked(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        internal Task OrderPlaceVoidAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnVoidClicked(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        internal Task OrderPlaceMoreAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnMoreClicked(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        internal Task OrderPlaceSendAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnSendClicked(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        internal Task OrderPlacePrintAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnPrintReceiptClicked(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        internal Task OrderPlacePayAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnPayClicked(null, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public OrderPlacementPageSimple()
        {
            InitializeComponent();
            SizeChanged += OnOrderPageSizeChanged;
            InitializeServices();
            _orderPlaceHost = new MotherOrderPlaceHost(this);
            ShellHost.Content = _orderPlaceShell;
            _orderPlaceShell.BindHost(_orderPlaceHost);
            _orderPlaceShell.SetFlyoutMenuOverlayMode(true);
            _orderPlaceShell.FlyoutMenuRequested += OnOrderPlaceMenuClicked;
        }

        private void OnOrderPageSizeChanged(object? sender, EventArgs e)
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            var tablet = Width <= 1280 || Height <= 800;
            // Tight page padding — flyout control is overlayed, not a left rail.
            MainContentGrid.Padding = tablet ? new Thickness(2) : new Thickness(4);
        }

        public OrderPlacementPageSimple(string tableNumber, int coverCount, string staffName, int staffId)
            : this()
        {
            InitializeOrderContext(tableNumber, coverCount, staffName, staffId, null, null);
        }

        public OrderPlacementPageSimple(string tableNumber, int coverCount, string staffName, int staffId, int? sessionId, string? existingOrderId)
            : this()
        {
            InitializeOrderContext(tableNumber, coverCount, staffName, staffId, sessionId, existingOrderId);
        }

        public OrderPlacementPageSimple(string existingOrderId)
            : this()
        {
            _pendingOrderId = existingOrderId;
            _currentOrder.Id = existingOrderId;
        }

        private void InitializeServices()
        {
            _databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
            _menuItemService = ServiceHelper.GetService<MenuItemService>() ?? new MenuItemService();
            _categoryService = ServiceHelper.GetService<MenuCategoryService>() ?? new MenuCategoryService();
            _mealDealService = ServiceHelper.GetService<MealDealService>() ?? new MealDealService();
            _tastingMenuService = ServiceHelper.GetService<TastingMenuService>() ?? new TastingMenuService();
            _orderService = ServiceHelper.GetService<OrderService>() ?? new OrderService();
            _tableSessionService = ServiceHelper.GetService<TableSessionService>() ?? new TableSessionService();
            _orderRoutingPrintService = ServiceHelper.GetService<OrderRoutingPrintService>() ?? new OrderRoutingPrintService();
            _kitchenRevisionService = ServiceHelper.GetService<KitchenOrderRevisionService>()
                ?? new KitchenOrderRevisionService(_databaseService);
            _orderNumberService = new OrderNumberService(_databaseService);
            _orderLifecycleRolloutService = ServiceHelper.GetService<OrderLifecycleRolloutService>() ?? new OrderLifecycleRolloutService(_databaseService);
            _cashDrawerService = ServiceHelper.GetService<CashDrawerService>()
                ?? new CashDrawerService(
                    _databaseService,
                    ServiceHelper.GetService<NetworkPrinterService>() ?? new NetworkPrinterService(),
                    ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance);
            _discountAuditService = ServiceHelper.GetService<DiscountAuditService>()
                ?? new DiscountAuditService(
                    _databaseService,
                    ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance);
            _loyaltyService = ServiceHelper.GetService<LoyaltyService>() ?? new LoyaltyService(_databaseService);
            _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
            _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
            _inactivityService = ServiceHelper.GetService<InactivityService>() ?? new InactivityService(_authService, _roleAccessService);
            _restaurantTableService = ServiceHelper.GetService<RestaurantTableService>() ?? new RestaurantTableService();
            _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
            _tableServiceChargeSettingsService = ServiceHelper.GetService<TableServiceChargeSettingsService>()
                ?? new TableServiceChargeSettingsService(_databaseService, _authService);
            _tableServiceChargeOrderAuditService = ServiceHelper.GetService<TableServiceChargeOrderAuditService>()
                ?? new TableServiceChargeOrderAuditService(_databaseService);
        }

        private async Task OnInitialLoadAsync()
        {
            var performance = PosPerformanceMonitor.BeginDataLoad("Order Entry");
            // Phase 1: skip fullscreen preloader when menu was warmed after login.
            var showLoader = !IsMenuCacheWarm;
            if (showLoader)
            {
                LoadingOverlay.Message = "Cooking up your data…";
                LoadingOverlay.IsLoading = true;
            }

            await Task.Yield();

            try
            {
                await RefreshRolloutConfigAsync();
                _currentUser = _authService.CurrentUser;
                EnsureCurrentOrderIdentity();
                await LoadDataAsync();
                await LoadExistingOrderIfNeededAsync();
                await ApplyServiceChargePolicyToNewOrderAsync();
                await EnsureLocalOrderDraftSavedAsync();
                PosPerformanceMonitor.MarkDataVisible(performance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Init error: {ex}");
                _ = ToastNotification.ShowAsync(
                    "Load failed",
                    "Order screen could not finish loading. Try again.",
                    NotificationType.Error,
                    2200);
            }
            finally
            {
                LoadingOverlay.IsLoading = false;
            }
        }

        private async Task EnsureLocalOrderDraftSavedAsync()
        {
            if (!string.IsNullOrWhiteSpace(_pendingOrderId) || _isLoadingPersistentOrder || _isFinalizingOrder)
            {
                return;
            }

            if (string.Equals(GetCanonicalOrderType(), "table", StringComparison.OrdinalIgnoreCase))
            {
                // Opening/seating a table creates only the table session. A real
                // order row is created by the first Kitchen Send or Payment action.
                await EnsureTableSessionContextAsync(skipOrderLink: true);
                _draftDirty = false;
                return;
            }

            _draftDirty = true;
            if (!await PersistDraftAsync(force: true, lifecycleOverride: LocalLifecycleState.Active))
            {
                await AppAlertService.ShowAlertAsync(
                    "Table Order Not Created",
                    "The table opened, but its order could not be created. Please close this screen and try the table again.");
            }
        }

        private async Task ApplyServiceChargePolicyToNewOrderAsync()
        {
            SyncCurrentOrderMode();
            if (_isDeliveryOrder || _isCollectionOrder)
            {
                if (!string.IsNullOrWhiteSpace(_pendingOrderId))
                {
                    return;
                }

                _currentOrder.ServiceChargePercent = 0m;
                _currentOrder.ServiceChargeClassification = null;
                _currentOrder.ServiceChargeStatus = TableServiceChargeStatus.NotConfigured;
                return;
            }

            var settings = await _tableServiceChargeSettingsService.GetAsync();
            var isExistingOrder = !string.IsNullOrWhiteSpace(_pendingOrderId);
            if (isExistingOrder)
            {
                var isModifiable = _currentOrder.Status is TableOrderStatus.Active or TableOrderStatus.Sent;
                if (!TableOrderFinancialPolicy.ShouldAdoptCurrentServiceCharge(
                        isTableOrder: true,
                        settingEnabled: settings.IsEnabled,
                        savedStatus: _currentOrder.ServiceChargeStatus,
                        savedPercentage: _currentOrder.ServiceChargePercent,
                        isOrderModifiable: isModifiable,
                        totalPaid: _currentOrder.TotalPaid))
                {
                    return;
                }
            }

            _currentOrder.ServiceChargePercent = settings.IsEnabled ? settings.Percentage : 0m;
            _currentOrder.ServiceChargeClassification = settings.IsEnabled ? ServiceChargeClassification.Optional : null;
            _currentOrder.ServiceChargeStatus = settings.IsEnabled
                ? TableServiceChargeStatus.Applied
                : TableServiceChargeStatus.NotConfigured;
            _currentOrder.RecalculateAll();
            UpdateDisplay();

            if (isExistingOrder)
            {
                _draftDirty = true;
                await PersistDraftAsync(force: true);
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _inactivityService.Start();
            _inactivityService.ResetActivity();
            _inactivityService.TrackPage(this);
            _idleDraftSaveRegistration ??= _inactivityService.RegisterBeforeIdleReturnHandler(SaveDraftBeforeIdleReturnAsync);
            SubscribeToLiveUpdates();
            await EnsurePageIsCurrentAsync();
        }

        private async Task SaveDraftBeforeIdleReturnAsync()
        {
            if (_isLoadingPersistentOrder || _isFinalizingOrder)
            {
                return;
            }

            await PersistDraftAsync(force: true);
        }

        public async Task<bool> CommitBeforeNavigationAsync()
        {
            if (_isFinalizingOrder || _isLoadingPersistentOrder)
            {
                return true;
            }

            if (IsUncommittedLocalTableOrder())
            {
                if (_currentOrder.Items.Count == 0)
                {
                    await ReleaseUncommittedTableAsync("empty_table_closed");
                    return true;
                }

                var discardDialog = new ModernConfirmDialog();
                discardDialog.SetConfirm(
                    "Items Not Sent",
                    "This table has items that have not been sent to the kitchen. Discard them and leave the table?",
                    "Discard & Leave",
                    "Stay Here",
                    string.Empty);
                if (!await discardDialog.ShowAsync())
                {
                    return false;
                }

                await ReleaseUncommittedTableAsync("unsent_basket_discarded");
                return true;
            }

            if (!_draftDirty)
            {
                return true;
            }

            await PersistDraftAsync(force: true);
            return true;
        }

        private async Task EnsurePageIsCurrentAsync()
        {
            if (_initialLoadTask == null)
            {
                _initialLoadTask = OnInitialLoadAsync();
                await _initialLoadTask;
                return;
            }

            await _initialLoadTask;

            // A pushed order page can reappear after a dialog or another terminal
            // changed the order. Refresh only the context that can be stale; the
            // cached menu remains instant unless it was explicitly invalidated.
            if (_cachedCategories == null || _cachedMenuItems == null)
            {
                await LoadDataAsync();
            }

            if (_draftDirty || _isLoadingPersistentOrder || _isFinalizingOrder || string.IsNullOrWhiteSpace(_pendingOrderId))
            {
                return;
            }

            try
            {
                var latest = await _orderService.GetOrderByExternalIdAsync(_pendingOrderId);
                if (latest != null && latest.UpdatedAt > _lastSavedAt)
                {
                    await ApplyLoadedOrderAsync(latest);
                    _hasLoadedPersistentOrder = true;
                    _draftDirty = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Resume refresh warning: {ex.Message}");
            }
        }

        private void InitializeOrderContext(string tableNumber, int coverCount, string staffName, int staffId, int? sessionId, string? existingOrderId)
        {
            _currentOrder.TableNumber = int.TryParse(tableNumber, out var parsedTableNumber) ? parsedTableNumber : 0;
            _currentOrder.CoverCount = coverCount;
            _currentOrder.StaffName = staffName;
            _currentOrder.StaffId = staffId;

            _tableSessionId = sessionId;
            _pendingOrderId = existingOrderId;
            EnsureCurrentOrderIdentity();
            UpdateDisplay();
        }

        public void SetCollectionOrderInfo(int customerId, string customerName, string customerPhone)
        {
            _isCollectionOrder = true;
            _isDeliveryOrder = false;
            _collectionCustomerId = customerId;
            _collectionCustomerName = customerName;
            _collectionCustomerPhone = customerPhone;
            _draftDirty = true;
            UpdateDisplay();
        }

        public void SetDeliveryOrderInfo(int customerId, string customerName, string customerPhone, string customerAddress)
        {
            SetDeliveryOrderInfo(customerId, customerName, customerPhone, customerAddress, 0m);
        }

        public void SetDeliveryOrderInfo(int customerId, string customerName, string customerPhone, string customerAddress, decimal deliveryFee)
        {
            _isDeliveryOrder = true;
            _isCollectionOrder = false;
            _deliveryCustomerId = customerId;
            _deliveryCustomerName = customerName;
            _deliveryCustomerPhone = customerPhone;
            _deliveryCustomerAddress = customerAddress;
            _currentOrder.DeliveryFee = Math.Max(0, deliveryFee);
            _draftDirty = true;
            UpdateDisplay();
        }

        private async Task RefreshRolloutConfigAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _isRolloutConfigLoaded)
            {
                return;
            }

            try
            {
                _rolloutConfig = await _orderLifecycleRolloutService.GetConfigAsync();
                _isRolloutConfigLoaded = true;
            }
            catch
            {
                _rolloutConfig = OrderLifecycleRolloutConfig.CreateDefault();
                _isRolloutConfigLoaded = true;
            }
        }

        private void EnsureCurrentOrderIdentity()
        {
            if (string.IsNullOrWhiteSpace(_currentOrder.Id))
            {
                _currentOrder.Id = !string.IsNullOrWhiteSpace(_pendingOrderId)
                    ? _pendingOrderId
                    : Guid.NewGuid().ToString("N");
            }

            if (_currentOrder.CreatedAt == default)
            {
                _currentOrder.CreatedAt = DateTime.Now;
            }

            if (_currentOrder.StartTime == default)
            {
                _currentOrder.StartTime = _currentOrder.CreatedAt;
            }
        }

        private async Task<(List<string> Critical, List<string> Informational)> CollectReadinessIssuesAsync()
        {
            var critical = new List<string>();
            var info = new List<string>();

            var routeIssues = await _orderRoutingPrintService.ValidateEnvironmentAsync();
            foreach (var issue in routeIssues)
            {
                if (issue.Code == "pdf_fallback_mode" || issue.Code == "single_printer_mode")
                {
                    info.Add(issue.Message);
                }
                else if (issue.Code == "missing_printer_ip")
                {
                    info.Add(issue.Message);
                }
                else
                {
                    critical.Add(issue.Message);
                }
            }

            return (critical, info);
        }

        private sealed class CategoryButtonModel
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public Color Color { get; set; }
            public MenuCategory Category { get; set; } = null!;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            LoadingOverlay.IsLoading = false;
            UnsubscribeFromLiveUpdates();
            _ = SaveAndPublishOnDisappearingAsync();

            _idleDraftSaveRegistration?.Dispose();
            _idleDraftSaveRegistration = null;
        }

        private async Task SaveAndPublishOnDisappearingAsync()
        {
            if (!_isFinalizingOrder && _draftDirty && !IsUncommittedLocalTableOrder())
            {
                await PersistDraftAsync(force: true);
            }

            // Subscribers are notified only after the final draft write completes.
            AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
        }

        private void SubscribeToLiveUpdates()
        {
            if (_isSubscribedToLiveUpdates)
            {
                return;
            }

            AppDataRefreshService.DataChanged += OnLiveOrderDataChanged;
            _isSubscribedToLiveUpdates = true;
        }

        private void UnsubscribeFromLiveUpdates()
        {
            if (!_isSubscribedToLiveUpdates)
            {
                return;
            }

            AppDataRefreshService.DataChanged -= OnLiveOrderDataChanged;
            _isSubscribedToLiveUpdates = false;
        }

        private async void OnLiveOrderDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (!e.HasKind(AppDataChangeKind.Orders) || e.IsFromCurrentTerminal)
            {
                return;
            }

            var currentOrderId = !string.IsNullOrWhiteSpace(_currentOrder.Id) ? _currentOrder.Id : _pendingOrderId;
            if (string.IsNullOrWhiteSpace(currentOrderId))
            {
                return;
            }

            try
            {
                var latest = await _orderService.GetOrderByExternalIdAsync(currentOrderId);
                if (latest == null || latest.UpdatedAt <= _lastSavedAt)
                {
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    // Phase 2: no toast — status line only when draft conflicts.
                    if (_isLoadingPersistentOrder || _isFinalizingOrder)
                    {
                        return;
                    }

                    if (_draftDirty)
                    {
                        UpdateSavedStatusLabel("Changed on another terminal", false, true);
                        return;
                    }

                    await ApplyLoadedOrderAsync(latest);
                    _hasLoadedPersistentOrder = true;
                    _draftDirty = false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Live order refresh warning: {ex.Message}");
            }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[OrderPlacement] Loading data...");
                _quickNotesCache.Clear();

                var useCache = _cachedCategories != null
                    && _cachedMenuItems != null
                    && _cachedMealDeals != null
                    && _cachedTastingMenus != null
                    && (DateTime.Now - _menuCacheUpdatedAt) < MenuCacheTtl;

                if (useCache)
                {
                    _allCategories = _cachedCategories!;
                    _allMenuItems = _cachedMenuItems!;
                    _activeMealDeals = _cachedMealDeals!;
                    _activeTastingMenus = _cachedTastingMenus!;
                }
                else
                {
                    // Keep DB/menu IO off the UI thread; only BuildCategories runs on main.
                    var categoryService = _categoryService;
                    var menuItemService = _menuItemService;
                    var mealDealService = _mealDealService;
                    var tastingMenuService = _tastingMenuService;

                    var snapshot = await Task.Run(async () =>
                    {
                        var categoriesTask = categoryService.GetAllCategoriesAsync();
                        var itemsTask = menuItemService.GetAllItemsAsync();
                        var dealsTask = mealDealService.GetActiveDealsAsync();
                        var tastingMenusTask = tastingMenuService.GetActiveAsync();
                        await Task.WhenAll(categoriesTask, itemsTask, dealsTask, tastingMenusTask);
                        return (
                            Categories: await categoriesTask,
                            Items: await itemsTask,
                            Deals: await dealsTask,
                            TastingMenus: await tastingMenusTask);
                    }).ConfigureAwait(true);

                    _allCategories = snapshot.Categories;
                    _allMenuItems = snapshot.Items;
                    _activeMealDeals = snapshot.Deals;
                    _activeTastingMenus = snapshot.TastingMenus;

                    _cachedCategories = _allCategories;
                    _cachedMenuItems = _allMenuItems;
                    _cachedMealDeals = _activeMealDeals;
                    _cachedTastingMenus = _activeTastingMenus;
                    _menuCacheUpdatedAt = DateTime.Now;
                }
                
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Loaded {_allCategories.Count} categories, {_allMenuItems.Count} items, {_activeMealDeals.Count} meal deals");

                // Warm cache + categories already published → skip full rebuild.
                if (useCache && _shellTopCategories.Count > 0 && _selectedCategory != null)
                {
                    PublishOrderPlaceSession();
                    return;
                }
                
                BuildCategories();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Load error: {ex}");
                throw;
            }
        }

        public static bool IsMenuCacheWarm =>
            _cachedCategories != null
            && _cachedMenuItems != null
            && _cachedMealDeals != null
            && _cachedTastingMenus != null
            && (DateTime.Now - _menuCacheUpdatedAt) < MenuCacheTtl;

        public static void InvalidateMenuCache()
        {
            _cachedCategories = null;
            _cachedMenuItems = null;
            _cachedMealDeals = null;
            _cachedTastingMenus = null;
            _menuCacheUpdatedAt = DateTime.MinValue;
        }

        /// <summary>
        /// Warms the static menu cache off the UI thread so the first table open is fast.
        /// </summary>
        public static async Task WarmMenuCacheAsync()
        {
            if (IsMenuCacheWarm)
            {
                return;
            }

            try
            {
                var categoryService = ServiceHelper.GetService<MenuCategoryService>() ?? new MenuCategoryService();
                var menuItemService = ServiceHelper.GetService<MenuItemService>() ?? new MenuItemService();
                var mealDealService = ServiceHelper.GetService<MealDealService>() ?? new MealDealService();
                var tastingMenuService = ServiceHelper.GetService<TastingMenuService>() ?? new TastingMenuService();

                var snapshot = await Task.Run(async () =>
                {
                    var categoriesTask = categoryService.GetAllCategoriesAsync();
                    var itemsTask = menuItemService.GetAllItemsAsync();
                    var dealsTask = mealDealService.GetActiveDealsAsync();
                    var tastingMenusTask = tastingMenuService.GetActiveAsync();
                    await Task.WhenAll(categoriesTask, itemsTask, dealsTask, tastingMenusTask);
                    return (
                        Categories: await categoriesTask,
                        Items: await itemsTask,
                        Deals: await dealsTask,
                        TastingMenus: await tastingMenusTask);
                }).ConfigureAwait(false);

                _cachedCategories = snapshot.Categories;
                _cachedMenuItems = snapshot.Items;
                _cachedMealDeals = snapshot.Deals;
                _cachedTastingMenus = snapshot.TastingMenus;
                _menuCacheUpdatedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] WarmMenuCacheAsync failed: {ex.Message}");
            }
        }

        private void BuildCategories()
        {
            var topCategories = _allCategories
                .Where(c => c.ParentId == null && c.Active)
                .OrderBy(c => c.DisplayOrder)
                .ToList();

            if (_activeMealDeals.Count > 0)
            {
                topCategories.Insert(0, CreateMealDealsCategory());
            }

            if (_activeTastingMenus.Count > 0)
            {
                topCategories.Insert(0, CreateTastingMenusCategory());
            }

            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Building {topCategories.Count} category chips");
            _shellTopCategories.Clear();
            _shellTopCategories.AddRange(topCategories);

            if (topCategories.Count > 0)
            {
                SelectCategory(topCategories.First());
            }
            else
            {
                _selectedCategory = null;
                _selectedSubCategory = null;
                _shellSelectedCategoryId = null;
                _shellSelectedSubcategoryId = null;
                _visibleSubcategories.Clear();
                _visibleProducts.Clear();
                PublishOrderPlaceSession();
            }
        }

        private void SyncCategoryChipSelection()
        {
            _shellSelectedCategoryId = _selectedCategory?.Id;
            PublishOrderPlaceSession();
        }

        private static MenuCategory CreateMealDealsCategory() => new()
        {
            Id = MealDeal.PosCategoryId,
            Name = "Meal Deals",
            Color = "#F59E0B",
            Active = true,
            DisplayOrder = -1
        };

        private static MenuCategory CreateTastingMenusCategory() => new()
        {
            Id = TastingMenu.PosCategoryId,
            Name = "Tasting Menus",
            Color = "#0EA5E9",
            Active = true,
            DisplayOrder = -2
        };

        private void SelectCategory(MenuCategory category)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Category selected: {category.Name}");

            _selectedCategory = category;
            _selectedSubCategory = null;
            _shellSelectedCategoryId = category.Id;
            _shellSelectedSubcategoryId = null;
            _visibleSubcategories.Clear();

            if (string.Equals(category.Id, MealDeal.PosCategoryId, StringComparison.Ordinal))
            {
                LoadMealDealsForOrder();
                return;
            }

            if (string.Equals(category.Id, TastingMenu.PosCategoryId, StringComparison.Ordinal))
            {
                LoadTastingMenusForOrder();
                return;
            }

            var subCategories = _allCategories
                .Where(c => c.ParentId == category.Id && c.Active)
                .OrderBy(c => c.DisplayOrder)
                .ToList();

            if (subCategories.Any())
            {
                BuildSubCategories(subCategories);

                if (CategoryHasVisibleItems(category.Id))
                {
                    LoadItemsForCategory(category.Id);
                }
                else
                {
                    SelectSubCategory(subCategories.First());
                }
            }
            else
            {
                LoadItemsForCategory(category.Id);
            }
        }

        private void BuildSubCategories(List<MenuCategory> subCategories)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Building {subCategories.Count} sub-category chips");

            _visibleSubcategories.Clear();
            if (_selectedCategory != null && CategoryHasVisibleItems(_selectedCategory.Id))
            {
                _visibleSubcategories.Add(_selectedCategory);
            }

            _visibleSubcategories.AddRange(subCategories);
            _shellSelectedSubcategoryId = _selectedSubCategory?.Id ?? _selectedCategory?.Id;
            PublishOrderPlaceSession();
        }

        private void SyncSubCategoryChipSelection()
        {
            _shellSelectedSubcategoryId = _selectedSubCategory?.Id ?? _selectedCategory?.Id;
            PublishOrderPlaceSession();
        }

        private void SelectSubCategory(MenuCategory subCategory)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Sub-category selected: {subCategory.Name}");

            _selectedSubCategory = string.Equals(subCategory.Id, _selectedCategory?.Id, StringComparison.Ordinal)
                ? null
                : subCategory;
            _shellSelectedSubcategoryId = subCategory.Id;
            LoadItemsForCategory(subCategory.Id);
        }

        private void LoadItemsForCategory(string categoryId)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Loading items for category ID: {categoryId}");

            var items = _allMenuItems
                .Where(i => i.CategoryId == categoryId)
                .Where(IsItemVisibleForCurrentOrderMode)
                .OrderBy(i => i.DisplayOrder)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Found {items.Count} items");
            _visibleProducts = items
                .Select(item => new OrderPlaceProductItem(
                    item.Id,
                    item.Name,
                    GetSafeEffectivePrice(item),
                    IsAvailable: true))
                .ToList();
            PublishOrderPlaceSession();
        }

        private void LoadMealDealsForOrder()
        {
            var deals = _activeMealDeals
                .OrderBy(d => d.DisplayOrder)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _visibleSubcategories.Clear();
            _visibleProducts = deals
                .Select(deal => new OrderPlaceProductItem(
                    $"md:{deal.Id}",
                    deal.Name,
                    deal.Price,
                    Badge: "DEAL",
                    PriceText: $"£{deal.Price:F2}",
                    IsAvailable: true))
                .ToList();
            PublishOrderPlaceSession();
        }

        private OrderPlaceProductCard CreateMealDealCard(MealDeal deal)
        {
            var card = new OrderPlaceProductCard
            {
                Name = deal.Name,
                Price = deal.Price,
                Badge = "DEAL",
                PriceText = $"£{deal.Price:F2}"
            };
            card.Tapped += async (_, _) => await OnMealDealTappedAsync(deal);
            return card;
        }

        private void LoadTastingMenusForOrder()
        {
            var menus = _activeTastingMenus
                .OrderBy(menu => menu.DisplayOrder)
                .ThenBy(menu => menu.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _visibleSubcategories.Clear();
            _visibleProducts = menus
                .Select(menu => new OrderPlaceProductItem(
                    $"tm:{menu.Id}",
                    menu.Name,
                    0m,
                    Badge: "TASTING",
                    PriceText: menu.Options.Count == 0 ? "No prices" : menu.OptionsDisplay,
                    IsAvailable: true))
                .ToList();
            PublishOrderPlaceSession();
        }

        private OrderPlaceProductCard CreateTastingMenuCard(TastingMenu menu)
        {
            var priceText = menu.Options.Count == 0 ? "No prices" : menu.OptionsDisplay;
            var card = new OrderPlaceProductCard
            {
                Name = menu.Name,
                Badge = "TASTING",
                PriceText = priceText
            };
            card.Tapped += async (_, _) => await OnTastingMenuTappedAsync(menu);
            return card;
        }

        private async Task OnTastingMenuTappedAsync(TastingMenu menu)
        {
            if (menu.Options.Count == 0 || menu.Courses.Count == 0)
            {
                await AppAlertService.ShowAlertAsync("Tasting Menu", "This tasting menu has no price options or courses configured.");
                return;
            }

            var option = await PromptTastingMenuOptionAsync(menu);
            if (option == null)
            {
                return;
            }

            var selections = new List<(TastingMenuCourse Course, TastingMenuChoice Choice)>();
            foreach (var course in menu.Courses.OrderBy(course => course.CourseNumber))
            {
                if (option.CourseCount > 0 && course.CourseNumber > option.CourseCount)
                {
                    continue;
                }

                selections.Add((course, new TastingMenuChoice
                {
                    Id = course.Id,
                    Name = course.Name,
                    SortOrder = 0
                }));
            }

            await AddTastingMenuToOrderAsync(menu, option, selections);
        }

        private async Task<TastingMenuOption?> PromptTastingMenuOptionAsync(TastingMenu menu)
        {
            var orderedOptions = menu.Options.OrderBy(option => option.SortOrder).ToList();
            if (orderedOptions.Count == 0)
            {
                return null;
            }

            var option = orderedOptions[0];
            if (orderedOptions.Count > 1)
            {
                var labels = orderedOptions
                    .Select(candidate => $"{candidate.DisplayName} - £{candidate.Price:F2}")
                    .ToList();
                var optionDialog = new ModernActionSheetDialog();
                optionDialog.SetActionSheet($"Select package for {menu.Name}", labels, "T", "#0EA5E9");
                var selected = await optionDialog.ShowAsync();
                var index = selected == null ? -1 : labels.IndexOf(selected);
                if (index < 0)
                {
                    return null;
                }

                option = orderedOptions[index];
            }

            var confirmation = new TastingMenuConfirmationDialog();
            return await confirmation.ShowAsync(menu, option) ? option : null;
        }

        private async Task AddTastingMenuToOrderAsync(TastingMenu menu, TastingMenuOption option, List<(TastingMenuCourse Course, TastingMenuChoice Choice)> selections)
        {
            SyncCurrentOrderMode();
            var menuItemId = TastingMenuNotesHelper.BuildOrderMenuItemId(menu.Id, option.Id);
            var notes = TastingMenuNotesHelper.FormatPackage(
                option,
                menu.FoodPrintGroupId,
                menu.WinePrintGroupId);
            var wasEmpty = _currentOrder.Items.Count == 0;
            var packageItemId = Guid.NewGuid().ToString();

            var packageItem = new TableOrderItem
            {
                Id = packageItemId,
                MenuItemId = menuItemId,
                Name = menu.Name,
                VariantId = option.Id,
                VariantName = option.DisplayName,
                DisplayName = $"{menu.Name} ({option.DisplayName})",
                Quantity = 1,
                UnitPrice = option.Price,
                VatCategory = string.IsNullOrWhiteSpace(menu.VatCategory) ? "HotFood" : menu.VatCategory,
                PrintGroupId = menu.FoodPrintGroupId,
                Notes = notes,
                SendStatus = ItemSendStatus.NotSent,
                CreatedAt = DateTime.Now
            };
            _currentOrder.Items.Add(packageItem);

            foreach (var selection in selections.OrderBy(selection => selection.Course.CourseNumber))
            {
                var course = selection.Course;
                var choice = selection.Choice;
                _currentOrder.Items.Add(new TableOrderItem
                {
                    Id = Guid.NewGuid().ToString(),
                    MenuItemId = TastingMenuNotesHelper.BuildOrderCourseMenuItemId(menu.Id, course.Id),
                    VariantId = packageItemId,
                    VariantName = $"{course.CourseNumber}/{option.CourseCount}",
                    DisplayName = $"Course {course.CourseNumber}: {course.Name}",
                    Name = course.Name,
                    Quantity = 1,
                    UnitPrice = 0m,
                    VatCategory = string.IsNullOrWhiteSpace(course.VatCategory) ? "HotFood" : course.VatCategory,
                    PrintGroupId = !string.IsNullOrWhiteSpace(menu.FoodPrintGroupId)
                        ? menu.FoodPrintGroupId
                        : choice.PrintGroupId,
                    Notes = !string.IsNullOrWhiteSpace(course.WineName)
                        ? $"Wine pairing: {course.WineName.Trim()}"
                        : string.Equals(choice.Name, course.Name, StringComparison.OrdinalIgnoreCase) ? null : choice.Name,
                    CourseType = $"Tasting Course {course.CourseNumber}",
                    // Child courses are held until the waiter explicitly fires one.
                    SendStatus = ItemSendStatus.Sent,
                    CreatedAt = DateTime.Now
                });
            }

            _currentOrder.RecalculateAll();
            RefreshOrderItems();
            await MarkCurrentOrderChangedAsync();
            if (wasEmpty && _currentOrder.Items.Count > 0)
            {
                ScheduleDraftAutosave(immediate: true);
            }
        }

        private async Task OnMealDealTappedAsync(MealDeal deal)
        {
            try
            {
                if (deal.Choices.Count == 0)
                {
                    await AppAlertService.ShowAlertAsync("Meal Deal", "This deal has no choices configured.");
                    return;
                }

                var picker = new MealDealPickerDialog(deal);
                var selections = await picker.ShowAsync(this);
                if (selections == null || selections.Count == 0)
                {
                    return;
                }

                await AddMealDealToOrderAsync(deal, selections);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MealDeal] Could not add deal: {ex}");
                await AppAlertService.ShowAlertAsync("Meal Deal Error", $"The deal could not be added: {ex.Message}");
            }
        }

        private async Task AddMealDealToOrderAsync(MealDeal deal, List<string> selections)
        {
            SyncCurrentOrderMode();
            var menuItemId = MealDealNotesHelper.BuildOrderMenuItemId(deal.Id);
            var notes = MealDealNotesHelper.FormatSelections(selections);
            var wasEmpty = _currentOrder.Items.Count == 0;

            var existingItem = _currentOrder.Items.FirstOrDefault(i =>
                i.MenuItemId == menuItemId &&
                string.Equals(NormalizeOrderItemNote(i.Notes), notes, StringComparison.OrdinalIgnoreCase) &&
                i.SendStatus == ItemSendStatus.NotSent);

            if (existingItem != null)
            {
                existingItem.Quantity++;
            }
            else
            {
                _currentOrder.Items.Add(new TableOrderItem
                {
                    Id = Guid.NewGuid().ToString(),
                    MenuItemId = menuItemId,
                    Name = deal.Name,
                    DisplayName = deal.Name,
                    CategoryColor = string.IsNullOrWhiteSpace(deal.Color) ? "#F59E0B" : deal.Color,
                    Quantity = 1,
                    UnitPrice = deal.Price,
                    VatCategory = string.IsNullOrWhiteSpace(deal.VatCategory) ? "HotFood" : deal.VatCategory,
                    Notes = notes,
                    SendStatus = ItemSendStatus.NotSent,
                    CreatedAt = DateTime.Now
                });
            }

            _currentOrder.RecalculateAll();
            RefreshOrderItems();
            await MarkCurrentOrderChangedAsync();
            if (wasEmpty && _currentOrder.Items.Count > 0)
            {
                ScheduleDraftAutosave(immediate: true);
            }
        }

        private static bool IsMealDealOrderItem(TableOrderItem item) =>
            item.MenuItemId.StartsWith(MealDeal.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase);

        private static bool IsTastingMenuOrderItem(TableOrderItem item) =>
            item.MenuItemId.StartsWith(TastingMenu.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase);

        private static bool IsTastingMenuCourseItem(TableOrderItem item) =>
            TastingMenuNotesHelper.IsOrderCourseItem(item.MenuItemId);

        private List<TableOrderItem> GetTastingMenuCourses(TableOrderItem package) =>
            _currentOrder.Items
                .Where(IsTastingMenuCourseItem)
                .Where(course => string.Equals(course.VariantId, package.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(TastingCourseProgressDialog.GetCourseNumber)
                .ToList();

        private OrderPlaceProductCard CreateItemCard(FoodMenuItem item)
        {
            var displayPrice = GetSafeEffectivePrice(item);
            var card = new OrderPlaceProductCard
            {
                Name = item.Name,
                Price = displayPrice
            };
            card.Tapped += async (_, _) => await OnMenuItemTappedAsync(item);
            return card;
        }

        private async Task OnMenuItemTappedAsync(FoodMenuItem item)
        {
            var (variantSelected, selectedVariant) = await PromptVariantSelectionAsync(item);
            if (!variantSelected)
            {
                return;
            }

            var (shouldAdd, selectedNote) = await PromptQuickNoteSelectionAsync(item);
            if (!shouldAdd)
            {
                return;
            }

            var selectedAddons = await PromptAddonSelectionAsync(item);
            if (selectedAddons == null)
            {
                return;
            }

            await AddItemToOrderAsync(item, selectedNote, selectedAddons, selectedVariant);
        }

        private async Task<(bool ShouldAdd, MenuItemVariant? Variant)> PromptVariantSelectionAsync(FoodMenuItem item)
        {
            var activeVariants = item.Variants?
                .Where(variant => variant.Active)
                .OrderBy(variant => variant.DisplayOrder)
                .ThenBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<MenuItemVariant>();

            if (activeVariants.Count == 0)
            {
                return (true, null);
            }

            var dialog = new VariantSelectionDialog();
            var selectedVariant = await dialog.ShowAsync(item.Name, activeVariants);
            return selectedVariant != null ? (true, selectedVariant) : (false, null);
        }

        private async Task<(bool ShouldAdd, string? SelectedNote)> PromptQuickNoteSelectionAsync(FoodMenuItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                return (true, null);
            }

            var quickNotes = await GetQuickNotesForItemAsync(item.Id);
            if (quickNotes.Count == 0)
            {
                return (true, null);
            }

            var noteDialog = new QuickNoteSelectionDialog();
            var selected = await noteDialog.ShowAsync(item.Name, quickNotes);
            if (selected.Kind == QuickNoteSelectionKind.Cancelled)
            {
                return (false, null);
            }

            if (selected.Kind == QuickNoteSelectionKind.NoNote)
            {
                return (true, null);
            }

            if (selected.Kind == QuickNoteSelectionKind.CustomNote)
            {
                var dialog = new StyledPromptDialog();
                dialog.SetDialog(
                    "Custom Note",
                    $"Enter note for {item.Name}:",
                    "e.g., No onions, extra spicy",
                    null,
                    "",
                    true);

                var customNote = await dialog.ShowAsync();
                if (customNote == null)
                {
                    return (false, null);
                }

                var normalizedCustomNote = NormalizeOrderItemNote(customNote);
                return (true, normalizedCustomNote);
            }

            return (true, NormalizeOrderItemNote(selected.NoteText));
        }

        private async Task<List<SelectedAddon>?> PromptAddonSelectionAsync(FoodMenuItem item)
        {
            if (item.Addons == null || item.Addons.Count == 0)
            {
                return new List<SelectedAddon>();
            }

            var dialog = new AddonSelectionDialog();
            return await dialog.ShowAsync(item.Name, item.Addons);
        }

        private static bool SelectedAddonsMatch(IEnumerable<SelectedAddon> first, IEnumerable<SelectedAddon> second)
        {
            var firstList = first
                .Select(addon => new { Id = addon.Id?.Trim() ?? string.Empty, Name = addon.Name.Trim(), addon.Price })
                .OrderBy(addon => string.IsNullOrWhiteSpace(addon.Id) ? addon.Name : addon.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(addon => addon.Price)
                .ToList();

            var secondList = second
                .Select(addon => new { Id = addon.Id?.Trim() ?? string.Empty, Name = addon.Name.Trim(), addon.Price })
                .OrderBy(addon => string.IsNullOrWhiteSpace(addon.Id) ? addon.Name : addon.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(addon => addon.Price)
                .ToList();

            if (firstList.Count != secondList.Count)
            {
                return false;
            }

            for (var i = 0; i < firstList.Count; i++)
            {
                var firstAddon = firstList[i];
                var secondAddon = secondList[i];

                if (!string.Equals(firstAddon.Id, secondAddon.Id, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(firstAddon.Name, secondAddon.Name, StringComparison.OrdinalIgnoreCase) ||
                    firstAddon.Price != secondAddon.Price)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<List<MenuItemQuickNote>> GetQuickNotesForItemAsync(string menuItemId)
        {
            if (_quickNotesCache.TryGetValue(menuItemId, out var cached))
            {
                return cached;
            }

            var loaded = await _menuItemService.GetQuickNotesAsync(menuItemId);
            var activeNotes = loaded
                .Where(n => n.Active && !string.IsNullOrWhiteSpace(n.NoteText))
                .OrderBy(n => n.DisplayOrder)
                .Take(6)
                .ToList();

            _quickNotesCache[menuItemId] = activeNotes;
            return activeNotes;
        }

        private static string? NormalizeOrderItemNote(string? note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return null;
            }

            return note.Trim();
        }

        private async Task LoadExistingOrderIfNeededAsync()
        {
            if (!_rolloutConfig.EnableResumePath)
            {
                return;
            }

            if (_hasLoadedPersistentOrder || _isLoadingPersistentOrder)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_pendingOrderId) && !_tableSessionId.HasValue)
            {
                return;
            }

            _isLoadingPersistentOrder = true;
            try
            {
                Order? loadedOrder = null;

                if (!string.IsNullOrWhiteSpace(_pendingOrderId))
                {
                    for (var attempt = 1; attempt <= 3 && loadedOrder == null; attempt++)
                    {
                        loadedOrder = await _orderService.GetOrderByExternalIdAsync(_pendingOrderId);
                        if (loadedOrder == null && attempt < 3)
                        {
                            await Task.Delay(100 * attempt);
                        }
                    }

                    if (!IsUsablePersistentOrder(loadedOrder))
                    {
                        AppDiagnostics.Log($"[OrderPlacement] Rejected stale order '{_pendingOrderId}' before opening table {_currentOrder.TableNumber}.");
                        ActiveTableOrderCacheService.Remove(_pendingOrderId, _tableSessionId, _currentOrder.TableNumber.ToString(CultureInfo.InvariantCulture));
                        _pendingOrderId = null;
                        _currentOrder.Id = Guid.NewGuid().ToString("N");
                        _lastSavedAt = DateTime.MinValue;
                        loadedOrder = null;
                    }
                }

                if (loadedOrder == null && _tableSessionId.HasValue)
                {
                    loadedOrder = await _orderService.GetOpenOrderByTableSessionIdAsync(_tableSessionId.Value);
                    if (!IsUsablePersistentOrder(loadedOrder))
                    {
                        loadedOrder = null;
                    }
                }

                if (loadedOrder == null)
                {
                    _hasLoadedPersistentOrder = true;
                    return;
                }

                await ApplyLoadedOrderAsync(loadedOrder);
                _hasLoadedPersistentOrder = true;

                // Phase 6: Smart Prompts - Partial payment on return
                if (_currentOrder.Status == TableOrderStatus.Partial || loadedOrder.LocalLifecycleState == LocalLifecycleState.PaymentPartial)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _ = ToastNotification.ShowAsync(
                            "Partial payment",
                            "This order has a partial payment recorded.",
                            NotificationType.Info,
                            1600);
                    });
                }
            }
            finally
            {
                _isLoadingPersistentOrder = false;
            }
        }

        private async Task ApplyLoadedOrderAsync(Order loadedOrder)
        {
            _currentOrder.Id = loadedOrder.OrderId;
            _pendingOrderId = loadedOrder.OrderId;
            _orderCloudOrderId = string.Equals(loadedOrder.SourceChannel, "web", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(loadedOrder.CloudOrderId) ? loadedOrder.OrderId : loadedOrder.CloudOrderId)
                : loadedOrder.CloudOrderId;
            var resolvedSessionId = loadedOrder.TableSessionId ?? _tableSessionId;
            _tableSessionId = resolvedSessionId;
            _currentOrder.OrderNumber = loadedOrder.OrderNumber;
            if (resolvedSessionId.HasValue)
            {
                var activeSession = await _tableSessionService.GetSessionByIdAsync(resolvedSessionId.Value);
                if (activeSession != null)
                {
                    var table = await _restaurantTableService.GetTableByIdAsync(activeSession.TableId);
                    if (table != null && int.TryParse(table.TableNumber, out var resolvedTableNumber))
                    {
                        _currentOrder.TableNumber = resolvedTableNumber;
                    }
                }
            }

            _currentOrder.TableNumber = _currentOrder.TableNumber > 0 ? _currentOrder.TableNumber : 1;
            _currentOrder.CoverCount = _currentOrder.CoverCount > 0 ? _currentOrder.CoverCount : 1;
            _currentOrder.StaffName = string.IsNullOrWhiteSpace(_currentOrder.StaffName) ? "Staff" : _currentOrder.StaffName;
            _currentOrder.StaffId = _currentOrder.StaffId == 0 ? 1 : _currentOrder.StaffId;
            _currentOrder.Notes = loadedOrder.SpecialInstructions;
            _currentOrder.Status = loadedOrder.LocalLifecycleState switch
            {
                LocalLifecycleState.Paid => TableOrderStatus.Paid,
                LocalLifecycleState.Voided => TableOrderStatus.Voided,
                LocalLifecycleState.SentFull or LocalLifecycleState.SentPartial => TableOrderStatus.Sent,
                LocalLifecycleState.PaymentPartial => TableOrderStatus.Partial,
                _ => TableOrderStatus.Active
            };
            _currentOrder.StartTime = loadedOrder.CreatedAt == default ? DateTime.Now : loadedOrder.CreatedAt;
            _currentOrder.CreatedAt = loadedOrder.CreatedAt == default ? DateTime.Now : loadedOrder.CreatedAt;
            _currentOrder.UpdatedAt = loadedOrder.UpdatedAt == default ? DateTime.Now : loadedOrder.UpdatedAt;
            _currentOrder.OrderMode = IsTableOrderType(loadedOrder.OrderType) ? "dine_in" : "takeaway";
            _currentOrder.Discount = loadedOrder.DiscountAmount;
            _currentOrder.DeliveryFee = loadedOrder.DeliveryFee;
            _currentOrder.ServiceChargePercent = loadedOrder.ServiceChargePercentage;
            _currentOrder.ServiceChargeClassification = ParseServiceChargeClassification(loadedOrder.ServiceChargeClassification);
            _currentOrder.ServiceChargeStatus = ParseServiceChargeStatus(loadedOrder.ServiceChargeStatus);
            _currentOrder.ServiceChargeRemovalReason = loadedOrder.ServiceChargeRemovalReason;
            _currentOrder.ServiceChargeRemovedByUserId = loadedOrder.ServiceChargeRemovedByUserId;
            _currentOrder.ServiceChargeRemovedByName = loadedOrder.ServiceChargeRemovedByName;
            _currentOrder.ServiceChargeApprovedByUserId = loadedOrder.ServiceChargeApprovedByUserId;
            _currentOrder.ServiceChargeApprovedByName = loadedOrder.ServiceChargeApprovedByName;
            _currentOrder.ServiceChargeRemovedAt = loadedOrder.ServiceChargeRemovedAt;
            _currentOrder.Items.Clear();
            _currentOrder.Payments.Clear();

            var payments = await _orderService.GetOrderPaymentsAsync(loadedOrder.Id);
            foreach (var payment in payments.Where(payment =>
                         string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase)))
            {
                _currentOrder.Payments.Add(new TableOrderPayment
                {
                    OrderId = _currentOrder.Id,
                    Method = ParsePaymentMethodType(payment.PaymentMethod),
                    Amount = payment.Amount,
                    AmountReceived = GetPaymentMetadataAmount(payment.MetadataJson, "amountReceived", payment.Amount),
                    Change = GetPaymentMetadataAmount(payment.MetadataJson, "change", 0m),
                    TipAmount = payment.TipAmount,
                    Reference = payment.Reference,
                    CreatedAt = payment.CreatedAt,
                    StaffName = payment.CreatedBy ?? string.Empty
                });
            }

            var latestSendTracking = await _orderService.GetLatestSendTrackingAsync(loadedOrder.Id);
            var latestTrackingByItemId = latestSendTracking
                .GroupBy(tracking => tracking.OrderItemDbId)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var item in loadedOrder.Items)
            {
                latestTrackingByItemId.TryGetValue(item.Id, out var itemTracking);

                _currentOrder.Items.Add(new TableOrderItem
                {
                    Id = !string.IsNullOrWhiteSpace(item.ClientItemId)
                        ? item.ClientItemId!
                        : $"db-{item.Id}",
                    DatabaseId = item.Id,
                    OrderId = _currentOrder.Id,
                    MenuItemId = item.MenuItemId ?? string.Empty,
                    VariantId = item.VariantId,
                    VariantName = item.VariantName,
                    DisplayName = item.DisplayName,
                    Name = item.ItemName,
                    Quantity = item.Quantity,
                    UnitPrice = item.ItemPrice ?? 0m,
                    Notes = item.SpecialInstructions,
                    PrintGroupId = !string.IsNullOrWhiteSpace(item.PrintGroupId)
                        ? item.PrintGroupId
                        : _allMenuItems.FirstOrDefault(menuItem =>
                            string.Equals(menuItem.Id, item.MenuItemId, StringComparison.OrdinalIgnoreCase))?.PrintGroupId,
                    PrintInRed = item.PrintInRed,
                    CourseType = !string.IsNullOrWhiteSpace(item.CourseType)
                        ? item.CourseType
                        : ResolveCourseType(item.MenuItemId),
                    FiredAt = item.FiredAt,
                    FiredBy = item.FiredBy,
                    SendStatus = itemTracking?.SendStatus?.ToLowerInvariant() switch
                    {
                        "printed" or "sent" => ItemSendStatus.Sent,
                        "failed" => ItemSendStatus.Failed,
                        _ => loadedOrder.LocalLifecycleState is LocalLifecycleState.SentPartial or LocalLifecycleState.SentFull or LocalLifecycleState.PaymentPartial or LocalLifecycleState.Paid
                            ? ItemSendStatus.Sent
                            : ItemSendStatus.NotSent
                    },
                    FailureReason = itemTracking?.FailureReason,
                    SentAt = itemTracking?.PrintedAt ?? itemTracking?.SentAt,
                    CreatedAt = loadedOrder.CreatedAt == default ? DateTime.Now : loadedOrder.CreatedAt,
                    SelectedAddons = new ObservableCollection<SelectedAddon>(
                        item.Addons.Select(addon => new SelectedAddon
                        {
                            Id = addon.AddonId,
                            Name = addon.AddonName,
                            Price = addon.AddonPrice ?? 0m
                        }))
                });
            }

            _persistentOrderNumber = loadedOrder.OrderNumber;
            _orderSourceChannel = string.Equals(loadedOrder.SourceChannel, "web", StringComparison.OrdinalIgnoreCase) ? "web" : "local";
            _requestedPaymentMethod = string.IsNullOrWhiteSpace(loadedOrder.PaymentMethod)
                ? null
                : loadedOrder.PaymentMethod.Trim();
            _lastSavedAt = loadedOrder.UpdatedAt == default ? DateTime.Now : loadedOrder.UpdatedAt;
            _hasShownConcurrencyConflict = false;
            ActiveTableOrderCacheService.Upsert(
                loadedOrder.OrderId,
                _tableSessionId,
                _currentOrder.TableNumber.ToString(),
                _lastSavedAt,
                loadedOrder.IsOpen);
            _currentOrder.RecalculateAll();
            _currentOrder.TipAmount = Math.Max(0m, loadedOrder.TotalAmount - _currentOrder.Total);
            SyncCurrentOrderMode();
            RefreshOrderItems();
            UpdateSavedStatusLabel();
            ApplyLoadedOrderContext(loadedOrder);
            UpdateDisplay();
            await _kitchenRevisionService.EnsureBaselineAsync(loadedOrder.Id, _currentOrder);
        }

        private async Task EnsureTableSessionContextAsync(bool skipOrderLink = false, bool allowSessionOpen = true)
        {
            if (!string.Equals(GetCanonicalOrderType(), "table", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                EnsureCurrentOrderIdentity();
                if (!_tableSessionId.HasValue)
                {
                    var tableNumberText = _currentOrder.TableNumber.ToString();
                    var table = (await _restaurantTableService.GetAllTablesAsync())
                        .FirstOrDefault(t => string.Equals(t.TableNumber?.Trim(), tableNumberText.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (table == null)
                    {
                        return;
                    }

                    var activeSession = await _tableSessionService.GetActiveSessionByTableIdAsync(table.Id);
                    if (activeSession != null)
                    {
                        _tableSessionId = activeSession.Id;
                    }
                    else
                    {
                        if (!allowSessionOpen)
                        {
                            return;
                        }

                        var openResult = await _tableSessionService.OpenTableWithSessionAsync(table.Id, Math.Max(_currentOrder.CoverCount, 1));
                        if (!openResult.success || !openResult.sessionId.HasValue)
                        {
                            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Could not ensure table session: {openResult.message}");
                            return;
                        }

                        _tableSessionId = openResult.sessionId.Value;
                    }
                }

                // Skip link if called from background context (already linked synchronously before background tasks)
                if (!skipOrderLink && _tableSessionId.HasValue)
                {
                    await LinkCurrentOrderToTableSessionAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] EnsureTableSessionContext warning: {ex.Message}");
            }
        }

        private async Task<bool> LinkCurrentOrderToTableSessionAsync()
        {
            if (!string.Equals(GetCanonicalOrderType(), "table", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            EnsureCurrentOrderIdentity();
            if (!_tableSessionId.HasValue || string.IsNullOrWhiteSpace(_currentOrder.Id))
            {
                AppDiagnostics.Log($"[OrderPlacement] Cannot link table order. Session={_tableSessionId?.ToString() ?? "missing"}, Order={_currentOrder.Id ?? "missing"}.");
                return false;
            }

            string? lastMessage = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var result = await _tableSessionService.LinkOrderToSessionAsync(_tableSessionId.Value, _currentOrder.Id);
                if (result.success)
                {
                    AppDiagnostics.Log($"[OrderPlacement] Linked order {_currentOrder.Id} to table session {_tableSessionId.Value}.");
                    return true;
                }

                lastMessage = result.message;
                if (attempt < 2)
                {
                    await Task.Delay(120);
                }
            }

            AppDiagnostics.Log($"[OrderPlacement] Failed to link order {_currentOrder.Id} to table session {_tableSessionId.Value}: {lastMessage}");
            return false;
        }

        private void ApplyLoadedOrderContext(Order loadedOrder)
        {
            var orderType = (loadedOrder.OrderType ?? string.Empty).Trim().ToLowerInvariant();

            if (orderType is "delivery" or "del")
            {
                _isCollectionOrder = false;
                _isDeliveryOrder = true;
                _deliveryCustomerName = loadedOrder.CustomerName;
                _deliveryCustomerPhone = loadedOrder.CustomerPhone ?? string.Empty;
                _deliveryCustomerAddress = loadedOrder.CustomerAddress ?? string.Empty;
            }
            else if (orderType is "pickup" or "collection" or "col" or "takeaway")
            {
                _isCollectionOrder = true;
                _isDeliveryOrder = false;
                _collectionCustomerName = loadedOrder.CustomerName;
                _collectionCustomerPhone = loadedOrder.CustomerPhone ?? string.Empty;
            }
            else
            {
                _isCollectionOrder = false;
                _isDeliveryOrder = false;
            }

            SyncCurrentOrderMode();
            UpdateDisplay();
        }

        private void UpdateSavedStatusLabel(string? overrideText = null, bool isSaving = false, bool isFailed = false)
        {
            // Autosave state is tracked internally; header sync labels were intentionally removed from UI.
        }

        private async Task QueueDraftAutosaveAsync(bool immediate = false)
        {
            if (!_rolloutConfig.EnableLifecycleWrites || !_rolloutConfig.IsDraftSaveEnabledForOrderType(GetCanonicalOrderType()))
            {
                return;
            }

            if (_isLoadingPersistentOrder || _isFinalizingOrder)
            {
                return;
            }

            _draftDirty = true;
            UpdateSavedStatusLabel(isSaving: true);

            _draftSaveDelayCts?.Cancel();
            _draftSaveDelayCts?.Dispose();
            _draftSaveDelayCts = new CancellationTokenSource();

            try
            {
                if (!immediate)
                {
                    // Coalesce rapid adds — one save after the burst, not per tap.
                    await Task.Delay(200, _draftSaveDelayCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_draftSaveDelayCts.IsCancellationRequested || _isFinalizingOrder)
            {
                return;
            }

            var saved = await PersistDraftAsync(force: true);
            if (!saved && _draftDirty && !IsUncommittedLocalTableOrder())
            {
                _ = ToastNotification.ShowAsync(
                    "Save delayed",
                    "Order line is on screen; draft save will retry.",
                    NotificationType.Warning,
                    1600);
            }
        }

        private void ScheduleDraftAutosave(bool immediate = false)
        {
            _ = QueueDraftAutosaveAsync(immediate);
        }

        private async Task<bool> PersistDraftAsync(bool force = false, LocalLifecycleState? lifecycleOverride = null, string? voidReason = null, string? voidedBy = null, DateTime? paidAt = null, DateTime? voidedAt = null)
        {
            if (!_rolloutConfig.EnableLifecycleWrites)
            {
                return false;
            }

            var lifecycleState = lifecycleOverride ?? GetDraftLifecycleState();

            // A newly opened local table is only a table session plus an in-memory
            // basket. Autosave/navigation must never manufacture an orders row.
            // The explicit Kitchen Send or Payment path temporarily opens this gate.
            if (IsUncommittedLocalTableOrder() && !_isExplicitOrderCommit)
            {
                _draftDirty = _currentOrder.Items.Count > 0;
                UpdateSavedStatusLabel(_draftDirty ? "Not sent" : null);
                return true;
            }

            var isDraftState = lifecycleState is LocalLifecycleState.Draft or LocalLifecycleState.Active;
            if (isDraftState && !_rolloutConfig.IsDraftSaveEnabledForOrderType(GetCanonicalOrderType()))
            {
                return false;
            }

            // EnableSendDurability only gates durable send-batch tracking, not writing
            // sent_partial/sent_full (needed for Live Order after Send to Kitchen).

            if ((_isLoadingPersistentOrder || _isFinalizingOrder && lifecycleOverride == null) || (lifecycleOverride == null && !_draftDirty && !force))
            {
                return false;
            }

            using var idleGuard = _inactivityService.BeginCriticalActivity();
            await _draftSaveLock.WaitAsync();
            try
            {
                EnsureCurrentOrderIdentity();
                var order = await BuildPersistentOrderSnapshotAsync(lifecycleState, voidReason, voidedBy, paidAt, voidedAt);
                if (order == null)
                {
                    return false;
                }

                const int maxAttempts = 3;
                string? lastFailureMessage = null;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var result = await _orderService.SaveOrderAsync(order);
                    if (result.Success)
                    {
                        _pendingOrderId = order.OrderId;
                        _persistentOrderNumber = order.OrderNumber;
                        _lastSavedAt = order.UpdatedAt == default ? DateTime.Now : order.UpdatedAt;

                        if (string.Equals(GetCanonicalOrderType(), "table", StringComparison.OrdinalIgnoreCase)
                            && !await LinkCurrentOrderToTableSessionAsync())
                        {
                            _draftDirty = true;
                            UpdateSavedStatusLabel("Table link failed", false, true);
                            return false;
                        }

                        _draftDirty = false;
                        _hasShownConcurrencyConflict = false;
                        ActiveTableOrderCacheService.Upsert(
                            order.OrderId,
                            _tableSessionId,
                            _currentOrder.TableNumber.ToString(),
                            _lastSavedAt,
                            order.IsOpen);
                        UpdateSavedStatusLabel();
                        return true;
                    }

                    lastFailureMessage = result.Message;
                    if (IsConcurrencyConflict(lastFailureMessage))
                    {
                        attempt = maxAttempts;
                    }

                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(200 * attempt);
                    }
                    else
                    {
                        if (IsConcurrencyConflict(lastFailureMessage))
                        {
                            UpdateSavedStatusLabel("Changed on another terminal", false, true);
                            if (!_hasShownConcurrencyConflict)
                            {
                                _hasShownConcurrencyConflict = true;
                                MainThread.BeginInvokeOnMainThread(async () =>
                                {
                                    await ShowConcurrencyConflictDialogAsync();
                                });
                            }
                        }
                        else
                        {
                            UpdateSavedStatusLabel("Save failed", false, true);
                            _ = ToastNotification.ShowAsync(
                                "Save failed",
                                "Line is still on the till — tap again later or Send to retry.",
                                NotificationType.Error,
                                1800);
                        }

                        System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Draft save failed: {result.Message}");
                    }
                }

                return false;
            }
            finally
            {
                _draftSaveLock.Release();
            }
        }

        private static bool IsConcurrencyConflict(string? message)
        {
            return !string.IsNullOrWhiteSpace(message)
                && message.Contains("another terminal", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ShowConcurrencyConflictDialogAsync()
        {
            _ = ToastNotification.ShowAsync(
                "Order refreshed",
                "Updated elsewhere. Loading latest version.",
                NotificationType.Warning,
                1600);
            await ReloadCurrentOrderFromDatabaseAsync();
        }

        private async Task ReloadCurrentOrderFromDatabaseAsync()
        {
            try
            {
                var orderId = !string.IsNullOrWhiteSpace(_currentOrder.Id) ? _currentOrder.Id : _pendingOrderId;
                if (string.IsNullOrWhiteSpace(orderId))
                {
                    return;
                }

                var latest = await _orderService.GetOrderByExternalIdAsync(orderId);
                if (latest != null)
                {
                    await ApplyLoadedOrderAsync(latest);
                    _hasLoadedPersistentOrder = true;
                    _draftDirty = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Conflict reload warning: {ex.Message}");
            }
        }

        private (string ActorType, string? ActorId, string? ActorName) ResolveCurrentActor()
        {
            var authService = ServiceHelper.GetService<AuthenticationService>();
            return ResolveActor(authService?.CurrentUser);
        }

        private static (string ActorType, string? ActorId, string? ActorName) ResolveActor(User? currentUser)
        {

            if (currentUser == null)
            {
                return ("system", null, "system");
            }

            var actorType = currentUser.Role == UserRole.Manager || currentUser.Role == UserRole.Admin
                ? "manager"
                : "user";
            var actorId = currentUser.Id > 0
                ? currentUser.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : (string.IsNullOrWhiteSpace(currentUser.Username) ? null : currentUser.Username);
            var actorName = !string.IsNullOrWhiteSpace(currentUser.Name) ? currentUser.Name : currentUser.Username;

            return (actorType, actorId, actorName);
        }

        private async Task LogOperationalEventAsync(
            string eventType,
            object? payload = null,
            DateTime? eventAt = null,
            User? actorOverride = null)
        {
            if (!_rolloutConfig.EnableLifecycleWrites)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentOrder?.Id) || string.IsNullOrWhiteSpace(eventType))
            {
                return;
            }

            var actor = actorOverride == null ? ResolveCurrentActor() : ResolveActor(actorOverride);
            await _orderService.LogOrderEventAsync(
                _currentOrder.Id,
                eventType,
                actor.ActorType,
                actor.ActorId,
                actor.ActorName,
                payload,
                eventAt);
        }

        private async Task<Order?> BuildPersistentOrderSnapshotAsync(LocalLifecycleState lifecycleState, string? voidReason = null, string? voidedBy = null, DateTime? paidAt = null, DateTime? voidedAt = null)
        {
            EnsureCurrentOrderIdentity();
            var orderType = GetCanonicalOrderType();
            var shouldAssignOrderNumber = lifecycleState is LocalLifecycleState.SentPartial
                or LocalLifecycleState.SentFull
                or LocalLifecycleState.PaymentPartial
                or LocalLifecycleState.Paid
                or LocalLifecycleState.Voided
                || !string.IsNullOrWhiteSpace(_persistentOrderNumber);

            if (shouldAssignOrderNumber)
            {
                await EnsureOrderNumberAssignedAsync();
            }

            var customerName = _isDeliveryOrder
                ? _deliveryCustomerName
                : _isCollectionOrder
                    ? _collectionCustomerName
                    : $"Table {_currentOrder.TableNumber}";

            var customerPhone = _isDeliveryOrder
                ? _deliveryCustomerPhone
                : _isCollectionOrder
                    ? _collectionCustomerPhone
                    : (!string.IsNullOrWhiteSpace(_currentOrder.CustomerPhone) ? _currentOrder.CustomerPhone : null);

            var customerAddress = _isDeliveryOrder ? _deliveryCustomerAddress : null;
            var now = DateTime.Now;
            var currentOrderItems = _currentOrder.Items
                .Select(item => new OrderItem
                {
                    ClientItemId = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id,
                    ItemName = item.Name,
                    VariantId = item.VariantId,
                    VariantName = item.VariantName,
                    DisplayName = item.DisplayName,
                    Quantity = item.Quantity,
                    ItemPrice = item.UnitPrice,
                    SpecialInstructions = item.Notes,
                    MenuItemId = item.MenuItemId,
                    PrintGroupId = item.PrintGroupId,
                    PrintInRed = item.PrintInRed,
                    CourseType = item.CourseType,
                    FiredAt = item.FiredAt,
                    FiredBy = item.FiredBy,
                    Addons = item.SelectedAddons
                        .Select(addon => new OrderItemAddon
                        {
                            AddonId = addon.Id,
                            AddonName = addon.Name,
                            AddonPrice = addon.Price,
                            Quantity = 1
                        })
                        .ToList()
                })
                .ToList();

            return new Order
            {
                OrderId = _currentOrder.Id,
                OrderNumber = shouldAssignOrderNumber ? _persistentOrderNumber : null,
                CloudOrderId = string.IsNullOrWhiteSpace(_orderCloudOrderId) ? null : _orderCloudOrderId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                CustomerAddress = customerAddress,
                TotalAmount = _currentOrder.Total,
                SubtotalAmount = _currentOrder.Subtotal,
                DiscountAmount = _currentOrder.Discount,
                DeliveryFee = _currentOrder.DeliveryFee,
                ServiceChargePercentage = _currentOrder.ServiceChargePercent,
                ServiceChargeBasis = _currentOrder.ServiceChargeBasis,
                ServiceChargeAmount = _currentOrder.ServiceCharge,
                ServiceChargeStatus = ToDatabaseValue(_currentOrder.ServiceChargeStatus),
                ServiceChargeClassification = ToDatabaseValue(_currentOrder.ServiceChargeClassification),
                ServiceChargeRemovalReason = _currentOrder.ServiceChargeRemovalReason,
                ServiceChargeRemovedByUserId = _currentOrder.ServiceChargeRemovedByUserId,
                ServiceChargeRemovedByName = _currentOrder.ServiceChargeRemovedByName,
                ServiceChargeApprovedByUserId = _currentOrder.ServiceChargeApprovedByUserId,
                ServiceChargeApprovedByName = _currentOrder.ServiceChargeApprovedByName,
                ServiceChargeRemovedAt = _currentOrder.ServiceChargeRemovedAt,
                CashTipAmount = _currentOrder.Payments.Where(payment => payment.Method == PaymentMethodType.Cash).Sum(payment => payment.TipAmount),
                CardTipAmount = _currentOrder.Payments.Where(payment => payment.Method == PaymentMethodType.Card).Sum(payment => payment.TipAmount),
                TaxAmount = _currentOrder.VAT,
                OrderType = orderType,
                SourceChannel = _orderSourceChannel,
                TableSessionId = _tableSessionId,
                PaymentMethod = BuildPersistentPaymentMethod(lifecycleState),
                SpecialInstructions = _currentOrder.Notes,
                LocalLifecycleState = lifecycleState,
                IsOpen = lifecycleState != LocalLifecycleState.Paid && lifecycleState != LocalLifecycleState.Voided,
                VoidReason = voidReason,
                VoidedAt = voidedAt,
                VoidedBy = voidedBy,
                PaidAt = paidAt,
                Status = lifecycleState switch
                {
                    LocalLifecycleState.Paid => OrderStatus.Completed,
                    LocalLifecycleState.Voided => OrderStatus.Cancelled,
                    LocalLifecycleState.SentPartial or LocalLifecycleState.SentFull => OrderStatus.Kitchen,
                    _ => OrderStatus.New
                },
                SyncStatus = POS_in_NET.Models.SyncStatus.Synced,
                PaymentStatus = lifecycleState == LocalLifecycleState.Paid
                    ? POS_in_NET.Models.PaymentStatus.Paid
                    : POS_in_NET.Models.PaymentStatus.Pending,
                CreatedAt = _currentOrder.CreatedAt == default ? now : _currentOrder.CreatedAt,
                UpdatedAt = now,
                ExpectedUpdatedAt = _lastSavedAt == default ? null : _lastSavedAt,
                Items = currentOrderItems,
                OrderData = null
            };
        }

        private LocalLifecycleState GetDraftLifecycleState()
        {
            if (_currentOrder.Status == TableOrderStatus.Sent)
            {
                return GetSendLifecycleState();
            }

            return LocalLifecycleState.Draft;
        }

        private LocalLifecycleState GetSendLifecycleState()
        {
            if (_currentOrder.Items.Count == 0)
            {
                return LocalLifecycleState.Active;
            }

            var allSent = _currentOrder.Items.All(item => item.SendStatus == ItemSendStatus.Sent || item.SendStatus == ItemSendStatus.Ready || item.SendStatus == ItemSendStatus.Served);
            var anySent = _currentOrder.Items.Any(item => item.SendStatus != ItemSendStatus.NotSent);

            if (allSent)
            {
                return LocalLifecycleState.SentFull;
            }

            return anySent ? LocalLifecycleState.SentPartial : LocalLifecycleState.Active;
        }

        private Task MarkCurrentOrderChangedAsync()
        {
            _inactivityService.ResetActivity();
            _currentOrder.UpdatedAt = DateTime.Now;
            UpdateTotalsDisplay();
            ScheduleDraftAutosave();
            return Task.CompletedTask;
        }

        private Task AddItemToOrderAsync(FoodMenuItem item, string? selectedNote = null, List<SelectedAddon>? selectedAddons = null, MenuItemVariant? selectedVariant = null)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Adding item: {item.Name}");

            SyncCurrentOrderMode();
            var effectivePrice = selectedVariant?.Price ?? GetSafeEffectivePrice(item);
            var normalizedNote = NormalizeOrderItemNote(selectedNote);
            var normalizedAddons = selectedAddons ?? new List<SelectedAddon>();
            var variantId = selectedVariant?.Id;
            var wasEmpty = _currentOrder.Items.Count == 0;
            
            // Check if item already exists in order
            var existingItem = _currentOrder.Items.FirstOrDefault(i => 
                i.MenuItemId == item.Id && 
                string.Equals(i.VariantId ?? string.Empty, variantId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeOrderItemNote(i.Notes), normalizedNote, StringComparison.OrdinalIgnoreCase) &&
                SelectedAddonsMatch(i.SelectedAddons, normalizedAddons) &&
                i.SendStatus == ItemSendStatus.NotSent);
            
            if (existingItem != null)
            {
                // Increment quantity if item exists
                existingItem.Quantity++;
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Increased quantity to {existingItem.Quantity}");
            }
            else
            {
                // Add new item
                var orderItem = new TableOrderItem
                {
                    Id = Guid.NewGuid().ToString(),
                    MenuItemId = item.Id ?? "",
                    VariantId = selectedVariant?.Id,
                    VariantName = selectedVariant?.Name,
                    DisplayName = BuildOrderItemDisplayName(item.Name, selectedVariant?.Name),
                    Name = item.Name,
                    Quantity = 1,
                    UnitPrice = effectivePrice,
                    VatCategory = string.IsNullOrWhiteSpace(item.VatCategory) ? "HotFood" : item.VatCategory,
                    PrintGroupId = item.PrintGroupId,
                    PrintInRed = item.PrintInRed,
                    CourseType = ResolveCourseType(item),
                    Notes = normalizedNote,
                    SendStatus = ItemSendStatus.NotSent,
                    CreatedAt = DateTime.Now
                };

                foreach (var addon in normalizedAddons)
                {
                    orderItem.SelectedAddons.Add(new SelectedAddon
                    {
                        Id = addon.Id,
                        Name = addon.Name,
                        Price = addon.Price
                    });
                }
                
                _currentOrder.Items.Add(orderItem);
            }
            
            // Optimistic UI: line + totals paint immediately; draft save is background.
            _currentOrder.RecalculateAll();
            RefreshOrderItems();
            _inactivityService.ResetActivity();
            _currentOrder.UpdatedAt = DateTime.Now;
            UpdateTotalsDisplay();
            ScheduleDraftAutosave(immediate: wasEmpty);
            return Task.CompletedTask;
        }

        private static string BuildOrderItemDisplayName(string itemName, string? variantName)
        {
            var baseName = string.IsNullOrWhiteSpace(itemName) ? "Item" : itemName.Trim();
            return string.IsNullOrWhiteSpace(variantName) ? baseName : $"{baseName} ({variantName.Trim()})";
        }

        private decimal GetSafeEffectivePrice(FoodMenuItem item)
        {
            if (!_rolloutConfig.ShouldUseLegacyFallbackPaths)
            {
                return item.GetEffectivePrice(GetPricingOrderType());
            }

            if (item.PriceDineIn is null || item.PriceTakeaway is null)
            {
                return item.Price;
            }

            return item.GetEffectivePrice(GetPricingOrderType());
        }

        private bool IsItemVisibleForCurrentOrderMode(FoodMenuItem item)
        {
            if (!IsTakeawayStyleOrder())
            {
                return true;
            }

            if (item.PriceTakeaway.HasValue)
            {
                return item.PriceTakeaway.Value > 0m;
            }

            return GetSafeEffectivePrice(item) > 0m;
        }

        private bool CategoryHasVisibleItems(string categoryId)
        {
            return _allMenuItems.Any(item =>
                item.CategoryId == categoryId &&
                IsItemVisibleForCurrentOrderMode(item));
        }

        private string? ResolveCourseType(string? menuItemId)
        {
            var menuItem = _allMenuItems.FirstOrDefault(item =>
                string.Equals(item.Id, menuItemId, StringComparison.OrdinalIgnoreCase));
            return menuItem == null ? null : ResolveCourseType(menuItem);
        }

        private string ResolveCourseType(FoodMenuItem item)
        {
            var categoryId = item.CategoryId;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrWhiteSpace(categoryId) && visited.Add(categoryId))
            {
                var category = _allCategories.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, categoryId, StringComparison.OrdinalIgnoreCase));
                if (category == null)
                {
                    break;
                }

                var course = NormalizeCourseType(category.Name);
                if (course != null)
                {
                    return course;
                }

                categoryId = category.ParentId;
            }

            return string.Equals(item.ItemType, "Drink", StringComparison.OrdinalIgnoreCase)
                ? "Drinks"
                : "Mains";
        }

        private static string? NormalizeCourseType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("starter") || normalized.Contains("appetiser") || normalized.Contains("appetizer"))
            {
                return "Starters";
            }
            if (normalized.Contains("dessert") || normalized.Contains("pudding") || normalized.Contains("sweet"))
            {
                return "Desserts";
            }
            if (normalized.Contains("drink") || normalized.Contains("beverage") || normalized.Contains("cocktail")
                || normalized.Contains("wine") || normalized.Contains("beer") || normalized.Contains("bar"))
            {
                return "Drinks";
            }
            if (normalized.Contains("main") || normalized.Contains("entree") || normalized.Contains("entrée"))
            {
                return "Mains";
            }

            return normalized switch
            {
                "starters" => "Starters",
                "mains" => "Mains",
                "desserts" => "Desserts",
                "drinks" => "Drinks",
                _ => null
            };
        }

        private void RefreshOrderItems()
        {
            PublishOrderPlaceSession();
        }

        private OrderPlaceLineRow CreateOrderLineRow(TableOrderItem item)
        {
            var row = new OrderPlaceLineRow();
            ApplyOrderLineRow(row, item);
            return row;
        }

        private void ApplyOrderLineRow(OrderPlaceLineRow row, TableOrderItem item)
        {
            row.BindingContext = item;
            var isMealDeal = IsMealDealOrderItem(item);
            var isTastingMenu = IsTastingMenuOrderItem(item);
            var visibleItemNote = isTastingMenu
                ? TastingMenuNotesHelper.GetOrderNote(item.Notes)
                : item.Notes;
            var hasNotes = !string.IsNullOrWhiteSpace(visibleItemNote);
            var isSent = HasReachedKitchen(item);
            var detailText = BuildShellLineDetails(item, isTastingMenu, visibleItemNote);

            row.ItemName = FormatShellLineName(item);
            row.LineTotal = item.TotalPrice;
            row.Quantity = item.Quantity;
            row.Details = detailText ?? string.Empty;
            row.IsSent = isSent;
            row.ShowNoteAction = !isMealDeal;
            row.NoteActionText = hasNotes ? "Note Added" : "+ Note";
            row.TrailingActionText = isTastingMenu ? "Fire" : string.Empty;
        }

        private void EnsureOrderLineRowAtIndex(OrderPlaceLineRow row, int index)
        {
            // Shell rebinds full line list; incremental container moves are unused.
        }

        private async Task ApplyOrderLineQuantityAsync(TableOrderItem item, bool isTastingMenu, int quantity)
        {
            var wasSent = HasReachedKitchen(item);
            var previousQuantity = item.Quantity;

            if (quantity <= 0)
            {
                if (isTastingMenu)
                {
                    foreach (var course in GetTastingMenuCourses(item))
                    {
                        _currentOrder.Items.Remove(course);
                    }
                }

                _currentOrder.Items.Remove(item);
                _currentOrder.RecalculateAll();
                RefreshOrderItems();
                await MarkCurrentOrderChangedAsync();
                return;
            }

            if (quantity > previousQuantity && wasSent)
            {
                // Already-sent lines stay as kitchen history; extra qty becomes a new unsent unit.
                var unitsToAdd = quantity - previousQuantity;
                for (var i = 0; i < unitsToAdd; i++)
                {
                    if (isTastingMenu)
                    {
                        AddAdditionalTastingMenuUnit(item);
                    }
                    else
                    {
                        _currentOrder.Items.Add(CreateAdditionalUnit(item));
                    }
                }

                _currentOrder.RecalculateAll();
                RefreshOrderItems();
                await MarkCurrentOrderChangedAsync();
                return;
            }

            item.Quantity = quantity;
            if (isTastingMenu)
            {
                foreach (var course in GetTastingMenuCourses(item))
                {
                    course.Quantity = item.Quantity;
                }
            }

            if (wasSent && quantity < previousQuantity)
            {
                item.SendStatus = ItemSendStatus.NotSent;
            }

            _currentOrder.RecalculateAll();
            RefreshOrderItems();
            await MarkCurrentOrderChangedAsync();
        }

        private bool IsUncommittedLocalTableOrder()
        {
            return string.Equals(GetCanonicalOrderType(), "table", StringComparison.OrdinalIgnoreCase)
                && string.Equals(_orderSourceChannel, "local", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_pendingOrderId)
                && string.IsNullOrWhiteSpace(_persistentOrderNumber);
        }

        private bool HasCommitEligibleItems()
        {
            return _currentOrder.Items.Any(item =>
                !item.IsVoided
                && item.Quantity > 0
                && !string.IsNullOrWhiteSpace(item.Name));
        }

        private async Task<bool> CommitOrderForActionAsync(LocalLifecycleState lifecycleState)
        {
            if (!HasCommitEligibleItems())
            {
                await AppAlertService.ShowAlertAsync(
                    "Order Not Created",
                    "Add at least one valid item before sending or taking payment.");
                return false;
            }

            if (IsUncommittedLocalTableOrder())
            {
                await EnsureOrderNumberAssignedAsync();
            }

            _isExplicitOrderCommit = true;
            _draftDirty = true;
            try
            {
                return await PersistDraftAsync(force: true, lifecycleOverride: lifecycleState);
            }
            finally
            {
                _isExplicitOrderCommit = false;
            }
        }

        private async Task ReleaseUncommittedTableAsync(string outcome)
        {
            if (!IsUncommittedLocalTableOrder())
            {
                return;
            }

            _isFinalizingOrder = true;
            try
            {
                if (_tableSessionId.HasValue)
                {
                    var actor = ResolveCurrentActor();
                    var closeResult = await _tableSessionService.CloseSessionForOrderAsync(
                        _tableSessionId.Value,
                        outcome,
                        actor.ActorName,
                        JsonSerializer.Serialize(new
                        {
                            tableNumber = _currentOrder.TableNumber,
                            discardedItemCount = _currentOrder.Items.Count,
                            permanentOrderCreated = false
                        }));
                    if (!closeResult.success)
                    {
                        AppDiagnostics.Log($"[OrderPlacement] Could not release uncommitted table: {closeResult.message}");
                    }
                }

                _currentOrder.Items.Clear();
                _currentOrder.RecalculateAll();
                _tableSessionId = null;
                _draftDirty = false;
                ActiveTableOrderCacheService.Remove(
                    _currentOrder.Id,
                    null,
                    _currentOrder.TableNumber.ToString(CultureInfo.InvariantCulture));
                AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
            }
            finally
            {
                // The page is normally leaving immediately. Keeping finalization
                // set prevents OnDisappearing from running a late draft save.
            }
        }

        private bool IsUsablePersistentOrder(Order? order)
        {
            if (order == null || !order.IsOpen || order.LocalLifecycleState is LocalLifecycleState.Paid or LocalLifecycleState.Voided)
            {
                return false;
            }

            if (!string.Equals(GetCanonicalOrderType(), "table", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(order.OrderType, "table", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_tableSessionId.HasValue && order.TableSessionId.HasValue && order.TableSessionId.Value != _tableSessionId.Value)
            {
                return false;
            }

            return string.Equals(
                order.CustomerName?.Trim(),
                $"Table {_currentOrder.TableNumber}",
                StringComparison.OrdinalIgnoreCase);
        }

        private string BuildTastingCourseStatusText(TableOrderItem package)
        {
            var courses = GetTastingMenuCourses(package);
            if (courses.Count == 0) return "No courses configured";

            return string.Join("  |  ", courses.Select(course =>
            {
                var number = TastingCourseProgressDialog.GetCourseNumber(course);
                var status = course.IsCourseFired ? "FIRED" : "WAITING";
                return $"{(number == int.MaxValue ? "-" : number)}. {course.Name} {status}";
            }));
        }

        private void AddAdditionalTastingMenuUnit(TableOrderItem sourcePackage)
        {
            var newPackage = CreateAdditionalUnit(sourcePackage);
            _currentOrder.Items.Add(newPackage);
            foreach (var sourceCourse in GetTastingMenuCourses(sourcePackage))
            {
                _currentOrder.Items.Add(new TableOrderItem
                {
                    Id = Guid.NewGuid().ToString(),
                    OrderId = sourceCourse.OrderId,
                    MenuItemId = sourceCourse.MenuItemId,
                    VariantId = newPackage.Id,
                    VariantName = sourceCourse.VariantName,
                    DisplayName = sourceCourse.DisplayName,
                    Name = sourceCourse.Name,
                    Quantity = 1,
                    UnitPrice = 0m,
                    VatCategory = sourceCourse.VatCategory,
                    PrintGroupId = sourceCourse.PrintGroupId,
                    PrintInRed = sourceCourse.PrintInRed,
                    Notes = sourceCourse.Notes,
                    CourseType = sourceCourse.CourseType,
                    SendStatus = ItemSendStatus.Sent,
                    CreatedAt = DateTime.Now
                });
            }
        }

        private async Task ShowNoteDialog(TableOrderItem item)
        {
            var originalNote = item.Notes;
            if (IsTastingMenuOrderItem(item))
            {
                var currentNote = TastingMenuNotesHelper.GetOrderNote(item.Notes);
                var tastingNoteDialog = new StyledPromptDialog();
                tastingNoteDialog.SetDialog(
                    "Tasting Menu Note",
                    $"Enter a note for {item.DisplayName}:",
                    "e.g., allergy information or serving instruction",
                    null,
                    currentNote ?? string.Empty,
                    true);

                var tastingNote = await tastingNoteDialog.ShowAsync();
                if (tastingNote != null)
                {
                    item.Notes = TastingMenuNotesHelper.WithOrderNote(
                        item.Notes,
                        NormalizeOrderItemNote(tastingNote));
                    MarkKitchenChangePending(item, originalNote);
                    RefreshOrderItems();
                    await MarkCurrentOrderChangedAsync();
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(item.MenuItemId))
            {
                var quickNotes = await GetQuickNotesForItemAsync(item.MenuItemId);
                if (quickNotes.Count > 0)
                {
                    var noteDialog = new QuickNoteSelectionDialog();
                    var selected = await noteDialog.ShowAsync(item.DisplayName, quickNotes);

                    if (selected.Kind == QuickNoteSelectionKind.Cancelled)
                    {
                        return;
                    }

                    if (selected.Kind == QuickNoteSelectionKind.NoNote)
                    {
                        item.Notes = null;
                        MarkKitchenChangePending(item, originalNote);
                        RefreshOrderItems();
                        await MarkCurrentOrderChangedAsync();
                        return;
                    }

                    if (selected.Kind == QuickNoteSelectionKind.SavedNote)
                    {
                        item.Notes = NormalizeOrderItemNote(selected.NoteText);
                        MarkKitchenChangePending(item, originalNote);
                        RefreshOrderItems();
                        await MarkCurrentOrderChangedAsync();
                        return;
                    }
                }
            }

            var result = await PromptCustomOrderItemNoteAsync(item);

            if (result != null)
            {
                item.Notes = string.IsNullOrWhiteSpace(result) ? null : NormalizeOrderItemNote(result);
                MarkKitchenChangePending(item, originalNote);
                RefreshOrderItems();
                await MarkCurrentOrderChangedAsync();
            }
        }

        private async Task<string?> PromptCustomOrderItemNoteAsync(TableOrderItem item)
        {
            var dialog = new StyledPromptDialog();
            dialog.SetDialog(
                "Add Note",
                $"Enter note for {item.DisplayName}:",
                "e.g., No onions, extra spicy",
                null,
                item.Notes ?? "",
                true
            );

            return await dialog.ShowAsync();
        }

        private void UpdateDisplay()
        {
            UpdateTopBarOrderTitle();
            PublishOrderPlaceSession();
        }

        private void UpdateOrderIdentityDisplay()
        {
            PublishOrderPlaceSession();
        }

        private void UpdateTotalsDisplay()
        {
            PublishOrderPlaceSession();
        }

        /// <summary>
        /// Phase 5 chrome that depends only on order type (same page, three modes).
        /// Pricing / pay-split / print behaviour stay in existing handlers.
        /// </summary>
        private void ApplyOrderTypeChrome()
        {
            PublishOrderPlaceSession();
        }

        private void PublishOrderPlaceSession()
        {
            var session = _orderPlaceSession;
            var takeaway = _isCollectionOrder || _isDeliveryOrder;

            session.Kind = _isDeliveryOrder
                ? OrderPlaceOrderKind.Delivery
                : _isCollectionOrder
                    ? OrderPlaceOrderKind.Collection
                    : OrderPlaceOrderKind.Table;

            var orderPart = !string.IsNullOrEmpty(_currentOrder.OrderNumber)
                ? $"Order #{_currentOrder.OrderNumber}"
                : "Order #";
            var orderNote = string.IsNullOrWhiteSpace(_currentOrder.Notes)
                ? null
                : TruncateHeaderNote(_currentOrder.Notes.Trim(), 15);

            if (_isCollectionOrder)
            {
                session.HeaderTitle = $"{orderPart} · COLLECTION";
                session.HeaderDetail = FormatOrderHeaderDetail(_collectionCustomerName, _collectionCustomerPhone, "Collection order");
                session.HeaderTable = null;
            }
            else if (_isDeliveryOrder)
            {
                session.HeaderTitle = $"{orderPart} · DELIVERY";
                session.HeaderDetail = FormatOrderHeaderDetail(_deliveryCustomerName, _deliveryCustomerPhone, "Delivery order");
                session.HeaderTable = null;
            }
            else
            {
                // Match Client: short title + guests + separate orange Table line (not truncated away).
                session.HeaderTitle = string.IsNullOrEmpty(orderNote) ? orderPart : $"{orderPart} | Note: {orderNote}";
                session.HeaderDetail = $"{Math.Max(_currentOrder.CoverCount, 1)} guests";
                session.HeaderTable = _currentOrder.TableNumber > 0
                    ? $"Table {_currentOrder.TableNumber}"
                    : null;
            }

            session.SelectedCategoryId = _shellSelectedCategoryId ?? _selectedCategory?.Id;
            session.SelectedSubcategoryId = _shellSelectedSubcategoryId
                ?? _selectedSubCategory?.Id
                ?? _selectedCategory?.Id;

            session.Categories.Clear();
            foreach (var category in _shellTopCategories)
            {
                session.Categories.Add(new OrderPlaceCategoryItem(
                    category.Id,
                    category.Name,
                    IsSpecial: string.Equals(category.Id, MealDeal.PosCategoryId, StringComparison.Ordinal)
                        || string.Equals(category.Id, TastingMenu.PosCategoryId, StringComparison.Ordinal)));
            }

            session.Subcategories.Clear();
            foreach (var category in _visibleSubcategories)
            {
                var label = _selectedCategory != null
                    && string.Equals(category.Id, _selectedCategory.Id, StringComparison.Ordinal)
                    && _visibleSubcategories.Count > 1
                        ? "Main"
                        : category.Name;
                session.Subcategories.Add(new OrderPlaceCategoryItem(
                    category.Id,
                    label,
                    _selectedCategory?.Id));
            }

            session.Products.Clear();
            foreach (var product in _visibleProducts)
            {
                session.Products.Add(product);
            }

            session.Lines.Clear();
            foreach (var item in _currentOrder.Items.Where(i => !IsTastingMenuCourseItem(i)))
            {
                var isMealDeal = IsMealDealOrderItem(item);
                var isTastingMenu = IsTastingMenuOrderItem(item);
                var visibleItemNote = isTastingMenu
                    ? TastingMenuNotesHelper.GetOrderNote(item.Notes)
                    : item.Notes;
                var hasNotes = !string.IsNullOrWhiteSpace(visibleItemNote);
                var detailExtras = BuildShellLineDetails(item, isTastingMenu, visibleItemNote);

                session.Lines.Add(new OrderPlaceBasketLine(
                    item.Id,
                    FormatShellLineName(item),
                    item.TotalPrice,
                    item.Quantity,
                    detailExtras,
                    IsSent: HasReachedKitchen(item),
                    ShowNoteAction: !isMealDeal,
                    TrailingActionText: isTastingMenu ? "Fire" : null));
            }

            session.Subtotal = _currentOrder.Subtotal;
            session.Discount = _currentOrder.Discount;
            session.ServiceCharge = _currentOrder.ServiceCharge;
            session.DeliveryFee = _currentOrder.DeliveryFee;
            session.Total = _currentOrder.Total;
            session.ShowServiceCharge = !takeaway;
            session.ShowDeliveryFee = _isDeliveryOrder;
            session.PrintActionLabel = takeaway ? "PRINT RECEIPT" : "PRINT BILL";
            session.PaymentActionLabel = $"PAYMENT £{_currentOrder.Total:F2}";

            OrderPlaceStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateTopBarOrderTitle()
        {
            // Order Place no longer shows TABLE/GUESTS in the top bar —
            // identity lives in SharedUI shell header next to PAYMENT.
            if (TopBar == null)
            {
                return;
            }

            TopBar.IsVisible = false;
            TopBar.HeightRequest = 0;
            TopBar.SetPageTitle(string.Empty);
        }

        private static string FormatOrderHeaderDetail(string? customerName, string? customerPhone, string fallback)
        {
            var name = (customerName ?? string.Empty).Trim();
            var phone = (customerPhone ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(phone))
            {
                return $"{name} · {phone}";
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                return phone;
            }

            return fallback;
        }

        /// <summary>Client-parity basket title: Item (Variant) without truncating into a second "learge" detail.</summary>
        private static string FormatShellLineName(TableOrderItem item)
        {
            var name = string.IsNullOrWhiteSpace(item.Name) ? "Item" : item.Name.Trim();
            var variant = item.VariantName?.Trim();
            if (string.IsNullOrWhiteSpace(variant))
            {
                return string.IsNullOrWhiteSpace(item.DisplayName) ? name : item.DisplayName.Trim();
            }

            var pretty = ToTitleCaseWords(variant);
            return $"{name} ({pretty})";
        }

        /// <summary>Details = notes / addons / free modifiers — not the variant (already in the name).</summary>
        private string? BuildShellLineDetails(TableOrderItem item, bool isTastingMenu, string? visibleItemNote)
        {
            if (isTastingMenu)
            {
                var tasting = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        BuildTastingCourseStatusText(item),
                        !string.IsNullOrWhiteSpace(visibleItemNote) ? $"Note: {visibleItemNote}" : null,
                        item.IsCourseFired ? $"FIRED {(item.CourseType ?? "COURSE").ToUpperInvariant()}" : null
                    }.Where(text => !string.IsNullOrWhiteSpace(text)));
                return string.IsNullOrWhiteSpace(tasting) ? null : tasting;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.Modifiers))
            {
                parts.Add(item.Modifiers.Trim());
            }

            if (item.SelectedAddons.Count > 0)
            {
                parts.AddRange(item.SelectedAddons
                    .Select(a => a.Name?.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))!);
            }

            if (!string.IsNullOrWhiteSpace(visibleItemNote))
            {
                parts.Add(visibleItemNote.Trim());
            }

            return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
        }

        private static string ToTitleCaseWords(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var words = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (w.Length == 1)
                {
                    words[i] = w.ToUpperInvariant();
                }
                else
                {
                    words[i] = char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
                }
            }

            return string.Join(' ', words);
        }

        private static string TruncateHeaderNote(string note, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(note) || maxChars <= 0)
            {
                return string.Empty;
            }

            return note.Length <= maxChars
                ? note
                : note.Substring(0, maxChars) + "..";
        }

        private async void OnNotesClicked(object? sender, EventArgs e)
        {
            // SharedUI Mother-parity dialog (single source with Client Order Place).
            var result = await new OrderPlacePromptDialog().ShowAsync(
                this,
                "Order Notes",
                "Enter notes for this order:",
                "Save",
                "Cancel",
                "e.g., Allergies, special requests...",
                _currentOrder.Notes ?? "");

            if (result != null)
            {
                _currentOrder.Notes = string.IsNullOrWhiteSpace(result) ? null : result;
                UpdateDisplay();
                
                var successDialog = new ModernAlertDialog();
                successDialog.SetAlert("Notes Saved", "Order notes have been saved.", "OK", "#10B981", "White");
                await successDialog.ShowAsync();
                await MarkCurrentOrderChangedAsync();
            }
        }

        private async void OnVoidClicked(object? sender, EventArgs e)
        {
            if (_isFinalizingOrder)
            {
                return;
            }

            // Before the first Kitchen Send/Payment there is no real order to
            // void. Discard the temporary basket and release the table without
            // creating a cancelled/voided history record.
            if (IsUncommittedLocalTableOrder())
            {
                var hasItems = _currentOrder.Items.Count > 0;
                var discardDialog = new ModernConfirmDialog();
                discardDialog.SetConfirm(
                    hasItems ? "Discard Unsent Items" : "Close Empty Table",
                    hasItems
                        ? "These items have not been sent to the kitchen. Discard them and release the table?"
                        : "No order has been created. Release this empty table?",
                    hasItems ? "Discard & Close" : "Release Table",
                    "Keep Open",
                    string.Empty);
                if (!await discardDialog.ShowAsync())
                {
                    return;
                }

                await ReleaseUncommittedTableAsync(hasItems ? "unsent_basket_discarded" : "empty_table_closed");
                await _navigationCoordinator.NavigateShellAsync("visuallayout", animated: false);
                return;
            }

            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "There are no items to void.", "i");
                await noItemsDialog.ShowAsync();
                return;
            }

            var approvedPaymentTotal = await GetExistingApprovedPaymentTotalAsync();
            if (approvedPaymentTotal > 0m)
            {
                await AppAlertService.ShowAlertAsync(
                    "Payment Already Taken",
                    $"£{approvedPaymentTotal:F2} has already been approved for this order. Refund or void the payment before voiding the order so payment and tip reports remain correct.");
                return;
            }
            
            // Show void reason selection — SharedUI Mother-parity dialog (single source with Client Order Place).
            var reason = await new OrderPlaceActionSheetDialog().ShowAsync(
                this,
                "Void Reason",
                new[] { "Customer changed mind", "Wrong item entered", "Kitchen error", "Manager override" });

            if (reason == null)
                return; // User cancelled
            
            // A logged-in Manager/Admin may approve directly; all other users need a manager PIN.
            var authService = ServiceHelper.GetService<AuthenticationService>();
            var currentUser = authService?.CurrentUser;
            if (authService == null)
            {
                var unavailableDialog = new ModernAlertDialog();
                unavailableDialog.SetAlert("Approval Unavailable", "Manager approval is unavailable. Please sign in again.", "OK", "#EF4444", "White");
                await unavailableDialog.ShowAsync();
                return;
            }

            User? approvingUser = currentUser is { Role: UserRole.Manager or UserRole.Admin }
                ? currentUser
                : null;

            if (approvingUser == null)
            {
                var pin = await new OrderPlacePromptDialog().ShowAsync(
                    this,
                    "Manager PIN Required",
                    $"Void amount £{_currentOrder.Total:F2} requires manager approval:",
                    "Continue",
                    "Cancel",
                    "Enter PIN",
                    numericOnly: true);

                if (string.IsNullOrEmpty(pin))
                    return; // User cancelled
                
                var approval = await authService.ValidatePinAsync(pin);
                if (!approval.Success || approval.User is not { Role: UserRole.Manager or UserRole.Admin })
                {
                    var errorDialog = new ModernAlertDialog();
                    var approvalMessage = approval.Success
                        ? "This PIN does not belong to a Manager or Administrator."
                        : approval.Message;
                    errorDialog.SetAlert("Approval Denied", approvalMessage, "OK", "#EF4444", "White");
                    await errorDialog.ShowAsync();
                    return;
                }

                approvingUser = approval.User;
            }
            
            var confirmDialog = new ModernConfirmDialog();
            confirmDialog.SetConfirm(
                "Void Order - Danger",
                $"This action cannot be undone and will permanently void {_currentOrder.Items.Count} items.\n\nReason: {reason}\nAmount: £{_currentOrder.Total:F2}",
                "Confirm Void",
                "Cancel",
                ""
            );
            
            var confirm = await confirmDialog.ShowAsync();
            
            if (confirm)
            {
                await LogOperationalEventAsync("void_requested", new
                {
                    reason,
                    amount = _currentOrder.Total,
                    itemCount = _currentOrder.Items.Count,
                    requestedByUserId = currentUser?.Id,
                    requestedByName = currentUser?.Name ?? currentUser?.Username,
                    approvedByUserId = approvingUser.Id,
                    approvedByName = approvingUser.Name
                }, actorOverride: approvingUser);

                _isFinalizingOrder = true;
                var saved = await SaveVoidedOrderAsync(reason, approvingUser);
                if (!saved)
                {
                    _isFinalizingOrder = false;
                    await AppAlertService.ShowAlertAsync(
                        "Void Failed",
                        "The order could not be voided. It remains open; please try again.");
                    return;
                }
                await PrintFullOrderVoidAsync(reason, approvingUser);
                var voidedDialog = new ModernAlertDialog();
                voidedDialog.SetAlert("Voided", $"Order has been voided.\nReason: {reason}", "", "#10B981", "White");
                await voidedDialog.ShowAsync();
                
                if (IsTakeawayStyleOrder())
                {
                    await NavigateToRoleDashboardAsync(noAnimation: true);
                }
                else
                {
                    await _navigationCoordinator.NavigateShellAsync("visuallayout", animated: false);
                }
            }
        }

        private async Task EnsureTableReleasedAfterFinalizeAsync(string outcome, string? actorName, object? payload)
        {
            if (!string.Equals(GetCanonicalOrderType(), "table", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var payloadJson = payload == null ? null : JsonSerializer.Serialize(payload);
            var released = false;

            await EnsureTableSessionContextAsync(skipOrderLink: true, allowSessionOpen: false);
            if (_tableSessionId.HasValue)
            {
                var closeResult = await _tableSessionService.CloseSessionForOrderAsync(_tableSessionId.Value, outcome, actorName, payloadJson);
                released = closeResult.success;
                if (!released)
                {
                    System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Close session warning: {closeResult.message}");
                }
            }

            if (!released)
            {
                var tableNumberText = _currentOrder.TableNumber.ToString();
                var table = (await _restaurantTableService.GetAllTablesAsync())
                    .FirstOrDefault(t => string.Equals(t.TableNumber?.Trim(), tableNumberText.Trim(), StringComparison.OrdinalIgnoreCase));
                if (table != null)
                {
                    var forceResult = await _tableSessionService.ForceReleaseTableAsync(table.Id, outcome, actorName, payloadJson);
                    if (!forceResult.success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Force release warning: {forceResult.message}");
                    }
                }
            }

            AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
        }

        private async void OnSendClicked(object? sender, EventArgs e)
        {
            _inactivityService.ResetActivity();
            using var idleGuard = _inactivityService.BeginCriticalActivity();

            var clickTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] USER CLICKED SEND TO KITCHEN at {clickTime:HH:mm:ss.fff}");

            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before sending to kitchen.", "i");
                await noItemsDialog.ShowAsync();
                return;
            }

            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] Entering ExecuteUltraFastSendAsync");
            await ExecuteUltraFastSendAsync();
            var afterSendTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] ExecuteUltraFastSendAsync completed in {(afterSendTime - clickTime).TotalMilliseconds:F0}ms");
        }

        private async Task ExecuteUltraFastSendAsync()
        {
            if (_isUltraFastSendInProgress)
            {
                System.Diagnostics.Debug.WriteLine("⏱ [SEND] Double-send tap blocked by re-entry guard");
                return;
            }

            EnsureCurrentOrderIdentity();
            _isUltraFastSendInProgress = true;
            var startTime = DateTime.Now;
            try
            {
                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] === START ExecuteUltraFastSendAsync at {startTime:HH:mm:ss.fff}");
                
                var pendingSendCount = _currentOrder.Items.Count(i => i.SendStatus == ItemSendStatus.NotSent);
                await RefreshRolloutConfigAsync(forceRefresh: false);
                await EnsureTableSessionContextAsync(skipOrderLink: true);
                if (!await CommitOrderForActionAsync(LocalLifecycleState.Active))
                {
                    await AppAlertService.ShowAlertAsync(
                        "Order Not Sent",
                        "The order could not be saved and linked to this table. Nothing was sent to the kitchen. Please try again.");
                    return;
                }
                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] Pending items to send: {pendingSendCount}");
                
                var cloneStartTime = DateTime.Now;
                var printSnapshot = CloneOrderForSend(_currentOrder);
                var cloneEndTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] Cloned order for print in {(cloneEndTime - cloneStartTime).TotalMilliseconds:F0}ms");

                var markStartTime = DateTime.Now;
                foreach (var item in _currentOrder.Items.Where(i => i.SendStatus == ItemSendStatus.NotSent))
                {
                    item.SendStatus = ItemSendStatus.Sent;
                    item.SentAt = DateTime.Now;
                    item.FailureReason = null;
                }
                var markEndTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] Marked items as sent in UI in {(markEndTime - markStartTime).TotalMilliseconds:F0}ms");

                _currentOrder.Status = TableOrderStatus.Sent;
                _currentOrder.UpdatedAt = DateTime.Now;
                RefreshOrderItems();
                UpdateTotalsDisplay();
                // Let SENT cues paint before kitchen/print work.
                await Task.Yield();

                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] Firing background ProcessUltraFastSendPipelineAsync (don't wait)");
                var sent = await ProcessUltraFastSendPipelineAsync(printSnapshot);
                if (!sent)
                {
                    var attemptedIds = printSnapshot.Items
                        .Where(item => item.SendStatus == ItemSendStatus.NotSent)
                        .Select(item => item.Id)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in _currentOrder.Items.Where(item => attemptedIds.Contains(item.Id)))
                    {
                        item.SendStatus = ItemSendStatus.NotSent;
                        item.SentAt = null;
                        item.FailureReason = "Kitchen print was not confirmed.";
                    }

                    _currentOrder.Status = TableOrderStatus.Active;
                    _currentOrder.UpdatedAt = DateTime.Now;
                    _draftDirty = true;
                    await PersistDraftAsync(force: true, lifecycleOverride: LocalLifecycleState.Active);
                    RefreshOrderItems();
                    UpdateDisplay();
                    await AppAlertService.ShowAlertAsync(
                        "Kitchen Send Failed",
                        "The kitchen printer did not confirm this order. The order remains open and can be sent again.");
                    return;
                }
                
                var navStartTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] About to call HandleSuccessfulSendAsync + Navigate");
                await HandleSuccessfulSendAsync(pendingSendCount, fastExit: true);
                var navEndTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] Navigation completed in {(navEndTime - navStartTime).TotalMilliseconds:F0}ms");
            }
            finally
            {
                _isUltraFastSendInProgress = false;
                var endTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] === END ExecuteUltraFastSendAsync (total: {(endTime - startTime).TotalMilliseconds:F0}ms)");
            }
        }

        private async Task<bool> ProcessUltraFastSendPipelineAsync(TableOrder printSnapshot)
        {
            try
            {
                var orderNumber = await EnsureOrderNumberAssignedAsync();
                printSnapshot.OrderNumber = orderNumber;

                var persistedOrder = await _orderService.GetOrderByExternalIdAsync(_currentOrder.Id);
                if (persistedOrder == null)
                {
                    await PersistDraftAsync(force: true, lifecycleOverride: LocalLifecycleState.Active);
                    persistedOrder = await _orderService.GetOrderByExternalIdAsync(_currentOrder.Id);
                }

                if (persistedOrder == null)
                {
                    await LogOperationalEventAsync("send_failed", new { reason = "persisted_order_missing" });
                    return false;
                }

                var actor = ResolveCurrentActor();
                var revisions = await _kitchenRevisionService.GetRetryableRevisionsAsync(persistedOrder.Id);
                var newRevision = await _kitchenRevisionService.CreateRevisionAsync(
                    persistedOrder.Id,
                    printSnapshot,
                    actor.ActorName);
                if (newRevision != null)
                {
                    revisions.Add(newRevision);
                }

                if (revisions.Count == 0)
                {
                    await LogOperationalEventAsync("send_failed", new { reason = "no_kitchen_changes" });
                    return false;
                }

                var anyKitchenPrint = false;
                foreach (var revision in revisions.OrderBy(item => item.RevisionNumber))
                {
                    var revisionPrintOrder = _kitchenRevisionService.BuildPrintOrder(printSnapshot, revision);
                    if (_rolloutConfig.EnableSendDurability)
                    {
                        var affectedClientIds = revision.Lines
                            .Select(line => line.ClientItemId)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var affectedItems = persistedOrder.Items
                            .Where(item => !string.IsNullOrWhiteSpace(item.ClientItemId) && affectedClientIds.Contains(item.ClientItemId!))
                            .ToList();
                        var batchId = await _orderService.CreateSendBatchAsync(persistedOrder.Id, affectedItems);
                        anyKitchenPrint |= await ProcessDurableSendInBackgroundAsync(persistedOrder, batchId, revisionPrintOrder, revision);
                    }
                    else
                    {
                        var legacyPrint = IsTakeawayStyleOrder()
                            ? await _orderRoutingPrintService.PrintTakeawayOrderAsync(revisionPrintOrder, GetCanonicalOrderType())
                            : await _orderRoutingPrintService.PrintOrderAsync(revisionPrintOrder);
                        await _kitchenRevisionService.MarkPrintResultAsync(revision, legacyPrint);
                        anyKitchenPrint |= legacyPrint.AnyPrinted;
                        if (!legacyPrint.AnyPrinted)
                        {
                            await LogOperationalEventAsync("send_failed", new
                            {
                                reason = "legacy_no_routes_printed",
                                kitchenRevision = revision.RevisionNumber,
                                failedRoutes = legacyPrint.FailedRoutes
                            });
                        }
                    }
                }

                if (!anyKitchenPrint)
                {
                    return false;
                }

                _draftDirty = true;
                if (!await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState()))
                {
                    return false;
                }
                AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
                return true;
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log($"[OrderPlacement] Kitchen send pipeline failed: {ex}");
                return false;
            }
        }

        private static TableOrder CloneOrderForSend(TableOrder source)
        {
            var clone = new TableOrder
            {
                Id = source.Id,
                OrderNumber = source.OrderNumber,
                TableNumber = source.TableNumber,
                CoverCount = source.CoverCount,
                StaffName = source.StaffName,
                StaffId = source.StaffId,
                StartTime = source.StartTime,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                Notes = source.Notes,
                OrderMode = source.OrderMode,
                ServiceChargePercent = source.ServiceChargePercent,
                ServiceChargeStatus = source.ServiceChargeStatus,
                ServiceChargeClassification = source.ServiceChargeClassification,
                ServiceChargeRemovalReason = source.ServiceChargeRemovalReason,
                ServiceChargeRemovedByUserId = source.ServiceChargeRemovedByUserId,
                ServiceChargeRemovedByName = source.ServiceChargeRemovedByName,
                ServiceChargeApprovedByUserId = source.ServiceChargeApprovedByUserId,
                ServiceChargeApprovedByName = source.ServiceChargeApprovedByName,
                ServiceChargeRemovedAt = source.ServiceChargeRemovedAt,
                DeliveryFee = source.DeliveryFee,
                Status = source.Status,
                KitchenRevisionNumber = source.KitchenRevisionNumber,
                KitchenTicketType = source.KitchenTicketType,
                KitchenRevisionReason = source.KitchenRevisionReason
            };

            foreach (var item in source.Items)
            {
                clone.Items.Add(new TableOrderItem
                {
                    Id = item.Id,
                    OrderId = item.OrderId,
                    MenuItemId = item.MenuItemId,
                    VariantId = item.VariantId,
                    VariantName = item.VariantName,
                    DisplayName = item.DisplayName,
                    Name = item.Name,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    VatCategory = item.VatCategory,
                    PrintGroupId = item.PrintGroupId,
                    PrintInRed = item.PrintInRed,
                    Notes = item.Notes,
                    Modifiers = item.Modifiers,
                    CourseType = item.CourseType,
                    FiredAt = item.FiredAt,
                    FiredBy = item.FiredBy,
                    SendStatus = item.SendStatus,
                    SentAt = item.SentAt,
                    FailureReason = item.FailureReason,
                    KitchenAction = item.KitchenAction,
                    PreviousQuantity = item.PreviousQuantity,
                    PreviousNotes = item.PreviousNotes,
                    SourceItemId = item.SourceItemId,
                    CreatedAt = item.CreatedAt,
                    SelectedAddons = new ObservableCollection<SelectedAddon>(item.SelectedAddons ?? new ObservableCollection<SelectedAddon>())
                });
            }

            clone.RecalculateAll();
            return clone;
        }

        private async Task<bool> ProcessDurableSendInBackgroundAsync(
            Order persistedOrder,
            string batchId,
            TableOrder? printSourceOrder = null,
            KitchenOrderRevision? revision = null)
        {
            try
            {
                var printOrder = printSourceOrder ?? _currentOrder;
                var printResult = IsTakeawayStyleOrder()
                    ? await _orderRoutingPrintService.PrintTakeawayOrderAsync(printOrder, GetCanonicalOrderType())
                    : await _orderRoutingPrintService.PrintOrderAsync(printOrder);
                if (revision != null)
                {
                    await _kitchenRevisionService.MarkPrintResultAsync(revision, printResult);
                }
                var labelResult = await PrintLabelsForPrintedItemsAsync(printOrder, printResult.PrintedItemIds);

                var printedSourceIds = revision == null
                    ? printResult.PrintedItemIds
                    : revision.Lines
                        .GroupBy(line => line.ClientItemId, StringComparer.OrdinalIgnoreCase)
                        .Where(group => group.All(line => printResult.PrintedItemIds.Contains(line.LineId)))
                        .Select(group => group.Key)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var affectedSourceIds = revision?.Lines
                    .Select(line => line.ClientItemId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var affectedPersistedItems = revision == null
                    ? persistedOrder.Items
                    : persistedOrder.Items
                        .Where(item => !string.IsNullOrWhiteSpace(item.ClientItemId) && affectedSourceIds!.Contains(item.ClientItemId!))
                        .ToList();
                var printedDbItemIds = affectedPersistedItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.ClientItemId) && printedSourceIds.Contains(item.ClientItemId!))
                    .Select(item => item.Id)
                    .ToList();

                var failedItems = new List<(int OrderItemDbId, string Reason)>();
                if (printResult.FailedRouteDetails.Count > 0)
                {
                    foreach (var failure in printResult.FailedRouteDetails)
                    {
                        var routeItems = affectedPersistedItems
                            .Where(item => string.IsNullOrWhiteSpace(failure.RouteTarget)
                                ? !printedDbItemIds.Contains(item.Id)
                                : string.Equals(item.PrintGroupId, failure.RouteTarget, StringComparison.OrdinalIgnoreCase))
                            .Select(item => item.Id);

                        foreach (var itemId in routeItems)
                        {
                            if (!failedItems.Any(entry => entry.OrderItemDbId == itemId))
                            {
                                failedItems.Add((itemId, failure.Reason));
                            }
                        }
                    }
                }
                else if (!printResult.AnyPrinted)
                {
                    foreach (var item in affectedPersistedItems)
                    {
                        failedItems.Add((item.Id, "No active print groups could be used."));
                    }
                }

                await _orderService.MarkSendBatchResultAsync(persistedOrder.Id, batchId, printedDbItemIds, failedItems);

                _ = LogOperationalEventAsync("sent", new
                {
                    batchId,
                    printedCount = printResult.PrintedItemIds.Count,
                    labelAttemptCount = labelResult.Attempted,
                    labelPrintedCount = labelResult.Printed,
                    labelFailedCount = labelResult.Failed,
                    failedCount = failedItems.Count,
                    hasFailures = printResult.HasFailures
                });

                if (labelResult.Failed > 0)
                {
                    _ = LogOperationalEventAsync("label_print_failed", new
                    {
                        batchId,
                        failedCount = labelResult.Failed,
                        errors = labelResult.Errors
                    });
                }

                if (printResult.HasFailures)
                {
                    _ = LogOperationalEventAsync("send_failed", new
                    {
                        batchId,
                        reason = "partial_route_failure",
                        failedRoutes = printResult.FailedRouteDetails.Select(route => new { route.RouteName, route.RouteTarget, route.Reason }).ToList()
                    });
                }

                AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
                return printResult.AnyPrinted;
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log($"[OrderPlacement] Durable kitchen send failed: {ex}");
                return false;
            }
        }

        private async Task<LabelPrintSummary> PrintLabelsForPrintedItemsAsync(TableOrder printOrder, ISet<string> printedItemIds)
        {
            var summary = new LabelPrintSummary();

            if (printedItemIds.Count == 0)
            {
                return summary;
            }

            var labelTarget = await ResolveLabelPrinterTargetAsync();
            if (labelTarget == null)
            {
                System.Diagnostics.Debug.WriteLine("[LABEL] No enabled label printer configured; skipping labels.");
                return summary;
            }

            var labelService = new LabelPrintingService(labelTarget.IpAddress, labelTarget.Port, enabled: true);
            var labelContext = BuildLabelPrintContext(printOrder);

            foreach (var orderItem in printOrder.Items.Where(item =>
                         printedItemIds.Contains(item.Id)
                         && item.KitchenAction is KitchenChangeAction.New or KitchenChangeAction.Add))
            {
                var menuItem = await ResolveMenuItemForLabelAsync(orderItem.MenuItemId);
                if (menuItem == null || !ShouldPrintLabel(menuItem))
                {
                    continue;
                }

                summary.Attempted++;
                var printed = await labelService.PrintItemLabelAsync(menuItem, labelContext, orderItem.Quantity);
                if (printed)
                {
                    summary.Printed++;
                }
                else
                {
                    summary.Failed++;
                    summary.Errors.Add($"{orderItem.Name}: label print failed on {labelTarget.Name}");
                }
            }

            return summary;
        }

        private async Task<FoodMenuItem?> ResolveMenuItemForLabelAsync(string? menuItemId)
        {
            if (string.IsNullOrWhiteSpace(menuItemId))
            {
                return null;
            }

            var menuItem = _allMenuItems.FirstOrDefault(item => string.Equals(item.Id, menuItemId, StringComparison.OrdinalIgnoreCase));
            menuItem ??= await _menuItemService.GetItemByIdAsync(menuItemId);

            if (menuItem != null && menuItem.PrintComponentLabels && string.IsNullOrWhiteSpace(menuItem.ComponentLabelsJson))
            {
                menuItem.Components = await _menuItemService.GetItemComponentsAsync(menuItem.Id);
            }

            return menuItem;
        }

        private static bool ShouldPrintLabel(FoodMenuItem item)
        {
            return !string.IsNullOrWhiteSpace(item.LabelText)
                || (item.PrintComponentLabels
                    && (!string.IsNullOrWhiteSpace(item.ComponentLabelsJson) || item.Components.Count > 0));
        }

        private async Task<LabelPrinterTarget?> ResolveLabelPrinterTargetAsync()
        {
            try
            {
                var printerDb = ServiceHelper.GetService<NetworkPrinterDatabaseService>();
                if (printerDb != null)
                {
                    var labelPrinters = await printerDb.GetPrintersByTypeAsync(NetworkPrinterType.Label);
                    var labelPrinter = labelPrinters.FirstOrDefault(printer => printer.IsEnabled);
                    if (labelPrinter != null)
                    {
                        return new LabelPrinterTarget(labelPrinter.Name, labelPrinter.IpAddress, labelPrinter.Port);
                    }
                }

                var printGroupService = ServiceHelper.GetService<PrintGroupService>() ?? new PrintGroupService();
                var labelGroup = (await printGroupService.GetActivePrintGroupsAsync())
                    .FirstOrDefault(group =>
                        string.Equals(group.PrinterType, "label", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(group.PrinterIp));

                if (labelGroup != null && !string.IsNullOrWhiteSpace(labelGroup.PrinterIp))
                {
                    return new LabelPrinterTarget(labelGroup.Name, labelGroup.PrinterIp, labelGroup.PrinterPort);
                }

                var businessSettings = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
                var businessInfo = await businessSettings.GetBusinessInfoAsync();
                if (businessInfo?.LabelPrinterEnabled == true && !string.IsNullOrWhiteSpace(businessInfo.LabelPrinterIp))
                {
                    return new LabelPrinterTarget("Business Label Printer", businessInfo.LabelPrinterIp, businessInfo.LabelPrinterPort);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LABEL] Failed to resolve label printer: {ex.Message}");
            }

            return null;
        }

        private string BuildLabelPrintContext(TableOrder printOrder)
        {
            var orderReference = !string.IsNullOrWhiteSpace(printOrder.OrderNumber)
                ? printOrder.OrderNumber
                : _persistentOrderNumber;

            if (string.IsNullOrWhiteSpace(orderReference))
            {
                orderReference = DateTime.Now.ToString("HH:mm");
            }

            if (_isDeliveryOrder)
            {
                return $"Delivery {orderReference}";
            }

            if (_isCollectionOrder)
            {
                return $"Collection {orderReference}";
            }

            return $"Table {printOrder.TableNumber} · {orderReference}";
        }

        private sealed class LabelPrintSummary
        {
            public int Attempted { get; set; }
            public int Printed { get; set; }
            public int Failed { get; set; }
            public List<string> Errors { get; } = new();
        }

        private sealed record LabelPrinterTarget(string Name, string IpAddress, int Port);

        private async Task HandleSuccessfulSendAsync(int successCount, bool fastExit = false)
        {
            var handleStartTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] HandleSuccessfulSendAsync start (fastExit={fastExit})");
            
            if (fastExit)
            {
                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] In fastExit mode - firing background FinalizeTableSessionAfterSendAsync");
                await FinalizeTableSessionAfterSendAsync();
            }
            else
            {
                await EnsureTableSessionContextAsync();

                if (_tableSessionId.HasValue)
                {
                    var linkResult = await _tableSessionService.LinkOrderToSessionAsync(_tableSessionId.Value, _currentOrder.Id);
                    if (!linkResult.success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Session order-link warning: {linkResult.message}");
                    }

                    var servedResult = await _tableSessionService.MarkSessionFoodServedAsync(_tableSessionId.Value);
                    if (!servedResult.success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Session status update warning: {servedResult.message}");
                    }

                    var occupiedResult = await _tableSessionService.MarkTableOccupiedAsync(_tableSessionId.Value);
                    if (!occupiedResult.success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Table status update warning: {occupiedResult.message}");
                    }
                }
            }

            if (ToastNotification != null)
            {
                var toastMessage = successCount > 0
                    ? $"Order sent  ({successCount} item{(successCount == 1 ? string.Empty : "s")})"
                    : "Order sent ";
                _ = ToastNotification.ShowAsync("Success", toastMessage, NotificationType.Success, 1200);
            }

            AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] About to call NavigateToRoleDashboardAsync");
            var navStartTime = DateTime.Now;
            await NavigateToRoleDashboardAsync(fastExit);
            var navEndTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] NavigateToRoleDashboardAsync returned in {(navEndTime - navStartTime).TotalMilliseconds:F0}ms");
            
            var handleEndTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] HandleSuccessfulSendAsync complete (total: {(handleEndTime - handleStartTime).TotalMilliseconds:F0}ms)");
        }

        private async Task FinalizeTableSessionAfterSendAsync()
        {
            try
            {
                // Skip link - already done synchronously before navigation
                await EnsureTableSessionContextAsync(skipOrderLink: true);

                if (!_tableSessionId.HasValue)
                {
                    return;
                }

                var servedResult = await _tableSessionService.MarkSessionFoodServedAsync(_tableSessionId.Value);
                if (!servedResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Session status update warning: {servedResult.message}");
                }

                var occupiedResult = await _tableSessionService.MarkTableOccupiedAsync(_tableSessionId.Value);
                if (!occupiedResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Table status update warning: {occupiedResult.message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Session finalize error: {ex.Message}");
            }
        }

        private async Task<(List<string> Critical, List<string> Informational)> GetReadinessIssuesCachedAsync()
        {
            if (_cachedReadinessIssues.HasValue && (DateTime.Now - _lastReadinessCheckAt) < TimeSpan.FromSeconds(30))
            {
                return _cachedReadinessIssues.Value;
            }

            var issues = await CollectReadinessIssuesAsync();
            _cachedReadinessIssues = issues;
            _lastReadinessCheckAt = DateTime.Now;
            return issues;
        }

        private async Task NavigateToRoleDashboardAsync(bool noAnimation = false)
        {
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] NavigateToRoleDashboardAsync START (noAnimation={noAnimation})");
            var navStart = DateTime.Now;
            
            var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
            var route = _roleAccessService.ResolveDashboardRoute(authService.CurrentUser?.Role);
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] Resolved route: //{route} (animate={!noAnimation})");
            
            await _navigationCoordinator.NavigateShellAsync(route, animated: !noAnimation);
            
            var navEnd = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱ [SEND] NavigateToRoleDashboardAsync COMPLETE - Shell.GoToAsync returned in {(navEnd - navStart).TotalMilliseconds:F0}ms");
        }

        private async Task ShowSendFailureRetryOptionsAsync(Order persistedOrder, List<PrintRouteFailure> failedRoutes)
        {
            if (failedRoutes.Count == 0)
            {
                return;
            }

            var options = failedRoutes.Select(route => route.RouteName).ToList();
            options.Add("Retry All");
            options.Add("Dismiss");

            var selected = await DisplayActionSheet("Retry failed routes", "Dismiss", null, options.Take(options.Count - 1).ToArray());
            if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, "Dismiss", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(selected, "Retry All", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var route in failedRoutes)
                {
                    await RetrySendRouteAsync(persistedOrder, route);
                }
                return;
            }

            var chosenRoute = failedRoutes.FirstOrDefault(route => string.Equals(route.RouteName, selected, StringComparison.OrdinalIgnoreCase));
            if (chosenRoute != null && !string.IsNullOrWhiteSpace(chosenRoute.RouteTarget))
            {
                await RetrySendRouteAsync(persistedOrder, chosenRoute);
            }
        }

        private async Task RetrySendRouteAsync(Order persistedOrder, PrintRouteFailure routeFailure)
        {
            var routeItems = persistedOrder.Items
                .Where(item => string.Equals(item.PrintGroupId, routeFailure.RouteTarget, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (routeItems.Count == 0)
            {
                return;
            }

            foreach (var item in _currentOrder.Items.Where(item => string.Equals(item.PrintGroupId, routeFailure.RouteTarget, StringComparison.OrdinalIgnoreCase)))
            {
                item.SendStatus = ItemSendStatus.NotSent;
                item.FailureReason = null;
            }

            var retryBatchId = await _orderService.CreateSendBatchAsync(persistedOrder.Id, routeItems);
            var retryOrder = new TableOrder
            {
                Id = _currentOrder.Id,
                OrderNumber = _currentOrder.OrderNumber,
                TableNumber = _currentOrder.TableNumber,
                StaffName = _currentOrder.StaffName,
                StaffId = _currentOrder.StaffId,
                CoverCount = _currentOrder.CoverCount,
                StartTime = _currentOrder.StartTime,
                CreatedAt = _currentOrder.CreatedAt,
                UpdatedAt = DateTime.Now,
                OrderMode = _currentOrder.OrderMode
            };

            foreach (var item in _currentOrder.Items.Where(item => string.Equals(item.PrintGroupId, routeFailure.RouteTarget, StringComparison.OrdinalIgnoreCase)))
            {
                retryOrder.Items.Add(item);
            }

            var retryResult = await _orderRoutingPrintService.PrintOrderAsync(retryOrder, routeFailure.RouteTarget);
            var retryPrintedIds = routeItems
                .Where(item => !string.IsNullOrWhiteSpace(item.ClientItemId) && retryResult.PrintedItemIds.Contains(item.ClientItemId!))
                .Select(item => item.Id)
                .ToList();
            var retryFailedItems = routeItems
                .Where(item => !retryPrintedIds.Contains(item.Id))
                .Select(item => (item.Id, retryResult.FailedRoutes.FirstOrDefault() ?? routeFailure.Reason))
                .ToList();

            await _orderService.MarkSendBatchResultAsync(persistedOrder.Id, retryBatchId, retryPrintedIds, retryFailedItems);

            await LogOperationalEventAsync("resend", new
            {
                retryBatchId,
                route = routeFailure.RouteName,
                routeTarget = routeFailure.RouteTarget,
                printedCount = retryPrintedIds.Count,
                failedCount = retryFailedItems.Count
            });

            if (retryFailedItems.Count > 0)
            {
                await LogOperationalEventAsync("send_failed", new
                {
                    retryBatchId,
                    reason = "retry_route_failure",
                    route = routeFailure.RouteName,
                    routeTarget = routeFailure.RouteTarget
                });
            }

            foreach (var item in _currentOrder.Items.Where(item => string.Equals(item.PrintGroupId, routeFailure.RouteTarget, StringComparison.OrdinalIgnoreCase) && retryResult.PrintedItemIds.Contains(item.Id)))
            {
                item.SendStatus = ItemSendStatus.Sent;
                item.SentAt = DateTime.Now;
                item.FailureReason = null;
            }

            await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState());
        }

        private async void OnPrintReceiptClicked(object? sender, EventArgs e)
        {
            _inactivityService.ResetActivity();

            if (_isPrintInProgress)
            {
                return;
            }

            _isPrintInProgress = true;
            SetOrderPlaceBusy(true);
            try
            {

            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before printing receipt.", "i");
                await noItemsDialog.ShowAsync();
                return;
            }

            var receiptPrinted = await PrintReceipt(
                _currentOrder.Total + _currentOrder.TipAmount,
                _currentOrder.TipAmount,
                isFinalPaymentReceipt: false);

            if (!IsTakeawayStyleOrder())
            {
                if (!receiptPrinted)
                {
                    await ShowPrintCopiesFailureAsync(receiptPrinted: false, kitchenPrinted: true);
                    return;
                }

                AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
                await NavigateToRoleDashboardAsync(noAnimation: true);
                return;
            }

            // PRINT on collection/delivery always produces a complete kitchen
            // copy as well as the customer bill. Use a snapshot so the live
            // order changes to sent only after both copies succeed.
            var kitchenCopy = CloneOrderForSend(_currentOrder);
            foreach (var item in kitchenCopy.Items.Where(item => !item.IsVoided))
            {
                item.SendStatus = ItemSendStatus.NotSent;
            }

            var kitchenResult = await _orderRoutingPrintService.PrintTakeawayOrderAsync(
                kitchenCopy,
                GetCanonicalOrderType());
            var kitchenPrinted = kitchenResult.AnyPrinted && !kitchenResult.HasFailures;

            await LogOperationalEventAsync(
                receiptPrinted && kitchenPrinted ? "takeaway_copies_printed" : "takeaway_copies_print_failed",
                new
                {
                    orderType = GetCanonicalOrderType(),
                    receiptPrinted,
                    kitchenPrinted,
                    kitchenFailures = kitchenResult.FailedRoutes
                });

            if (!receiptPrinted || !kitchenPrinted)
            {
                await ShowPrintCopiesFailureAsync(receiptPrinted, kitchenPrinted);
                return;
            }

            // A successful takeaway PRINT is also the completion of the send
            // workflow: both copies have left the POS, so preserve that state
            // before returning staff to their role-appropriate dashboard.
            var sentAt = DateTime.Now;
            foreach (var item in _currentOrder.Items.Where(item => !item.IsVoided))
            {
                item.SendStatus = ItemSendStatus.Sent;
                item.SentAt ??= sentAt;
                item.FailureReason = null;
            }

            _currentOrder.Status = TableOrderStatus.Sent;
            _currentOrder.UpdatedAt = sentAt;
            _draftDirty = true;
            await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState());
            AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
            await NavigateToRoleDashboardAsync(noAnimation: true);
            }
            finally
            {
                _isPrintInProgress = false;
                SetOrderPlaceBusy(false);
            }
        }

        private void SetOrderPlaceBusy(bool isBusy)
        {
            _orderPlaceSession.IsBusy = isBusy;
            OrderPlaceStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static async Task ShowPrintCopiesFailureAsync(bool receiptPrinted, bool kitchenPrinted)
        {
            var message = (!receiptPrinted, !kitchenPrinted) switch
            {
                (true, true) => "Neither copy printed. Please check the receipt and kitchen printer setup.",
                (true, false) => "The kitchen copy printed, but the customer bill did not. Please check the receipt printer.",
                (false, true) => "The customer bill printed, but the kitchen copy did not. Please check the kitchen printer.",
                _ => "Could not print the receipt. Please check the printer setup."
            };

            var printDialog = new ModernAlertDialog();
            printDialog.SetAlert("Print Failed", message, "!", "#EF4444", "White");
            await printDialog.ShowAsync();
        }

        private async Task<string> EnsureOrderNumberAssignedAsync()
        {
            await _orderNumberLock.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(_persistentOrderNumber))
                {
                    _persistentOrderNumber = !string.IsNullOrWhiteSpace(_currentOrder.OrderNumber)
                        ? _currentOrder.OrderNumber
                        : await _orderNumberService.GenerateOrderNumberAsync(GetCanonicalOrderType());
                }

                _currentOrder.OrderNumber = _persistentOrderNumber;
                return _persistentOrderNumber;
            }
            finally
            {
                _orderNumberLock.Release();
            }
        }

        private async void OnMoreClicked(object? sender, EventArgs e)
        {
            var isTakeaway = _isCollectionOrder || _isDeliveryOrder;
            var customerPhone = _isDeliveryOrder ? _deliveryCustomerPhone : _collectionCustomerPhone;

            var authService = ServiceHelper.GetService<AuthenticationService>();
            var currentUser = authService?.CurrentUser;
            var options = new List<(string Text, string Icon, bool IsEnabled, bool IsDestructive)>
            {
                ("Discount", "", true, false)
            };

            if (isTakeaway)
            {
                // COL/DEL: no Transfer / Merge / Fire Course. Keep Previous Orders.
                options.Add(("Previous Orders", "", !string.IsNullOrWhiteSpace(customerPhone), false));
            }
            else
            {
                options.Add(("Table Transfer", "", true, false));
                options.Add(("Merge Tables", "", true, false));
                options.Add(("Fire Course", "", _currentOrder.Items.Count > 0, false));
            }

            options.Add(("Loyalty Points", "", true, false));
            options.Add(("Cash Drawer", "", currentUser != null, false));

            // Service charge remove/restore is table-only (CanChangeServiceCharge already gates type).
            if (CanChangeServiceCharge())
            {
                options.Insert(0, _currentOrder.ServiceChargeStatus == TableServiceChargeStatus.Applied
                    ? ("REMOVE SERVICE CHARGE", "", true, true)
                    : ("RESTORE SERVICE CHARGE", "", true, false));
            }

            // SharedUI Mother-parity dialog (single source with Client Order Place).
            var sharedOptions = options
                .Select(o => new OrderPlaceMoreOption(o.Text, o.IsEnabled, o.IsDestructive))
                .ToList();
            var selected = await new OrderPlaceMoreOptionsDialog().ShowAsync(this, sharedOptions);

            if (selected != null)
            {
                switch (selected)
                {
                    case "Discount":
                        await ShowDiscountDialog();
                        break;
                    case "Table Transfer":
                        if (!isTakeaway)
                        {
                            await ShowTableTransferDialog();
                        }
                        break;
                    case "Merge Tables":
                        if (!isTakeaway)
                        {
                            await ShowMergeTablesDialog();
                        }
                        break;
                    case "Fire Course":
                        if (!isTakeaway)
                        {
                            await ShowFireCourseDialog();
                        }
                        break;
                    case "Previous Orders":
                        await ShowPreviousOrdersDialogAsync();
                        break;
                    case "Loyalty Points":
                        await ShowLoyaltyPointsDialog();
                        break;
                    case "Cash Drawer":
                        await OpenCashDrawer();
                        break;
                    case "REMOVE SERVICE CHARGE":
                        await ChangeServiceChargeStatusAsync(remove: true);
                        break;
                    case "RESTORE SERVICE CHARGE":
                        await ChangeServiceChargeStatusAsync(remove: false);
                        break;
                }
            }
        }

        private async Task ShowPreviousOrdersDialogAsync()
        {
            if (!_isCollectionOrder && !_isDeliveryOrder)
            {
                return;
            }

            var customerName = _isDeliveryOrder ? _deliveryCustomerName : _collectionCustomerName;
            var customerPhone = _isDeliveryOrder ? _deliveryCustomerPhone : _collectionCustomerPhone;
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                await AppAlertService.ShowAlertAsync(
                    "Customer Required",
                    "Select a saved customer before viewing previous orders.");
                return;
            }

            try
            {
                var previousOrders = await _orderService.GetPreviousCustomerOrdersAsync(
                    customerPhone,
                    maximumOrders: 3,
                    monthsBack: 12);

                var historyDialog = new PreviousOrdersDialog();
                historyDialog.SetCustomer(customerName, customerPhone);
                historyDialog.SetOrders(previousOrders);
                await historyDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Previous Orders] Could not load history: {ex.Message}");
                await AppAlertService.ShowAlertAsync(
                    "Previous Orders Unavailable",
                    "The customer order history could not be loaded. The current order has not been changed.");
            }
        }

        private bool CanChangeServiceCharge()
        {
            var modifiableStatus = _currentOrder.Status is TableOrderStatus.Active or TableOrderStatus.Sent;
            return TableOrderFinancialPolicy.CanChangeServiceCharge(
                !_isCollectionOrder && !_isDeliveryOrder,
                _currentOrder.ServiceChargePercent,
                _currentOrder.ServiceChargeStatus,
                modifiableStatus,
                _currentOrder.TotalPaid);
        }

        private async Task ChangeServiceChargeStatusAsync(bool remove)
        {
            if (!CanChangeServiceCharge())
            {
                await AppAlertService.ShowAlertAsync("Service Charge", "This order can no longer change its service charge.");
                return;
            }

            var reason = remove ? await SelectServiceChargeRemovalReasonAsync() : "Service charge restored";
            if (string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            var pinDialog = new StyledPromptDialog();
            pinDialog.SetDialog(
                "Manager Approval",
                remove ? "Enter a manager PIN to remove the service charge." : "Enter a manager PIN to restore the service charge.",
                "4-digit manager PIN",
                Keyboard.Numeric,
                string.Empty);
            var pin = await pinDialog.ShowAsync();
            if (string.IsNullOrWhiteSpace(pin))
            {
                return;
            }

            var approval = await _authService.ValidatePinAsync(pin);
            if (!approval.Success || approval.User == null || approval.User.Role is not (UserRole.Manager or UserRole.Admin))
            {
                await AppAlertService.ShowAlertAsync("Approval Failed", "A valid Manager or Administrator PIN is required.");
                return;
            }

            var amount = remove
                ? _currentOrder.ServiceCharge
                : TableServiceChargeCalculator.Calculate(
                    _currentOrder.OrderMode,
                    _currentOrder.Subtotal,
                    _currentOrder.Discount,
                    _currentOrder.ServiceChargePercent).ServiceCharge;
            var confirmation = new ModernConfirmDialog();
            confirmation.SetConfirm(
                remove ? "Remove Service Charge" : "Restore Service Charge",
                remove
                    ? $"Remove £{amount:F2} service charge?\nReason: {reason}"
                    : $"Restore £{amount:F2} service charge?",
                remove ? "Remove" : "Restore",
                "Cancel",
                string.Empty);
            if (!await confirmation.ShowAsync())
            {
                return;
            }

            var performedBy = _authService.CurrentUser ?? approval.User;
            var now = DateTime.Now;
            if (remove)
            {
                _currentOrder.ServiceChargeRemovalReason = reason;
                _currentOrder.ServiceChargeRemovedByUserId = performedBy.Id;
                _currentOrder.ServiceChargeRemovedByName = GetUserDisplayName(performedBy);
                _currentOrder.ServiceChargeApprovedByUserId = approval.User.Id;
                _currentOrder.ServiceChargeApprovedByName = GetUserDisplayName(approval.User);
                _currentOrder.ServiceChargeRemovedAt = now;
                _currentOrder.ServiceChargeStatus = TableServiceChargeStatus.Removed;
            }
            else
            {
                _currentOrder.ServiceChargeRemovalReason = null;
                _currentOrder.ServiceChargeRemovedByUserId = null;
                _currentOrder.ServiceChargeRemovedByName = null;
                _currentOrder.ServiceChargeApprovedByUserId = null;
                _currentOrder.ServiceChargeApprovedByName = null;
                _currentOrder.ServiceChargeRemovedAt = null;
                _currentOrder.ServiceChargeStatus = TableServiceChargeStatus.Applied;
            }

            await PersistDraftAsync(force: true);
            await _tableServiceChargeOrderAuditService.RecordAsync(
                _currentOrder.Id,
                remove ? "removed" : "restored",
                _currentOrder.ServiceChargePercent,
                _currentOrder.ServiceChargeBasis,
                amount,
                _currentOrder.ServiceChargeClassification,
                reason,
                performedBy,
                approval.User);
            UpdateDisplay();
            _ = ToastNotification.ShowAsync(
                remove ? "Service charge removed" : "Service charge restored",
                $"£{amount:F2} {(remove ? "removed" : "restored")}.",
                NotificationType.Success,
                1500);
        }

        private async Task<string?> SelectServiceChargeRemovalReasonAsync()
        {
            var dialog = new ModernActionSheetDialog();
            dialog.SetActionSheet(
                "Removal Reason",
                new List<string> { "Customer request", "Service issue", "Manager discretion", "Custom reason" },
                string.Empty);
            var selected = await dialog.ShowAsync();
            if (selected != "Custom reason")
            {
                return selected;
            }

            var customDialog = new StyledPromptDialog();
            customDialog.SetDialog("Custom Reason", "Enter the reason for removing the service charge:", "Reason", Keyboard.Text, string.Empty);
            return (await customDialog.ShowAsync())?.Trim();
        }

        private static string GetUserDisplayName(User user) =>
            !string.IsNullOrWhiteSpace(user.Name) ? user.Name : user.Username;

        private void OnOrderPlaceMenuClicked(object? sender, EventArgs e)
        {
            _inactivityService?.ResetActivity();
            if (Shell.Current != null)
            {
                Shell.Current.FlyoutIsPresented = true;
            }
        }

        private async void OnPayClicked(object? sender, EventArgs e)
        {
            _inactivityService.ResetActivity();
            using var idleGuard = _inactivityService.BeginCriticalActivity();

            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before proceeding to payment.", "i");
                await noItemsDialog.ShowAsync();
                return;
            }
            
            decimal tip = _currentOrder.TipAmount;
            decimal totalDue = _currentOrder.Total;
            
            // A removed configured charge still suppresses tipping for this table order.
            if (tip <= 0m && ShouldOfferTip() && _currentOrder.TotalPaid <= 0m)
            {
                var tipDialog = new TipSelectionDialog();
                tipDialog.SetOrderTotal(_currentOrder.Total);
                tip = await tipDialog.ShowAsync();
                
                if (tip == -1) // Cancelled
                {
                    return;
                }
                
                totalDue = _currentOrder.Total + tip;
                _currentOrder.TipAmount = tip;
            }
            else
            {
                totalDue = _currentOrder.Total + tip;
            }

            // Track remaining balance for partial payments
            decimal totalPaid = await GetExistingApprovedPaymentTotalAsync();
            decimal tipRemaining = Math.Max(0m, tip - _currentOrder.Payments
                .Where(payment => payment.Amount > 0m)
                .Sum(payment => payment.TipAmount));
            decimal remainingBalance = Math.Max(0, totalDue - totalPaid);

            if (remainingBalance <= 0)
            {
                await CompletePaymentFromLedgerAsync(totalDue, tip);
                return;
            }

            if (totalPaid > 0)
            {
                _ = ToastNotification.ShowAsync(
                    "Partial payment",
                    $"£{totalPaid:F2} paid. £{remainingBalance:F2} remaining.",
                    NotificationType.Info,
                    1500);
            }

            // Collection and delivery are single-bill takeaway orders. Send
            // them directly to full payment; split, item and custom partial
            // payment choices remain available for table orders only.
            var splitPlan = IsTakeawayStyleOrder()
                ? PaymentSplitPlan.Full(totalDue)
                : await ShowPaymentSplitPlanDialog(totalDue, remainingBalance);
            if (splitPlan == null)
            {
                return;
            }

            splitPlan.ApplyExistingPaid(totalPaid);
            decimal currentSplitRemaining = splitPlan.GetNextAmount(remainingBalance);
            // Production payments must always have an auditable payment line. This is
            // required for manual card-terminal references and daily reconciliation,
            // including ordinary (non-split) orders.
            const bool shouldRecordPaymentLines = true;

            if (shouldRecordPaymentLines)
            {
                if (!await CommitOrderForActionAsync(GetDraftLifecycleState()))
                {
                    await AppAlertService.ShowAlertAsync(
                        "Payment Not Started",
                        "The order could not be created safely. No payment was attempted.");
                    return;
                }

                await EnsureTableSessionContextAsync(skipOrderLink: true, allowSessionOpen: false);
                if (_tableSessionId.HasValue)
                {
                    await _tableSessionService.MarkSessionPaymentAsync(_tableSessionId.Value);
                }
            }
            
            // Payment loop for partial payments
            while (remainingBalance > 0)
            {
                var paymentAmount = splitPlan.TracksPartRemaining
                    ? Math.Min(currentSplitRemaining, remainingBalance)
                    : splitPlan.GetNextAmount(remainingBalance);

                if (paymentAmount <= 0)
                {
                    paymentAmount = remainingBalance;
                }

                // Step 2: Show payment method selection
                var methodDialog = new PaymentMethodDialog();
                methodDialog.SetAmountDue(
                    paymentAmount,
                    Math.Max(0, remainingBalance - paymentAmount),
                    splitPlan.GetPaymentTitle());
                var paymentMethod = await methodDialog.ShowAsync();
                
                if (paymentMethod == PaymentMethod.Cancelled)
                {
                    if (totalPaid > 0)
                    {
                        await SavePartialPaymentOrderAsync(totalDue, tip, totalPaid, remainingBalance, splitPlan);
                        _ = ToastNotification.ShowAsync(
                            "Partial payment saved",
                            $"£{totalPaid:F2} paid. £{remainingBalance:F2} remaining.",
                            NotificationType.Warning,
                            1600);
                    }
                    return;
                }
                
                // Step 3: Process selected payment method
                await LogOperationalEventAsync("payment_attempt", new
                {
                    method = paymentMethod.ToString(),
                    amountDue = paymentAmount,
                    totalRemaining = remainingBalance,
                    split = splitPlan.RequiresPaymentLine ? splitPlan.GetPaymentTitle() : null
                });

                if (shouldRecordPaymentLines)
                {
                    var actor = ResolveCurrentActor();
                    await _orderService.RecordPaymentLineAsync(
                        _currentOrder.Id,
                        paymentMethod.ToString(),
                        paymentAmount,
                        "attempted",
                        0,
                        null,
                        actor.ActorName,
                        new { amountDue = paymentAmount, totalRemaining = remainingBalance, split = splitPlan.ToMetadata(), orderMode = _currentOrder.OrderMode });
                }

                decimal paidThisAttempt = 0;
                decimal amountReceivedThisAttempt = 0;
                decimal changeThisAttempt = 0;
                decimal tipThisAttempt = 0;
                string? paymentReferenceThisAttempt = null;

                switch (paymentMethod)
                {
                    case PaymentMethod.Cash:
                        var cashResult = await ProcessCashPayment(paymentAmount);
                        if (cashResult.Success)
                        {
                            paidThisAttempt = cashResult.AmountPaid;
                            tipThisAttempt = AllocateTipForPayment(tipRemaining, paidThisAttempt, remainingBalance);
                            paymentReferenceThisAttempt = BuildPaymentTransactionId("cash", paidThisAttempt, totalPaid, splitPlan.GetPaymentTitle());
                            amountReceivedThisAttempt = cashResult.AmountReceived;
                            changeThisAttempt = cashResult.Change;
                            await LogOperationalEventAsync("payment_approved", new
                            {
                                method = "cash",
                                amountPaid = cashResult.AmountPaid,
                                remaining = Math.Max(0, remainingBalance - cashResult.AmountPaid),
                                split = splitPlan.RequiresPaymentLine ? splitPlan.GetPaymentTitle() : null
                            });
                            if (shouldRecordPaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                var paymentSaved = await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "cash", cashResult.AmountPaid, "approved", tipThisAttempt, paymentReferenceThisAttempt, actor.ActorName, new
                                {
                                    split = splitPlan.ToMetadata(),
                                    chargeAmount = paymentAmount,
                                    amountReceived = cashResult.AmountReceived,
                                    change = cashResult.Change
                                }, maximumApprovedTotal: totalDue);
                                if (!paymentSaved)
                                {
                                    await ShowPaymentSaveFailureAsync();
                                    return;
                                }
                            }
                        }
                        else
                        {
                            await LogOperationalEventAsync("payment_failed", new { method = "cash", amountDue = paymentAmount, split = splitPlan.RequiresPaymentLine ? splitPlan.GetPaymentTitle() : null });
                            if (shouldRecordPaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "cash", paymentAmount, "failed", 0, null, actor.ActorName, new { split = splitPlan.ToMetadata(), chargeAmount = paymentAmount });
                            }
                        }
                        break;
                        
                    case PaymentMethod.Card:
                        // Card tender is recorded immediately when the operator selects
                        // CARD. The separate manual terminal confirmation dialog is no
                        // longer part of the POS flow.
                        var directCardReference = BuildDirectCardReference(paymentAmount);
                        paidThisAttempt = paymentAmount;
                        tipThisAttempt = AllocateTipForPayment(tipRemaining, paidThisAttempt, remainingBalance);
                        amountReceivedThisAttempt = paymentAmount;
                        paymentReferenceThisAttempt = directCardReference;
                        await LogOperationalEventAsync("payment_approved", new
                        {
                            method = "card",
                            amountPaid = paymentAmount,
                            terminalReference = directCardReference,
                            captureMode = "direct_pos_selection",
                            remaining = Math.Max(0, remainingBalance - paymentAmount),
                            split = splitPlan.RequiresPaymentLine ? splitPlan.GetPaymentTitle() : null
                        });
                        if (shouldRecordPaymentLines)
                        {
                            var actor = ResolveCurrentActor();
                            var paymentSaved = await _orderService.RecordPaymentLineAsync(
                                _currentOrder.Id,
                                "card",
                                paymentAmount,
                                "approved",
                                tipThisAttempt,
                                directCardReference,
                                actor.ActorName,
                                new
                                {
                                    split = splitPlan.ToMetadata(),
                                    chargeAmount = paymentAmount,
                                    captureMode = "direct_pos_selection"
                                },
                                maximumApprovedTotal: totalDue);
                            if (!paymentSaved)
                            {
                                await ShowPaymentSaveFailureAsync();
                                return;
                            }
                        }
                        break;
                        
                    case PaymentMethod.GiftCard:
                        var giftTransactionId = BuildPaymentTransactionId("gift-card", paymentAmount, totalPaid, splitPlan.GetPaymentTitle());
                        var giftResult = await ProcessGiftCardPayment(paymentAmount, giftTransactionId);
                        if (giftResult.Success)
                        {
                            paidThisAttempt = giftResult.AmountApplied;
                            tipThisAttempt = AllocateTipForPayment(tipRemaining, paidThisAttempt, remainingBalance);
                            amountReceivedThisAttempt = giftResult.AmountApplied;
                            paymentReferenceThisAttempt = MaskGiftCardNumber(giftResult.GiftCardNumber);
                            await LogOperationalEventAsync("payment_approved", new
                            {
                                method = "gift_card",
                                amountPaid = giftResult.AmountApplied,
                                remaining = Math.Max(0, remainingBalance - giftResult.AmountApplied),
                                split = splitPlan.RequiresPaymentLine ? splitPlan.GetPaymentTitle() : null
                            });
                            if (shouldRecordPaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                var paymentSaved = await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "gift_card", giftResult.AmountApplied, "approved", tipThisAttempt, giftTransactionId, actor.ActorName, new
                                {
                                    split = splitPlan.ToMetadata(),
                                    chargeAmount = paymentAmount,
                                    giftCardNumber = MaskGiftCardNumber(giftResult.GiftCardNumber),
                                    previousCardBalance = giftResult.PreviousCardBalance,
                                    newCardBalance = giftResult.NewCardBalance,
                                    orderWebMessage = giftResult.OrderWebMessage,
                                    transactionId = giftTransactionId
                                }, maximumApprovedTotal: totalDue);
                                if (!paymentSaved)
                                {
                                    await ShowPaymentSaveFailureAsync();
                                    return;
                                }
                            }
                        }
                        else
                        {
                            await LogOperationalEventAsync("payment_failed", new { method = "gift_card", amountDue = paymentAmount, split = splitPlan.RequiresPaymentLine ? splitPlan.GetPaymentTitle() : null });
                            if (shouldRecordPaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "gift_card", paymentAmount, "failed", 0, null, actor.ActorName, new { split = splitPlan.ToMetadata(), chargeAmount = paymentAmount });
                            }
                        }
                        break;
                }

                if (paidThisAttempt > 0)
                {
                    paidThisAttempt = Math.Min(paidThisAttempt, remainingBalance);
                    AddReceiptPayment(
                        paymentMethod,
                        paidThisAttempt,
                        amountReceivedThisAttempt > 0 ? amountReceivedThisAttempt : paidThisAttempt,
                        changeThisAttempt,
                        tipThisAttempt,
                        paymentReferenceThisAttempt);
                    tipRemaining = Math.Max(0m, tipRemaining - tipThisAttempt);
                    totalPaid += paidThisAttempt;
                    remainingBalance = Math.Max(0, remainingBalance - paidThisAttempt);

                    if (splitPlan.TracksPartRemaining)
                    {
                        currentSplitRemaining = Math.Max(0, currentSplitRemaining - paidThisAttempt);
                        if (currentSplitRemaining <= 0.009m && remainingBalance > 0)
                        {
                            splitPlan.MarkPartComplete();
                            currentSplitRemaining = splitPlan.GetNextAmount(remainingBalance);
                        }
                    }

                    if (remainingBalance > 0)
                    {
                        await SavePartialPaymentOrderAsync(totalDue, tip, totalPaid, remainingBalance, splitPlan);

                        if (splitPlan.StopsAfterOnePartialPayment)
                        {
                            _ = ToastNotification.ShowAsync(
                                splitPlan.IsPayByItems ? "Item payment saved" : "Partial payment saved",
                                $"£{remainingBalance:F2} remaining.",
                                NotificationType.Info,
                                1500
                            );
                            return;
                        }
                    }
                }
            }
            
            // Payment complete - print receipt and close table
            await CompletePaymentFromLedgerAsync(totalDue, tip);
        }

        private async Task<PaymentSplitPlan?> ShowPaymentSplitPlanDialog(decimal totalDue, decimal remainingBalance)
        {
            while (true)
            {
                var setupDialog = new ModernActionSheetDialog();
                setupDialog.SetActionSheetGrid(
                    "Payment Setup",
                    new List<string> { "Pay Full", "Split Evenly", "Pay By Items", "Custom Amount" },
                    "£",
                    "#059669"
                );
                setupDialog.SetCancelText("Back");
                setupDialog.HighlightGridOption("Pay Full", "#DCFCE7", "#065F46", "#10B981");

                var selected = await setupDialog.ShowAsync();
                if (selected == null)
                {
                    return null;
                }

                if (selected == "Pay Full")
                {
                    return PaymentSplitPlan.Full(totalDue);
                }

                if (selected == "Custom Amount")
                {
                    var customPlan = await ShowCustomPaymentAmountDialog(totalDue, remainingBalance);
                    if (customPlan != null)
                    {
                        return customPlan;
                    }

                    continue;
                }

                if (selected == "Pay By Items")
                {
                    var itemPlan = await ShowPayByItemsDialog(totalDue, remainingBalance);
                    if (itemPlan != null)
                    {
                        return itemPlan;
                    }

                    continue;
                }

                if (selected == "Split Evenly")
                {
                    var splitPlan = await ShowEvenSplitPlanDialog(remainingBalance);
                    if (splitPlan != null)
                    {
                        return splitPlan;
                    }
                }
            }
        }

        private async Task<PaymentSplitPlan?> ShowEvenSplitPlanDialog(decimal remainingBalance)
        {
            var splitDialog = new ModernActionSheetDialog();
            splitDialog.SetActionSheetGrid(
                "Split Evenly",
                new List<string> { "Split by 2", "Split by 3", "Split by 4", "Split by 5", "Split by 6", "Custom Split" },
                "÷",
                "#2563EB"
            );
            splitDialog.SetCancelText("Back");

            var selected = await splitDialog.ShowAsync();
            if (selected == null)
            {
                return null;
            }

            if (selected == "Custom Split")
            {
                var prompt = new StyledPromptDialog();
                prompt.SetDialog(
                    "Custom Split",
                    "How many ways do you want to split this bill?",
                    "e.g., 3",
                    Keyboard.Numeric,
                    string.Empty,
                    true
                );
                prompt.SetCancelText("Back");

                var value = await prompt.ShowAsync();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                if (!int.TryParse(value.Trim(), out var customParts) || customParts < 2 || customParts > 20)
                {
                    var alert = new ModernAlertDialog();
                    alert.SetAlert("Invalid Split", "Enter a split number between 2 and 20.", "!", "#EF4444", "White");
                    await alert.ShowAsync();
                    return null;
                }

                return PaymentSplitPlan.Equal(remainingBalance, customParts);
            }

            var splitCount = selected switch
            {
                "Split by 2" => 2,
                "Split by 3" => 3,
                "Split by 4" => 4,
                "Split by 5" => 5,
                "Split by 6" => 6,
                _ => 1
            };

            return splitCount <= 1
                ? PaymentSplitPlan.Full(remainingBalance)
                : PaymentSplitPlan.Equal(remainingBalance, splitCount);
        }

        private async Task<PaymentSplitPlan?> ShowCustomPaymentAmountDialog(decimal totalDue, decimal remainingBalance)
        {
            var keyboard = new NumericKeyboardDialog();
            var value = await keyboard.ShowCurrencyAsync(null, "Custom Amount");
            if (!value.HasValue)
            {
                return null;
            }

            var customAmount = value.Value;
            if (customAmount <= 0 || customAmount > remainingBalance + 0.009m)
            {
                var alert = new ModernAlertDialog();
                alert.SetAlert("Invalid Amount", $"Enter an amount between £0.01 and £{remainingBalance:F2}.", "!", "#EF4444", "White");
                await alert.ShowAsync();
                return null;
            }

            return PaymentSplitPlan.Custom(totalDue, Math.Min(customAmount, remainingBalance));
        }

        private async Task<PaymentSplitPlan?> ShowPayByItemsDialog(decimal totalDue, decimal remainingBalance)
        {
            _currentOrder.RecalculateAll();

            var dialog = new PayByItemsDialog();
            dialog.SetOrder(
                _currentOrder.Items.Where(item => !IsTastingMenuCourseItem(item)).ToList(),
                _currentOrder.Subtotal,
                _currentOrder.ServiceCharge,
                _currentOrder.DeliveryFee,
                _currentOrder.Discount,
                remainingBalance);

            var result = await dialog.ShowAsync();
            if (!result.Success || result.Amount <= 0)
            {
                return null;
            }

            return PaymentSplitPlan.PayByItems(totalDue, Math.Min(result.Amount, remainingBalance), result.SelectedItems);
        }

        private static bool TryParsePaymentAmount(string input, out decimal amount)
        {
            var normalized = input
                .Replace("£", string.Empty)
                .Replace(",", string.Empty)
                .Trim();

            return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out amount)
                || decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
        }

        private async Task<decimal> GetExistingApprovedPaymentTotalAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_currentOrder.Id))
                {
                    return 0m;
                }

                var existingOrder = await _orderService.GetOrderByExternalIdAsync(_currentOrder.Id);
                if (existingOrder == null || existingOrder.Id <= 0)
                {
                    return 0m;
                }

                var payments = await _orderService.GetOrderPaymentsAsync(existingOrder.Id);
                return payments
                    .Where(payment => string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase))
                    .Sum(payment => payment.Amount);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading existing approved payments: {ex.Message}");
                return 0m;
            }
        }

        private async Task SavePartialPaymentOrderAsync(
            decimal totalAmount,
            decimal tip,
            decimal totalPaid,
            decimal remainingBalance,
            PaymentSplitPlan splitPlan)
        {
            try
            {
                var order = await BuildPersistentOrderSnapshotAsync(LocalLifecycleState.PaymentPartial);
                if (order == null)
                {
                    return;
                }

                order.TotalAmount = totalAmount;
                order.PaymentMethod = splitPlan.IsSplit ? "split" : "partial";
                var saveResult = await _orderService.SaveOrderAsync(order);
                if (!saveResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving partial payment order: {saveResult.Message}");
                    return;
                }

                await LogOperationalEventAsync("state_changed", new
                {
                    to = "payment_partial",
                    totalAmount,
                    tip,
                    totalPaid,
                    remainingBalance,
                    split = splitPlan.ToMetadata()
                }, DateTime.Now);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving partial payment order: {ex.Message}");
            }
        }

        private sealed class PaymentSplitPlan
        {
            private PaymentSplitPlan(
                decimal totalAmount,
                int totalParts,
                PaymentSplitMode mode,
                decimal customAmount = 0,
                IReadOnlyList<PayByItemsSelection>? selectedItems = null)
            {
                TotalAmount = totalAmount;
                TotalParts = Math.Max(1, totalParts);
                CurrentPart = 1;
                Mode = mode;
                CustomAmount = customAmount;
                SelectedItems = selectedItems ?? Array.Empty<PayByItemsSelection>();
                AmountPerPart = TotalParts <= 1
                    ? totalAmount
                    : Math.Round(totalAmount / TotalParts, 2, MidpointRounding.AwayFromZero);
            }

            public PaymentSplitMode Mode { get; }
            public decimal TotalAmount { get; }
            public int TotalParts { get; }
            public int CurrentPart { get; private set; }
            public decimal AmountPerPart { get; }
            public decimal CustomAmount { get; }
            public IReadOnlyList<PayByItemsSelection> SelectedItems { get; }
            public bool IsSplit => Mode == PaymentSplitMode.EqualSplit;
            public bool IsCustomAmount => Mode == PaymentSplitMode.CustomAmount;
            public bool IsPayByItems => Mode == PaymentSplitMode.PayByItems;
            public bool RequiresPaymentLine => Mode != PaymentSplitMode.Full;
            public bool TracksPartRemaining => Mode == PaymentSplitMode.EqualSplit;
            public bool StopsAfterOnePartialPayment => Mode is PaymentSplitMode.CustomAmount or PaymentSplitMode.PayByItems;

            public static PaymentSplitPlan Full(decimal totalAmount) => new(totalAmount, 1, PaymentSplitMode.Full);

            public static PaymentSplitPlan Equal(decimal totalAmount, int totalParts) => new(totalAmount, totalParts, PaymentSplitMode.EqualSplit);

            public static PaymentSplitPlan Custom(decimal totalAmount, decimal customAmount) => new(totalAmount, 1, PaymentSplitMode.CustomAmount, customAmount);

            public static PaymentSplitPlan PayByItems(decimal totalAmount, decimal amount, IReadOnlyList<PayByItemsSelection> selectedItems)
                => new(totalAmount, 1, PaymentSplitMode.PayByItems, amount, selectedItems);

            public decimal GetNextAmount(decimal remainingBalance)
            {
                if (Mode is PaymentSplitMode.CustomAmount or PaymentSplitMode.PayByItems)
                {
                    return Math.Min(CustomAmount, remainingBalance);
                }

                if (Mode == PaymentSplitMode.Full)
                {
                    return remainingBalance;
                }

                var remainingParts = TotalParts - CurrentPart + 1;
                return remainingParts <= 1
                    ? remainingBalance
                    : Math.Min(remainingBalance, AmountPerPart);
            }

            public void MarkPartComplete()
            {
                if (Mode == PaymentSplitMode.EqualSplit && CurrentPart < TotalParts)
                {
                    CurrentPart++;
                }
            }

            public void ApplyExistingPaid(decimal totalPaid)
            {
                if (Mode != PaymentSplitMode.EqualSplit || totalPaid <= 0 || AmountPerPart <= 0)
                {
                    return;
                }

                var paidRemaining = totalPaid;
                CurrentPart = 1;
                while (CurrentPart < TotalParts && paidRemaining >= AmountPerPart - 0.009m)
                {
                    paidRemaining -= AmountPerPart;
                    CurrentPart++;
                }
            }

            public string GetPaymentTitle()
            {
                return Mode switch
                {
                    PaymentSplitMode.EqualSplit => $"SPLIT PAYMENT {CurrentPart} OF {TotalParts}",
                    PaymentSplitMode.CustomAmount => "CUSTOM PAYMENT",
                    PaymentSplitMode.PayByItems => "PAY BY ITEMS",
                    _ => "SELECT PAYMENT METHOD"
                };
            }

            public object ToMetadata()
            {
                return new
                {
                    isSplit = IsSplit,
                    mode = Mode.ToString(),
                    totalParts = TotalParts,
                    currentPart = CurrentPart,
                    amountPerPart = AmountPerPart,
                    customAmount = CustomAmount,
                    selectedItems = SelectedItems.Select(item => new
                    {
                        item.ItemId,
                        item.ItemName,
                        item.Quantity,
                        item.Amount
                    }).ToList(),
                    totalAmount = TotalAmount
                };
            }
        }

        private enum PaymentSplitMode
        {
            Full,
            EqualSplit,
            CustomAmount,
            PayByItems
        }

        private async Task<CashPaymentResult> ProcessCashPayment(decimal amountDue)
        {
            var cashDialog = new CashPaymentDialog();
            cashDialog.SetAmountDue(amountDue);
            return await cashDialog.ShowAsync();
        }

        private async Task<GiftCardPaymentResult> ProcessGiftCardPayment(decimal amountDue, string transactionId)
        {
            var giftDialog = new GiftCardPaymentDialog();
            giftDialog.SetAmountDue(amountDue, GetReceiptOrderReference(), transactionId);
            return await giftDialog.ShowAsync();
        }

        private void AddReceiptPayment(
            PaymentMethod paymentMethod,
            decimal amount,
            decimal amountReceived,
            decimal change,
            decimal tipAmount,
            string? reference)
        {
            if (amount <= 0)
            {
                return;
            }

            var method = paymentMethod switch
            {
                PaymentMethod.Card => PaymentMethodType.Card,
                PaymentMethod.GiftCard => PaymentMethodType.GiftCard,
                PaymentMethod.Cash => PaymentMethodType.Cash,
                _ => PaymentMethodType.Cash
            };

            _currentOrder.Payments.Add(new TableOrderPayment
            {
                Id = Guid.NewGuid().ToString("N"),
                OrderId = _currentOrder.Id,
                Method = method,
                Amount = amount,
                AmountReceived = amountReceived,
                Change = change,
                TipAmount = tipAmount,
                Reference = reference,
                CreatedAt = DateTime.Now,
                StaffName = _currentUser?.Name ?? _currentUser?.Username ?? string.Empty
            });
        }

        private static PaymentMethodType ParsePaymentMethodType(string? paymentMethod)
        {
            var normalized = (paymentMethod ?? string.Empty)
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty);

            return normalized.ToLowerInvariant() switch
            {
                "card" => PaymentMethodType.Card,
                "giftcard" => PaymentMethodType.GiftCard,
                _ => PaymentMethodType.Cash
            };
        }

        private string? BuildPersistentPaymentMethod(LocalLifecycleState lifecycleState)
        {
            if (lifecycleState != LocalLifecycleState.Paid)
            {
                return string.IsNullOrWhiteSpace(_requestedPaymentMethod)
                    ? null
                    : OnlineOrderPaymentHelper.GetStorageMethod(_requestedPaymentMethod);
            }

            var methods = _currentOrder.Payments
                .Where(payment => payment.Amount > 0)
                .Select(payment => payment.Method switch
                {
                    PaymentMethodType.Card => "card",
                    PaymentMethodType.GiftCard => "gift_card",
                    _ => "cash"
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return methods.Count switch
            {
                0 => string.IsNullOrWhiteSpace(_requestedPaymentMethod)
                    ? null
                    : OnlineOrderPaymentHelper.GetStorageMethod(_requestedPaymentMethod),
                1 => methods[0],
                _ => "split"
            };
        }

        private string BuildPaymentTransactionId(string method, decimal amount, decimal alreadyPaid, string? splitTitle = null)
        {
            var orderReference = GetReceiptOrderReference();
            var splitPart = string.IsNullOrWhiteSpace(splitTitle) ? "single" : splitTitle.Trim();
            return $"{orderReference}:{method}:{amount:F2}:{alreadyPaid:F2}:{splitPart}";
        }

        private async Task<decimal?> ReloadApprovedReceiptPaymentsAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_currentOrder.Id))
                {
                    return null;
                }

                var existingOrder = await _orderService.GetOrderByExternalIdAsync(_currentOrder.Id);
                if (existingOrder == null || existingOrder.Id <= 0)
                {
                    return null;
                }

                var approvedPayments = (await _orderService.GetOrderPaymentsAsync(existingOrder.Id))
                    .Where(payment => string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                _currentOrder.Payments.Clear();
                foreach (var payment in approvedPayments)
                {
                    _currentOrder.Payments.Add(new TableOrderPayment
                    {
                        Id = payment.Id.ToString(CultureInfo.InvariantCulture),
                        OrderId = _currentOrder.Id,
                        Method = ParsePaymentMethodType(payment.PaymentMethod),
                        Amount = payment.Amount,
                        AmountReceived = GetPaymentMetadataAmount(payment.MetadataJson, "amountReceived", payment.Amount),
                        Change = GetPaymentMetadataAmount(payment.MetadataJson, "change", 0m),
                        TipAmount = payment.TipAmount,
                        Reference = payment.Reference,
                        CreatedAt = payment.CreatedAt,
                        StaffName = payment.CreatedBy ?? string.Empty
                    });
                }

                return approvedPayments.Sum(payment => payment.Amount);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing approved payments: {ex.Message}");
                return null;
            }
        }

        private async Task CompletePaymentFromLedgerAsync(decimal totalAmount, decimal tip)
        {
            var approvedTotal = await ReloadApprovedReceiptPaymentsAsync();
            if (!approvedTotal.HasValue)
            {
                await ShowPaymentSaveFailureAsync();
                return;
            }

            var difference = totalAmount - approvedTotal.Value;
            if (Math.Abs(difference) > 0.009m)
            {
                var message = difference > 0
                    ? $"The bill still has Â£{difference:F2} to pay. Existing payments were kept, so you can safely continue."
                    : $"Payments are Â£{Math.Abs(difference):F2} above the bill total. The order remains open for a manager to review; no extra payment was added.";
                await AppAlertService.ShowAlertAsync("Review Payment", message);
                return;
            }

            await CompletePayment(totalAmount, tip, approvedTotal.Value);
        }

        private static async Task ShowPaymentSaveFailureAsync()
        {
            await AppAlertService.ShowAlertAsync(
                "Payment Not Saved",
                "This payment could not be added to the bill. The order is still open. Check the connection, then review the existing payments before trying again.");
        }

        private bool ShouldOfferTip()
        {
            return TableOrderFinancialPolicy.ShouldOfferTip(
                _isCollectionOrder || _isDeliveryOrder,
                _currentOrder.ServiceChargeStatus,
                _currentOrder.ServiceChargePercent);
        }

        private static decimal AllocateTipForPayment(decimal tipRemaining, decimal paymentAmount, decimal remainingBalance)
        {
            return TableOrderFinancialPolicy.AllocateTip(tipRemaining, paymentAmount, remainingBalance);
        }

        private static decimal GetPaymentMetadataAmount(string? metadataJson, string propertyName, decimal fallback)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                return fallback;
            }

            try
            {
                using var document = JsonDocument.Parse(metadataJson);
                if (document.RootElement.TryGetProperty(propertyName, out var value)
                    && value.TryGetDecimal(out var amount))
                {
                    return amount;
                }
            }
            catch (JsonException)
            {
                // Older payment records may not have structured metadata.
            }

            return fallback;
        }

        private string BuildDirectCardReference(decimal amount)
        {
            var orderReference = GetReceiptOrderReference()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("#", string.Empty, StringComparison.Ordinal);
            return $"POS-CARD-{orderReference}-{amount:0.00}-{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        }

        private static string? MaskGiftCardNumber(string? cardNumber)
        {
            if (string.IsNullOrWhiteSpace(cardNumber))
            {
                return null;
            }

            var trimmed = cardNumber.Trim();
            return trimmed.Length <= 4
                ? trimmed
                : $"{new string('*', Math.Max(0, trimmed.Length - 4))}{trimmed[^4..]}";
        }

        private async Task CompletePayment(decimal totalAmount, decimal tip, decimal totalPaid)
        {
            var paymentNotice = BuildPaymentCompletionNotice(totalAmount);

            // Persist every paid order (collection, delivery, and table).
            _isFinalizingOrder = true;
            var saved = await SavePaidOrder(totalAmount, tip, totalPaid);
            if (!saved)
            {
                _isFinalizingOrder = false;
                return;
            }

            await EnsureTableReleasedAfterFinalizeAsync("payment_completed", "system", new
            {
                totalAmount,
                tip,
                totalPaid
            });
            
            // Show payment success without blocking receipt printing.
            _ = ToastNotification.ShowAsync(
                paymentNotice.Title,
                paymentNotice.Message,
                NotificationType.Success,
                1200
            );
            
            // Print receipt (always attempt, but do not block payment completion).
            var receiptPrinted = await PrintReceipt(totalAmount, tip, isFinalPaymentReceipt: true);
            if (!receiptPrinted)
            {
                var receiptFailedDialog = new ModernAlertDialog();
                receiptFailedDialog.SetAlert("Receipt Failed", "Payment is saved, but the receipt could not print. Please check the receipt printer setup.", "!", "#EF4444", "White");
                await receiptFailedDialog.ShowAsync();
            }
            
            // Close table and navigate back
            await CloseTable();
        }

        private (string Title, string Message) BuildPaymentCompletionNotice(decimal totalAmount)
        {
            var methods = _currentOrder.Payments
                .Where(payment => payment.Amount > 0)
                .Select(payment => payment.Method)
                .Distinct()
                .ToList();

            if (methods.Count == 1 && methods[0] == PaymentMethodType.Card)
            {
                return ("Paid by card", $"£{totalAmount:F2} paid. Printing receipt...");
            }

            if (methods.Count == 1 && methods[0] == PaymentMethodType.Cash)
            {
                return ("Paid by cash", $"£{totalAmount:F2} paid. Printing receipt...");
            }

            if (methods.Count == 1 && methods[0] == PaymentMethodType.GiftCard)
            {
                return ("Paid by gift card", $"£{totalAmount:F2} paid. Printing receipt...");
            }

            return ("Payment complete", $"£{totalAmount:F2} paid. Printing receipt...");
        }

        private async Task<bool> SavePaidOrder(decimal totalAmount, decimal tip, decimal totalPaid)
        {
            try
            {
                if (_rolloutConfig.EnableStrictFinalizeRules)
                {
                    var unsentCount = _currentOrder.Items.Count(i => i.SendStatus == ItemSendStatus.NotSent);
                    if (unsentCount > 0)
                    {
                        var unsentDialog = new ModernAlertDialog();
                        unsentDialog.SetAlert("Finalize Blocked", $"{unsentCount} item(s) are not sent yet.", "", "#EF4444", "White");
                        await unsentDialog.ShowAsync();
                        return false;
                    }

                    if (Math.Abs(totalPaid - totalAmount) > 0.009m)
                    {
                        var balanceDialog = new ModernAlertDialog();
                        balanceDialog.SetAlert("Finalize Blocked", "Payment is not fully settled.", "", "#EF4444", "White");
                        await balanceDialog.ShowAsync();
                        return false;
                    }
                }

                var collectionCustomerService = new CollectionCustomerService();
                var deliveryCustomerService = new DeliveryCustomerService();
                var order = await BuildPersistentOrderSnapshotAsync(LocalLifecycleState.Paid, paidAt: DateTime.Now);
                if (order == null)
                {
                    return false;
                }

                order.TotalAmount = totalAmount;
                order.PaidAt = DateTime.Now;
                order.CompletedTime = DateTime.Now;
                var saveResult = await _orderService.SaveOrderAsync(order);
                if (!saveResult.Success)
                {
                    if (IsConcurrencyConflict(saveResult.Message))
                    {
                        await ShowConcurrencyConflictDialogAsync();
                    }
                    System.Diagnostics.Debug.WriteLine($"Error saving paid order: {saveResult.Message}");
                    return false;
                }

                _lastSavedAt = order.UpdatedAt == default ? DateTime.Now : order.UpdatedAt;
                ActiveTableOrderCacheService.Remove(order.OrderId, _tableSessionId, _currentOrder.TableNumber.ToString());

                await LogOperationalEventAsync("state_changed", new
                {
                    to = "paid",
                    totalAmount,
                    tip,
                    totalPaid = totalAmount
                }, DateTime.Now);
                
                // Update customer last order date for customer-based orders.
                if (_isCollectionOrder && _collectionCustomerId > 0)
                {
                    await collectionCustomerService.UpdateLastOrderDateAsync(_collectionCustomerId);
                }

                if (_isDeliveryOrder && _deliveryCustomerId > 0)
                {
                    await deliveryCustomerService.UpdateLastOrderDateAsync(_deliveryCustomerId);
                }

                await SendOrderWebSettlementIfNeededAsync(order);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving paid order: {ex.Message}");
                return false;
            }
        }

        private async Task PrintFullOrderVoidAsync(string reason, User approvingUser)
        {
            try
            {
                var persistedOrder = await _orderService.GetOrderByExternalIdAsync(_currentOrder.Id);
                if (persistedOrder == null)
                {
                    return;
                }

                var actor = ResolveActor(approvingUser);
                var revision = await _kitchenRevisionService.CreateFullVoidRevisionAsync(
                    persistedOrder.Id,
                    CloneOrderForSend(_currentOrder),
                    actor.ActorName,
                    reason);
                if (revision == null)
                {
                    return;
                }

                var printOrder = _kitchenRevisionService.BuildPrintOrder(_currentOrder, revision);
                var result = IsTakeawayStyleOrder()
                    ? await _orderRoutingPrintService.PrintTakeawayOrderAsync(printOrder, GetCanonicalOrderType())
                    : await _orderRoutingPrintService.PrintOrderAsync(printOrder);
                await _kitchenRevisionService.MarkPrintResultAsync(revision, result);
                await LogOperationalEventAsync("resend", new
                {
                    kitchenRevision = revision.RevisionNumber,
                    ticketType = revision.TicketType,
                    reason,
                    printedCount = result.PrintedItemIds.Count,
                    failedRoutes = result.FailedRoutes
                }, actorOverride: approvingUser);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KITCHEN REVISION] Full void print failed: {ex.Message}");
            }
        }

        private static bool HasReachedKitchen(TableOrderItem item) =>
            item.SendStatus is ItemSendStatus.Sent or ItemSendStatus.Preparing or ItemSendStatus.Ready or ItemSendStatus.Served;

        private static void MarkKitchenChangePending(TableOrderItem item, string? originalNote)
        {
            if (HasReachedKitchen(item)
                && !string.Equals(originalNote?.Trim(), item.Notes?.Trim(), StringComparison.Ordinal))
            {
                item.SendStatus = ItemSendStatus.NotSent;
            }
        }

        private static TableOrderItem CreateAdditionalUnit(TableOrderItem source)
        {
            return new TableOrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = source.OrderId,
                MenuItemId = source.MenuItemId,
                VariantId = source.VariantId,
                VariantName = source.VariantName,
                DisplayName = source.DisplayName,
                Name = source.Name,
                Quantity = 1,
                UnitPrice = source.UnitPrice,
                VatCategory = source.VatCategory,
                PrintGroupId = source.PrintGroupId,
                PrintInRed = source.PrintInRed,
                Notes = source.Notes,
                Modifiers = source.Modifiers,
                CourseType = source.CourseType,
                SendStatus = ItemSendStatus.NotSent,
                CreatedAt = DateTime.Now,
                SelectedAddons = new ObservableCollection<SelectedAddon>(source.SelectedAddons.Select(addon => new SelectedAddon
                {
                    Id = addon.Id,
                    Name = addon.Name,
                    Price = addon.Price
                }))
            };
        }

        private async Task SendOrderWebSettlementIfNeededAsync(Order order)
        {
            if (!string.Equals(order.SourceChannel, "web", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                if (cloudService == null)
                {
                    System.Diagnostics.Debug.WriteLine("OrderWeb settlement skipped: CloudOrderService unavailable");
                    return;
                }

                var actor = ResolveCurrentActor();
                var sentOrQueued = await cloudService.SendOrderSettlementAsync(
                    order,
                    status: "paid",
                    staffId: actor.ActorId,
                    staffName: actor.ActorName,
                    notes: BuildOrderWebSettlementNotes(order));

                System.Diagnostics.Debug.WriteLine(sentOrQueued
                    ? $"OrderWeb settlement sent/queued for {order.OrderId}"
                    : $"OrderWeb settlement not sent for {order.OrderId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrderWeb settlement warning: {ex.Message}");
            }
        }

        private static string? BuildOrderWebSettlementNotes(Order order)
        {
            if (string.IsNullOrWhiteSpace(order.PaymentMethod))
            {
                return null;
            }

            return $"POS tender: {order.PaymentMethod}";
        }

        private async Task<bool> SaveVoidedOrderAsync(string reason, User approvingUser)
        {
            try
            {
                var approverName = !string.IsNullOrWhiteSpace(approvingUser.Name)
                    ? approvingUser.Name
                    : approvingUser.Username;
                var order = await BuildPersistentOrderSnapshotAsync(
                    LocalLifecycleState.Voided,
                    voidReason: reason,
                    voidedBy: approverName,
                    voidedAt: DateTime.Now);
                if (order == null)
                {
                    return false;
                }

                var saveResult = await _orderService.SaveOrderAsync(order);
                if (!saveResult.Success)
                {
                    if (IsConcurrencyConflict(saveResult.Message))
                    {
                        await ShowConcurrencyConflictDialogAsync();
                    }
                    System.Diagnostics.Debug.WriteLine($"Error saving voided order: {saveResult.Message}");
                    return false;
                }

                _lastSavedAt = order.UpdatedAt == default ? DateTime.Now : order.UpdatedAt;
                ActiveTableOrderCacheService.Remove(order.OrderId, _tableSessionId, _currentOrder.TableNumber.ToString());

                await LogOperationalEventAsync("voided", new
                {
                    reason,
                    itemCount = _currentOrder.Items.Count,
                    amount = _currentOrder.Total,
                    approvedByUserId = approvingUser.Id,
                    approvedByName = approverName
                }, DateTime.Now, approvingUser);

                await EnsureTableReleasedAfterFinalizeAsync("voided", approverName, new
                {
                    orderId = order.OrderId,
                    orderNumber = order.OrderNumber,
                    reason
                });

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving voided order: {ex.Message}");
                return false;
            }
        }

        private string GetCanonicalOrderType()
        {
            if (_isDeliveryOrder)
            {
                return "delivery";
            }

            if (_isCollectionOrder)
            {
                return "pickup";
            }

            return "table";
        }

        private string GetPricingOrderType()
        {
            return _isDeliveryOrder || _isCollectionOrder ? "takeaway" : "table";
        }

        private static bool IsTableOrderType(string? orderType) =>
            string.Equals(orderType?.Trim(), "table", StringComparison.OrdinalIgnoreCase);

        private static TableServiceChargeStatus ParseServiceChargeStatus(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "applied" => TableServiceChargeStatus.Applied,
                "removed" => TableServiceChargeStatus.Removed,
                _ => TableServiceChargeStatus.NotConfigured
            };

        private static ServiceChargeClassification? ParseServiceChargeClassification(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "optional" => ServiceChargeClassification.Optional,
                "compulsory" => ServiceChargeClassification.Compulsory,
                _ => null
            };

        private static string ToDatabaseValue(TableServiceChargeStatus value) => value switch
        {
            TableServiceChargeStatus.Applied => "applied",
            TableServiceChargeStatus.Removed => "removed",
            _ => "not_configured"
        };

        private static string? ToDatabaseValue(ServiceChargeClassification? value) => value switch
        {
            ServiceChargeClassification.Optional => "optional",
            ServiceChargeClassification.Compulsory => "compulsory",
            _ => null
        };

        private bool IsTakeawayStyleOrder()
        {
            return _isDeliveryOrder || _isCollectionOrder;
        }

        private void SyncCurrentOrderMode()
        {
            if (_currentOrder == null)
            {
                return;
            }

            _currentOrder.OrderMode = _isDeliveryOrder || _isCollectionOrder ? "takeaway" : "dine_in";
        }

        private string GetSavedOrderTypeLabel()
        {
            if (_isDeliveryOrder)
            {
                return "Delivery";
            }

            if (_isCollectionOrder)
            {
                return "Collection";
            }

            return "Table";
        }

        private async Task<bool> PrintReceipt(decimal total, decimal tip, bool isFinalPaymentReceipt = false)
        {
            using var idleGuard = _inactivityService.BeginCriticalActivity();

            try
            {
                _currentOrder.RecalculateAll();
                await EnsureOrderNumberAssignedAsync();

                var receiptPrinter = await ResolveLocalReceiptPrinterAsync();
                if (receiptPrinter == null)
                {
                    await LogOperationalEventAsync("receipt_print_failed", new
                    {
                        reason = "receipt_printer_not_configured",
                        orderType = GetCanonicalOrderType()
                    });
                    return false;
                }

                var printerService = ServiceHelper.GetService<NetworkPrinterService>() ?? new NetworkPrinterService();
                var receiptData = await BuildLocalReceiptDataAsync(receiptPrinter, total, tip, isFinalPaymentReceipt);
                var sent = await printerService.SendToPrinterAsync(receiptPrinter, receiptData);

                await LogOperationalEventAsync(sent ? "receipt_printed" : "receipt_print_failed", new
                {
                    printerId = receiptPrinter.Id,
                    printerName = receiptPrinter.Name,
                    orderType = GetCanonicalOrderType(),
                    amount = total
                });

                return sent;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Receipt print failed: {ex.Message}");
                await LogOperationalEventAsync("receipt_print_failed", new
                {
                    reason = ex.Message,
                    orderType = GetCanonicalOrderType()
                });
                return false;
            }
        }

        private async Task<NetworkPrinter?> ResolveLocalReceiptPrinterAsync()
        {
            var printerDb = ServiceHelper.GetService<NetworkPrinterDatabaseService>();
            if (printerDb == null)
            {
                return null;
            }

            var routingService = ServiceHelper.GetService<PrinterRoutingService>();
            if (routingService != null)
            {
                return IsTakeawayStyleOrder()
                    ? await routingService.ResolvePrinterAsync(
                        NetworkPrinterType.Receipt,
                        NetworkPrinterType.Online,
                        NetworkPrinterType.Takeaway,
                        NetworkPrinterType.Kitchen)
                    : await routingService.ResolvePrinterAsync(
                        NetworkPrinterType.Receipt,
                        NetworkPrinterType.Online);
            }

            var receiptPrinters = await printerDb.GetPrintersByTypeAsync(NetworkPrinterType.Receipt);
            var receiptPrinter = receiptPrinters.FirstOrDefault(printer => printer.IsEnabled);
            if (receiptPrinter != null)
            {
                return receiptPrinter;
            }

            if (IsTakeawayStyleOrder())
            {
                var onlineReceiptPrinters = await printerDb.GetPrintersByTypeAsync(NetworkPrinterType.Online);
                var onlinePrinter = onlineReceiptPrinters.FirstOrDefault(printer => printer.IsEnabled);
                if (onlinePrinter != null)
                {
                    return onlinePrinter;
                }

                foreach (var fallbackType in new[] { NetworkPrinterType.Takeaway, NetworkPrinterType.Kitchen })
                {
                    var fallbackPrinter = (await printerDb.GetPrintersByTypeAsync(fallbackType))
                        .FirstOrDefault(printer => printer.IsEnabled);
                    if (fallbackPrinter != null)
                    {
                        return fallbackPrinter;
                    }
                }
            }

            return null;
        }

        private async Task<byte[]> BuildLocalReceiptDataAsync(NetworkPrinter printer, decimal total, decimal tip, bool isFinalPaymentReceipt)
        {
            var businessService = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
            var businessInfo = await businessService.GetBusinessInfoAsync();

            if (_isCollectionOrder || _isDeliveryOrder)
            {
                var receiptKind = _isDeliveryOrder ? CustomerReceiptKind.Delivery : CustomerReceiptKind.Collection;
                return await CollectionReceiptTemplateService.BuildLocalReceiptAsync(
                    _currentOrder,
                    businessInfo,
                    printer,
                    receiptKind,
                    GetReceiptOrderReference(),
                    _isDeliveryOrder ? _deliveryCustomerName : _collectionCustomerName,
                    _isDeliveryOrder ? _deliveryCustomerPhone : _collectionCustomerPhone,
                    _isDeliveryOrder ? _deliveryCustomerAddress : null,
                    _isDeliveryOrder ? _currentOrder.Notes : null,
                    total,
                    tip);
            }

            return await CollectionReceiptTemplateService.BuildLocalReceiptAsync(
                _currentOrder,
                businessInfo,
                printer,
                isFinalPaymentReceipt ? CustomerReceiptKind.TablePayment : CustomerReceiptKind.TableBill,
                GetReceiptOrderReference(),
                string.Empty,
                string.Empty,
                null,
                null,
                total,
                tip);
        }

        private byte[] BuildLocalReceiptData(NetworkPrinter printer, decimal total, decimal tip)
        {
            var builder = new EscPosBuilder(printer.Brand, printer.PaperWidth);
            var lineWidth = printer.PaperWidth == PaperWidth.Mm80 ? 48 : 32;
            var orderReference = GetReceiptOrderReference();
            var orderType = GetSavedOrderTypeLabel().ToUpperInvariant();

            builder.Initialize()
                   .SetAlign(TextAlign.Center)
                   .SetBold(true)
                   .SetFontSize(2, 2)
                   .PrintLine("RECEIPT")
                   .SetNormalSize()
                   .SetBold(false)
                   .PrintLine(orderType)
                   .PrintLine($"Order #{orderReference}")
                   .PrintLine(DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
                   .PrintLine(new string('=', lineWidth))
                   .SetAlign(TextAlign.Left);

            AppendReceiptCustomerBlock(builder);

            builder.PrintLine(new string('-', lineWidth));

            foreach (var item in _currentOrder.Items.Where(item => !item.IsVoided && !IsTastingMenuCourseItem(item)))
            {
                var itemTotal = item.TotalPriceWithVat > 0 ? item.TotalPriceWithVat : item.TotalPrice;
                builder.PrintColumns($"{item.Quantity}x {item.DisplayName}", FormatCurrency(itemTotal));

                foreach (var addon in item.SelectedAddons)
                {
                    var addonTotal = addon.Price * item.Quantity;
                    builder.PrintColumns($"  + {addon.Name}", addonTotal > 0 ? FormatCurrency(addonTotal) : string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(item.Notes) && !IsTastingMenuOrderItem(item))
                {
                    builder.PrintLine($"  Note: {item.Notes}");
                }
            }

            builder.PrintLine(new string('-', lineWidth))
                   .PrintColumns("Subtotal:", FormatCurrency(_currentOrder.Subtotal));

            if (_currentOrder.ServiceChargeStatus == TableServiceChargeStatus.Applied && _currentOrder.ServiceCharge > 0)
            {
                builder.PrintColumns($"Service Charge ({_currentOrder.ServiceChargePercent:0.##}%):", FormatCurrency(_currentOrder.ServiceCharge));
            }
            else if (!IsTakeawayStyleOrder() && _currentOrder.ServiceChargeStatus == TableServiceChargeStatus.Removed)
            {
                builder.PrintLine($"Service charge ({_currentOrder.ServiceChargePercent:0.##}%): Removed");
            }
            else if (!IsTakeawayStyleOrder() && _currentOrder.ServiceChargeStatus == TableServiceChargeStatus.NotConfigured)
            {
                builder.PrintLine("Service charge not included");
            }

            if (_currentOrder.DeliveryFee > 0)
            {
                builder.PrintColumns("Delivery Fee:", FormatCurrency(_currentOrder.DeliveryFee));
            }

            if (_currentOrder.Discount > 0)
            {
                builder.PrintColumns("Discount:", $"-{FormatCurrency(_currentOrder.Discount)}");
            }

            if (tip > 0)
            {
                builder.PrintColumns("Tip:", FormatCurrency(tip));
            }

            builder.PrintLine(new string('=', lineWidth))
                   .SetBold(true)
                   .SetFontSize(2, 1)
                   .PrintColumns("TOTAL:", FormatCurrency(total))
                   .SetNormalSize()
                   .SetBold(false)
                   .PrintLine("(VAT included in item prices)");

            if (_currentOrder.VAT > 0)
            {
                builder.PrintColumns("VAT included:", FormatCurrency(_currentOrder.VAT));
            }

            if (_currentOrder.Payments.Count > 0)
            {
                builder.PrintLine(new string('-', lineWidth));
                foreach (var payment in _currentOrder.Payments)
                {
                    builder.PrintColumns(payment.Method.ToString(), FormatCurrency(payment.Amount));
                }
            }

            builder.FeedLines(1)
                   .SetAlign(TextAlign.Center)
                   .PrintLine("Thank you")
                   .FeedLines(3);

            if (printer.HasCutter)
            {
                builder.Cut(true);
            }

            return builder.Build();
        }

        private void AppendReceiptCustomerBlock(EscPosBuilder builder)
        {
            if (_isDeliveryOrder)
            {
                if (!string.IsNullOrWhiteSpace(_deliveryCustomerName))
                {
                    builder.PrintLine($"Customer: {_deliveryCustomerName}");
                }
                if (!string.IsNullOrWhiteSpace(_deliveryCustomerPhone))
                {
                    builder.PrintLine($"Phone: {_deliveryCustomerPhone}");
                }
                if (!string.IsNullOrWhiteSpace(_deliveryCustomerAddress))
                {
                    builder.PrintLine($"Address: {_deliveryCustomerAddress}");
                }
                return;
            }

            if (_isCollectionOrder)
            {
                if (!string.IsNullOrWhiteSpace(_collectionCustomerName))
                {
                    builder.PrintLine($"Customer: {_collectionCustomerName}");
                }
                if (!string.IsNullOrWhiteSpace(_collectionCustomerPhone))
                {
                    builder.PrintLine($"Phone: {_collectionCustomerPhone}");
                }
                return;
            }

            builder.PrintLine($"Table: {_currentOrder.TableNumber}");
            if (_currentOrder.CoverCount > 0)
            {
                builder.PrintLine($"Covers: {_currentOrder.CoverCount}");
            }
        }

        private string GetReceiptOrderReference()
        {
            if (!string.IsNullOrWhiteSpace(_currentOrder.OrderNumber))
            {
                return _currentOrder.OrderNumber;
            }

            if (!string.IsNullOrWhiteSpace(_persistentOrderNumber))
            {
                return _persistentOrderNumber;
            }

            return string.IsNullOrWhiteSpace(_currentOrder.Id) ? DateTime.Now.ToString("HHmmss") : _currentOrder.Id;
        }

        private static string FormatCurrency(decimal amount)
        {
            return $"£{amount:F2}";
        }

        private async Task CloseTable()
        {
            // Navigate back based on order type
            if (_isCollectionOrder || _isDeliveryOrder)
            {
                // A completed takeaway order must discard both the order page and
                // its customer-entry page, then return to the correct role dashboard.
                await NavigateToRoleDashboardAsync(noAnimation: true);
            }
            else
            {
                // Dine-in: pop back to existing layout page instantly when possible.
                if (Navigation?.NavigationStack?.Count > 1)
                {
                    await _navigationCoordinator.PopTemporaryPageAsync(Navigation, animated: false);
                }
                else
                {
                    await _navigationCoordinator.NavigateShellAsync("visuallayout", animated: false);
                }
            }
        }

        private async Task ShowDiscountDialog()
        {
            var previousDiscount = _currentOrder.Discount;
            var pick = await new OrderPlaceDiscountDialog().ShowAsync(this, _currentOrder.Subtotal);
            if (pick is null)
            {
                return;
            }

            decimal discountAmount;
            decimal discountPercent;
            string? reason;
            var discountType = "fixed";

            if (pick.Removed)
            {
                discountAmount = 0m;
                discountPercent = 0m;
                reason = null;
            }
            else if (pick.Applied)
            {
                if (pick.IsPercentage)
                {
                    discountPercent = pick.Amount;
                    discountAmount = Math.Round(
                        _currentOrder.Subtotal * (Math.Clamp(pick.Amount, 0m, 100m) / 100m),
                        2,
                        MidpointRounding.AwayFromZero);
                    discountType = "percent";
                }
                else
                {
                    discountAmount = pick.Amount;
                    discountPercent = 0m;
                }

                reason = pick.Reason;
            }
            else
            {
                return;
            }

            var mapped = new DiscountDialogResult
            {
                DiscountAmount = discountAmount,
                DiscountPercent = discountPercent,
                Reason = reason,
                DiscountType = discountType,
                ApprovalRequired = !string.IsNullOrWhiteSpace(pick.Pin),
                ApprovedBy = string.IsNullOrWhiteSpace(pick.Pin)
                    ? null
                    : new DiscountApprovalInfo { Name = "Manager PIN", Role = "manager" }
            };

            _currentOrder.Discount = discountAmount;
            _currentOrder.DiscountPercent = discountPercent;
            _currentOrder.DiscountReason = reason;

            UpdateDisplay();
            await MarkCurrentOrderChangedAsync();
            await LogDiscountAuditAsync(mapped, previousDiscount);

            if (discountAmount > 0)
            {
                var alert = new ModernAlertDialog();
                alert.SetAlert("Discount Applied", $"£{discountAmount:F2} discount applied.\nReason: {reason}", "", "#10B981", "White");
                await alert.ShowAsync();
            }
            else if (pick.Removed)
            {
                var alert = new ModernAlertDialog();
                alert.SetAlert("Discount Removed", "Discount has been removed from the order.", "", "#3B82F6", "White");
                await alert.ShowAsync();
            }
        }

        private async Task LogDiscountAuditAsync(DiscountDialogResult result, decimal previousDiscount)
        {
            if (previousDiscount <= 0 && result.DiscountAmount <= 0)
            {
                return;
            }

            EnsureCurrentOrderIdentity();

            var action = result.DiscountAmount <= 0
                ? "removed"
                : previousDiscount > 0
                    ? "updated"
                    : "applied";

            try
            {
                await _discountAuditService.LogAsync(new DiscountAuditRequest
                {
                    OrderId = _currentOrder.Id,
                    OrderNumber = string.IsNullOrWhiteSpace(_persistentOrderNumber)
                        ? _currentOrder.OrderNumber
                        : _persistentOrderNumber,
                    TableSessionId = _tableSessionId,
                    TableNumber = _currentOrder.TableNumber > 0
                        ? _currentOrder.TableNumber.ToString()
                        : null,
                    Action = action,
                    DiscountType = result.DiscountType,
                    SubtotalAmount = _currentOrder.Subtotal,
                    PreviousDiscountAmount = previousDiscount,
                    DiscountAmount = result.DiscountAmount,
                    DiscountPercent = result.DiscountPercent,
                    TotalAfterDiscount = _currentOrder.Total,
                    Reason = action == "removed"
                        ? "Discount removed"
                        : result.Reason,
                    SourceArea = "order_more_options",
                    ApprovalRequired = result.ApprovalRequired,
                    ApprovedBy = result.ApprovedBy
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discount audit log failed: {ex.Message}");
            }
        }

        private async Task ShowTableTransferDialog()
        {
            var tableService = _restaurantTableService ?? new RestaurantTableService();
            var currentLabel = _currentOrder.TableNumber.ToString(CultureInfo.InvariantCulture);
            var available = (await tableService.GetAllTablesAsync())
                .Where(t => t.Status == TableStatus.Available &&
                            !string.Equals(t.TableNumber, currentLabel, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.FloorId)
                .ThenBy(t => t.TableNumber)
                .Select(t => new OrderPlaceTableOption(t.Id.ToString(CultureInfo.InvariantCulture), $"Table {t.TableNumber}"))
                .ToList();

            var picked = await new OrderPlaceTableTransferDialog().ShowAsync(this, $"Table {currentLabel}", available);
            if (picked is null)
            {
                return;
            }

            if (!int.TryParse(picked.Id, out var destinationTableId))
            {
                var errorDialog = new ModernAlertDialog();
                errorDialog.SetAlert("Transfer Failed", "Could not resolve the selected table.", "", "#EF4444", "White");
                await errorDialog.ShowAsync();
                return;
            }

            var selectedTable = (await tableService.GetAllTablesAsync())
                .FirstOrDefault(t => t.Id == destinationTableId);
            if (selectedTable is null)
            {
                var errorDialog = new ModernAlertDialog();
                errorDialog.SetAlert("Transfer Failed", "Selected table is no longer available.", "", "#EF4444", "White");
                await errorDialog.ShowAsync();
                return;
            }

            int oldTableNumber = _currentOrder.TableNumber;
            var transferResult = _tableSessionId.HasValue
                ? await _tableSessionService.TransferSessionAsync(_tableSessionId.Value, selectedTable.Id, "system", $"Transferred from Table {oldTableNumber} to Table {selectedTable.TableNumber}")
                : (false, "No active table session to transfer");
            var transferSuccess = transferResult.Item1;
            var transferMessage = transferResult.Item2;

            if (!transferSuccess)
            {
                var errorDialog = new ModernAlertDialog();
                errorDialog.SetAlert("Transfer Failed", transferMessage, "", "#EF4444", "White");
                await errorDialog.ShowAsync();
                return;
            }

            _currentOrder.TableNumber = int.Parse(selectedTable.TableNumber);

            UpdateTopBarOrderTitle();
            UpdateDisplay();
            await MarkCurrentOrderChangedAsync();

            var alert = new ModernAlertDialog();
            alert.SetAlert(
                "Transfer Complete",
                $"Order transferred from Table {oldTableNumber} to Table {selectedTable.TableNumber}",
                "",
                "#4CAF50",
                "White");
            await alert.ShowAsync();

            await _navigationCoordinator.NavigateShellAsync("visuallayout", animated: false);
        }

        private async Task ShowMergeTablesDialog()
        {
            if (!_tableSessionId.HasValue)
            {
                var infoDialog = new ModernAlertDialog();
                infoDialog.SetAlert("Merge Tables", "No active session is available for merging.", "", "#3B82F6", "White");
                await infoDialog.ShowAsync();
                return;
            }

            var targetTableNumber = await new OrderPlacePromptDialog().ShowAsync(
                this,
                "Merge Tables",
                "Enter the child table number to merge into this table:",
                "Continue",
                "Cancel",
                "e.g., 12",
                numericOnly: true);
            if (string.IsNullOrWhiteSpace(targetTableNumber))
            {
                return;
            }

            var tables = await _tableSessionService.GetTablesWithSessionsAsync();
            var childTable = tables.FirstOrDefault(t => string.Equals(t.TableNumber, targetTableNumber.Trim(), StringComparison.OrdinalIgnoreCase));
            if (childTable?.CurrentSession == null)
            {
                var errorDialog = new ModernAlertDialog();
                errorDialog.SetAlert("Merge Failed", "The selected table does not have an active session.", "", "#EF4444", "White");
                await errorDialog.ShowAsync();
                return;
            }

            if (!await new OrderPlaceConfirmDialog().ShowAsync(
                    this,
                    "Confirm Merge",
                    $"Merge Table {_currentOrder.TableNumber} with Table {childTable.TableNumber}?\nThis will keep the current table as the parent session.",
                    "Merge",
                    "Cancel"))
            {
                return;
            }

            var mergeResult = await _tableSessionService.MergeSessionsAsync(_tableSessionId.Value, childTable.CurrentSession.Id, "system", $"Merged Table {childTable.TableNumber} into Table {_currentOrder.TableNumber}");
            if (!mergeResult.success)
            {
                var errorDialog = new ModernAlertDialog();
                errorDialog.SetAlert("Merge Failed", mergeResult.message, "", "#EF4444", "White");
                await errorDialog.ShowAsync();
                return;
            }

            var successDialog = new ModernAlertDialog();
            successDialog.SetAlert("Merged", $"Table {childTable.TableNumber} merged into Table {_currentOrder.TableNumber}.", "", "#10B981", "White");
            await successDialog.ShowAsync();
        }

        private async Task ShowFireCourseDialog()
        {
            var selected = await new OrderPlaceFireCourseDialog().ShowAsync(this, includeDrinks: true);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                await FireCourseAsync(selected);
            }
        }

        private async Task ShowTastingCourseProgressAsync(TableOrderItem package)
        {
            var courses = GetTastingMenuCourses(package);
            if (courses.Count == 0)
            {
                await AppAlertService.ShowAlertAsync(
                    "No Courses",
                    "This tasting-menu package has no saved courses. Remove it and add the package again from the current menu.");
                return;
            }

            var dialog = new TastingCourseProgressDialog();
            var selectedCourse = await dialog.ShowAsync(package, courses);
            if (selectedCourse != null)
            {
                var number = TastingCourseProgressDialog.GetCourseNumber(selectedCourse);
                var confirmation = new ModernConfirmDialog();
                confirmation.SetConfirm(
                    $"Fire Course {number}",
                    $"Send {selectedCourse.Name} to the kitchen now?",
                    "Fire Course",
                    "Cancel",
                    "F",
                    "#F59E0B");
                if (await confirmation.ShowAsync())
                {
                    await FireTastingMenuCourseAsync(package, selectedCourse);
                }
            }
        }

        private async Task FireTastingMenuCourseAsync(TableOrderItem package, TableOrderItem course)
        {
            if (_isCourseFireInProgress)
            {
                return;
            }

            if (course.IsCourseFired)
            {
                await AppAlertService.ShowAlertAsync("Already Fired", $"{course.Name} has already been fired.");
                RefreshOrderItems();
                return;
            }

            _isCourseFireInProgress = true;
            var claimAcquired = false;
            var firedBy = "Staff";
            try
            {
                EnsureCurrentOrderIdentity();
                await EnsureOrderNumberAssignedAsync();
                await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState());

                var actor = ResolveCurrentActor();
                firedBy = string.IsNullOrWhiteSpace(actor.ActorName) ? "Staff" : actor.ActorName;
                var firedAt = DateTime.Now;
                claimAcquired = await _orderService.TryClaimTastingCourseFireAsync(
                    _currentOrder.Id,
                    course.Id,
                    firedAt,
                    firedBy);

                if (!claimAcquired)
                {
                    await AppAlertService.ShowAlertAsync(
                        "Already Fired",
                        $"{course.Name} was already fired by another user or terminal. The order will refresh now.");
                    var latest = await _orderService.GetOrderByExternalIdAsync(_currentOrder.Id);
                    if (latest != null)
                    {
                        await ApplyLoadedOrderAsync(latest);
                        _hasLoadedPersistentOrder = true;
                        _draftDirty = false;
                    }
                    return;
                }

                var printResult = await _orderRoutingPrintService.PrintTastingCourseAsync(_currentOrder, package, course);
                if (!printResult.FoodPrinted)
                {
                    await _orderService.ReleaseTastingCourseFireClaimAsync(_currentOrder.Id, course.Id, firedBy);
                    claimAcquired = false;
                    var reason = printResult.RoutingResult.FailedRoutes.FirstOrDefault()
                        ?? "No configured kitchen route accepted the course ticket.";
                    await AppAlertService.ShowAlertAsync("Course Not Fired", $"Nothing was printed. {reason}");
                    return;
                }

                course.FiredAt = firedAt;
                course.FiredBy = firedBy;
                _currentOrder.UpdatedAt = firedAt;
                _draftDirty = true;
                await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState());
                RefreshOrderItems();
                AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);

                var message = $"Course {TastingCourseProgressDialog.GetCourseNumber(course)} - {course.Name} printed.";
                if (printResult.WineRequested && !printResult.WinePrinted)
                {
                    message += " The food ticket printed, but the wine/bar ticket failed; please notify the bar.";
                }
                else if (printResult.CombinedOnSinglePrinter)
                {
                    message += " Food and wine were combined on the single available printer.";
                }
                else if (printResult.WineRedirected)
                {
                    message += $" The Bar route was unavailable, so the wine was redirected to {printResult.WineDestination ?? "the food printer"}; please notify the bar.";
                }
                else if (printResult.WineRequested)
                {
                    message += " Kitchen and Bar tickets printed successfully.";
                }

                var successDialog = new ModernAlertDialog();
                successDialog.SetAlert("Course Fired", message, "", "#10B981", "White");
                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                if (claimAcquired && !course.IsCourseFired)
                {
                    try
                    {
                        await _orderService.ReleaseTastingCourseFireClaimAsync(_currentOrder.Id, course.Id, firedBy);
                    }
                    catch
                    {
                        // Preserve the original failure for the operator.
                    }
                }

                await AppAlertService.ShowAlertAsync("Course Fire Failed", ex.Message);
            }
            finally
            {
                _isCourseFireInProgress = false;
            }
        }

        private async Task FireCourseAsync(string selectedCourse)
        {
            if (_isCourseFireInProgress)
            {
                return;
            }

            var fireAll = string.Equals(selectedCourse, "All", StringComparison.OrdinalIgnoreCase);
            var normalizedCourse = fireAll ? null : NormalizeCourseType(selectedCourse);
            if (!fireAll && normalizedCourse == null)
            {
                await AppAlertService.ShowAlertAsync("Course Not Found", "The selected course could not be identified.");
                return;
            }

            foreach (var item in _currentOrder.Items
                         .Where(item => !IsTastingMenuOrderItem(item) && !IsTastingMenuCourseItem(item))
                         .Where(item => string.IsNullOrWhiteSpace(item.CourseType)))
            {
                item.CourseType = ResolveCourseType(item.MenuItemId) ?? "Mains";
            }

            var matchingItems = _currentOrder.Items
                .Where(item => !item.IsVoided)
                .Where(item => !IsTastingMenuOrderItem(item) && !IsTastingMenuCourseItem(item))
                .Where(item => fireAll || string.Equals(item.CourseType, normalizedCourse, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var itemsToFire = matchingItems.Where(item => !item.IsCourseFired).ToList();

            if (itemsToFire.Count == 0)
            {
                var message = matchingItems.Count > 0
                    ? $"{(fireAll ? "All matching courses have" : $"{normalizedCourse} has")} already been fired."
                    : $"There are no {(fireAll ? "courses" : normalizedCourse)} on this order.";
                await AppAlertService.ShowAlertAsync("Nothing to Fire", message);
                RefreshOrderItems();
                return;
            }

            _isCourseFireInProgress = true;
            try
            {
                EnsureCurrentOrderIdentity();
                await EnsureOrderNumberAssignedAsync();
                await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState());

                var itemIds = itemsToFire.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var fireOrder = CloneOrderForSend(_currentOrder);
                foreach (var item in fireOrder.Items.Where(item => !itemIds.Contains(item.Id)).ToList())
                {
                    fireOrder.Items.Remove(item);
                }

                var fireLabel = fireAll ? "ALL COURSES" : normalizedCourse!.ToUpperInvariant();
                fireOrder.KitchenTicketType = $"FIRE {fireLabel}";
                foreach (var item in fireOrder.Items)
                {
                    item.SendStatus = ItemSendStatus.NotSent;
                    item.KitchenAction = KitchenChangeAction.New;
                    item.FailureReason = null;
                }

                var printResult = await _orderRoutingPrintService.PrintOrderAsync(fireOrder);
                if (!printResult.AnyPrinted)
                {
                    var reason = printResult.FailedRoutes.FirstOrDefault() ?? "No configured kitchen route accepted the course ticket.";
                    await AppAlertService.ShowAlertAsync("Course Not Fired", $"Nothing was printed. {reason}");
                    return;
                }

                var firedAt = DateTime.Now;
                var actor = ResolveCurrentActor();
                var firedBy = string.IsNullOrWhiteSpace(actor.ActorName) ? "Staff" : actor.ActorName;
                var printedIds = printResult.PrintedItemIds;
                foreach (var item in itemsToFire.Where(item => printedIds.Contains(item.Id)))
                {
                    item.FiredAt = firedAt;
                    item.FiredBy = firedBy;
                }

                _currentOrder.UpdatedAt = firedAt;
                _draftDirty = true;
                await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState());
                RefreshOrderItems();
                AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);

                var firedCount = itemsToFire.Count(item => printedIds.Contains(item.Id));
                var message = $"{fireLabel} printed to the kitchen. {firedCount} item{(firedCount == 1 ? string.Empty : "s")} marked as fired.";
                if (printResult.HasFailures)
                {
                    message += $" Some routes failed: {string.Join(", ", printResult.FailedRoutes)}";
                }

                var successDialog = new ModernAlertDialog();
                successDialog.SetAlert("Course Fired", message, "", "#10B981", "White");
                await successDialog.ShowAsync();
            }
            finally
            {
                _isCourseFireInProgress = false;
            }
        }

        private async Task ShowSplitBillDialog()
        {
            var dialog = new ModernActionSheetDialog();
            dialog.SetActionSheet(
                "Split Bill",
                new List<string> { "Split by Items", "Split by 2", "Split by 4", "Split by 6" },
                ""
            );
            
            var selected = await dialog.ShowAsync();
            
            if (selected != null)
            {
                var successDialog = new ModernAlertDialog();
                successDialog.SetAlert("Bill Split", $"Bill split: {selected}", "", "#10B981", "White");
                await successDialog.ShowAsync();
            }
        }

        private async Task ShowLoyaltyPointsDialog()
        {
            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Add items before redeeming loyalty points.", "i");
                await noItemsDialog.ShowAsync();
                return;
            }

            if (_currentOrder.Total <= 0)
            {
                var noTotalDialog = new ModernAlertDialog();
                noTotalDialog.SetAlert("No Bill", "There is no bill amount to offset with loyalty points.", "i");
                await noTotalDialog.ShowAsync();
                return;
            }

            try
            {
                await _loyaltyService.ReinitializeAsync();

                var customerNumber = await DisplayPromptAsync(
                    "Loyalty Points",
                    "Enter customer phone number or loyalty card number:",
                    "Continue",
                    "Cancel",
                    "e.g. 07123 456 789",
                    maxLength: 32,
                    keyboard: Keyboard.Telephone);

                if (string.IsNullOrWhiteSpace(customerNumber))
                {
                    return;
                }

                var lookup = await _loyaltyService.SearchCustomerAsync(customerNumber);
                if (!lookup.Success || lookup.Customer == null)
                {
                    var errorDialog = new ModernAlertDialog();
                    errorDialog.SetAlert("Loyalty Lookup Failed", lookup.Error ?? "Customer not found.", "", "#EF4444", "White");
                    await errorDialog.ShowAsync();
                    return;
                }

                var customer = lookup.Customer;
                var availablePoints = customer.PointsBalance;
                var maxBillPoints = (int)Math.Floor(_currentOrder.Total * 100m);
                var maxRedeemablePoints = Math.Min(availablePoints, maxBillPoints);

                var balanceDialog = new ModernAlertDialog();
                balanceDialog.SetAlert(
                    "Loyalty Balance",
                    $"Customer: {customer.CustomerName}\n" +
                    $"Phone: {customer.DisplayPhone}\n" +
                    $"Card: {customer.LoyaltyCardNumber}\n" +
                    $"Available points: {availablePoints:N0}\n" +
                    $"Current bill: £{_currentOrder.Total:F2}\n" +
                    $"Max redeemable: {maxRedeemablePoints:N0} points",
                    "⭐",
                    "#3B82F6",
                    "White");
                await balanceDialog.ShowAsync();

                if (maxRedeemablePoints <= 0)
                {
                    var noRedeemDialog = new ModernAlertDialog();
                    noRedeemDialog.SetAlert("Nothing to Redeem", "There are no points available to apply to this bill.", "i");
                    await noRedeemDialog.ShowAsync();
                    return;
                }

                var pointsText = await DisplayPromptAsync(
                    "Redeem Loyalty Points",
                    $"Available balance: {availablePoints:N0} pts\n" +
                    $"Bill limit: {maxBillPoints:N0} pts\n" +
                    $"1 point = £0.01\n\n" +
                    $"Enter points to redeem (max {maxRedeemablePoints:N0}):",
                    "Apply",
                    "Cancel",
                    $"{maxRedeemablePoints}",
                    maxLength: 8,
                    keyboard: Keyboard.Numeric);

                if (string.IsNullOrWhiteSpace(pointsText))
                {
                    return;
                }

                if (!int.TryParse(pointsText, out var pointsToRedeem) || pointsToRedeem <= 0)
                {
                    var invalidDialog = new ModernAlertDialog();
                    invalidDialog.SetAlert("Invalid Points", "Enter a valid number of points.", "", "#EF4444", "White");
                    await invalidDialog.ShowAsync();
                    return;
                }

                if (pointsToRedeem > maxRedeemablePoints)
                {
                    var limitDialog = new ModernAlertDialog();
                    limitDialog.SetAlert("Points Too High", $"You can only redeem up to {maxRedeemablePoints:N0} points on this bill.", "", "#EF4444", "White");
                    await limitDialog.ShowAsync();
                    return;
                }

                var discountAmount = Math.Round(pointsToRedeem / 100m, 2, MidpointRounding.AwayFromZero);
                var remainingTotal = Math.Max(0m, _currentOrder.Total - discountAmount);
                var confirm = await DisplayAlert(
                    "Confirm Loyalty Redemption",
                    $"Redeem {pointsToRedeem:N0} points for £{discountAmount:F2}?\n\n" +
                    $"New bill total: £{remainingTotal:F2}",
                    "Redeem",
                    "Cancel");

                if (!confirm)
                {
                    return;
                }

                var reason = $"Loyalty redemption - {customer.CustomerName}";
                var loyaltyTransactionId = $"{GetReceiptOrderReference()}:loyalty-redeem:{customer.LoyaltyCardNumber}:{pointsToRedeem}";
                var result = await _loyaltyService.RedeemPointsAsync(
                    customer.Phone,
                    pointsToRedeem,
                    reason,
                    loyaltyTransactionId);

                if (!result.Success || result.Customer == null)
                {
                    var redeemErrorDialog = new ModernAlertDialog();
                    redeemErrorDialog.SetAlert("Redemption Failed", result.Error ?? "Unable to redeem loyalty points.", "", "#EF4444", "White");
                    await redeemErrorDialog.ShowAsync();
                    return;
                }

                var previousDiscount = _currentOrder.Discount;
                _currentOrder.CustomerName = customer.CustomerName;
                _currentOrder.CustomerPhone = customer.Phone;
                _currentOrder.LoyaltyCardNumber = customer.LoyaltyCardNumber;
                _currentOrder.LoyaltyPointsRedeemed += pointsToRedeem;
                _currentOrder.Discount = previousDiscount + discountAmount;
                _currentOrder.DiscountReason = string.IsNullOrWhiteSpace(_currentOrder.DiscountReason)
                    ? reason
                    : $"{_currentOrder.DiscountReason}; {reason}";

                UpdateDisplay();
                await MarkCurrentOrderChangedAsync();

                try
                {
                    await _discountAuditService.LogAsync(new DiscountAuditRequest
                    {
                        OrderId = _currentOrder.Id,
                        OrderNumber = string.IsNullOrWhiteSpace(_persistentOrderNumber) ? _currentOrder.OrderNumber : _persistentOrderNumber,
                        TableSessionId = _tableSessionId,
                        TableNumber = _currentOrder.TableNumber > 0 ? _currentOrder.TableNumber.ToString() : null,
                        Action = previousDiscount > 0 ? "updated" : "applied",
                        DiscountType = "loyalty",
                        SubtotalAmount = _currentOrder.Subtotal,
                        PreviousDiscountAmount = previousDiscount,
                        DiscountAmount = _currentOrder.Discount,
                        DiscountPercent = 0,
                        TotalAfterDiscount = _currentOrder.Total,
                        Reason = reason,
                        SourceArea = "order_loyalty",
                        ApprovalRequired = false
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Loyalty discount audit failed: {ex.Message}");
                }

                var successDialog = new ModernAlertDialog();
                successDialog.SetAlert(
                    "Loyalty Applied",
                    $"Redeemed {pointsToRedeem:N0} points for £{discountAmount:F2}.\n" +
                    $"Remaining balance: {result.Customer.PointsBalance:N0} pts\n" +
                    $"New bill total: £{_currentOrder.Total:F2}",
                    "",
                    "#10B981",
                    "White");
                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errorDialog = new ModernAlertDialog();
                errorDialog.SetAlert("Loyalty Error", $"Unable to process loyalty redemption: {ex.Message}", "", "#EF4444", "White");
                await errorDialog.ShowAsync();
            }
        }

        private async Task ShowPriceOverrideDialog()
        {
            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before overriding prices.", "i");
                await noItemsDialog.ShowAsync();
                return;
            }
            
            var infoDialog = new ModernAlertDialog();
            infoDialog.SetAlert("Price Override", "Select an item to override its price.", "", "#3B82F6", "White");
            await infoDialog.ShowAsync();
        }

        private async Task OpenCashDrawer()
        {
            var flowService = ServiceHelper.GetService<CashDrawerFlowService>()
                ?? new CashDrawerFlowService(
                    _cashDrawerService,
                    ServiceHelper.GetService<TillExpenseService>() ?? new TillExpenseService(
                        ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(),
                        AuthenticationService.Instance),
                    AuthenticationService.Instance);

            await flowService.RunAsync(new CashDrawerFlowContext
            {
                SourceArea = "order_more_options",
                OrderId = _currentOrder.Id,
                OrderNumber = string.IsNullOrWhiteSpace(_persistentOrderNumber)
                    ? _currentOrder.OrderNumber
                    : _persistentOrderNumber,
                TableSessionId = _tableSessionId,
                TableNumber = _currentOrder.TableNumber > 0
                    ? _currentOrder.TableNumber.ToString()
                    : null
            });
        }
    }
}
