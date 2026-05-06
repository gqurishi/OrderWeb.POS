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
        private OrderService _orderService;
        private TableSessionService _tableSessionService;
        private OrderRoutingPrintService _orderRoutingPrintService;
        private OrderNumberService _orderNumberService;
        private OrderLifecycleRolloutService _orderLifecycleRolloutService;
        private RoleAccessService _roleAccessService;
        private AuthenticationService _authService;
        private RestaurantTableService _restaurantTableService;
        private DatabaseService _databaseService;
        private OrderLifecycleRolloutConfig _rolloutConfig = OrderLifecycleRolloutConfig.CreateDefault();

        // Model State
        private TableOrder _currentOrder = new();
        private List<MenuCategory> _allCategories = new();
        private List<MenuCategory> _topLevelCategories = new();
        private List<FoodMenuItem> _allMenuItems = new();
        private List<FoodMenuItem> _filteredItems = new();

        // UI State
        private MenuCategory? _selectedCategory;
        private MenuCategory? _selectedSubCategory;
        private FoodMenuItem? _modifierPopupItem;
        private string _searchQuery = string.Empty;

        // Session/Order State  
        private string? _pendingOrderId;
        private int? _tableSessionId;
        private string _persistentOrderNumber = string.Empty;
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
        private CancellationTokenSource _draftSaveDelayCts = new();
        private readonly SemaphoreSlim _draftSaveLock = new(1, 1);

        // Menu Caching
        private static List<MenuCategory>? _cachedCategories;
        private static List<FoodMenuItem>? _cachedMenuItems;
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
            InitializeServices();
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
            _orderService = new OrderService();
            _tableSessionService = new TableSessionService();
            _orderRoutingPrintService = new OrderRoutingPrintService();
            _orderNumberService = new OrderNumberService(_databaseService);
            _orderLifecycleRolloutService = new OrderLifecycleRolloutService(_databaseService);
            _roleAccessService = new RoleAccessService();
            _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Init error: {ex}");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartInitialLoadOnce();
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
        }

        public void SetCollectionOrderInfo(int customerId, string customerName, string customerPhone)
        {
            _isCollectionOrder = true;
            _isDeliveryOrder = false;
            _collectionCustomerId = customerId;
            _collectionCustomerName = customerName;
            _collectionCustomerPhone = customerPhone;
        }

        public void SetDeliveryOrderInfo(int customerId, string customerName, string customerPhone, string customerAddress)
        {
            _isDeliveryOrder = true;
            _isCollectionOrder = false;
            _deliveryCustomerId = customerId;
            _deliveryCustomerName = customerName;
            _deliveryCustomerPhone = customerPhone;
            _deliveryCustomerAddress = customerAddress;
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
            if (!_isFinalizingOrder && _draftDirty)
            {
                _ = PersistDraftAsync(force: true);
            }

            // Force subscribers (layout/live pages) to reload when user leaves edit screen.
            AppDataRefreshService.RequestRefresh();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[OrderPlacement] Loading data...");
                _quickNotesCache.Clear();

                var useCache = _cachedCategories != null
                    && _cachedMenuItems != null
                    && (DateTime.Now - _menuCacheUpdatedAt) < MenuCacheTtl;

                if (useCache)
                {
                    _allCategories = _cachedCategories!;
                    _allMenuItems = _cachedMenuItems!;
                }
                else
                {
                    // Load categories and menu items in parallel for faster loading
                    var categoriesTask = _categoryService.GetAllCategoriesAsync();
                    var itemsTask = _menuItemService.GetAllItemsAsync();

                    await Task.WhenAll(categoriesTask, itemsTask);

                    _allCategories = await categoriesTask;
                    _allMenuItems = await itemsTask;

                    _cachedCategories = _allCategories;
                    _cachedMenuItems = _allMenuItems;
                    _menuCacheUpdatedAt = DateTime.Now;
                }
                
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Loaded {_allCategories.Count} categories and {_allMenuItems.Count} items");
                
                BuildCategories();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Load error: {ex}");
                throw;
            }
        }

        private void BuildCategories()
        {
            var topCategories = _allCategories
                .Where(c => c.ParentId == null && c.Active)
                .OrderBy(c => c.DisplayOrder)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Building {topCategories.Count} category buttons");
            
            // Group categories into pages of 10 (2 rows × 5 columns)
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

        private void SelectCategory(MenuCategory category)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Category selected: {category.Name}");
            
            _selectedCategory = category;
            _selectedSubCategory = null;
            _searchQuery = string.Empty;
            
            // Clear search display
            UpdateSearchDisplay();
            
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
                
                // Auto-select first sub-category
                SelectSubCategory(subCategories.First());
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
            
            // Group sub-categories into pages of 10 (2 rows × 5 columns)
            var subCategoryPages = new ObservableCollection<CategoryPage>();
            const int BUTTONS_PER_PAGE = 10;
            
            for (int i = 0; i < subCategories.Count; i += BUTTONS_PER_PAGE)
            {
                var page = new CategoryPage();
                var pageSubCategories = subCategories.Skip(i).Take(BUTTONS_PER_PAGE).ToList();
                
                // First 5 go to Row 1
                for (int j = 0; j < Math.Min(5, pageSubCategories.Count); j++)
                {
                    var subCat = pageSubCategories[j];
                    page.Row1.Add(new CategoryButtonModel
                    {
                        Id = subCat.Id,
                        Name = subCat.Name,
                        Color = buttonColor,
                        Category = subCat
                    });
                }
                
                // Next 5 go to Row 2
                for (int j = 5; j < pageSubCategories.Count; j++)
                {
                    var subCat = pageSubCategories[j];
                    page.Row2.Add(new CategoryButtonModel
                    {
                        Id = subCat.Id,
                        Name = subCat.Name,
                        Color = buttonColor,
                        Category = subCat
                    });
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
                .OrderBy(i => i.DisplayOrder)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Found {items.Count} items");
            
            foreach (var item in items)
            {
                var itemButton = CreateItemButton(item);
                ItemsContainer.Children.Add(itemButton);
            }
        }

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
            var (shouldAdd, selectedNote) = await PromptQuickNoteSelectionAsync(item);
            if (!shouldAdd)
            {
                return;
            }

            await AddItemToOrderAsync(item, selectedNote);
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

            const string noNoteOption = "No note";
            const string customNoteOption = "Custom note...";
            var options = new List<string> { noNoteOption };
            options.AddRange(quickNotes.Select(n => n.NoteText));
            options.Add(customNoteOption);

            var selected = await DisplayActionSheet($"Note for {item.Name}", "Cancel", null, options.ToArray());
            if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, "Cancel", StringComparison.OrdinalIgnoreCase))
            {
                return (false, null);
            }

            if (string.Equals(selected, noNoteOption, StringComparison.OrdinalIgnoreCase))
            {
                return (true, null);
            }

            if (string.Equals(selected, customNoteOption, StringComparison.OrdinalIgnoreCase))
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

            return (true, NormalizeOrderItemNote(selected));
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
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        var partialPaymentDialog = new ModernAlertDialog();
                        partialPaymentDialog.SetAlert("Partial Payment", "This order has a partial payment recorded.", "ℹ️", "#3B82F6", "White");
                        await partialPaymentDialog.ShowAsync();
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
            _currentOrder.ServiceChargePercent = loadedOrder.DeliveryFee > 0 && loadedOrder.SubtotalAmount > 0
                ? Math.Round((loadedOrder.DeliveryFee / loadedOrder.SubtotalAmount) * 100m, 2)
                : _currentOrder.ServiceChargePercent;
            _currentOrder.Items.Clear();
            _currentOrder.Payments.Clear();

            var payments = await _orderService.GetOrderPaymentsAsync(loadedOrder.Id);
            foreach (var payment in payments)
            {
                _currentOrder.Payments.Add(new TableOrderPayment
                {
                    OrderId = _currentOrder.Id,
                    Method = Enum.TryParse<PaymentMethodType>(payment.PaymentMethod, true, out var method)
                        ? method
                        : PaymentMethodType.Cash,
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
                    Name = item.ItemName,
                    Quantity = item.Quantity,
                    UnitPrice = item.ItemPrice ?? 0m,
                    Notes = item.SpecialInstructions,
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
                    CreatedAt = loadedOrder.CreatedAt == default ? DateTime.Now : loadedOrder.CreatedAt
                });
            }

            _persistentOrderNumber = loadedOrder.OrderNumber;
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
            UpdateDisplay();
            UpdateSavedStatusLabel();
            ApplyLoadedOrderContext(loadedOrder);
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

            if (orderType == "delivery")
            {
                _isCollectionOrder = false;
                _isDeliveryOrder = true;
                _deliveryCustomerName = loadedOrder.CustomerName;
                _deliveryCustomerPhone = loadedOrder.CustomerPhone ?? string.Empty;
                _deliveryCustomerAddress = loadedOrder.CustomerAddress ?? string.Empty;
                if (TopBar != null)
                {
                    TopBar.SetPageTitle($"Delivery Order - {loadedOrder.CustomerName}");
                }
            }
            else if (orderType == "pickup")
            {
                _isCollectionOrder = true;
                _isDeliveryOrder = false;
                _collectionCustomerName = loadedOrder.CustomerName;
                _collectionCustomerPhone = loadedOrder.CustomerPhone ?? string.Empty;
                if (TopBar != null)
                {
                    TopBar.SetPageTitle($"Collection Order - {loadedOrder.CustomerName}");
                }
            }
            else
            {
                _isCollectionOrder = false;
                _isDeliveryOrder = false;
                if (TopBar != null)
                {
                    TopBar.SetPageTitle($"Table {_currentOrder.TableNumber}");
                }
            }

            SyncCurrentOrderMode();
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
            var conflictDialog = new ModernAlertDialog();
            conflictDialog.SetAlert("Order Updated Elsewhere", "Order changed on another terminal, reloading latest version.", "⚠️", "#F59E0B", "White");
            await conflictDialog.ShowAsync();
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
            var currentUser = authService?.CurrentUser;

            if (currentUser == null)
            {
                return ("system", null, "system");
            }

            var actorType = currentUser.Role == UserRole.Manager || currentUser.Role == UserRole.Admin
                ? "manager"
                : "user";
            var actorId = string.IsNullOrWhiteSpace(currentUser.Username) ? null : currentUser.Username;
            var actorName = !string.IsNullOrWhiteSpace(currentUser.Name) ? currentUser.Name : currentUser.Username;

            return (actorType, actorId, actorName);
        }

        private async Task LogOperationalEventAsync(string eventType, object? payload = null, DateTime? eventAt = null)
        {
            if (!_rolloutConfig.EnableLifecycleWrites)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentOrder?.Id) || string.IsNullOrWhiteSpace(eventType))
            {
                return;
            }

            var actor = ResolveCurrentActor();
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
                    : null;

            var customerAddress = _isDeliveryOrder ? _deliveryCustomerAddress : null;
            var now = DateTime.Now;
            var currentOrderItems = _currentOrder.Items
                .Select(item => new OrderItem
                {
                    ClientItemId = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id,
                    ItemName = item.Name,
                    Quantity = item.Quantity,
                    ItemPrice = item.UnitPrice,
                    SpecialInstructions = item.Notes,
                    MenuItemId = item.MenuItemId
                })
                .ToList();

            return new Order
            {
                OrderId = _currentOrder.Id,
                OrderNumber = shouldAssignOrderNumber ? _persistentOrderNumber : null,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                CustomerAddress = customerAddress,
                TotalAmount = _currentOrder.Total,
                SubtotalAmount = _currentOrder.Subtotal,
                DeliveryFee = _currentOrder.ServiceCharge,
                TaxAmount = _currentOrder.VAT,
                OrderType = orderType,
                SourceChannel = "local",
                TableSessionId = _tableSessionId,
                PaymentMethod = lifecycleState == LocalLifecycleState.Paid ? "paid" : null,
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
            _currentOrder.UpdatedAt = DateTime.Now;
            UpdateDisplay();
            await QueueDraftAutosaveAsync();
        }

        private async Task AddItemToOrderAsync(FoodMenuItem item, string? selectedNote = null)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Adding item: {item.Name}");

            SyncCurrentOrderMode();
            var effectivePrice = GetSafeEffectivePrice(item);
            var normalizedNote = NormalizeOrderItemNote(selectedNote);
            var wasEmpty = _currentOrder.Items.Count == 0;
            
            // Check if item already exists in order
            var existingItem = _currentOrder.Items.FirstOrDefault(i => 
                i.MenuItemId == item.Id && 
                string.Equals(NormalizeOrderItemNote(i.Notes), normalizedNote, StringComparison.OrdinalIgnoreCase) &&
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
                    Name = item.Name,
                    Quantity = 1,
                    UnitPrice = effectivePrice,
                    VatCategory = string.IsNullOrWhiteSpace(item.VatCategory) ? "HotFood" : item.VatCategory,
                    PrintGroupId = item.PrintGroupId,
                    Notes = normalizedNote,
                    SendStatus = ItemSendStatus.NotSent,
                    CreatedAt = DateTime.Now
                };
                
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

        private void RefreshOrderItems()
        {
            OrderItemsContainer.Children.Clear();
            
            foreach (var item in _currentOrder.Items)
            {
                // Card container - more compact
                var itemView = new Border
                {
                    BackgroundColor = Color.FromArgb("#F9FAFB"),
                    StrokeThickness = 0,
                    Padding = new Thickness(8, 6),
                    Margin = new Thickness(0, 0, 0, 5),
                    HeightRequest = 40,
                    StrokeShape = new RoundRectangle { CornerRadius = 8 }
                };
                
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
                    Text = item.Name,
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
                    if (item.Quantity > 1)
                    {
                        item.Quantity--;
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
                    item.Quantity++;
                    _currentOrder.RecalculateAll();
                    RefreshOrderItems();
                    await MarkCurrentOrderChangedAsync();
                };
                mainGrid.Add(plusBtn, 3, 0);
                
                // Note button
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
                
                // Price
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
                
                itemView.Content = mainGrid;
                OrderItemsContainer.Children.Add(itemView);
            }
        }
        
        private async Task ShowNoteDialog(TableOrderItem item)
        {
            var dialog = new StyledPromptDialog();
            dialog.SetDialog(
                "Add Note",
                $"Enter note for {item.Name}:",
                "e.g., No onions, extra spicy",
                null,
                item.Notes ?? "",
                true
            );
            
            var result = await dialog.ShowAsync();
            
            if (result != null)
            {
                item.Notes = string.IsNullOrWhiteSpace(result) ? null : result;
                RefreshOrderItems();
                await MarkCurrentOrderChangedAsync();
            }
        }

        private void UpdateDisplay()
        {
            GuestsLabel.Text = $"Guests = {_currentOrder.CoverCount}";
            
            var orderPart = !string.IsNullOrEmpty(_currentOrder.OrderNumber) ? $"Order #{_currentOrder.OrderNumber}" : "Order #";
            var orderNote = string.IsNullOrWhiteSpace(_currentOrder.Notes)
                ? null
                : TruncateHeaderNote(_currentOrder.Notes.Trim(), 15);
            
            if (_isCollectionOrder)
            {
                OrderIdentityLabel.Text = $"{orderPart} Collection";
            }
            else if (_isDeliveryOrder)
            {
                OrderIdentityLabel.Text = $"{orderPart} Delivery";
            }
            else
            {
                var baseText = $"{orderPart} Table {_currentOrder.TableNumber}";
                OrderIdentityLabel.Text = string.IsNullOrEmpty(orderNote)
                    ? baseText
                    : $"{baseText} | Note: {orderNote}";
            }
            
            SubtotalLabel.Text = $"£{_currentOrder.Subtotal:F2}";
            VATLabel.Text = $"£{_currentOrder.VAT:F2}";
            
            if (_currentOrder.ServiceChargePercent > 0)
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
                "🧾"
            );
            
            var action = await dialog.ShowAsync();
            
            if (action == "None (0%)")
                _currentOrder.ServiceChargePercent = 0;
            else if (action == "10%")
                _currentOrder.ServiceChargePercent = 10;
            else if (action == "12.5%")
                _currentOrder.ServiceChargePercent = 12.5m;
            else if (action == "15%")
                _currentOrder.ServiceChargePercent = 15;
            
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
                noItemsDialog.SetAlert("No Items", "There are no items to void.", "ℹ️");
                await noItemsDialog.ShowAsync();
                return;
            }
            
            // Show void reason selection
            var reasonDialog = new ModernActionSheetDialog();
            reasonDialog.SetActionSheet(
                "Void Reason",
                new List<string> { "Customer changed mind", "Wrong item entered", "Kitchen error", "Manager override" },
                "⚠️"
            );
            
            var reason = await reasonDialog.ShowAsync();
            
            if (reason == null)
                return; // User cancelled
            
            // Check if PIN required (for voids over £30 and not Manager/Admin)
            var authService = ServiceHelper.GetService<AuthenticationService>();
            var currentUser = authService?.CurrentUser;
            var requiresPin = _currentOrder.Total > 30 && 
                             (currentUser == null || currentUser.Role == UserRole.User);
            
            if (requiresPin)
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
                
                // TODO: Validate manager PIN
                // For now, accept any 4-digit PIN as placeholder
                if (pin.Length != 4)
                {
                    var errorDialog = new ModernAlertDialog();
                    errorDialog.SetAlert("Invalid PIN", "Please enter a valid 4-digit PIN.", "❌", "#EF4444", "White");
                    await errorDialog.ShowAsync();
                    return;
                }
            }
            
            var confirmDialog = new ModernConfirmDialog();
            confirmDialog.SetConfirm(
                "Void Order - Danger",
                $"This action cannot be undone and will permanently void {_currentOrder.Items.Count} items.\n\nReason: {reason}\nAmount: £{_currentOrder.Total:F2}",
                "Confirm Void",
                "Cancel",
                "⚠️"
            );
            
            var confirm = await confirmDialog.ShowAsync();
            
            if (confirm)
            {
                await LogOperationalEventAsync("void_requested", new
                {
                    reason,
                    amount = _currentOrder.Total,
                    itemCount = _currentOrder.Items.Count
                });

                _isFinalizingOrder = true;
                var saved = await SaveVoidedOrderAsync(reason);
                if (!saved)
                {
                    _isFinalizingOrder = false;
                    return;
                }
                var voidedDialog = new ModernAlertDialog();
                voidedDialog.SetAlert("Voided", $"Order has been voided.\nReason: {reason}", "✅", "#10B981", "White");
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

            AppDataRefreshService.RequestRefresh();
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
            
            IEnumerable<FoodMenuItem> items;
            
            if (string.IsNullOrEmpty(_searchQuery))
            {
                // No search - show items from selected category
                if (_selectedCategory != null)
                {
                    items = _allMenuItems
                        .Where(i => i.CategoryId == _selectedCategory.Id)
                        .OrderBy(i => i.DisplayOrder);
                }
                else
                {
                    return;
                }
            }
            else
            {
                // Search across ALL items
                items = _allMenuItems
                    .Where(i => i.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i.Name);
            }
            
            foreach (var item in items)
            {
                var itemButton = CreateItemButton(item);
                ItemsContainer.Children.Add(itemButton);
            }
        }

        private async void OnSendClicked(object? sender, EventArgs e)
        {
            var clickTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] USER CLICKED SEND TO KITCHEN at {clickTime:HH:mm:ss.fff}");

            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before sending to kitchen.", "ℹ️");
                await noItemsDialog.ShowAsync();
                return;
            }

            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] Entering ExecuteUltraFastSendAsync");
            await ExecuteUltraFastSendAsync();
            var afterSendTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] ExecuteUltraFastSendAsync completed in {(afterSendTime - clickTime).TotalMilliseconds:F0}ms");
        }

        private async Task ExecuteUltraFastSendAsync()
        {
            if (_isUltraFastSendInProgress)
            {
                System.Diagnostics.Debug.WriteLine("⏱️ [SEND] Double-send tap blocked by re-entry guard");
                return;
            }

            EnsureCurrentOrderIdentity();
            _isUltraFastSendInProgress = true;
            var startTime = DateTime.Now;
            try
            {
                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] === START ExecuteUltraFastSendAsync at {startTime:HH:mm:ss.fff}");
                
                var pendingSendCount = _currentOrder.Items.Count(i => i.SendStatus == ItemSendStatus.NotSent);
                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] Pending items to send: {pendingSendCount}");
                
                var cloneStartTime = DateTime.Now;
                var printSnapshot = CloneOrderForSend(_currentOrder);
                var cloneEndTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] Cloned order for print in {(cloneEndTime - cloneStartTime).TotalMilliseconds:F0}ms");

                var markStartTime = DateTime.Now;
                foreach (var item in _currentOrder.Items.Where(i => i.SendStatus == ItemSendStatus.NotSent))
                {
                    item.SendStatus = ItemSendStatus.Sent;
                    item.SentAt = DateTime.Now;
                    item.FailureReason = null;
                }
                var markEndTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] Marked items as sent in UI in {(markEndTime - markStartTime).TotalMilliseconds:F0}ms");

                _currentOrder.Status = TableOrderStatus.Sent;
                _currentOrder.UpdatedAt = DateTime.Now;

                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] Firing background ProcessUltraFastSendPipelineAsync (don't wait)");
                _ = ProcessUltraFastSendPipelineAsync(printSnapshot);
                
                var navStartTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] About to call HandleSuccessfulSendAsync + Navigate");
                await HandleSuccessfulSendAsync(pendingSendCount, fastExit: true);
                var navEndTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] Navigation completed in {(navEndTime - navStartTime).TotalMilliseconds:F0}ms");
            }
            finally
            {
                _isUltraFastSendInProgress = false;
                var endTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] === END ExecuteUltraFastSendAsync (total: {(endTime - startTime).TotalMilliseconds:F0}ms)");
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

                if (_rolloutConfig.EnableSendDurability)
                {
                    var batchId = await _orderService.CreateSendBatchAsync(persistedOrder.Id, persistedOrder.Items);
                    await ProcessDurableSendInBackgroundAsync(persistedOrder, batchId, printSnapshot);
                }
                else
                {
                    var legacyPrint = await _orderRoutingPrintService.PrintOrderAsync(printSnapshot);
                    if (!legacyPrint.AnyPrinted)
                    {
                        await LogOperationalEventAsync("send_failed", new
                        {
                            reason = "legacy_no_routes_printed",
                            failedRoutes = legacyPrint.FailedRoutes
                        });
                    }
                }

                await PersistDraftAsync(force: true, lifecycleOverride: GetSendLifecycleState());
                AppDataRefreshService.RequestRefresh();
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
                ServiceChargePercent = source.ServiceChargePercent,
                Status = source.Status
            };

            foreach (var item in source.Items)
            {
                clone.Items.Add(new TableOrderItem
                {
                    Id = item.Id,
                    OrderId = item.OrderId,
                    MenuItemId = item.MenuItemId,
                    Name = item.Name,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    VatCategory = item.VatCategory,
                    PrintGroupId = item.PrintGroupId,
                    Notes = item.Notes,
                    SendStatus = item.SendStatus,
                    SentAt = item.SentAt,
                    FailureReason = item.FailureReason,
                    CreatedAt = item.CreatedAt,
                    SelectedAddons = new ObservableCollection<SelectedAddon>(item.SelectedAddons ?? new ObservableCollection<SelectedAddon>())
                });
            }

            clone.RecalculateAll();
            return clone;
        }

        private async Task ProcessDurableSendInBackgroundAsync(Order persistedOrder, string batchId, TableOrder? printSourceOrder = null)
        {
            try
            {
                var printOrder = printSourceOrder ?? _currentOrder;
                var printResult = await _orderRoutingPrintService.PrintOrderAsync(printOrder);

                var printedDbItemIds = persistedOrder.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.ClientItemId) && printResult.PrintedItemIds.Contains(item.ClientItemId!))
                    .Select(item => item.Id)
                    .ToList();

                var failedItems = new List<(int OrderItemDbId, string Reason)>();
                if (printResult.FailedRouteDetails.Count > 0)
                {
                    foreach (var failure in printResult.FailedRouteDetails)
                    {
                        var routeItems = persistedOrder.Items
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
                    foreach (var item in persistedOrder.Items)
                    {
                        failedItems.Add((item.Id, "No active print groups could be used."));
                    }
                }

                await _orderService.MarkSendBatchResultAsync(persistedOrder.Id, batchId, printedDbItemIds, failedItems);

                _ = LogOperationalEventAsync("sent", new
                {
                    batchId,
                    printedCount = printResult.PrintedItemIds.Count,
                    failedCount = failedItems.Count,
                    hasFailures = printResult.HasFailures
                });

                if (printResult.HasFailures)
                {
                    _ = LogOperationalEventAsync("send_failed", new
                    {
                        batchId,
                        reason = "partial_route_failure",
                        failedRoutes = printResult.FailedRouteDetails.Select(route => new { route.RouteName, route.RouteTarget, route.Reason }).ToList()
                    });
                }

                AppDataRefreshService.RequestRefresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderPlacement] Instant durable send error: {ex.Message}");
            }
        }

        private async Task HandleSuccessfulSendAsync(int successCount, bool fastExit = false)
        {
            var handleStartTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] HandleSuccessfulSendAsync start (fastExit={fastExit})");
            
            if (fastExit)
            {
                System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] In fastExit mode - firing background FinalizeTableSessionAfterSendAsync");
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
                    ? $"Order sent ✓ ({successCount} item{(successCount == 1 ? string.Empty : "s")})"
                    : "Order sent ✓";
                _ = ToastNotification.ShowAsync("Success", toastMessage, NotificationType.Success, 1200);
            }

            AppDataRefreshService.RequestRefresh();
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] About to call NavigateToRoleDashboardAsync");
            var navStartTime = DateTime.Now;
            await NavigateToRoleDashboardAsync(fastExit);
            var navEndTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] NavigateToRoleDashboardAsync returned in {(navEndTime - navStartTime).TotalMilliseconds:F0}ms");
            
            var handleEndTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] HandleSuccessfulSendAsync complete (total: {(handleEndTime - handleStartTime).TotalMilliseconds:F0}ms)");
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
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] NavigateToRoleDashboardAsync START (noAnimation={noAnimation})");
            var navStart = DateTime.Now;
            
            var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
            var route = _roleAccessService.ResolveDashboardRoute(authService.CurrentUser?.Role);
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] Resolved route: //{route} (animate={!noAnimation})");
            
            await Shell.Current.GoToAsync($"//{route}", !noAnimation);
            
            var navEnd = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"⏱️ [SEND] NavigateToRoleDashboardAsync COMPLETE - Shell.GoToAsync returned in {(navEnd - navStart).TotalMilliseconds:F0}ms");
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
            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before printing receipt.", "ℹ️");
                await noItemsDialog.ShowAsync();
                return;
            }
            
            // TODO: Implement actual receipt printing logic here
            // For now, show a confirmation message
            var printDialog = new ModernAlertDialog();
            printDialog.SetAlert("Printing", $"Receipt for Table {_currentOrder.TableNumber} sent to printer!", "🖨️", "#6366F1", "White");
            await printDialog.ShowAsync();
        }

        private async void OnMoreClicked(object? sender, EventArgs e)
        {
            var dialog = new MoreOptionsDialog();
            
            // Check user role for restricted features
            var authService = ServiceHelper.GetService<AuthenticationService>();
            var currentUser = authService?.CurrentUser;
            bool isManagerOrAdmin = currentUser?.Role == UserRole.Manager || currentUser?.Role == UserRole.Admin;
            
            var options = new List<(string Text, string Icon, bool IsEnabled)>
            {
                ("Discount", "", true),
                ("Table Transfer", "", true),
                ("Merge Tables", "", true),
                ("Fire Course", "", _currentOrder.Items.Count > 0),
                ("Loyalty Points", "", true), // Now enabled
                ("Split Bill", "", _currentOrder.Items.Count > 0),
                ("Price Override", "", isManagerOrAdmin), // Manager/Admin only
                ("Cash Drawer", "", isManagerOrAdmin) // Manager/Admin only
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
                    case "Split Bill":
                        await ShowSplitBillDialog();
                        break;
                    case "Price Override":
                        await ShowPriceOverrideDialog();
                        break;
                    case "Cash Drawer":
                        await OpenCashDrawer();
                        break;
                }
            }
        }

        private async void OnPayClicked(object? sender, EventArgs e)
        {
            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before proceeding to payment.", "ℹ️");
                await noItemsDialog.ShowAsync();
                return;
            }
            
            // Phase 6: Smart Prompts - Unsent Items Check
            if (_currentOrder.Items.Any(i => i.SendStatus == ItemSendStatus.NotSent))
            {
                var sendFirstDialog = new ModernConfirmDialog();
                sendFirstDialog.SetConfirm(
                    "Unsent Items",
                    "You have items that haven't been sent to the kitchen yet.\nDo you want to send them before paying?",
                    "Send & Pay",
                    "Pay Anyway",
                    "⚠️"
                );
                
                var sendFirst = await sendFirstDialog.ShowAsync();
                if (sendFirst)
                {
                    // Attempt to send items first
                    OnSendClicked(this, EventArgs.Empty);
                    // Just give it a short await to let send start, we don't await the void returning method.
                    await Task.Delay(1500); 
                }
            }

            decimal tip = 0;
            decimal totalDue = _currentOrder.Total;
            
            // Step 1: Check if TIP should be shown (only if Service Charge = 0)
            if (_currentOrder.ServiceCharge == 0)
            {
                var tipDialog = new TipSelectionDialog();
                tipDialog.SetOrderTotal(_currentOrder.Subtotal);
                tip = await tipDialog.ShowAsync();
                
                if (tip == -1) // Cancelled
                {
                    return;
                }
                
                totalDue = _currentOrder.Subtotal + tip;
            }

            await EnsureTableSessionContextAsync(skipOrderLink: true, allowSessionOpen: false);
            if (_tableSessionId.HasValue)
            {
                await _tableSessionService.MarkSessionPaymentAsync(_tableSessionId.Value);
            }
            
            // Track remaining balance for partial payments
            decimal remainingBalance = totalDue;
            decimal totalPaid = 0;

            if (_rolloutConfig.EnablePaymentLines)
            {
                await PersistDraftAsync(force: true, lifecycleOverride: GetDraftLifecycleState());
            }
            
            // Payment loop for partial payments
            while (remainingBalance > 0)
            {
                // Step 2: Show payment method selection
                var methodDialog = new PaymentMethodDialog();
                methodDialog.SetAmountDue(remainingBalance, totalDue - remainingBalance > 0 ? totalDue - remainingBalance : 0);
                var paymentMethod = await methodDialog.ShowAsync();
                
                if (paymentMethod == PaymentMethod.Cancelled)
                {
                    if (totalPaid > 0)
                    {
                        // Partial payment already made
                        var partialDialog = new ModernAlertDialog();
                        partialDialog.SetAlert("Partial Payment", $"£{totalPaid:F2} already paid. £{remainingBalance:F2} remaining.", "⚠️", "#F59E0B", "White");
                        await partialDialog.ShowAsync();
                    }
                    return;
                }
                
                // Step 3: Process selected payment method
                await LogOperationalEventAsync("payment_attempt", new
                {
                    method = paymentMethod.ToString(),
                    amountDue = remainingBalance
                });

                if (_rolloutConfig.EnablePaymentLines)
                {
                    var actor = ResolveCurrentActor();
                    await _orderService.RecordPaymentLineAsync(
                        _currentOrder.Id,
                        paymentMethod.ToString(),
                        remainingBalance,
                        "attempted",
                        0,
                        null,
                        actor.ActorName,
                        new { amountDue = remainingBalance, orderMode = _currentOrder.OrderMode });
                }

                switch (paymentMethod)
                {
                    case PaymentMethod.Cash:
                        var cashResult = await ProcessCashPayment(remainingBalance);
                        if (cashResult.Success)
                        {
                            await LogOperationalEventAsync("payment_approved", new
                            {
                                method = "cash",
                                amountPaid = cashResult.AmountPaid,
                                remaining = cashResult.Remaining
                            });
                            if (_rolloutConfig.EnablePaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "cash", cashResult.AmountPaid, "approved", 0, null, actor.ActorName);
                            }
                            totalPaid += cashResult.AmountPaid;
                            remainingBalance = cashResult.Remaining;
                        }
                        else
                        {
                            await LogOperationalEventAsync("payment_failed", new { method = "cash", amountDue = remainingBalance });
                            if (_rolloutConfig.EnablePaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "cash", remainingBalance, "failed", 0, null, actor.ActorName);
                            }
                        }
                        break;
                        
                    case PaymentMethod.Card:
                        var cardResult = await ProcessCardPayment(remainingBalance);
                        if (cardResult.Success)
                        {
                            await LogOperationalEventAsync("payment_approved", new
                            {
                                method = "card",
                                amountPaid = cardResult.AmountPaid,
                                remaining = 0m
                            });
                            if (_rolloutConfig.EnablePaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "card", cardResult.AmountPaid, "approved", 0, null, actor.ActorName);
                            }
                            totalPaid += cardResult.AmountPaid;
                            remainingBalance = 0;
                        }
                        else
                        {
                            await LogOperationalEventAsync("payment_failed", new { method = "card", amountDue = remainingBalance });
                            if (_rolloutConfig.EnablePaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "card", remainingBalance, "failed", 0, null, actor.ActorName);
                            }
                        }
                        break;
                        
                    case PaymentMethod.GiftCard:
                        var giftResult = await ProcessGiftCardPayment(remainingBalance);
                        if (giftResult.Success)
                        {
                            await LogOperationalEventAsync("payment_approved", new
                            {
                                method = "gift_card",
                                amountPaid = giftResult.AmountApplied,
                                remaining = giftResult.Remaining
                            });
                            if (_rolloutConfig.EnablePaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "gift_card", giftResult.AmountApplied, "approved", 0, null, actor.ActorName);
                            }
                            totalPaid += giftResult.AmountApplied;
                            remainingBalance = giftResult.Remaining;
                        }
                        else
                        {
                            await LogOperationalEventAsync("payment_failed", new { method = "gift_card", amountDue = remainingBalance });
                            if (_rolloutConfig.EnablePaymentLines)
                            {
                                var actor = ResolveCurrentActor();
                                await _orderService.RecordPaymentLineAsync(_currentOrder.Id, "gift_card", remainingBalance, "failed", 0, null, actor.ActorName);
                            }
                        }
                        break;
                }
            }
            
            // Payment complete - print receipt and close table
            await CompletePayment(totalDue, tip, totalPaid);
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

        private async Task<GiftCardPaymentResult> ProcessGiftCardPayment(decimal amountDue)
        {
            var giftDialog = new GiftCardPaymentDialog();
            giftDialog.SetAmountDue(amountDue);
            return await giftDialog.ShowAsync();
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
            
            // Show payment success
            var successDialog = new ModernAlertDialog();
            string tipText = tip > 0 ? $"\nTip: £{tip:F2}" : "";
            string orderTypeText = $"\n\n{GetSavedOrderTypeLabel()} order saved!";
            successDialog.SetAlert("Payment Complete", $"Total: £{totalAmount:F2}{tipText}{orderTypeText}\n\nPrinting receipt...", "✅", "#10B981", "White");
            await successDialog.ShowAsync();
            
            // Print receipt (always)
            await PrintReceipt(totalAmount, tip);
            
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
                        unsentDialog.SetAlert("Finalize Blocked", $"{unsentCount} item(s) are not sent yet.", "⚠️", "#EF4444", "White");
                        await unsentDialog.ShowAsync();
                        return false;
                    }

                    if (Math.Abs(totalPaid - totalAmount) > 0.009m)
                    {
                        var balanceDialog = new ModernAlertDialog();
                        balanceDialog.SetAlert("Finalize Blocked", "Payment is not fully settled.", "⚠️", "#EF4444", "White");
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

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving paid order: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SaveVoidedOrderAsync(string reason)
        {
            try
            {
                var orderService = new OrderService();

                var authService = ServiceHelper.GetService<AuthenticationService>();
                var currentUser = authService?.CurrentUser;

                var order = await BuildPersistentOrderSnapshotAsync(LocalLifecycleState.Voided, voidReason: reason, voidedBy: currentUser?.Name ?? currentUser?.Username, voidedAt: DateTime.Now);
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
                    amount = _currentOrder.Total
                }, DateTime.Now);

                await EnsureTableReleasedAfterFinalizeAsync("voided", currentUser?.Name ?? currentUser?.Username, new
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

        private async Task PrintReceipt(decimal total, decimal tip)
        {
            // TODO: Implement actual receipt printing
            // For now, simulate print delay
            await Task.Delay(500);
            
            // In real implementation:
            // - Connect to printer service
            // - Format receipt with order items, totals, tip, etc.
            // - Send to printer
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
                // Navigate back to Visual Table Layout for dine-in orders
                await Shell.Current.GoToAsync("//visuallayout");
            }
        }

        private async Task ShowDiscountDialog()
        {
            var dialog = new DiscountDialog();
            dialog.SetOrderSubtotal(_currentOrder.Subtotal);
            
            var result = await dialog.ShowAsync();
            
            if (result.HasValue)
            {
                var (discountAmount, discountPercent, reason) = result.Value;
                
                _currentOrder.Discount = discountAmount;
                _currentOrder.DiscountPercent = discountPercent;
                _currentOrder.DiscountReason = reason;
                
                UpdateDisplay();
                await MarkCurrentOrderChangedAsync();
                
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
                    errorDialog.SetAlert("Transfer Failed", transferMessage, "❌", "#EF4444", "White");
                    await errorDialog.ShowAsync();
                    return;
                }

                _currentOrder.TableNumber = int.Parse(selectedTable.TableNumber);
                
                // Update TopBar title
                if (TopBar != null)
                {
                    TopBar.SetPageTitle($"Table {selectedTable.TableNumber}");
                }
                
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
                
                // TODO: Update table status in database
                // - Mark old table as Available
                // - Mark new table as Occupied
                await MarkCurrentOrderChangedAsync();
            }
        }

        private async Task ShowMergeTablesDialog()
        {
            if (!_tableSessionId.HasValue)
            {
                var infoDialog = new ModernAlertDialog();
                infoDialog.SetAlert("Merge Tables", "No active session is available for merging.", "🔗", "#3B82F6", "White");
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
                errorDialog.SetAlert("Merge Failed", "The selected table does not have an active session.", "❌", "#EF4444", "White");
                await errorDialog.ShowAsync();
                return;
            }

            var confirmDialog = new ModernConfirmDialog();
            confirmDialog.SetConfirm(
                "Confirm Merge",
                $"Merge Table {_currentOrder.TableNumber} with Table {childTable.TableNumber}?\nThis will keep the current table as the parent session.",
                "Merge",
                "Cancel",
                "🔗"
            );

            if (!await confirmDialog.ShowAsync())
            {
                return;
            }

            var mergeResult = await _tableSessionService.MergeSessionsAsync(_tableSessionId.Value, childTable.CurrentSession.Id, "system", $"Merged Table {childTable.TableNumber} into Table {_currentOrder.TableNumber}");
            if (!mergeResult.success)
            {
                var errorDialog = new ModernAlertDialog();
                errorDialog.SetAlert("Merge Failed", mergeResult.message, "❌", "#EF4444", "White");
                await errorDialog.ShowAsync();
                return;
            }

            var successDialog = new ModernAlertDialog();
            successDialog.SetAlert("Merged", $"Table {childTable.TableNumber} merged into Table {_currentOrder.TableNumber}.", "✅", "#10B981", "White");
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
                "✂️"
            );
            
            var selected = await dialog.ShowAsync();
            
            if (selected != null)
            {
                var successDialog = new ModernAlertDialog();
                successDialog.SetAlert("Bill Split", $"Bill split: {selected}", "✅", "#10B981", "White");
                await successDialog.ShowAsync();
            }
        }

        private async Task ShowLoyaltyPointsDialog()
        {
            var dialog = new StyledPromptDialog();
            dialog.SetDialog(
                "Loyalty Points",
                "Enter customer phone or loyalty card number:",
                "e.g., 555-1234",
                Keyboard.Telephone
            );
            
            var result = await dialog.ShowAsync();
            
            if (!string.IsNullOrEmpty(result))
            {
                var infoDialog = new ModernAlertDialog();
                infoDialog.SetAlert("Loyalty", "Loyalty points feature coming soon!", "⭐", "#3B82F6", "White");
                await infoDialog.ShowAsync();
            }
        }

        private async Task ShowPriceOverrideDialog()
        {
            if (_currentOrder.Items.Count == 0)
            {
                var noItemsDialog = new ModernAlertDialog();
                noItemsDialog.SetAlert("No Items", "Please add items before overriding prices.", "ℹ️");
                await noItemsDialog.ShowAsync();
                return;
            }
            
            var infoDialog = new ModernAlertDialog();
            infoDialog.SetAlert("Price Override", "Select an item to override its price.", "💵", "#3B82F6", "White");
            await infoDialog.ShowAsync();
        }

        private async Task OpenCashDrawer()
        {
            var confirmDialog = new ModernConfirmDialog();
            confirmDialog.SetConfirm(
                "Open Cash Drawer",
                "Are you sure you want to open the cash drawer?",
                "Yes",
                "No",
                "💳"
            );
            
            var confirm = await confirmDialog.ShowAsync();
            
            if (confirm)
            {
                // TODO: Send command to cash drawer hardware
                var successDialog = new ModernAlertDialog();
                successDialog.SetAlert("Cash Drawer", "Cash drawer opened successfully!", "✅", "#10B981", "White");
                await successDialog.ShowAsync();
            }
        }
    }
}
