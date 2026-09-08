using System.Globalization;
using OrderWeb.Client.Dialogs;
using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Payments;
using OrderWeb.Client.Services;
using OrderWeb.Client.Views.Orders;
using OrderWeb.Contracts.Access;

namespace OrderWeb.Client.Pages.Orders;

public partial class OrderPage : ContentPage
{
        private readonly ClientCacheService _cache = new();
        private readonly MotherOrderClient _orderClient = new();
        private readonly MotherMenuClient _menuClient = new();
        private readonly ClientOfflinePolicy _offlinePolicy = new();
        private readonly MotherPrintClient _printClient = new();
        private readonly List<CachedMenuCategory> _categories = new();
        private readonly List<CachedProduct> _products = new();
        private readonly List<OrderSummaryLine> _basket = new();
        private readonly CachedTable? _table;
        private readonly int _covers;
        private string? _customerName;
        private string? _customerPhone;
        private CachedMenuCategory? _selectedCategory;
        private MotherOrderState? _currentOrder;
        private bool _loaded;
        private bool _orderContextLoaded;
        private bool _isVisible;
        private bool _liveReloadInFlight;

        public OrderPage()
        {
            InitializeComponent();
            ClientPageChrome.HideSystemBackChrome(this);

        MenuGrid.ItemTapped += async (_, item) => await AddItemAsync(item);
        Summary.SendClicked += async (_, _) => await SendToKitchenAsync();
        Summary.PaymentClicked += async (_, _) =>
        {
            if (_currentOrder is null)
            {
                await DisplayAlert("Payment", "Payment requires a Mother-confirmed order.", "OK");
                return;
            }

            if (IsCustomerHubOrder(_currentOrder))
            {
                var online = await _offlinePolicy.IsMotherOnlineAsync();
                if (!online)
                {
                    await DisplayAlert("Payment blocked", "Taking payment for this order requires Mother POS.", "OK");
                    return;
                }
            }

            await Navigation.PushAsync(
                new PaymentPage(CurrentTotal(), _currentOrder.OrderId, _currentOrder.Version),
                false);
        };
        Summary.ServiceClicked += async (_, _) => await QueueOrderActionAsync("service_charge", "Service charge request queued for Mother POS.");
        Summary.NotesClicked += async (_, _) => await AddOrderNoteAsync();
        Summary.VoidClicked += async (_, _) => await VoidOrderAsync();
        Summary.MoreClicked += async (_, _) => await Navigation.PushModalAsync(new MoreOptionsDialog(), false);
        Summary.PrintClicked += async (_, _) => await PrintOrderAsync();
        Summary.LineClicked += async (_, line) => await EditMotherLineAsync(line);

        TopBar.MenuClicked += async (_, _) => await OnMenuClickedAsync();
        TopBar.LogoutClicked += async (_, _) => await OnLogoutClickedAsync();

        SizeChanged += (_, _) => ApplyResponsiveLayout();
            ApplyResponsiveLayout();
            Refresh();
        }

        public OrderPage(CachedTable table, int covers)
            : this()
        {
            _table = table;
            _covers = Math.Max(covers, table.Covers > 0 ? table.Covers : 1);
            Refresh();
        }

        public OrderPage(MotherOrderState order, string? customerName = null, string? customerPhone = null)
            : this()
        {
            _currentOrder = order;
            _customerName = FirstNonEmpty(customerName, order.CustomerName);
            _customerPhone = FirstNonEmpty(customerPhone, order.CustomerPhone);
            if (string.IsNullOrWhiteSpace(_customerName) &&
                string.IsNullOrWhiteSpace(_customerPhone) &&
                !string.IsNullOrWhiteSpace(order.ConflictMessage) &&
                order.ConflictMessage.Contains('·'))
            {
                var parts = order.ConflictMessage.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                _customerName = parts.ElementAtOrDefault(0);
                _customerPhone = parts.ElementAtOrDefault(1);
            }

            Refresh();
        }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClientPageChrome.HideSystemBackChrome(this);
        _isVisible = true;
        MotherEventClient.SharedAuthoritativeDataChanged += OnMotherOrderUpdated;
            if (!_loaded)
            {
                _loaded = true;
                await EnsureOrderContextAsync();
                await LoadMenuAsync();
            }
            else
            {
                Refresh();
            }
        }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;
        MotherEventClient.SharedAuthoritativeDataChanged -= OnMotherOrderUpdated;
    }

    protected override bool OnBackButtonPressed() => true;

    private async void OnMotherOrderUpdated(object? sender, MotherDataChangedEventArgs e)
    {
        if (!_isVisible ||
            _currentOrder is null ||
            string.IsNullOrWhiteSpace(e.EventType) ||
            !e.EventType.Contains("order", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // version carries the Mother order id for order.updated
        if (!string.IsNullOrWhiteSpace(e.Version) &&
            !string.Equals(e.Version.Trim(), _currentOrder.OrderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(ReloadOpenOrderFromMotherAsync);
    }

    private async Task ReloadOpenOrderFromMotherAsync()
    {
        if (_currentOrder is null || _liveReloadInFlight)
        {
            return;
        }

        if (_basket.Count > 0)
        {
            _currentOrder = _currentOrder with
            {
                ConflictMessage = "Changed on another terminal — finish or discard local items, then reopen."
            };
            Refresh();
            return;
        }

        _liveReloadInFlight = true;
        try
        {
            var previousVersion = _currentOrder.Version;
            var previousUpdated = _currentOrder.UpdatedUtc;
            var result = await _orderClient.RefreshLatestAsync(_currentOrder);
            var changedElsewhere =
                result.State.Version != previousVersion ||
                !string.Equals(result.State.UpdatedUtc, previousUpdated, StringComparison.Ordinal);
            _currentOrder = result.State with
            {
                ConflictMessage = changedElsewhere ? null : _currentOrder.ConflictMessage
            };
            // Keep collection/delivery customer header across Mother refresh.
            if (string.IsNullOrWhiteSpace(_customerName) &&
                !string.IsNullOrWhiteSpace(_currentOrder.ConflictMessage) &&
                _currentOrder.ConflictMessage.Contains('·'))
            {
                var parts = _currentOrder.ConflictMessage.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                _customerName = parts.ElementAtOrDefault(0);
                _customerPhone = parts.ElementAtOrDefault(1);
            }
            await _cache.SaveOrderStateAsync(_currentOrder);
            _basket.Clear();
            Refresh();
        }
        catch
        {
            // Order may have been paid/voided elsewhere — leave current UI; Live Order will drop it.
        }
        finally
        {
            _liveReloadInFlight = false;
        }
    }

    private async Task LoadMenuAsync()
    {
        var refreshed = await _menuClient.RefreshCacheAsync();
        if (!refreshed)
        {
            // Refresh failed — still try last-good cache, then a full operational menu pull.
            try
            {
                await new MotherOperationalSyncClient(_cache).PullAllAsync();
            }
            catch
            {
                // Fall through to whatever is already in SQLite.
            }
        }

        _categories.Clear();
        _categories.AddRange(await _cache.GetMenuCategoriesAsync());

        _selectedCategory = _categories.FirstOrDefault();
        await LoadProductsForSelectedCategoryAsync();
        Refresh();

        if (_categories.Count == 0)
        {
            await DisplayAlert(
                "No menu on this Client",
                "Mother POS menu is not in the local cache, so Collection/Delivery order place has no products.\n\nUse Update All from the sidebar, confirm Terminal Access includes ordering, then open the order again.",
                "OK");
        }
    }

    private async Task LoadProductsForSelectedCategoryAsync()
    {
        _products.Clear();
        if (_selectedCategory == null)
        {
            return;
        }

        _products.AddRange(await _cache.GetProductsByCategoryAsync(_selectedCategory.Id));
    }

    private void ApplyResponsiveLayout()
    {
        var pageWidth = Width > 0 ? Width : (Window?.Width ?? 0);
        // Stack panels earlier so the order column never overflows the window edge.
        var compact = pageWidth > 0 && pageWidth < 1280;
        OrderLayout.RowDefinitions.Clear();
        OrderLayout.ColumnDefinitions.Clear();

        // Never force a fixed summary width — that clipped PAYMENT / MORE / PRINT on Client.
        Summary.WidthRequest = -1;
        Summary.MinimumWidthRequest = -1;
        Summary.MaximumWidthRequest = -1;
        Summary.HorizontalOptions = LayoutOptions.Fill;

        if (compact)
        {
            OrderLayout.Padding = 12;
            OrderLayout.ColumnSpacing = 0;
            OrderLayout.RowSpacing = 12;
            OrderLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            OrderLayout.RowDefinitions.Add(new RowDefinition(new GridLength(0.95, GridUnitType.Star)));
            OrderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetRow(MenuPanel, 0);
            Grid.SetColumn(MenuPanel, 0);
            Grid.SetRow(Summary, 1);
            Grid.SetColumn(Summary, 0);
            return;
        }

        // Mother OrderPlacementPageSimple: ColumnDefinitions="2*,*" ColumnSpacing=15 Padding=15
        OrderLayout.Padding = 15;
        OrderLayout.ColumnSpacing = 15;
        OrderLayout.RowSpacing = 0;
        OrderLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        OrderLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        OrderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetRow(MenuPanel, 0);
        Grid.SetColumn(MenuPanel, 0);
        Grid.SetRow(Summary, 0);
        Grid.SetColumn(Summary, 1);
    }

        private void Refresh()
        {
            BuildCategoryTabs();
            MenuGrid.SetItems(FilteredProducts());
            ApplyOrderChrome();
            // Mother never shows a SERVICE quick button — service is a totals row / MORE action.
            Summary.SetShowServiceButton(false);
            Summary.SetOrder(
                OrderTitle(),
                OrderDetail(),
                SummaryLines(),
                CurrentSubtotal(),
                CurrentTotal(),
                showServiceChargeRow: IsTableOrder(),
                serviceChargeDescription: "Service charge",
                serviceChargeValue: "Not included");

            if (_categories.Count == 0)
            {
                CategoryTabsHost.IsVisible = true;
                CategoryTabs.Children.Clear();
                CategoryTabs.Children.Add(new Label
                {
                    Text = "No menu synced — use Update All",
                    FontSize = 14,
                    FontFamily = "OpenSansSemibold",
                    TextColor = Color.FromArgb("#B45309"),
                    VerticalTextAlignment = TextAlignment.Center,
                    Margin = new Thickness(8, 0)
                });
            }
        }

        private void ApplyOrderChrome()
        {
            var title = OrderScreenTitle();
            // Paint Mother title immediately (Collection Order / Delivery Order / Table Order).
            TopBar.SetPageTitle(title);
            _ = ApplyOrderChromeAsync(title);
        }

        private async Task ApplyOrderChromeAsync(string title)
        {
            try
            {
                var session = await _cache.GetCurrentLoginSessionAsync();
                var online = await _offlinePolicy.IsMotherOnlineAsync();
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TopBar.ConfigurePosChrome(
                        title,
                        session?.UserName,
                        session?.Role ?? "Client",
                        online ? "Connected" : "Mother Offline");
                });
            }
            catch
            {
                await MainThread.InvokeOnMainThreadAsync(() => TopBar.SetPageTitle(title));
            }
        }

        private string OrderScreenTitle()
        {
            if (IsDeliveryOrder())
            {
                return "Delivery Order";
            }

            if (IsCollectionOrder())
            {
                return "Collection Order";
            }

            return "Table Order";
        }

        private bool IsCollectionOrder() =>
            CustomerOrderHubRules.IsCollectionOrderType(_currentOrder?.OrderType);

        private bool IsDeliveryOrder() =>
            CustomerOrderHubRules.IsDeliveryOrderType(_currentOrder?.OrderType);

        private bool IsTableOrder() => !IsCollectionOrder() && !IsDeliveryOrder();

        private string OrderTitle()
        {
            // Mother shows "Order # Table {n}" for new table tickets (no order number yet).
            var number = string.IsNullOrWhiteSpace(_currentOrder?.OrderNumber)
                ? string.Empty
                : _currentOrder.OrderNumber.Trim();
            if (IsTableOrder() &&
                (string.Equals(number, _currentOrder?.OrderId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(number, "Table", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(number, "table", StringComparison.OrdinalIgnoreCase)))
            {
                number = string.Empty;
            }

            var orderPart = string.IsNullOrWhiteSpace(number) ? "Order #" : $"Order #{number}";

            if (IsDeliveryOrder())
            {
                return $"{orderPart} Delivery";
            }

            if (IsCollectionOrder())
            {
                return $"{orderPart} Collection";
            }

            if (!string.IsNullOrWhiteSpace(_currentOrder?.TableNumber))
            {
                return $"{orderPart} Table {_currentOrder.TableNumber}";
            }

            if (!string.IsNullOrWhiteSpace(_table?.TableNumber))
            {
                return $"{orderPart} Table {_table.TableNumber}";
            }

            return string.IsNullOrWhiteSpace(number) ? "Order # New" : orderPart;
        }

        private string OrderDetail()
        {
            if (IsCollectionOrder() || IsDeliveryOrder())
            {
                return FormatCustomerDetail(
                    FirstNonEmpty(_customerName, _currentOrder?.CustomerName),
                    FirstNonEmpty(_customerPhone, _currentOrder?.CustomerPhone),
                    IsDeliveryOrder() ? "Delivery order" : "Collection order");
            }

            if (_currentOrder?.OrderType.Equals("Table", StringComparison.OrdinalIgnoreCase) == true || _table != null)
            {
                return $"Guests = {Math.Max(_currentOrder?.Guests ?? _covers, 1)}";
            }

            if (!string.IsNullOrWhiteSpace(_currentOrder?.ConflictMessage) &&
                _currentOrder.ConflictMessage.Contains('·'))
            {
                return _currentOrder.ConflictMessage;
            }

            return string.IsNullOrWhiteSpace(_currentOrder?.OrderType) ? "Order" : $"{_currentOrder.OrderType} order";
        }

        private static string FormatCustomerDetail(string? name, string? phone, string fallback)
        {
            var cleanName = name?.Trim();
            var cleanPhone = phone?.Trim();
            if (!string.IsNullOrWhiteSpace(cleanName) && !string.IsNullOrWhiteSpace(cleanPhone))
            {
                return $"{cleanName} · {cleanPhone}";
            }

            if (!string.IsNullOrWhiteSpace(cleanName))
            {
                return cleanName;
            }

            if (!string.IsNullOrWhiteSpace(cleanPhone))
            {
                return cleanPhone;
            }

            return fallback;
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

        private async Task EnsureOrderContextAsync()
        {
            if (_orderContextLoaded || _currentOrder != null || _table == null)
            {
                _orderContextLoaded = true;
                return;
            }

            _orderContextLoaded = true;

            try
            {
                if (!string.IsNullOrWhiteSpace(_table.CurrentOrderId))
                {
                    var online = await _offlinePolicy.IsMotherOnlineAsync();
                    var decision = _offlinePolicy.Evaluate(ClientOperation.OpenCollectionOrder, online);
                    if (!decision.Allowed)
                    {
                        await DisplayAlert("Open Table blocked", decision.Message, "OK");
                        return;
                    }

                    var opened = await _orderClient.OpenOrderForEditAsync(_table.CurrentOrderId);
                    _currentOrder = opened.State;
                    await _cache.SaveOrderStateAsync(_currentOrder);
                    Refresh();
                    return;
                }

                var session = await _cache.GetCurrentLoginSessionAsync();
                var createDecision = _offlinePolicy.Evaluate(
                    ClientOperation.SaveCollectionOrder,
                    await _offlinePolicy.IsMotherOnlineAsync());
                if (!createDecision.Allowed)
                {
                    await DisplayAlert("Table Order blocked", createDecision.Message, "OK");
                    return;
                }

                var result = await _orderClient.OpenOrCreateTableOrderAsync(_table, _covers, session);
                _currentOrder = result.State;
                await _cache.SaveOrderStateAsync(_currentOrder);
                Refresh();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Table Order", $"Could not open this table order from Mother POS.\n\n{ex.Message}", "OK");
            }
        }

        private IReadOnlyList<OrderSummaryLine> SummaryLines()
        {
            if (_basket.Count > 0)
            {
                return _basket;
            }

            return _currentOrder?.Lines
                .Select(line => new OrderSummaryLine(
                    line.Name,
                    line.Quantity,
                    line.UnitPrice,
                    line.Modifiers.Count > 0 ? string.Join(", ", line.Modifiers) : null,
                    line.Notes,
                    line.Id))
                .ToList()
                ?? _basket;
        }

    private async Task EditMotherLineAsync(OrderSummaryLine summaryLine)
    {
        if (_currentOrder == null || string.IsNullOrWhiteSpace(summaryLine.LineId))
        {
            return;
        }

        if (IsCustomerHubOrder(_currentOrder))
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.EditCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                await DisplayAlert("Order edit blocked", decision.Message, "OK");
                return;
            }
        }

        var motherLine = _currentOrder.Lines.FirstOrDefault(line =>
            string.Equals(line.Id, summaryLine.LineId, StringComparison.OrdinalIgnoreCase));
        if (motherLine == null)
        {
            return;
        }

        var action = await DisplayActionSheet(
            $"{motherLine.Quantity} x {motherLine.Name}",
            "Cancel",
            "Remove item",
            "+1 quantity",
            "-1 quantity");
        if (string.IsNullOrWhiteSpace(action) || action == "Cancel")
        {
            return;
        }

        try
        {
            MotherCommandResult result;
            if (action == "Remove item")
            {
                result = await _orderClient.RemoveItemAsync(_currentOrder, motherLine);
            }
            else if (action == "+1 quantity")
            {
                result = await _orderClient.UpdateQuantityAsync(_currentOrder, motherLine, motherLine.Quantity + 1);
            }
            else if (action == "-1 quantity")
            {
                if (motherLine.Quantity <= 1)
                {
                    result = await _orderClient.RemoveItemAsync(_currentOrder, motherLine);
                }
                else
                {
                    result = await _orderClient.UpdateQuantityAsync(_currentOrder, motherLine, motherLine.Quantity - 1);
                }
            }
            else
            {
                return;
            }

            _currentOrder = result.State;
            await _cache.SaveOrderStateAsync(_currentOrder);
            _basket.Clear();
            Refresh();
            if (result.ConflictDetected)
            {
                await DisplayAlert("Order conflict", result.Message, "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Mother POS", ex.Message, "OK");
        }
    }

    private IEnumerable<CachedProduct> FilteredProducts()
    {
        var query = SearchEntry.Text?.Trim();
        return _products.Where(product =>
            string.IsNullOrWhiteSpace(query)
            || product.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void BuildCategoryTabs()
    {
        CategoryTabs.Children.Clear();
        CategoryTabsHost.IsVisible = _categories.Count > 0;
        foreach (var category in _categories)
        {
            var selected = category == _selectedCategory;
            var button = new Button
            {
                Text = category.Name.ToUpperInvariant(),
                HeightRequest = 48,
                WidthRequest = 158,
                FontFamily = "OpenSansSemibold",
                FontSize = 16,
                CornerRadius = 8,
                Padding = 0,
                BackgroundColor = Color.FromArgb(selected ? "#EF4444" : "#3B82F6"),
                TextColor = Colors.White
            };
            button.Clicked += async (_, _) =>
            {
                _selectedCategory = category;
                await LoadProductsForSelectedCategoryAsync();
                Refresh();
            };
            CategoryTabs.Children.Add(button);
        }
    }

    private async Task AddItemAsync(CachedProduct item)
    {
        if (_currentOrder != null && IsCustomerHubOrder(_currentOrder))
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.EditCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                await DisplayAlert("Order edit blocked", decision.Message, "OK");
                return;
            }
        }

        var selectedModifiers = item.ModifierGroups
            .SelectMany(group => group.Modifiers.Take(Math.Min(group.MaxSelect, 1)))
            .Select(modifier => modifier.Name)
            .ToArray();
        var modifiers = selectedModifiers.Length > 0
            ? string.Join(", ", selectedModifiers)
            : null;

        if (item.ModifierGroups.Count > 0)
        {
            await Navigation.PushModalAsync(new ModifierDialog(), false);
        }

        if (_currentOrder != null)
        {
            try
            {
                var result = await _orderClient.AddItemAsync(_currentOrder, item, selectedModifiers);
                _currentOrder = result.State;
                await _cache.SaveOrderStateAsync(_currentOrder);
                _basket.Clear();
                Refresh();
                if (result.ConflictDetected)
                {
                    await DisplayAlert("Order conflict", result.Message, "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Mother POS", ex.Message, "OK");
            }

            return;
        }

        var existing = _basket.FirstOrDefault(line => line.Name == item.Name && line.Modifiers == modifiers);
        if (existing != null)
        {
            var index = _basket.IndexOf(existing);
            _basket[index] = existing with { Quantity = existing.Quantity + 1 };
        }
        else
        {
            _basket.Add(new OrderSummaryLine(item.Name, 1, item.Price, modifiers));
        }

        Refresh();
    }

    private async Task SendToKitchenAsync()
    {
            if (SummaryLines().Count == 0)
            {
                await DisplayAlert("Send to Kitchen", "Add items before sending to kitchen.", "OK");
                return;
            }

            var kitchenOp = _currentOrder != null && IsCustomerHubOrder(_currentOrder)
                ? ClientOperation.PrintCollectionOrder
                : ClientOperation.SubmitFinalOrder;
            var decision = _offlinePolicy.Evaluate(kitchenOp, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                await DisplayAlert("Send to Kitchen blocked", decision.Message, "OK");
                return;
            }

            if (_currentOrder == null)
            {
                await DisplayAlert("Send to Kitchen failed", "This order has not been created on Mother POS.", "OK");
                return;
            }

            try
            {
                var result = await _orderClient.SendToKitchenAsync(_currentOrder);
                _currentOrder = result.State;
                await _cache.SaveOrderStateAsync(_currentOrder);
                _basket.Clear();
                Refresh();
                await DisplayAlert(
                    result.ConflictDetected ? "Order conflict" : "Mother POS",
                    result.Message,
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Send to Kitchen failed", ex.Message, "OK");
            }
        }

    private async Task PrintOrderAsync()
    {
            if (SummaryLines().Count == 0)
            {
                await DisplayAlert("Print", "Add items before printing.", "OK");
                return;
            }

        if (_currentOrder != null && IsCustomerHubOrder(_currentOrder))
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.PrintCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                await DisplayAlert("Print blocked", decision.Message, "OK");
                return;
            }
        }

        await RequestPrintAsync("bill");
    }

        private async Task AddOrderNoteAsync()
        {
            if (SummaryLines().Count == 0)
            {
                await DisplayAlert("Notes", "Add an item before adding notes.", "OK");
                return;
            }

        var note = await DisplayPromptAsync("Notes", "Add note to the first item", "Save", "Cancel", "Kitchen note");
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

            if (_basket.Count > 0)
            {
                _basket[0] = _basket[0] with { Note = note.Trim() };
                Refresh();
                return;
            }

            if (_currentOrder?.Lines.Count > 0)
            {
                if (IsCustomerHubOrder(_currentOrder))
                {
                    var decision = _offlinePolicy.Evaluate(ClientOperation.EditCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
                    if (!decision.Allowed)
                    {
                        await DisplayAlert("Order edit blocked", decision.Message, "OK");
                        return;
                    }
                }

                try
                {
                    var firstLine = _currentOrder.Lines[0];
                    var result = await _orderClient.AddNoteAsync(_currentOrder, firstLine, note.Trim());
                    _currentOrder = result.State;
                    await _cache.SaveOrderStateAsync(_currentOrder);
                    Refresh();
                    await DisplayAlert(
                        result.ConflictDetected ? "Order conflict" : "Mother POS",
                        result.Message,
                        "OK");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Mother POS", ex.Message, "OK");
                }
            }
        }

        private async Task VoidOrderAsync()
        {
            if (SummaryLines().Count == 0)
            {
                await DisplayAlert("Void", "There are no items to void.", "OK");
                return;
            }

        var confirm = await DisplayAlert("Void Order", "Void all items from this order?", "Void", "Cancel");
        if (!confirm)
        {
            return;
        }

            if (_currentOrder != null && IsCustomerHubOrder(_currentOrder))
            {
                var decision = _offlinePolicy.Evaluate(ClientOperation.VoidCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
                if (!decision.Allowed)
                {
                    await DisplayAlert("Void blocked", decision.Message, "OK");
                    return;
                }

                try
                {
                    var result = await _orderClient.VoidCollectionOrderAsync(_currentOrder);
                    _currentOrder = result.State;
                    await _cache.SaveOrderStateAsync(_currentOrder);
                    _basket.Clear();
                    Refresh();
                    await DisplayAlert("Mother POS", result.Message, "OK");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Void failed", ex.Message, "OK");
                }

                return;
            }

            _basket.Clear();
            if (_currentOrder != null)
            {
                _currentOrder = _currentOrder with
                {
                    Lines = Array.Empty<MotherOrderLine>(),
                    Subtotal = 0m,
                    Tax = 0m,
                    Total = 0m,
                    Version = _currentOrder.Version + 1,
                    UpdatedUtc = DateTimeOffset.UtcNow.ToString("O")
                };
                await _cache.SaveOrderStateAsync(_currentOrder);
            }

            await QueueOrderActionAsync("void_order", "Void request queued for Mother POS.");
            Refresh();
        }

    private static bool IsCustomerHubOrder(MotherOrderState order) =>
        CustomerOrderHubRules.IsCustomerHubOrderType(order.OrderType);

    private async Task QueueOrderActionAsync(string actionType, string message)
    {
        if (!string.Equals(actionType, "unsent_draft", StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlert("Mother POS", "This action requires Mother POS confirmation and was not queued locally.", "OK");
            return;
        }

        await _cache.QueuePendingActionAsync(actionType, new
        {
            orderType = "Table",
            total = CurrentTotal(),
            orderId = _currentOrder?.OrderId,
            tableId = _currentOrder?.TableId ?? _table?.Id,
            tableNumber = _currentOrder?.TableNumber ?? _table?.TableNumber,
            lines = SummaryLines().Select(line => new
            {
                line.Name,
                line.Quantity,
                line.UnitPrice,
                line.Modifiers,
                line.Note
            }).ToList(),
            queuedAtUtc = DateTimeOffset.UtcNow
        });
        await DisplayAlert("Mother POS", message, "OK");
    }

    private async Task RequestPrintAsync(string printType)
    {
        var session = await _cache.GetCurrentLoginSessionAsync();
        var request = await _printClient.RequestPrintAsync(printType, _currentOrder?.OrderId, session);
        await _cache.SavePrintRequestAsync(request);

        await DisplayAlert("Print", request.Message, "OK");
    }

        private decimal CurrentSubtotal()
        {
            if (_currentOrder != null && _basket.Count == 0)
            {
                return _currentOrder.Subtotal;
            }

            return _basket.Sum(line => line.Quantity * line.UnitPrice);
        }

        private decimal CurrentTotal()
        {
            if (_currentOrder != null && _basket.Count == 0)
            {
                return _currentOrder.Total;
            }

            var subtotal = CurrentSubtotal();
            return subtotal + Math.Round(subtotal * 0.2m, 2);
        }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        MenuGrid.SetItems(FilteredProducts());
    }

    private async Task OnMenuClickedAsync()
    {
        // Mother order page opens Shell flyout; Client returns to the POS shell dashboard.
        await Navigation.PopToRootAsync(false);
    }

    private async Task OnLogoutClickedAsync()
    {
        await Navigation.PopToRootAsync(false);
    }
}
