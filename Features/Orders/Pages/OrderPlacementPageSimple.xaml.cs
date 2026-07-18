using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
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
    public partial class OrderPlacementPageSimple : ContentPage
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
        private OrderLifecycleRolloutConfig _rolloutConfig = OrderLifecycleRolloutConfig.CreateDefault();

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
        private string _searchQuery = string.Empty;

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
        private bool _isUltraFastSendInProgress = false;
        private bool _isInitialLoadStarted = false;
        private bool _isRolloutConfigLoaded = false;
        private bool _hasShownConcurrencyConflict = false;
        private bool _isSubscribedToLiveUpdates = false;
        private CancellationTokenSource _draftSaveDelayCts = new();
        private readonly SemaphoreSlim _draftSaveLock = new(1, 1);
        private IDisposable? _idleDraftSaveRegistration;

        // Menu Caching
        private static List<MenuCategory>? _cachedCategories;
        private static List<FoodMenuItem>? _cachedMenuItems;
        private static List<MealDeal>? _cachedMealDeals;
        private static List<TastingMenu>? _cachedTastingMenus;
        private static DateTime _menuCacheUpdatedAt = DateTime.MinValue;
        private static readonly TimeSpan MenuCacheTtl = TimeSpan.FromMinutes(30);
        private readonly Dictionary<string, List<MenuItemQuickNote>> _quickNotesCache = new();

        // Feature Flags (Runtime)
        private bool EnableInstantSendMode = true;
        private bool EnableStartupReadinessChecks = false;

        // Readiness Checks
        private DateTime _lastReadinessCheckAt = DateTime.MinValue;
        private (List<string> Critical, List<string> Info)? _cachedReadinessIssues;

        public OrderPlacementPageSimple()
        {
            InitializeComponent();
            SizeChanged += OnOrderPageSizeChanged;
            InitializeServices();
        }

        private void OnOrderPageSizeChanged(object? sender, EventArgs e)
        {
            var compact = Width > 0 && (Width < 1450 || Height < 850);
            MainContentGrid.Padding = compact ? new Thickness(10) : new Thickness(15);
            MainContentGrid.ColumnSpacing = compact ? 10 : 15;
            CategoryCarousel.HeightRequest = compact ? 135 : 155;
            SubCategoryCarousel.HeightRequest = compact ? 118 : 135;
            OrderActionsCard.Padding = compact ? new Thickness(12) : new Thickness(20);
            OrderActionsCard.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0, 15, 0, 0);
            OrderActionsLayout.Spacing = compact ? 9 : 15;
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
            _menuItemService = new MenuItemService();
            _categoryService = new MenuCategoryService();
            _mealDealService = new MealDealService();
            _tastingMenuService = new TastingMenuService();
            _orderService = new OrderService();
            _tableSessionService = new TableSessionService();
            _orderRoutingPrintService = new OrderRoutingPrintService();
            _kitchenRevisionService = ServiceHelper.GetService<KitchenOrderRevisionService>()
                ?? new KitchenOrderRevisionService(_databaseService);
            _orderNumberService = new OrderNumberService(_databaseService);
            _orderLifecycleRolloutService = new OrderLifecycleRolloutService(_databaseService);
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
            _roleAccessService = new RoleAccessService();
            _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
            _inactivityService = ServiceHelper.GetService<InactivityService>() ?? new InactivityService(_authService, _roleAccessService);
            _restaurantTableService = new RestaurantTableService();
        }

        private async Task OnInitialLoadAsync()
        {
            try
            {
                await RefreshRolloutConfigAsync(forceRefresh: true);
                _currentUser = _authService.CurrentUser;
                EnsureCurrentOrderIdentity();
                await LoadDataAsync();
                await LoadExistingOrderIfNeededAsync();
                await EnsureLocalOrderDraftSavedAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Init error: {ex}");
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
                await EnsureTableSessionContextAsync();
            }

            await QueueDraftAutosaveAsync(immediate: true);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _inactivityService.Start();
            _inactivityService.ResetActivity();
            _inactivityService.TrackPage(this);
            _idleDraftSaveRegistration ??= _inactivityService.RegisterBeforeIdleReturnHandler(SaveDraftBeforeIdleReturnAsync);
            SubscribeToLiveUpdates();
            StartInitialLoadOnce();
        }

        private async Task SaveDraftBeforeIdleReturnAsync()
        {
            if (_isLoadingPersistentOrder || _isFinalizingOrder)
            {
                return;
            }

            await PersistDraftAsync(force: true);
        }

        private void StartInitialLoadOnce()
        {
            if (_isInitialLoadStarted)
            {
                return;
            }

            _isInitialLoadStarted = true;
            _ = OnInitialLoadAsync();
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
            _currentOrder.FixedServiceCharge = Math.Max(0, deliveryFee);
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

        private sealed class CategoryPage
        {
            public List<CategoryButtonModel> Row1 { get; } = new();
            public List<CategoryButtonModel> Row2 { get; } = new();
        }
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            UnsubscribeFromLiveUpdates();
            if (!_isFinalizingOrder && _draftDirty)
            {
                _ = PersistDraftAsync(force: true);
            }

            // Force subscribers (layout/live pages) to reload when user leaves edit screen.
            AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);

            _idleDraftSaveRegistration?.Dispose();
            _idleDraftSaveRegistration = null;
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
                    await ToastNotification.ShowAsync("Live update", e.ToastMessage, NotificationType.Info, 1400);

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
                    var categoriesTask = _categoryService.GetAllCategoriesAsync();
                    var itemsTask = _menuItemService.GetAllItemsAsync();
                    var dealsTask = _mealDealService.GetActiveDealsAsync();
                    var tastingMenusTask = _tastingMenuService.GetActiveAsync();

                    await Task.WhenAll(categoriesTask, itemsTask, dealsTask, tastingMenusTask);

                    _allCategories = await categoriesTask;
                    _allMenuItems = await itemsTask;
                    _activeMealDeals = await dealsTask;
                    _activeTastingMenus = await tastingMenusTask;

                    _cachedCategories = _allCategories;
                    _cachedMenuItems = _allMenuItems;
                    _cachedMealDeals = _activeMealDeals;
                    _cachedTastingMenus = _activeTastingMenus;
                    _menuCacheUpdatedAt = DateTime.Now;
                }
                
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Loaded {_allCategories.Count} categories, {_allMenuItems.Count} items, {_activeMealDeals.Count} meal deals");
                
                BuildCategories();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Load error: {ex}");
                throw;
            }
        }

        public static void InvalidateMenuCache()
        {
            _cachedCategories = null;
            _cachedMenuItems = null;
            _cachedMealDeals = null;
            _cachedTastingMenus = null;
            _menuCacheUpdatedAt = DateTime.MinValue;
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
            
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Building {topCategories.Count} category buttons");
            
            // Group categories into pages of 10 (2 rows x 5 columns)
            var categoryPages = new ObservableCollection<CategoryPage>();
            const int BUTTONS_PER_PAGE = 10;
            
            for (int i = 0; i < topCategories.Count; i += BUTTONS_PER_PAGE)
            {
                var page = new CategoryPage();
                var pageCategories = topCategories.Skip(i).Take(BUTTONS_PER_PAGE).ToList();
                
                // First 5 go to Row 1
                for (int j = 0; j < Math.Min(5, pageCategories.Count); j++)
                {
                    var cat = pageCategories[j];
                    page.Row1.Add(new CategoryButtonModel
                    {
                        Id = cat.Id,
                        Name = cat.Name,
                        Color = Color.FromArgb(cat.Color),
                        Category = cat
                    });
                }
                
                // Next 5 go to Row 2
                for (int j = 5; j < pageCategories.Count; j++)
                {
                    var cat = pageCategories[j];
                    page.Row2.Add(new CategoryButtonModel
                    {
                        Id = cat.Id,
                        Name = cat.Name,
                        Color = Color.FromArgb(cat.Color),
                        Category = cat
                    });
                }
                
                categoryPages.Add(page);
            }
            
            CategoryCarousel.ItemsSource = categoryPages;
            
            // Auto-load first category
            if (topCategories.Any())
            {
                SelectCategory(topCategories.First());
            }
        }

        private void OnCategoryButtonClicked(object? sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is CategoryButtonModel model)
            {
                SelectCategory(model.Category);
            }
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
            _searchQuery = string.Empty;
            
            UpdateSearchDisplay();

            if (string.Equals(category.Id, MealDeal.PosCategoryId, StringComparison.Ordinal))
            {
                SubCategorySection.IsVisible = false;
                LoadMealDealsForOrder();
                return;
            }

            if (string.Equals(category.Id, TastingMenu.PosCategoryId, StringComparison.Ordinal))
            {
                SubCategorySection.IsVisible = false;
                LoadTastingMenusForOrder();
                return;
            }
            
            // Check if this category has sub-categories
            var subCategories = _allCategories
                .Where(c => c.ParentId == category.Id && c.Active)
                .OrderBy(c => c.DisplayOrder)
                .ToList();
            
            if (subCategories.Any())
            {
                // Show sub-categories
                BuildSubCategories(subCategories);
                SubCategorySection.IsVisible = true;

                if (CategoryHasVisibleItems(category.Id))
                {
                    LoadItemsForCategory(category.Id);
                }
                else
                {
                    // Auto-select first sub-category when the main category has no direct items.
                    SelectSubCategory(subCategories.First());
                }
            }
            else
            {
                // No sub-categories, load items directly
                SubCategorySection.IsVisible = false;
                LoadItemsForCategory(category.Id);
            }
        }

        private void BuildSubCategories(List<MenuCategory> subCategories)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Building {subCategories.Count} sub-category buttons");
            
            // Use lighter shade of parent category color
            var lightColor = LightenColor(_selectedCategory?.Color ?? "#3B82F6");
            var buttonColor = Color.FromArgb(lightColor);
            var categoryButtons = subCategories
                .Select(subCat => new CategoryButtonModel
                {
                    Id = subCat.Id,
                    Name = subCat.Name,
                    Color = buttonColor,
                    Category = subCat
                })
                .ToList();

            if (_selectedCategory != null && CategoryHasVisibleItems(_selectedCategory.Id))
            {
                categoryButtons.Insert(0, new CategoryButtonModel
                {
                    Id = _selectedCategory.Id,
                    Name = "Main",
                    Color = Color.FromArgb(_selectedCategory.Color ?? "#3B82F6"),
                    Category = _selectedCategory
                });
            }
            
            // Group sub-categories into pages of 10 (2 rows x 5 columns)
            var subCategoryPages = new ObservableCollection<CategoryPage>();
            const int BUTTONS_PER_PAGE = 10;
            
            for (int i = 0; i < categoryButtons.Count; i += BUTTONS_PER_PAGE)
            {
                var page = new CategoryPage();
                var pageSubCategories = categoryButtons.Skip(i).Take(BUTTONS_PER_PAGE).ToList();
                
                // First 5 go to Row 1
                for (int j = 0; j < Math.Min(5, pageSubCategories.Count); j++)
                {
                    page.Row1.Add(pageSubCategories[j]);
                }
                
                // Next 5 go to Row 2
                for (int j = 5; j < pageSubCategories.Count; j++)
                {
                    page.Row2.Add(pageSubCategories[j]);
                }
                
                subCategoryPages.Add(page);
            }
            
            SubCategoryCarousel.ItemsSource = subCategoryPages;
        }

        private void OnSubCategoryButtonClicked(object? sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is CategoryButtonModel model)
            {
                SelectSubCategory(model.Category);
            }
        }

        private void SelectSubCategory(MenuCategory subCategory)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Sub-category selected: {subCategory.Name}");
            
            _selectedSubCategory = subCategory;
            LoadItemsForCategory(subCategory.Id);
        }

        private string LightenColor(string hexColor)
        {
            try
            {
                // Remove # if present
                hexColor = hexColor.TrimStart('#');
                
                // Parse RGB
                int r = Convert.ToInt32(hexColor.Substring(0, 2), 16);
                int g = Convert.ToInt32(hexColor.Substring(2, 2), 16);
                int b = Convert.ToInt32(hexColor.Substring(4, 2), 16);
                
                // Lighten by 40% (move toward white)
                r = (int)(r + (255 - r) * 0.4);
                g = (int)(g + (255 - g) * 0.4);
                b = (int)(b + (255 - b) * 0.4);
                
                return $"#{r:X2}{g:X2}{b:X2}";
            }
            catch
            {
                return "#E0E7FF"; // Default light blue
            }
        }

        private void LoadItemsForCategory(string categoryId)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Loading items for category ID: {categoryId}");
            
            ItemsContainer.Children.Clear();
            
            var items = _allMenuItems
                .Where(i => i.CategoryId == categoryId)
                .Where(IsItemVisibleForCurrentOrderMode)
                .OrderBy(i => i.DisplayOrder)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Found {items.Count} items");
            
            foreach (var item in items)
            {
                var itemButton = CreateItemButton(item);
                ItemsContainer.Children.Add(itemButton);
            }
        }

        private void LoadMealDealsForOrder()
        {
            ItemsContainer.Children.Clear();

            var deals = _activeMealDeals
                .OrderBy(d => d.DisplayOrder)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var deal in deals)
            {
                ItemsContainer.Children.Add(CreateMealDealButton(deal));
            }
        }

        private Border CreateMealDealButton(MealDeal deal)
        {
            var border = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#F59E0B"),
                StrokeThickness = 2,
                Padding = 12,
                Margin = new Thickness(0, 0, 12, 12),
                WidthRequest = 180,
                HeightRequest = 130,
                StrokeShape = new RoundRectangle { CornerRadius = 10 }
            };

            var gesture = new TapGestureRecognizer();
            gesture.Tapped += async (_, _) => await OnMealDealTappedAsync(deal);
            border.GestureRecognizers.Add(gesture);

            var stack = new VerticalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Fill
            };

            stack.Children.Add(new Label
            {
                Text = deal.Name,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B"),
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 2
            });

            stack.Children.Add(new Label
            {
                Text = deal.PickRuleDisplay,
                FontSize = 12,
                TextColor = Color.FromArgb("#64748B")
            });

            stack.Children.Add(new Label
            {
                Text = $"£{deal.Price:F2}",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#F59E0B"),
                VerticalOptions = LayoutOptions.EndAndExpand
            });

            border.Content = stack;
            return border;
        }

        private void LoadTastingMenusForOrder()
        {
            ItemsContainer.Children.Clear();

            var menus = _activeTastingMenus
                .OrderBy(menu => menu.DisplayOrder)
                .ThenBy(menu => menu.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var menu in menus)
            {
                ItemsContainer.Children.Add(CreateTastingMenuButton(menu));
            }
        }

        private Border CreateTastingMenuButton(TastingMenu menu)
        {
            var border = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#0EA5E9"),
                StrokeThickness = 2,
                Padding = 12,
                Margin = new Thickness(0, 0, 12, 12),
                WidthRequest = 190,
                HeightRequest = 140,
                StrokeShape = new RoundRectangle { CornerRadius = 10 }
            };

            var gesture = new TapGestureRecognizer();
            gesture.Tapped += async (_, _) => await OnTastingMenuTappedAsync(menu);
            border.GestureRecognizers.Add(gesture);

            border.Content = new VerticalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Fill,
                Children =
                {
                    new Label
                    {
                        Text = menu.Name,
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1E293B"),
                        LineBreakMode = LineBreakMode.WordWrap,
                        MaxLines = 2
                    },
                    new Label
                    {
                        Text = menu.CoursesDisplay,
                        FontSize = 12,
                        TextColor = Color.FromArgb("#64748B")
                    },
                    new Label
                    {
                        Text = menu.Options.Count == 0 ? "No prices" : menu.OptionsDisplay,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#0EA5E9"),
                        LineBreakMode = LineBreakMode.TailTruncation,
                        VerticalOptions = LayoutOptions.EndAndExpand
                    }
                }
            };

            return border;
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

                if (course.Choices.Count == 0)
                {
                    continue;
                }

                var choice = await PromptTastingMenuCourseChoiceAsync(menu, course);
                if (choice == null)
                {
                    return;
                }

                selections.Add((course, choice));
            }

            await AddTastingMenuToOrderAsync(menu, option, selections);
        }

        private async Task<TastingMenuOption?> PromptTastingMenuOptionAsync(TastingMenu menu)
        {
            var options = menu.Options
                .OrderBy(option => option.SortOrder)
                .Select(option => $"{option.DisplayName} - £{option.Price:F2}")
                .ToArray();

            var selected = await DisplayActionSheet($"Select package for {menu.Name}", "Cancel", null, options);
            if (string.IsNullOrWhiteSpace(selected) || selected.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var index = Array.IndexOf(options, selected);
            return index >= 0 ? menu.Options.OrderBy(option => option.SortOrder).ElementAt(index) : null;
        }

        private async Task<TastingMenuChoice?> PromptTastingMenuCourseChoiceAsync(TastingMenu menu, TastingMenuCourse course)
        {
            var choices = course.Choices
                .OrderBy(choice => choice.SortOrder)
                .Select(choice => choice.Name)
                .ToArray();

            var selected = await DisplayActionSheet($"{menu.Name} - {course.Name}", "Cancel", null, choices);
            if (string.IsNullOrWhiteSpace(selected) || selected.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return course.Choices.FirstOrDefault(choice => string.Equals(choice.Name, selected, StringComparison.OrdinalIgnoreCase));
        }

        private async Task AddTastingMenuToOrderAsync(TastingMenu menu, TastingMenuOption option, List<(TastingMenuCourse Course, TastingMenuChoice Choice)> selections)
        {
            SyncCurrentOrderMode();
            var menuItemId = TastingMenuNotesHelper.BuildOrderMenuItemId(menu.Id, option.Id);
            var notes = TastingMenuNotesHelper.FormatSelections(option, selections);
            var wasEmpty = _currentOrder.Items.Count == 0;

            _currentOrder.Items.Add(new TableOrderItem
            {
                Id = Guid.NewGuid().ToString(),
                MenuItemId = menuItemId,
                Name = menu.Name,
                VariantId = option.Id,
                VariantName = option.DisplayName,
                DisplayName = $"{menu.Name} ({option.DisplayName})",
                Quantity = 1,
                UnitPrice = option.Price,
                VatCategory = string.IsNullOrWhiteSpace(menu.VatCategory) ? "HotFood" : menu.VatCategory,
                Notes = notes,
                SendStatus = ItemSendStatus.NotSent,
                CreatedAt = DateTime.Now
            });

            _currentOrder.RecalculateAll();
            RefreshOrderItems();
            await MarkCurrentOrderChangedAsync();

            if (wasEmpty && _currentOrder.Items.Count > 0)
            {
                await PersistDraftAsync(force: true);
            }
        }

        private async Task OnMealDealTappedAsync(MealDeal deal)
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
                await PersistDraftAsync(force: true);
            }
        }

        private static bool IsMealDealOrderItem(TableOrderItem item) =>
            item.MenuItemId.StartsWith(MealDeal.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase);

        private static bool IsTastingMenuOrderItem(TableOrderItem item) =>
            item.MenuItemId.StartsWith(TastingMenu.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase);

        private Border CreateItemButton(FoodMenuItem item)
        {
            var displayPrice = GetSafeEffectivePrice(item);

            var border = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#E2E8F0"),
                StrokeThickness = 1,
                Padding = 12,
                Margin = new Thickness(0, 0, 12, 12),
                WidthRequest = 160,
                HeightRequest = 120,
                StrokeShape = new RoundRectangle { CornerRadius = 10 }
            };
            
            var gesture = new TapGestureRecognizer();
            gesture.Tapped += async (s, e) => await OnMenuItemTappedAsync(item);
            border.GestureRecognizers.Add(gesture);
            
            var stack = new VerticalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Fill
            };
            
            stack.Children.Add(new Label
            {
                Text = item.Name,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B"),
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 2
            });
            
            stack.Children.Add(new Label
            {
                Text = $"£{displayPrice:F2}",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#10B981"),
                VerticalOptions = LayoutOptions.EndAndExpand
            });
            
            border.Content = stack;
            return border;
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
                }

                if (loadedOrder == null && _tableSessionId.HasValue)
                {
                    loadedOrder = await _orderService.GetOpenOrderByTableSessionIdAsync(_tableSessionId.Value);
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
                    var tableService = new RestaurantTableService();
                    var table = await tableService.GetTableByIdAsync(activeSession.TableId);
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
            _currentOrder.Discount = loadedOrder.DiscountAmount;
            _currentOrder.FixedServiceCharge = loadedOrder.DeliveryFee;
            _currentOrder.Items.Clear();
            _currentOrder.Payments.Clear();

            var payments = await _orderService.GetOrderPaymentsAsync(loadedOrder.Id);
            foreach (var payment in payments)
            {
                _currentOrder.Payments.Add(new TableOrderPayment
                {
                    OrderId = _currentOrder.Id,
                    Method = ParsePaymentMethodType(payment.PaymentMethod),
                    Amount = payment.Amount,
                    AmountReceived = payment.Amount,
                    Change = 0m,
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
                    var tableService = new RestaurantTableService();
                    var table = (await tableService.GetAllTablesAsync())
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
                    await _tableSessionService.LinkOrderToSessionAsync(_tableSessionId.Value, _currentOrder.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] EnsureTableSessionContext warning: {ex.Message}");
            }
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
                    await Task.Delay(250, _draftSaveDelayCts.Token);
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

            await PersistDraftAsync(force: true);
        }

        private async Task<bool> PersistDraftAsync(bool force = false, LocalLifecycleState? lifecycleOverride = null, string? voidReason = null, string? voidedBy = null, DateTime? paidAt = null, DateTime? voidedAt = null)
        {
            if (!_rolloutConfig.EnableLifecycleWrites)
            {
                return false;
            }

            var lifecycleState = lifecycleOverride ?? GetDraftLifecycleState();
            var isDraftState = lifecycleState is LocalLifecycleState.Draft or LocalLifecycleState.Active;
            if (isDraftState && !_rolloutConfig.IsDraftSaveEnabledForOrderType(GetCanonicalOrderType()))
            {
                return false;
            }

            if ((lifecycleState is LocalLifecycleState.SentPartial or LocalLifecycleState.SentFull) && !_rolloutConfig.EnableSendDurability)
            {
                return false;
            }

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
                        if (_tableSessionId.HasValue)
                        {
                            await _tableSessionService.LinkOrderToSessionAsync(_tableSessionId.Value, order.OrderId);
                        }

                        _persistentOrderNumber = order.OrderNumber;
                        _lastSavedAt = order.UpdatedAt == default ? DateTime.Now : order.UpdatedAt;
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

            if (shouldAssignOrderNumber && string.IsNullOrWhiteSpace(_persistentOrderNumber))
            {
                _persistentOrderNumber = await _orderNumberService.GenerateOrderNumberAsync(orderType);
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
                DeliveryFee = _currentOrder.ServiceCharge,
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

        private async Task MarkCurrentOrderChangedAsync()
        {
            _inactivityService.ResetActivity();
            _currentOrder.UpdatedAt = DateTime.Now;
            UpdateDisplay();
            await QueueDraftAutosaveAsync();
        }

        private async Task AddItemToOrderAsync(FoodMenuItem item, string? selectedNote = null, List<SelectedAddon>? selectedAddons = null, MenuItemVariant? selectedVariant = null)
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
            
            _currentOrder.RecalculateAll();
            
            RefreshOrderItems();
            await MarkCurrentOrderChangedAsync();

            if (wasEmpty && _currentOrder.Items.Count > 0)
            {
                await PersistDraftAsync(force: true);
            }
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

        private void RefreshOrderItems()
        {
            OrderItemsContainer.Children.Clear();
            
            foreach (var item in _currentOrder.Items)
            {
                var hasNotes = !string.IsNullOrWhiteSpace(item.Notes);
                var hasModifiers = !string.IsNullOrWhiteSpace(item.ModifiersDisplay);
                var hasDetails = hasNotes || hasModifiers;
                var isMealDeal = IsMealDealOrderItem(item);
                var isTastingMenu = IsTastingMenuOrderItem(item);

                var itemView = new Border
                {
                    BackgroundColor = Color.FromArgb("#F9FAFB"),
                    StrokeThickness = 0,
                    Padding = new Thickness(8, 6),
                    Margin = new Thickness(0, 0, 0, 5),
                    StrokeShape = new RoundRectangle { CornerRadius = 8 }
                };

                if (!hasDetails)
                {
                    itemView.HeightRequest = 40;
                }
                
                // Single-line grid layout
                var mainGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, // Item name
                        new ColumnDefinition { Width = GridLength.Auto }, // Minus button
                        new ColumnDefinition { Width = new GridLength(25, GridUnitType.Absolute) }, // Quantity
                        new ColumnDefinition { Width = GridLength.Auto }, // Plus button
                        new ColumnDefinition { Width = new GridLength(85, GridUnitType.Absolute) }, // Note button
                        new ColumnDefinition { Width = new GridLength(65, GridUnitType.Absolute) } // Price
                    },
                    ColumnSpacing = 6,
                    RowDefinitions = new RowDefinitionCollection
                    {
                        new RowDefinition { Height = GridLength.Auto }
                    }
                };
                
                // Item name
                var nameLabel = new Label
                {
                    Text = item.DisplayName,
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1E293B"),
                    VerticalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                mainGrid.Add(nameLabel, 0, 0);
                
                // Minus button
                var minusBtn = new Button
                {
                    Text = "-",
                    BackgroundColor = Color.FromArgb("#EF4444"),
                    TextColor = Colors.White,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    WidthRequest = 32,
                    HeightRequest = 32,
                    Padding = 0,
                    CornerRadius = 6,
                    VerticalOptions = LayoutOptions.Center
                };
                minusBtn.Clicked += async (s, e) => {
                    var wasSent = HasReachedKitchen(item);
                    if (item.Quantity > 1)
                    {
                        item.Quantity--;
                        if (wasSent)
                        {
                            item.SendStatus = ItemSendStatus.NotSent;
                        }
                        _currentOrder.RecalculateAll();
                        RefreshOrderItems();
                        await MarkCurrentOrderChangedAsync();
                    }
                    else
                    {
                        _currentOrder.Items.Remove(item);
                        _currentOrder.RecalculateAll();
                        RefreshOrderItems();
                        await MarkCurrentOrderChangedAsync();
                    }
                };
                mainGrid.Add(minusBtn, 1, 0);
                
                // Quantity label
                var qtyLabel = new Label
                {
                    Text = item.Quantity.ToString(),
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1E293B"),
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center
                };
                mainGrid.Add(qtyLabel, 2, 0);
                
                // Plus button
                var plusBtn = new Button
                {
                    Text = "+",
                    BackgroundColor = Color.FromArgb("#10B981"),
                    TextColor = Colors.White,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    WidthRequest = 32,
                    HeightRequest = 32,
                    Padding = 0,
                    CornerRadius = 6,
                    VerticalOptions = LayoutOptions.Center
                };
                plusBtn.Clicked += async (s, e) => {
                    if (HasReachedKitchen(item))
                    {
                        _currentOrder.Items.Add(CreateAdditionalUnit(item));
                    }
                    else
                    {
                        item.Quantity++;
                    }
                    _currentOrder.RecalculateAll();
                    RefreshOrderItems();
                    await MarkCurrentOrderChangedAsync();
                };
                mainGrid.Add(plusBtn, 3, 0);
                
                if (!isMealDeal && !isTastingMenu)
                {
                    var noteBtn = new Button
                    {
                        Text = item.HasNotes ? "Note Added" : "+ Note",
                        BackgroundColor = Color.FromArgb("#3B82F6"),
                        TextColor = Colors.White,
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        HeightRequest = 32,
                        CornerRadius = 6,
                        Padding = new Thickness(8, 0),
                        VerticalOptions = LayoutOptions.Center
                    };
                    noteBtn.Clicked += async (s, e) => await ShowNoteDialog(item);
                    mainGrid.Add(noteBtn, 4, 0);
                }
                
                var priceLabel = new Label
                {
                    Text = $"£{item.TotalPrice:F2}",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#10B981"),
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.End
                };
                mainGrid.Add(priceLabel, 5, 0);

                if (hasDetails)
                {
                    var detailText = string.Join(
                        Environment.NewLine,
                        new[]
                        {
                            hasModifiers ? item.ModifiersDisplay : null,
                            hasNotes ? item.Notes : null
                        }.Where(text => !string.IsNullOrWhiteSpace(text)));

                    var detailsLabel = new Label
                    {
                        Text = detailText,
                        FontSize = 12,
                        TextColor = Color.FromArgb("#64748B"),
                        LineBreakMode = LineBreakMode.WordWrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    };

                    itemView.Content = new VerticalStackLayout
                    {
                        Spacing = 0,
                        Children = { mainGrid, detailsLabel }
                    };
                }
                else
                {
                    itemView.Content = mainGrid;
                }
                
                OrderItemsContainer.Children.Add(itemView);
            }
        }
        
        private async Task ShowNoteDialog(TableOrderItem item)
        {
            var originalNote = item.Notes;
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

            var orderPart = !string.IsNullOrEmpty(_currentOrder.OrderNumber) ? $"Order #{_currentOrder.OrderNumber}" : "Order #";
            var orderNote = string.IsNullOrWhiteSpace(_currentOrder.Notes)
                ? null
                : TruncateHeaderNote(_currentOrder.Notes.Trim(), 15);
            
            if (_isCollectionOrder)
            {
                OrderIdentityLabel.Text = $"{orderPart} Collection";
                GuestsLabel.Text = FormatOrderHeaderDetail(_collectionCustomerName, _collectionCustomerPhone, "Collection order");
            }
            else if (_isDeliveryOrder)
            {
                OrderIdentityLabel.Text = $"{orderPart} Delivery";
                GuestsLabel.Text = FormatOrderHeaderDetail(_deliveryCustomerName, _deliveryCustomerPhone, "Delivery order");
            }
            else
            {
                var baseText = $"{orderPart} Table {_currentOrder.TableNumber}";
                OrderIdentityLabel.Text = string.IsNullOrEmpty(orderNote)
                    ? baseText
                    : $"{baseText} | Note: {orderNote}";
                GuestsLabel.Text = $"Guests = {_currentOrder.CoverCount}";
            }
            
            SubtotalLabel.Text = $"£{_currentOrder.Subtotal:F2}";
            VATLabel.Text = $"£{_currentOrder.VAT:F2}";
            
            if (_currentOrder.ServiceCharge > 0)
            {
                ServiceChargeRow.IsVisible = true;
                ServiceChargeLabel.Text = $"£{_currentOrder.ServiceCharge:F2}";
            }
            else
            {
                ServiceChargeRow.IsVisible = false;
            }
            
            // Show discount if applied
            if (_currentOrder.Discount > 0)
            {
                DiscountRow.IsVisible = true;
                DiscountLabel.Text = $"-£{_currentOrder.Discount:F2}";
                if (!string.IsNullOrEmpty(_currentOrder.DiscountReason))
                {
                    DiscountReasonLabel.Text = $"Discount ({_currentOrder.DiscountReason})";
                }
                else
                {
                    DiscountReasonLabel.Text = "Discount";
                }
            }
            else
            {
                DiscountRow.IsVisible = false;
            }
            
            TotalLabel.Text = $"£{_currentOrder.Total:F2}";
        }

        private void UpdateTopBarOrderTitle()
        {
            if (TopBar == null)
            {
                return;
            }

            if (_isDeliveryOrder)
            {
                TopBar.SetPageTitle("Delivery Order");
                return;
            }

            if (_isCollectionOrder)
            {
                TopBar.SetPageTitle("Collection Order");
                return;
            }

            TopBar.SetPageTitle("Table Order");
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

        private async void OnServiceFeeClicked(object? sender, EventArgs e)
        {
            var dialog = new ModernActionSheetDialog();
            dialog.SetActionSheet(
                "Service Charge",
                new List<string> { "None (0%)", "10%", "12.5%", "15%" },
                ""
            );
            
            var action = await dialog.ShowAsync();
            
            if (action == "None (0%)")
            {
                _currentOrder.FixedServiceCharge = 0;
                _currentOrder.ServiceChargePercent = 0;
            }
            else if (action == "10%")
            {
                _currentOrder.FixedServiceCharge = 0;
                _currentOrder.ServiceChargePercent = 10;
            }
            else if (action == "12.5%")
            {
                _currentOrder.FixedServiceCharge = 0;
                _currentOrder.ServiceChargePercent = 12.5m;
            }
            else if (action == "15%")
            {
                _currentOrder.FixedServiceCharge = 0;
                _currentOrder.ServiceChargePercent = 15;
            }
            
            if (action != null)
            {
                _currentOrder.RecalculateAll();
                UpdateDisplay();
                await MarkCurrentOrderChangedAsync();
            }
        }

        private async void OnNotesClicked(object? sender, EventArgs e)
        {
            var dialog = new StyledPromptDialog();
            dialog.SetDialog(
                "Order Notes",
                "Enter notes for this order:",
                "e.g., Allergies, special requests...",
                null,
                _currentOrder.Notes ?? "",
                true
            );
            
            var result = await dialog.ShowAsync();
            
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
            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "There are no items to void.", "i");
                await noItemsDialog.ShowAsync();
                return;
            }
            
            // Show void reason selection
            var reasonDialog = new ModernActionSheetDialog();
            reasonDialog.SetActionSheet(
                "Void Reason",
                new List<string> { "Customer changed mind", "Wrong item entered", "Kitchen error", "Manager override" },
                ""
            );
            
            var reason = await reasonDialog.ShowAsync();
            
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
                var pinDialog = new StyledPromptDialog();
                pinDialog.SetDialog(
                    "Manager PIN Required",
                    $"Void amount £{_currentOrder.Total:F2} requires manager approval:",
                    "Enter PIN",
                    Keyboard.Numeric
                );
                
                var pin = await pinDialog.ShowAsync();
                
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
                    return;
                }
                await PrintFullOrderVoidAsync(reason, approvingUser);
                var voidedDialog = new ModernAlertDialog();
                voidedDialog.SetAlert("Voided", $"Order has been voided.\nReason: {reason}", "", "#10B981", "White");
                await voidedDialog.ShowAsync();
                
                // Navigate back to Visual Table Layout
                await Shell.Current.GoToAsync("..");
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

        private async void OnSearchBarTapped(object? sender, TappedEventArgs e)
        {
            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetInitialText(_searchQuery);
            
            var result = await keyboard.ShowAsync();
            
            if (result != null)
            {
                _searchQuery = result.Trim();
                UpdateSearchDisplay();
                FilterAndDisplayItems();
            }
        }

        private void UpdateSearchDisplay()
        {
            if (string.IsNullOrEmpty(_searchQuery))
            {
                SearchDisplayLabel.Text = "Search menu items...";
                SearchDisplayLabel.TextColor = Color.FromArgb("#94A3B8");
                ClearSearchButton.IsVisible = false;
            }
            else
            {
                SearchDisplayLabel.Text = _searchQuery;
                SearchDisplayLabel.TextColor = Color.FromArgb("#1E293B");
                ClearSearchButton.IsVisible = true;
            }
        }

        private void OnClearSearchClicked(object? sender, EventArgs e)
        {
            _searchQuery = string.Empty;
            UpdateSearchDisplay();
            FilterAndDisplayItems();
        }

        private void FilterAndDisplayItems()
        {
            ItemsContainer.Children.Clear();
            
            if (string.IsNullOrEmpty(_searchQuery))
            {
                if (_selectedCategory == null)
                {
                    return;
                }

                if (string.Equals(_selectedCategory.Id, MealDeal.PosCategoryId, StringComparison.Ordinal))
                {
                    LoadMealDealsForOrder();
                    return;
                }

                var activeCategoryId = _selectedSubCategory?.Id ?? _selectedCategory.Id;
                var items = _allMenuItems
                    .Where(i => i.CategoryId == activeCategoryId)
                    .Where(IsItemVisibleForCurrentOrderMode)
                    .OrderBy(i => i.DisplayOrder);

                foreach (var item in items)
                {
                    ItemsContainer.Children.Add(CreateItemButton(item));
                }

                return;
            }

            var matchingDeals = _activeMealDeals
                .Where(d =>
                    d.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    d.Choices.Any(c => c.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d.DisplayOrder)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var deal in matchingDeals)
            {
                ItemsContainer.Children.Add(CreateMealDealButton(deal));
            }

            var matchingItems = _allMenuItems
                .Where(i => i.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                .Where(IsItemVisibleForCurrentOrderMode)
                .OrderBy(i => i.Name);

            foreach (var item in matchingItems)
            {
                ItemsContainer.Children.Add(CreateItemButton(item));
            }
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

                System.Diagnostics.Debug.WriteLine($"⏱ [SEND] Firing background ProcessUltraFastSendPipelineAsync (don't wait)");
                _ = ProcessUltraFastSendPipelineAsync(printSnapshot);
                
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

        private async Task ProcessUltraFastSendPipelineAsync(TableOrder printSnapshot)
        {
            try
            {
                await RefreshRolloutConfigAsync(forceRefresh: false);

                if (string.IsNullOrWhiteSpace(_persistentOrderNumber))
                {
                    _persistentOrderNumber = await _orderNumberService.GenerateOrderNumberAsync(GetCanonicalOrderType());
                    _currentOrder.OrderNumber = _persistentOrderNumber;
                }

                // Skip order link (already done synchronously before background tasks started)
                await EnsureTableSessionContextAsync(skipOrderLink: true);
                await PersistDraftAsync(force: true, lifecycleOverride: LocalLifecycleState.Active);

                var persistedOrder = await _orderService.GetOrderByExternalIdAsync(_currentOrder.Id);
                if (persistedOrder == null)
                {
                    await PersistDraftAsync(force: true, lifecycleOverride: LocalLifecycleState.Active);
                    persistedOrder = await _orderService.GetOrderByExternalIdAsync(_currentOrder.Id);
                }

                if (persistedOrder == null)
                {
                    await LogOperationalEventAsync("send_failed", new { reason = "persisted_order_missing" });
                    return;
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
                    return;
                }

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
                        await ProcessDurableSendInBackgroundAsync(persistedOrder, batchId, revisionPrintOrder, revision);
                    }
                    else
                    {
                        var legacyPrint = IsTakeawayStyleOrder()
                            ? await _orderRoutingPrintService.PrintTakeawayOrderAsync(revisionPrintOrder, GetCanonicalOrderType())
                            : await _orderRoutingPrintService.PrintOrderAsync(revisionPrintOrder);
                        await _kitchenRevisionService.MarkPrintResultAsync(revision, legacyPrint);
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

                await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState());
                AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Ultra-fast send pipeline error: {ex.Message}");
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
                FixedServiceCharge = source.FixedServiceCharge,
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
                    Notes = item.Notes,
                    Modifiers = item.Modifiers,
                    CourseType = item.CourseType,
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

        private async Task ProcessDurableSendInBackgroundAsync(
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Instant durable send error: {ex.Message}");
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
                _ = Task.Run(async () => await FinalizeTableSessionAfterSendAsync());
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
            
            await Shell.Current.GoToAsync($"//{route}", !noAnimation);
            
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
            if (!string.IsNullOrWhiteSpace(chosenRoute.RouteTarget))
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

            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before printing receipt.", "i");
                await noItemsDialog.ShowAsync();
                return;
            }

            var printed = await PrintReceipt(_currentOrder.Total + _currentOrder.TipAmount, _currentOrder.TipAmount, isFinalPaymentReceipt: false);
            var printDialog = new ModernAlertDialog();
            if (printed)
            {
                printDialog.SetAlert("Receipt Printed", "Receipt sent to printer.", "", "#10B981", "White");
            }
            else
            {
                printDialog.SetAlert("Receipt Failed", "Could not print receipt. Please check the receipt printer setup.", "!", "#EF4444", "White");
            }

            await printDialog.ShowAsync();
        }

        private async void OnMoreClicked(object? sender, EventArgs e)
        {
            var dialog = new MoreOptionsDialog();
            
            // Check user role for restricted features
            var authService = ServiceHelper.GetService<AuthenticationService>();
            var currentUser = authService?.CurrentUser;
            var options = new List<(string Text, string Icon, bool IsEnabled)>
            {
                ("Discount", "", true),
                ("Table Transfer", "", true),
                ("Merge Tables", "", true),
                ("Fire Course", "", _currentOrder.Items.Count > 0),
                ("Loyalty Points", "", true), // Now enabled
                ("Cash Drawer", "", currentUser != null)
            };
            
            dialog.SetOptions(options);
            var selected = await dialog.ShowAsync();
            
            if (selected != null)
            {
                switch (selected)
                {
                    case "Discount":
                        await ShowDiscountDialog();
                        break;
                    case "Table Transfer":
                        await ShowTableTransferDialog();
                        break;
                    case "Merge Tables":
                        await ShowMergeTablesDialog();
                        break;
                    case "Fire Course":
                        await ShowFireCourseDialog();
                        break;
                    case "Loyalty Points":
                        await ShowLoyaltyPointsDialog();
                        break;
                    case "Cash Drawer":
                        await OpenCashDrawer();
                        break;
                }
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
            
            decimal tip = 0;
            decimal totalDue = _currentOrder.Total;
            
            // Step 1: Check if TIP should be shown (only if Service Charge = 0)
            if (_currentOrder.ServiceCharge == 0)
            {
                var tipDialog = new TipSelectionDialog();
                tipDialog.SetOrderTotal(_currentOrder.Total);
                tip = await tipDialog.ShowAsync();
                
                if (tip == -1) // Cancelled
                {
                    return;
                }
                
                totalDue = _currentOrder.Total + tip;
            }

            await EnsureTableSessionContextAsync(skipOrderLink: true, allowSessionOpen: false);
            if (_tableSessionId.HasValue)
            {
                await _tableSessionService.MarkSessionPaymentAsync(_tableSessionId.Value);
            }

            // Track remaining balance for partial payments
            decimal totalPaid = await GetExistingApprovedPaymentTotalAsync();
            decimal remainingBalance = Math.Max(0, totalDue - totalPaid);

            if (remainingBalance <= 0)
            {
                await CompletePayment(totalDue, tip, totalPaid);
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

            var splitPlan = await ShowPaymentSplitPlanDialog(totalDue, remainingBalance);
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
                await PersistDraftAsync(force: true, lifecycleOverride: GetDraftLifecycleState());
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

                switch (paymentMethod)
                {
                    case PaymentMethod.Cash:
                        var cashResult = await ProcessCashPayment(paymentAmount);
                        if (cashResult.Success)
                        {
                            paidThisAttempt = cashResult.AmountPaid;
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
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "cash", cashResult.AmountPaid, "approved", 0, null, actor.ActorName, new { split = splitPlan.ToMetadata(), chargeAmount = paymentAmount, change = cashResult.Change });
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
                        var cardResult = await ProcessCardPayment(paymentAmount);
                        if (cardResult.Success)
                        {
                            paidThisAttempt = cardResult.AmountPaid;
                            await LogOperationalEventAsync("payment_approved", new
                            {
                                method = "card",
                                amountPaid = cardResult.AmountPaid,
                                terminalReference = cardResult.TerminalReference,
                                remaining = Math.Max(0, remainingBalance - cardResult.AmountPaid),
                                split = splitPlan.RequiresPaymentLine ? splitPlan.GetPaymentTitle() : null
                            });
                            if (shouldRecordPaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(
                                    _currentOrder.Id,
                                    "card",
                                    cardResult.AmountPaid,
                                    "approved",
                                    0,
                                    cardResult.TerminalReference,
                                    actor.ActorName,
                                    new
                                    {
                                        split = splitPlan.ToMetadata(),
                                        chargeAmount = paymentAmount,
                                        manualTerminal = true,
                                        amountMatchConfirmed = true,
                                        terminalApprovalConfirmed = true
                                    });
                            }
                        }
                        else
                        {
                            await LogOperationalEventAsync("payment_failed", new { method = "card", amountDue = paymentAmount, split = splitPlan.RequiresPaymentLine ? splitPlan.GetPaymentTitle() : null });
                            if (shouldRecordPaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "card", paymentAmount, "failed", 0, null, actor.ActorName, new { split = splitPlan.ToMetadata(), chargeAmount = paymentAmount });
                            }
                        }
                        break;
                        
                    case PaymentMethod.GiftCard:
                        var giftTransactionId = BuildPaymentTransactionId("gift-card", paymentAmount, totalPaid, splitPlan.GetPaymentTitle());
                        var giftResult = await ProcessGiftCardPayment(paymentAmount, giftTransactionId);
                        if (giftResult.Success)
                        {
                            paidThisAttempt = giftResult.AmountApplied;
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
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "gift_card", giftResult.AmountApplied, "approved", 0, MaskGiftCardNumber(giftResult.GiftCardNumber), actor.ActorName, new
                                {
                                    split = splitPlan.ToMetadata(),
                                    chargeAmount = paymentAmount,
                                    giftCardNumber = MaskGiftCardNumber(giftResult.GiftCardNumber),
                                    previousCardBalance = giftResult.PreviousCardBalance,
                                    newCardBalance = giftResult.NewCardBalance,
                                    orderWebMessage = giftResult.OrderWebMessage,
                                    transactionId = giftTransactionId
                                });
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
                    AddReceiptPayment(paymentMethod, paidThisAttempt);
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
            await CompletePayment(totalDue, tip, totalPaid);
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
                    var splitPlan = await ShowEvenSplitPlanDialog(totalDue);
                    if (splitPlan != null)
                    {
                        return splitPlan;
                    }
                }
            }
        }

        private async Task<PaymentSplitPlan?> ShowEvenSplitPlanDialog(decimal totalDue)
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

                return PaymentSplitPlan.Equal(totalDue, customParts);
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
                ? PaymentSplitPlan.Full(totalDue)
                : PaymentSplitPlan.Equal(totalDue, splitCount);
        }

        private async Task<PaymentSplitPlan?> ShowCustomPaymentAmountDialog(decimal totalDue, decimal remainingBalance)
        {
            var prompt = new StyledPromptDialog();
            prompt.SetDialog(
                "Custom Amount",
                $"Enter the amount to take now. Remaining balance: £{remainingBalance:F2}",
                $"Max £{remainingBalance:F2}",
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

            if (!TryParsePaymentAmount(value, out var customAmount) || customAmount <= 0 || customAmount > remainingBalance + 0.009m)
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
                _currentOrder.Items,
                _currentOrder.Subtotal,
                _currentOrder.ServiceCharge,
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
                if (existingOrder?.Id <= 0)
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

        private async Task<CardPaymentResult> ProcessCardPayment(decimal amountDue)
        {
            var cardDialog = new CardPaymentDialog();
            cardDialog.SetAmount(amountDue);
            return await cardDialog.ShowAsync();
        }

        private async Task<GiftCardPaymentResult> ProcessGiftCardPayment(decimal amountDue, string transactionId)
        {
            var giftDialog = new GiftCardPaymentDialog();
            giftDialog.SetAmountDue(amountDue, GetReceiptOrderReference(), transactionId);
            return await giftDialog.ShowAsync();
        }

        private void AddReceiptPayment(PaymentMethod paymentMethod, decimal amount)
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
                "Payment saved",
                $"Printing receipt for £{totalAmount:F2}...",
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

                var orderService = new OrderService();
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
                var saveResult = await orderService.SaveOrderAsync(order);
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
                var orderService = new OrderService();

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

                var saveResult = await orderService.SaveOrderAsync(order);
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
                return await routingService.ResolvePrinterAsync(
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
                return onlineReceiptPrinters.FirstOrDefault(printer => printer.IsEnabled);
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

            foreach (var item in _currentOrder.Items.Where(item => !item.IsVoided))
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

            if (_currentOrder.ServiceCharge > 0)
            {
                builder.PrintColumns(IsTakeawayStyleOrder() ? "Delivery/Fee:" : "Service:", FormatCurrency(_currentOrder.ServiceCharge));
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
            if (_isCollectionOrder)
            {
                // Navigate back to dashboard for collection orders
                await Shell.Current.GoToAsync("//dashboard");
            }
            else
            {
                // Dine-in: pop back to existing layout page instantly when possible.
                if (Navigation?.NavigationStack?.Count > 1)
                {
                    await Navigation.PopAsync(animated: false);
                }
                else
                {
                    await Shell.Current.GoToAsync("//visuallayout");
                }
            }
        }

        private async Task ShowDiscountDialog()
        {
            var dialog = new DiscountDialog();
            dialog.SetOrderSubtotal(_currentOrder.Subtotal);
            var previousDiscount = _currentOrder.Discount;
            
            var result = await dialog.ShowAsync();
            
            if (result != null)
            {
                var discountAmount = result.DiscountAmount;
                var discountPercent = result.DiscountPercent;
                var reason = result.Reason;
                
                _currentOrder.Discount = discountAmount;
                _currentOrder.DiscountPercent = discountPercent;
                _currentOrder.DiscountReason = reason;
                
                UpdateDisplay();
                await MarkCurrentOrderChangedAsync();
                await LogDiscountAuditAsync(result, previousDiscount);
                
                if (discountAmount > 0)
                {
                    var alert = new ModernAlertDialog();
                    alert.SetAlert("Discount Applied", $"£{discountAmount:F2} discount applied.\nReason: {reason}", "", "#10B981", "White");
                    await alert.ShowAsync();
                }
                else if (discountAmount == 0 && string.IsNullOrEmpty(reason))
                {
                    // Discount was removed
                    var alert = new ModernAlertDialog();
                    alert.SetAlert("Discount Removed", "Discount has been removed from the order.", "", "#3B82F6", "White");
                    await alert.ShowAsync();
                }
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
            var dialog = new TableTransferDialog();
            dialog.SetCurrentTable(_currentOrder.TableNumber.ToString());
            
            var selectedTable = await dialog.ShowAsync();
            
            if (selectedTable != null)
            {
                // Transfer the order to the new table
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

                // Update TopBar title
                if (TopBar != null)
                {
                    TopBar.SetPageTitle($"Table {selectedTable.TableNumber}");
                }

                await MarkCurrentOrderChangedAsync();
                
                // Show success message
                var alert = new ModernAlertDialog();
                alert.SetAlert(
                    "Transfer Complete", 
                    $"Order transferred from Table {oldTableNumber} to Table {selectedTable.TableNumber}", 
                    "", 
                    "#4CAF50", 
                    "White"
                );
                await alert.ShowAsync();

                await Shell.Current.GoToAsync("//visuallayout", false);
            }
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

            var dialog = new StyledPromptDialog();
            dialog.SetDialog(
                "Merge Tables",
                "Enter the child table number to merge into this table:",
                "e.g., 12",
                Keyboard.Numeric
            );

            var targetTableNumber = await dialog.ShowAsync();
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

            var confirmDialog = new ModernConfirmDialog();
            confirmDialog.SetConfirm(
                "Confirm Merge",
                $"Merge Table {_currentOrder.TableNumber} with Table {childTable.TableNumber}?\nThis will keep the current table as the parent session.",
                "Merge",
                "Cancel",
                ""
            );

            if (!await confirmDialog.ShowAsync())
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
            var dialog = new FireCourseDialog();
            
            var selected = await dialog.ShowAsync();
            
            if (selected != null)
            {
                string message = selected == "All" 
                    ? "All courses sent to kitchen!" 
                    : $"{selected} sent to kitchen!";
                    
                var successDialog = new ModernAlertDialog();
                successDialog.SetAlert("Course Fired", message, "", "#10B981", "White");
                await successDialog.ShowAsync();
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
