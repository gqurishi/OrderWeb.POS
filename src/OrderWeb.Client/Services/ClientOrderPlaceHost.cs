using System.Globalization;
using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Payments;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Access;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Controls.OrderPlace;
using OrderWeb.SharedUI.Hosting;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client host for SharedUI Order Place — Mother HTTP only; no Client→cloud.
/// </summary>
public sealed class ClientOrderPlaceHost : IOrderPlaceHost
{
    private static readonly TimeSpan WarmMenuTtl = TimeSpan.FromMinutes(20);

    private readonly ClientCacheService _cache = new();
    private readonly MotherOrderClient _orderClient = new();
    private readonly MotherMenuClient _menuClient = new();
    private readonly ClientOfflinePolicy _offlinePolicy = new();
    private readonly MotherPrintClient _printClient = new();
    private readonly List<CachedMenuCategory> _categories = new();
    private readonly List<CachedProduct> _products = new();
    private readonly List<CachedMealDeal> _mealDeals = new();
    private readonly List<CachedTastingMenu> _tastingMenus = new();
    private readonly List<OrderPlaceProductItem> _specialProducts = new();
    private readonly HashSet<int> _categoryIdsWithProducts = new();
    private const string MealDealsMotherId = "__meal_deals__";
    private const string TastingMenusMotherId = "__tasting_menus__";
    private readonly IClientOrderPlaceUi _ui;
    private readonly SemaphoreSlim _motherWriteGate = new(1, 1);
    private readonly object _orderGate = new();

    private CachedTable? _table;
    private int _covers;
    private string? _customerName;
    private string? _customerPhone;
    private CachedMenuCategory? _selectedTopCategory;
    private CachedMenuCategory? _selectedCategory;
    private MotherOrderState? _currentOrder;
    private bool _initialized;
    private bool _persistQueued;
    private readonly HashSet<string> _loyaltyEarnedOrderIds = new(StringComparer.OrdinalIgnoreCase);
    private string? _loyaltyAddIdempotencyKey;
    private string? _loyaltyAddIdempotencyFingerprint;
    private bool _loyaltyAddBusy;

    public ClientOrderPlaceHost(IClientOrderPlaceUi ui)
    {
        _ui = ui;
        Session = new OrderPlaceSessionState();
    }

    public OrderPlaceSessionState Session { get; }

    public event EventHandler? StateChanged;

    public MotherOrderState? CurrentOrder => _currentOrder;

    public void ConfigureTable(CachedTable table, int covers)
    {
        _table = table;
        _covers = Math.Max(covers, table.Covers > 0 ? table.Covers : 1);
        PublishSession();
    }

    public void ConfigureExistingOrder(MotherOrderState order, string? customerName, string? customerPhone)
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

        PublishSession();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            PublishSession();
            return;
        }

        _initialized = true;

        var needsMotherOpen = _currentOrder is null && _table is not null;
        if (needsMotherOpen)
        {
            Session.StatusMessage = "Opening…";
            await _ui.SetLoadingAsync(true, "Opening order…");
        }

        try
        {
            // Paint categories/products from SQLite immediately (busy-dinner: no menu wait).
            await ReloadMenuFromCacheAsync();
            PublishSession();

            var orderTask = EnsureOrderContextAsync();
            var menuTask = EnsureMenuFreshInBackgroundAsync();

            await orderTask;
            if (string.Equals(Session.StatusMessage, "Opening…", StringComparison.Ordinal))
            {
                Session.StatusMessage = null;
            }

            PublishSession();
            await menuTask;
            PublishSession();
        }
        finally
        {
            if (needsMotherOpen)
            {
                await _ui.SetLoadingAsync(false);
            }
        }
    }

    /// <summary>
    /// When cache is warm, refresh menu in background; when cold, pull before continuing.
    /// </summary>
    private async Task EnsureMenuFreshInBackgroundAsync()
    {
        var status = await _cache.GetStatusAsync();
        var sections = await _cache.GetOperationalSectionSyncStatusAsync();
        var hasWarmCache = status.Categories > 0 && status.Products > 0;
        var menuFresh = hasWarmCache &&
                        sections.MenuOk == true &&
                        IsMenuSyncFresh(sections.MenuUtc);

        if (menuFresh)
        {
            _ = BackgroundRefreshMenuAsync();
            return;
        }

        await LoadMenuAsync();
    }

    public async Task SelectCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(categoryId, out var id))
        {
            return;
        }

        var category = _categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
        {
            return;
        }

        await SelectTopCategoryAsync(category);
    }

    public async Task SelectSubcategoryAsync(string? subcategoryId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subcategoryId) || !int.TryParse(subcategoryId, out var id))
        {
            return;
        }

        var category = _categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
        {
            return;
        }

        _selectedCategory = category;
        await LoadProductsForSelectedCategoryAsync();
        PublishSession();
    }

    public async Task AddProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            return;
        }

        if (productId.StartsWith("md:", StringComparison.OrdinalIgnoreCase))
        {
            await AddMealDealProductAsync(productId["md:".Length..]);
            return;
        }

        if (productId.StartsWith("tm:", StringComparison.OrdinalIgnoreCase))
        {
            await AddTastingMenuProductAsync(productId["tm:".Length..]);
            return;
        }

        if (!int.TryParse(productId, out var id))
        {
            return;
        }

        var item = _products.FirstOrDefault(p => p.Id == id)
            ?? (await _cache.GetProductsByCategoryAsync(_selectedCategory?.Id ?? 0)).FirstOrDefault(p => p.Id == id);
        if (item is null)
        {
            return;
        }

        if (_currentOrder != null && IsCustomerHubOrder(_currentOrder))
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.EditCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                await _ui.ShowAlertAsync("Order edit blocked", decision.Message);
                return;
            }
        }

        if (_currentOrder is null)
        {
            await _ui.ShowAlertAsync("Order", "This order has not been created on Mother POS yet.");
            return;
        }

        // Mother parity: variant → quick note → addons → add (cancel at any step aborts).
        var takeaway = IsCollectionOrder() || IsDeliveryOrder();
        var variants = item.ActiveVariants
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => new OrderPlaceVariantChoice(
                v.MotherId,
                v.Name,
                v.Description,
                v.PriceFor(takeaway)))
            .ToList();

        CachedProductVariant? selectedVariant = null;
        if (variants.Count > 0)
        {
            var picked = await _ui.PickVariantAsync(item.Name, variants);
            if (picked is null)
            {
                return;
            }

            selectedVariant = item.ActiveVariants.FirstOrDefault(v =>
                string.Equals(v.MotherId, picked.Id, StringComparison.OrdinalIgnoreCase))
                ?? new CachedProductVariant(
                    picked.Id,
                    picked.Name,
                    picked.Description,
                    picked.Price,
                    picked.Price,
                    0);
        }

        string? selectedNote = null;
        var quickNotes = item.ActiveQuickNotes.ToList();
        if (quickNotes.Count > 0)
        {
            var noteResult = await _ui.PickQuickNoteAsync(item.Name, quickNotes);
            if (noteResult.Kind == OrderPlaceQuickNoteKind.Cancelled)
            {
                return;
            }

            if (noteResult.Kind == OrderPlaceQuickNoteKind.SavedNote)
            {
                selectedNote = NormalizeNote(noteResult.NoteText);
            }
            else if (noteResult.Kind == OrderPlaceQuickNoteKind.CustomNote)
            {
                var custom = await _ui.PromptAsync(
                    "Custom Note",
                    $"Enter note for {item.Name}:",
                    "Save",
                    "Cancel",
                    "e.g., No onions, extra spicy");
                if (custom is null)
                {
                    return;
                }

                selectedNote = NormalizeNote(custom);
            }
        }

        var addonChoices = item.ModifierGroups
            .SelectMany(group => group.Modifiers)
            .Where(modifier => !string.IsNullOrWhiteSpace(modifier.Name))
            .GroupBy(modifier => modifier.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(modifier => new OrderPlaceAddonChoice(
                modifier.Name.Trim(),
                modifier.Name.Trim(),
                modifier.PriceDelta))
            .ToList();

        IReadOnlyList<string> selectedModifiers = Array.Empty<string>();
        if (addonChoices.Count > 0)
        {
            var pickedAddons = await _ui.PickAddonsAsync(item.Name, addonChoices);
            if (pickedAddons is null)
            {
                return;
            }

            selectedModifiers = pickedAddons.Select(addon => addon.Name).ToList();
        }

        // Optimistic: show line immediately; Mother upsert in background (serial).
        lock (_orderGate)
        {
            _currentOrder = _orderClient.BuildStateWithAddItem(
                _currentOrder,
                item,
                selectedModifiers,
                selectedNote,
                selectedVariant);
        }

        _ = _cache.SaveOrderStateAsync(_currentOrder);
        PublishSession();
        QueueMotherPersist();
    }

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public async Task SetLineQuantityAsync(string lineId, int quantity, CancellationToken cancellationToken = default)
    {
        if (_currentOrder is null)
        {
            return;
        }

        if (IsCustomerHubOrder(_currentOrder))
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.EditCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                await _ui.ShowAlertAsync("Order edit blocked", decision.Message);
                PublishSession();
                return;
            }
        }

        var motherLine = _currentOrder.Lines.FirstOrDefault(line =>
            string.Equals(line.Id, lineId, StringComparison.OrdinalIgnoreCase));
        if (motherLine is null)
        {
            return;
        }

        lock (_orderGate)
        {
            _currentOrder = _orderClient.BuildStateWithQuantity(_currentOrder, motherLine, quantity);
        }

        _ = _cache.SaveOrderStateAsync(_currentOrder);
        PublishSession();
        QueueMotherPersist();
    }

    public async Task EditLineNoteAsync(string lineId, CancellationToken cancellationToken = default)
    {
        if (_currentOrder is null)
        {
            return;
        }

        var motherLine = _currentOrder.Lines.FirstOrDefault(line =>
            string.Equals(line.Id, lineId, StringComparison.OrdinalIgnoreCase));
        if (motherLine is null)
        {
            return;
        }

        if (IsCustomerHubOrder(_currentOrder))
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.EditCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                await _ui.ShowAlertAsync("Order edit blocked", decision.Message);
                return;
            }
        }

        var note = await _ui.PromptAsync(
            "Notes",
            $"Note for {motherLine.Name}",
            "Save",
            "Cancel",
            "Kitchen note",
            motherLine.Notes ?? string.Empty);
        if (note is null)
        {
            return;
        }

        try
        {
            var result = await _orderClient.AddNoteAsync(_currentOrder, motherLine, note.Trim());
            _currentOrder = result.State;
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Mother POS", ex.Message);
        }
    }

    public Task TrailingLineActionAsync(string lineId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task OrderNotesAsync(CancellationToken cancellationToken = default)
    {
        if (_currentOrder is null)
        {
            await _ui.ShowAlertAsync("Notes", "Open an order before adding notes.");
            return;
        }

        var note = await _ui.PromptAsync(
            "Order Notes",
            "Enter notes for this order:",
            "Save",
            "Cancel",
            "e.g., Allergies, special requests...",
            _currentOrder.Notes ?? string.Empty);
        if (note is null)
        {
            return;
        }

        try
        {
            Session.IsBusy = true;
            RaiseChanged();
            var result = await _orderClient.SetOrderNotesAsync(_currentOrder, note);
            _currentOrder = PreserveCustomer(result.State);
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            await _ui.ShowToastAsync("Notes saved", "Order notes have been saved.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            await _ui.ShowToastAsync("Notes", ex.Message, StatusKind.Error);
        }
        finally
        {
            Session.IsBusy = false;
            RaiseChanged();
        }
    }

    public async Task VoidAsync(CancellationToken cancellationToken = default)
    {
        var isTable = IsTableOrder();
        var hasItems = _currentOrder?.Lines.Count > 0;
        // Match Mother IsUncommittedLocalTableOrder: no Mother order number yet
        // (do not use Version alone — failed sync can leave Version 0 with a real order).
        var isUncommittedTable = isTable &&
                                 (_currentOrder is null ||
                                  string.IsNullOrWhiteSpace(_currentOrder.OrderNumber));

        if (isUncommittedTable && !hasItems)
        {
            if (!await _ui.ConfirmAsync(
                    "Close Empty Table",
                    "No order has been created. Release this empty table?",
                    "Release Table",
                    "Keep Open"))
            {
                return;
            }

            await DiscardOrVoidOnMotherAsync(reason: "empty_table_closed", approvingPin: null, requireItems: false);
            return;
        }

        if (isUncommittedTable && hasItems)
        {
            if (!await _ui.ConfirmAsync(
                    "Discard Unsent Items",
                    "These items have not been sent to the kitchen. Discard them and release the table?",
                    "Discard & Close",
                    "Keep Open"))
            {
                return;
            }

            await DiscardOrVoidOnMotherAsync(reason: "unsent_basket_discarded", approvingPin: null, requireItems: false);
            return;
        }

        if (_currentOrder is null || _currentOrder.Lines.Count == 0)
        {
            await _ui.ShowAlertAsync("Void", "There are no items to void.");
            return;
        }

        // Same SharedUI Void Reason sheet as Mother (blue "i", stacked reasons, Cancel).
        var reason = await _ui.PickActionAsync(
            "Void Reason",
            "Customer changed mind",
            "Wrong item entered",
            "Kitchen error",
            "Manager override");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var session = await _cache.GetCurrentLoginSessionAsync();
        string? approvingPin = null;
        if (!IsManagerOrAdmin(session?.Role))
        {
            approvingPin = await _ui.PromptAsync(
                "Manager PIN Required",
                $"Void amount £{_currentOrder.Total:F2} requires manager approval:",
                "Continue",
                "Cancel",
                "4-digit PIN");
            if (string.IsNullOrWhiteSpace(approvingPin))
            {
                return;
            }
        }

        if (!await _ui.ConfirmAsync(
                "Void Order - Danger",
                $"This action cannot be undone and will permanently void {_currentOrder.Lines.Count} items.\n\nReason: {reason}\nAmount: £{_currentOrder.Total:F2}",
                "Confirm Void",
                "Cancel"))
        {
            return;
        }

        await DiscardOrVoidOnMotherAsync(reason, approvingPin, requireItems: true);
    }

    public async Task MoreAsync(CancellationToken cancellationToken = default)
    {
        var takeaway = IsCollectionOrder() || IsDeliveryOrder();
        var options = new List<OrderPlaceMoreOption>();

        if (!takeaway && CanChangeServiceCharge())
        {
            var scRemoved = string.Equals(_currentOrder?.ServiceChargeStatus, "removed", StringComparison.OrdinalIgnoreCase);
            options.Add(scRemoved
                ? new OrderPlaceMoreOption("RESTORE SERVICE CHARGE")
                : new OrderPlaceMoreOption("REMOVE SERVICE CHARGE", IsDestructive: true));
        }

        options.Add(new OrderPlaceMoreOption("Discount"));

        if (takeaway)
        {
            var customerPhone = FirstNonEmpty(_customerPhone, _currentOrder?.CustomerPhone);
            options.Add(new OrderPlaceMoreOption("Previous Orders", IsEnabled: !string.IsNullOrWhiteSpace(customerPhone)));
        }
        else
        {
            options.Add(new OrderPlaceMoreOption("Table Transfer"));
            options.Add(new OrderPlaceMoreOption("Merge Tables"));
            options.Add(new OrderPlaceMoreOption("Fire Course", IsEnabled: _currentOrder?.Lines.Count > 0));
        }

        options.Add(new OrderPlaceMoreOption(
            "Add Loyalty Points",
            IsEnabled: _currentOrder?.Lines.Count > 0
                       && _currentOrder.Total > 0m
                       && _currentOrder.LoyaltyPointsEarned <= 0
                       && HasLoyaltyAccess()
                       && await CanRunLoyaltyOnlineAsync()));
        options.Add(new OrderPlaceMoreOption("Cash Drawer"));

        var selected = await _ui.ShowMoreOptionsAsync(options);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        switch (selected)
        {
            case "Discount":
                await ShowDiscountAsync();
                break;
            case "Previous Orders":
                await ShowPreviousOrdersAsync();
                break;
            case "Table Transfer":
                await ShowTableTransferAsync();
                break;
            case "Merge Tables":
                await ShowMergeTablesAsync();
                break;
            case "Fire Course":
                await ShowFireCourseAsync();
                break;
            case "Add Loyalty Points":
            case "Loyalty Points":
                await ShowLoyaltyAddAsync();
                break;
            case "Cash Drawer":
                await OpenCashDrawerFromMoreAsync();
                break;
            case "REMOVE SERVICE CHARGE":
                await ChangeServiceChargeAsync(remove: true);
                break;
            case "RESTORE SERVICE CHARGE":
                await ChangeServiceChargeAsync(remove: false);
                break;
        }
    }

    private async Task DiscardOrVoidOnMotherAsync(string? reason, string? approvingPin, bool requireItems)
    {
        if (_currentOrder is null)
        {
            await _ui.ShowAlertAsync("Void", "No open order.");
            return;
        }

        if (requireItems && _currentOrder.Lines.Count == 0)
        {
            await _ui.ShowAlertAsync("Void", "There are no items to void.");
            return;
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var op = IsCustomerHubOrder(_currentOrder) ? ClientOperation.VoidCollectionOrder : ClientOperation.SubmitFinalOrder;
        var decision = _offlinePolicy.Evaluate(op, online);
        if (!decision.Allowed)
        {
            await _ui.ShowAlertAsync("Void blocked", decision.Message);
            return;
        }

        try
        {
            var tableId = _table?.Id ?? _currentOrder.TableId;
            var result = await _orderClient.VoidCollectionOrderAsync(
                _currentOrder,
                reason,
                approvingPin,
                tableId);
            var orderId = _currentOrder.OrderId;
            _currentOrder = result.State;
            await _cache.RemoveOpenOrderAsync(orderId, tableId);
            PublishSession();
            // Match Mother: confirm void, then leave Order Place (dashboard / prior surface).
            await _ui.ShowAlertAsync("Voided", string.IsNullOrWhiteSpace(result.Message)
                ? $"Order has been voided.\nReason: {reason}"
                : result.Message);
            await _ui.CloseOrderPageAsync();
        }
        catch (Exception ex)
        {
            await _ui.ShowToastAsync("Void failed", ex.Message, StatusKind.Error);
        }
    }

    private async Task ShowDiscountAsync()
    {
        if (_currentOrder is null)
        {
            await _ui.ShowAlertAsync("Discount", "Open an order first.");
            return;
        }

        var discountPick = await _ui.ShowDiscountAsync(_currentOrder.Subtotal);
        if (discountPick is null)
        {
            return;
        }

        decimal amount = 0m;
        decimal percent = 0m;
        var discountType = "fixed";
        string? reason;

        if (discountPick.Removed)
        {
            discountType = "fixed";
            amount = 0m;
            reason = "Discount removed";
        }
        else if (discountPick.Applied)
        {
            discountType = discountPick.IsPercentage ? "percent" : "fixed";
            if (discountPick.IsPercentage)
            {
                percent = discountPick.Amount;
            }
            else
            {
                amount = discountPick.Amount;
            }

            reason = discountPick.Reason;
        }
        else
        {
            return;
        }

        var appliedAmount = discountType == "percent"
            ? Math.Round(_currentOrder.Subtotal * (Math.Clamp(percent, 0m, 100m) / 100m), 2, MidpointRounding.AwayFromZero)
            : amount;
        string? approvingPin = null;
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (appliedAmount > 20m && !IsManagerOrAdmin(session?.Role))
        {
            approvingPin = await _ui.PromptAsync(
                "Manager PIN Required",
                $"Discount of £{appliedAmount:F2} requires manager approval:",
                "Continue",
                "Cancel",
                "4-digit PIN");
            if (string.IsNullOrWhiteSpace(approvingPin))
            {
                return;
            }
        }

        try
        {
            await FlushMotherPersistAsync();
            var result = await _orderClient.ApplyDiscountAsync(
                _currentOrder,
                amount,
                percent,
                reason,
                discountType,
                approvingPin);
            _currentOrder = MergeFinancials(_currentOrder, result.State);
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            await _ui.ShowToastAsync("Discount", result.Message, StatusKind.Success);
        }
        catch (Exception ex)
        {
            await _ui.ShowToastAsync("Discount failed", ex.Message, StatusKind.Error);
        }
    }

    private async Task ChangeServiceChargeAsync(bool remove)
    {
        if (_currentOrder is null || !CanChangeServiceCharge())
        {
            await _ui.ShowAlertAsync("Service Charge", "This order can no longer change its service charge.");
            return;
        }

        string? reason = null;
        if (remove)
        {
            reason = await _ui.PickActionAsync(
                "Removal Reason",
                "Customer request",
                "Service issue",
                "Manager discretion",
                "Custom reason");
            if (string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            if (reason == "Custom reason")
            {
                reason = await _ui.PromptAsync("Custom Reason", "Enter the reason for removing the service charge:", "Continue", "Cancel", "Reason");
                if (string.IsNullOrWhiteSpace(reason))
                {
                    return;
                }
            }
        }

        var pin = await _ui.PromptAsync(
            "Manager Approval",
            remove ? "Enter a manager PIN to remove the service charge." : "Enter a manager PIN to restore the service charge.",
            "Continue",
            "Cancel",
            "4-digit PIN");
        if (string.IsNullOrWhiteSpace(pin))
        {
            return;
        }

        if (!await _ui.ConfirmAsync(
                remove ? "Remove Service Charge" : "Restore Service Charge",
                remove
                    ? $"Remove £{_currentOrder.ServiceCharge:F2} service charge?\nReason: {reason}"
                    : $"Restore service charge on this table order?",
                remove ? "Remove" : "Restore",
                "Cancel"))
        {
            return;
        }

        try
        {
            await FlushMotherPersistAsync();
            var result = await _orderClient.SetServiceChargeAsync(
                _currentOrder,
                remove ? "remove" : "restore",
                reason,
                pin);
            _currentOrder = MergeFinancials(_currentOrder, result.State);
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            await _ui.ShowToastAsync("Service charge", result.Message, StatusKind.Success);
        }
        catch (Exception ex)
        {
            await _ui.ShowToastAsync("Service charge failed", ex.Message, StatusKind.Error);
        }
    }

    private async Task ShowTableTransferAsync()
    {
        if (_currentOrder is null)
        {
            return;
        }

        var floors = await _cache.GetFloorsWithTablesAsync();
        var available = floors
            .SelectMany(floor => floor.Tables)
            .Where(table => string.Equals(table.Status, "Available", StringComparison.OrdinalIgnoreCase))
            .Where(table => table.Id != (_table?.Id ?? _currentOrder.TableId))
            .OrderBy(table => table.TableNumber)
            .ToList();
        if (available.Count == 0)
        {
            await _ui.ShowAlertAsync("Table Transfer", "No available tables to transfer to.");
            return;
        }

        var currentLabel = $"Table {FirstNonEmpty(_currentOrder.TableNumber, _table?.TableNumber) ?? "?"}";
        var tableOptions = available
            .Select(table => new OrderPlaceTableOption(table.Id.ToString(CultureInfo.InvariantCulture), $"Table {table.TableNumber}"))
            .ToList();
        var picked = await _ui.ShowTableTransferAsync(currentLabel, tableOptions);
        if (picked is null)
        {
            return;
        }

        var target = available.FirstOrDefault(table =>
            string.Equals(table.Id.ToString(CultureInfo.InvariantCulture), picked.Id, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        try
        {
            await FlushMotherPersistAsync();
            var result = await _orderClient.TransferTableAsync(_currentOrder, target.Id);
            _currentOrder = result.State;
            _table = target;
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            await _ui.ShowAlertAsync("Transfer Complete", result.Message);
            await _ui.CloseOrderPageAsync();
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Transfer Failed", ex.Message);
        }
    }

    private async Task ShowMergeTablesAsync()
    {
        if (_currentOrder is null)
        {
            return;
        }

        var childNumber = await _ui.PromptAsync(
            "Merge Tables",
            "Enter the child table number to merge into this table:",
            "Merge",
            "Cancel",
            "e.g. 12");
        if (string.IsNullOrWhiteSpace(childNumber))
        {
            return;
        }

        if (!await _ui.ConfirmAsync(
                "Confirm Merge",
                $"Merge this table with Table {childNumber.Trim()}?\nThis keeps the current table as the parent session.",
                "Merge",
                "Cancel"))
        {
            return;
        }

        try
        {
            await FlushMotherPersistAsync();
            var result = await _orderClient.MergeTablesAsync(_currentOrder, childNumber.Trim());
            _currentOrder = MergeFinancials(_currentOrder, result.State);
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            await _ui.ShowAlertAsync("Merged", result.Message);
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Merge Failed", ex.Message);
        }
    }

    private async Task ShowFireCourseAsync()
    {
        if (_currentOrder is null || _currentOrder.Lines.Count == 0)
        {
            await _ui.ShowAlertAsync("Fire Course", "Add items before firing a course.");
            return;
        }

        var course = await _ui.ShowFireCourseAsync(includeDrinks: true);
        if (string.IsNullOrWhiteSpace(course))
        {
            return;
        }

        try
        {
            await FlushMotherPersistAsync();
            var result = await _orderClient.FireCourseAsync(_currentOrder, course);
            _currentOrder = MergeFinancials(_currentOrder, result.State);
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            await _ui.ShowAlertAsync("Fire Course", result.Message);
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Fire Course failed", ex.Message);
        }
    }

    private async Task ShowLoyaltyAddAsync()
    {
        if (_loyaltyAddBusy)
        {
            return;
        }

        if (_currentOrder is null || _currentOrder.Lines.Count == 0)
        {
            await _ui.ShowAlertAsync("Add Loyalty Points", "Add items before adding loyalty points.");
            return;
        }

        if (_currentOrder.Total <= 0m)
        {
            await _ui.ShowAlertAsync("Add Loyalty Points", "There is no bill amount to earn loyalty points on.");
            return;
        }

        if (!HasLoyaltyAccess())
        {
            const string message =
                "This Client terminal is not allowed to use Loyalty. On Mother: Terminal Health → Access → Loyalty ON → Save, then Update All (or wait for features.updated).";
            ClientLoyaltyDiagnostics.Record(
                "order_loyalty_add",
                false,
                message,
                LoyaltyErrorCodes.AccessDenied);
            await _ui.ShowAlertAsync("Add Loyalty Points", message);
            return;
        }

        if (_currentOrder.LoyaltyPointsEarned > 0
            || (!string.IsNullOrWhiteSpace(_currentOrder.OrderId)
                && _loyaltyEarnedOrderIds.Contains(_currentOrder.OrderId)))
        {
            await _ui.ShowAlertAsync(
                "Already Added",
                _currentOrder.LoyaltyPointsEarned > 0
                    ? $"Loyalty points were already added for this order ({_currentOrder.LoyaltyPointsEarned:N0} pts)."
                    : "Loyalty points were already added for this order on this till.");
            return;
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var decision = _offlinePolicy.Evaluate(ClientOperation.Loyalty, online);
        if (!decision.Allowed)
        {
            ClientLoyaltyDiagnostics.Record(
                "order_loyalty_add",
                false,
                decision.Message,
                LoyaltyErrorCodes.OfflineMother);
            await _ui.ShowAlertAsync("Add Loyalty Points", decision.Message);
            return;
        }

        var pointsPreview = OrderPlaceLoyaltyEarnRules.ComputePointsFromBillTotal(_currentOrder.Total);
        if (pointsPreview <= 0)
        {
            await _ui.ShowAlertAsync("Add Loyalty Points", "Bill total is under £1 — no points to add.");
            return;
        }

        _loyaltyAddBusy = true;
        try
        {
            // Sync basket to Mother first so order id + total are authoritative for earn.
            await FlushMotherPersistAsync();
            if (string.IsNullOrWhiteSpace(_currentOrder.OrderId))
            {
                await _ui.ShowAlertAsync("Add Loyalty Points", "This order has not been created on Mother POS yet.");
                return;
            }

            if (_currentOrder.LoyaltyPointsEarned > 0
                || _loyaltyEarnedOrderIds.Contains(_currentOrder.OrderId))
            {
                await _ui.ShowAlertAsync(
                    "Already Added",
                    _currentOrder.LoyaltyPointsEarned > 0
                        ? $"Loyalty points were already added for this order ({_currentOrder.LoyaltyPointsEarned:N0} pts)."
                        : "Loyalty points were already added for this order on this till.");
                return;
            }

            var orderId = _currentOrder.OrderId;
            var outcome = await _ui.ShowLoyaltyAddAsync(
                _currentOrder.Total,
                orderId,
                (lookup, points) => GetStickyLoyaltyAddKey(orderId, lookup, points));
            if (outcome is null)
            {
                return;
            }

            if (!outcome.Success)
            {
                var queued = string.Equals(outcome.ErrorCode, LoyaltyErrorCodes.Queued, StringComparison.OrdinalIgnoreCase);
                ClientLoyaltyDiagnostics.Record(
                    "order_loyalty_add",
                    false,
                    outcome.Message,
                    outcome.ErrorCode,
                    queued);
                await _ui.ShowAlertAsync("Add Loyalty Points", outcome.Message);
                return;
            }

            ClearStickyLoyaltyAddKey();
            _loyaltyEarnedOrderIds.Add(orderId);
            ClientLoyaltyDiagnostics.Record("order_loyalty_add", true, outcome.Message);

            try
            {
                var refreshed = await _orderClient.OpenOrderForEditAsync(orderId);
                _currentOrder = MergeFinancials(_currentOrder, refreshed.State);
                if (_currentOrder.LoyaltyPointsEarned <= 0 && outcome.PointsAdded is > 0)
                {
                    _currentOrder = _currentOrder with { LoyaltyPointsEarned = outcome.PointsAdded.Value };
                }

                await _cache.SaveOrderStateAsync(_currentOrder);
                PublishSession();
            }
            catch
            {
                // Points already added in cloud via Mother; keep till usable.
                if (outcome.PointsAdded is > 0)
                {
                    _currentOrder = _currentOrder with { LoyaltyPointsEarned = outcome.PointsAdded.Value };
                }

                PublishSession();
            }

            await _ui.ShowAlertAsync(
                "Loyalty Points Added",
                string.IsNullOrWhiteSpace(outcome.Message)
                    ? $"Added {outcome.PointsAdded:N0} points. New balance: {outcome.PointsBalance:N0}."
                    : outcome.Message);
        }
        catch (Exception ex)
        {
            ClientLoyaltyDiagnostics.Record(
                "order_loyalty_add",
                false,
                ex.Message,
                LoyaltyErrorCodes.Unknown);
            await _ui.ShowAlertAsync("Add Loyalty Points", ex.Message);
        }
        finally
        {
            _loyaltyAddBusy = false;
        }
    }

    private static bool HasLoyaltyAccess() =>
        ClientHostAccess.Features.Contains(PosFeatureKeys.CustomerPoints) ||
        ClientHostAccess.CanOpenMenu("Loyalty Points") ||
        ClientHostAccess.CanOpenMenu("Loyalty");

    private async Task<bool> CanRunLoyaltyOnlineAsync()
    {
        var online = await _offlinePolicy.IsMotherOnlineAsync();
        return _offlinePolicy.Evaluate(ClientOperation.Loyalty, online).Allowed;
    }

    private string GetStickyLoyaltyAddKey(string orderId, string lookup, int points)
    {
        var fingerprint = $"{orderId}|{lookup.Trim()}|{points}";
        if (!string.Equals(_loyaltyAddIdempotencyFingerprint, fingerprint, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(_loyaltyAddIdempotencyKey))
        {
            _loyaltyAddIdempotencyFingerprint = fingerprint;
            _loyaltyAddIdempotencyKey =
                $"client-order-loyalty-add:{orderId}:{lookup.Trim()}:{points}:{Guid.NewGuid():N}";
        }

        return _loyaltyAddIdempotencyKey;
    }

    private void ClearStickyLoyaltyAddKey()
    {
        _loyaltyAddIdempotencyKey = null;
        _loyaltyAddIdempotencyFingerprint = null;
    }

    private async Task ShowPreviousOrdersAsync()
    {
        var phone = FirstNonEmpty(_customerPhone, _currentOrder?.CustomerPhone);
        if (string.IsNullOrWhiteSpace(phone))
        {
            await _ui.ShowAlertAsync("Customer Required", "Select a saved customer before viewing previous orders.");
            return;
        }

        try
        {
            var (success, message, orders) = await _orderClient.GetPreviousOrdersAsync(phone);
            if (!success)
            {
                await _ui.ShowAlertAsync("Previous Orders Unavailable", message);
                return;
            }

            if (orders.Count == 0)
            {
                await _ui.ShowAlertAsync("Previous Orders", "No recent Collection/Delivery orders found for this customer.");
                return;
            }

            var labels = orders
                .Select(order =>
                    $"{order.OrderNumber ?? $"#{order.OrderDatabaseId}"} · {order.OrderType} · £{order.TotalAmount:F2}")
                .ToArray();
            var picked = await _ui.PickActionAsync("Previous Orders", labels);
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            var selected = orders.FirstOrDefault(order =>
                string.Equals(
                    $"{order.OrderNumber ?? $"#{order.OrderDatabaseId}"} · {order.OrderType} · £{order.TotalAmount:F2}",
                    picked,
                    StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                return;
            }

            await _ui.ShowAlertAsync(
                selected.OrderNumber ?? $"Order #{selected.OrderDatabaseId}",
                $"{selected.OrderType} · {selected.Status}\n£{selected.TotalAmount:F2}\n\n{selected.ItemsText}" +
                (string.IsNullOrWhiteSpace(selected.OrderNotes) ? string.Empty : $"\n\nNotes: {selected.OrderNotes}"));
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Previous Orders Unavailable", ex.Message);
        }
    }

    private async Task OpenCashDrawerFromMoreAsync()
    {
        try
        {
            var result = await _orderClient.OpenOrderPlaceCashDrawerAsync(_currentOrder, "Order place MORE");
            await _ui.ShowAlertAsync(result.Success ? "Cash Drawer" : "Cash Drawer Failed", result.Message);
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Cash Drawer Failed", ex.Message);
        }
    }

    private bool CanChangeServiceCharge()
    {
        if (!IsTableOrder() || _currentOrder is null)
        {
            return false;
        }

        var status = (_currentOrder.ServiceChargeStatus ?? string.Empty).Trim().ToLowerInvariant();
        return status is "applied" or "removed";
    }

    private static bool IsManagerOrAdmin(string? role) =>
        string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);

    private static MotherOrderState MergeFinancials(MotherOrderState current, MotherOrderState mother) =>
        current with
        {
            OrderNumber = string.IsNullOrWhiteSpace(mother.OrderNumber) ? current.OrderNumber : mother.OrderNumber,
            Status = mother.Status,
            Subtotal = mother.Subtotal,
            Tax = mother.Tax,
            Total = mother.Total,
            Version = mother.Version,
            UpdatedUtc = mother.UpdatedUtc,
            Discount = mother.Discount,
            ServiceCharge = mother.ServiceCharge,
            ServiceChargeStatus = mother.ServiceChargeStatus,
            ServiceChargePercent = mother.ServiceChargePercent,
            LoyaltyPointsEarned = mother.LoyaltyPointsEarned > 0
                ? mother.LoyaltyPointsEarned
                : current.LoyaltyPointsEarned,
            TableId = mother.TableId ?? current.TableId,
            TableNumber = mother.TableNumber ?? current.TableNumber,
            CustomerName = FirstNonEmpty(mother.CustomerName, current.CustomerName),
            CustomerPhone = FirstNonEmpty(mother.CustomerPhone, current.CustomerPhone),
            Lines = mother.Lines.Count > 0 ? mother.Lines : current.Lines
        };

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        // No auto loyalty earn on Send — OrderPlaceLoyaltyEarnRules.AutoEarnOnSendOrPrint is false.
        // Staff add points only via More → Add Loyalty Points.
        if (_currentOrder is null || _currentOrder.Lines.Count == 0)
        {
            await _ui.ShowAlertAsync("Send to Kitchen", "Add items before sending to kitchen.");
            return;
        }

        var kitchenOp = IsCustomerHubOrder(_currentOrder)
            ? ClientOperation.PrintCollectionOrder
            : ClientOperation.SubmitFinalOrder;
        var decision = _offlinePolicy.Evaluate(kitchenOp, await _offlinePolicy.IsMotherOnlineAsync());
        if (!decision.Allowed)
        {
            await _ui.ShowAlertAsync("Send to Kitchen blocked", decision.Message);
            return;
        }

        try
        {
            Session.ActionsBusy = true;
            Session.SendActionLabel = "SENDING…";
            Session.StatusMessage = "Sending to kitchen…";
            RaiseChanged();

            await FlushMotherPersistAsync();
            if (_currentOrder is null)
            {
                return;
            }

            var result = await _orderClient.SendToKitchenAsync(_currentOrder);
            lock (_orderGate)
            {
                _currentOrder = result.State;
            }

            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            if (result.ConflictDetected)
            {
                await _ui.ShowToastAsync("Order conflict", result.Message, StatusKind.Warning);
            }
            else
            {
                await _ui.ShowToastAsync("Sent to kitchen", result.Message, StatusKind.Success);
                await _ui.CloseOrderPageAsync();
            }
        }
        catch (Exception ex)
        {
            await _ui.ShowToastAsync("Send failed", ex.Message, StatusKind.Error);
        }
        finally
        {
            Session.ActionsBusy = false;
            Session.SendActionLabel = "SEND TO KITCHEN";
            if (string.Equals(Session.StatusMessage, "Sending to kitchen…", StringComparison.Ordinal))
            {
                Session.StatusMessage = null;
            }

            RaiseChanged();
        }
    }

    public async Task PrintAsync(CancellationToken cancellationToken = default)
    {
        // No auto loyalty earn on Print — same defer as Send (AutoEarnOnSendOrPrint = false).
        if (_currentOrder is null || _currentOrder.Lines.Count == 0)
        {
            await _ui.ShowAlertAsync("Print", "Add items before printing.");
            return;
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var decision = _offlinePolicy.Evaluate(
            IsCustomerHubOrder(_currentOrder) ? ClientOperation.PrintCollectionOrder : ClientOperation.SubmitFinalOrder,
            online);
        if (!decision.Allowed)
        {
            await _ui.ShowAlertAsync("Print blocked", decision.Message);
            return;
        }

        try
        {
            Session.ActionsBusy = true;
            Session.StatusMessage = "Printing…";
            RaiseChanged();
            await FlushMotherPersistAsync();
            if (_currentOrder is null)
            {
                return;
            }

            if (IsCollectionOrder() || IsDeliveryOrder())
            {
                // Takeaway PRINT = customer receipt + kitchen (Mother parity).
                var result = await _orderClient.SendToKitchenAndReceiptAsync(_currentOrder);
                lock (_orderGate)
                {
                    _currentOrder = result.State;
                }

                await _cache.SaveOrderStateAsync(_currentOrder);
                PublishSession();
                if (result.ConflictDetected)
                {
                    await _ui.ShowToastAsync("Order conflict", result.Message, StatusKind.Warning);
                }
                else
                {
                    await _ui.ShowToastAsync("Printed", result.Message, StatusKind.Success);
                    await _ui.CloseOrderPageAsync();
                }
            }
            else
            {
                var session = await _cache.GetCurrentLoginSessionAsync();
                var request = await _printClient.RequestPrintAsync("bill", _currentOrder.OrderId, session);
                await _cache.SavePrintRequestAsync(request);
                if (string.Equals(request.Status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    await _ui.ShowToastAsync("Print failed", request.Message, StatusKind.Error);
                }
                else
                {
                    await _ui.ShowToastAsync("Print bill", request.Message, StatusKind.Success);
                    await _ui.CloseOrderPageAsync();
                }
            }
        }
        catch (Exception ex)
        {
            await _ui.ShowToastAsync("Print failed", ex.Message, StatusKind.Error);
        }
        finally
        {
            Session.ActionsBusy = false;
            if (string.Equals(Session.StatusMessage, "Printing…", StringComparison.Ordinal))
            {
                Session.StatusMessage = null;
            }

            RaiseChanged();
        }
    }

    public async Task PayAsync(CancellationToken cancellationToken = default)
    {
        if (_currentOrder is null)
        {
            await _ui.ShowAlertAsync("Payment", "Payment requires a Mother-confirmed order.");
            return;
        }

        if (IsCustomerHubOrder(_currentOrder))
        {
            var online = await _offlinePolicy.IsMotherOnlineAsync();
            if (!online)
            {
                await _ui.ShowAlertAsync("Payment blocked", "Taking payment for this order requires Mother POS.");
                return;
            }
        }

        try
        {
            Session.ActionsBusy = true;
            Session.PaymentActionLabel = "OPENING…";
            RaiseChanged();
            await FlushMotherPersistAsync();
            if (_currentOrder is null)
            {
                return;
            }

            var billTotal = _currentOrder.Total;
            var isTakeaway = IsCollectionOrder() || IsDeliveryOrder();
            // Table can tip/split; Collection/Delivery stay full-bill only.
            var allowSplit = !isTakeaway;

            decimal tip = 0m;
            if (allowSplit && ShouldOfferTip())
            {
                var tipResult = await _ui.ShowPaymentTipAsync(billTotal);
                if (tipResult is null)
                {
                    return;
                }

                tip = Math.Max(0m, tipResult.Value);
            }

            var totalDue = billTotal + tip;
            var remainingBalance = totalDue;

            OrderWeb.SharedUI.Payments.PaymentSplitPlan plan;
            if (isTakeaway)
            {
                plan = OrderWeb.SharedUI.Payments.PaymentSplitPlan.Full(totalDue);
            }
            else
            {
                var setup = await _ui.ShowPaymentSetupPlanAsync(
                    totalDue,
                    remainingBalance,
                    BuildPayByItemsLines(_currentOrder),
                    _currentOrder.Subtotal,
                    _currentOrder.ServiceCharge,
                    GetDeliveryFee(_currentOrder),
                    _currentOrder.Discount);
                if (setup is null)
                {
                    return;
                }

                plan = setup;
            }

            var paymentAmount = plan.GetThisPaymentAmount(remainingBalance);
            if (paymentAmount <= 0m)
            {
                await _ui.ShowAlertAsync("Payment", "There is nothing left to pay on this order.");
                return;
            }

            var tipThisAttempt = AllocateTip(tip, paymentAmount, remainingBalance);
            var remainingAfter = Math.Max(0m, remainingBalance - paymentAmount);

            // Mother chrome: SELECT PAYMENT METHOD (not the full Client PaymentView page).
            var method = await _ui.ShowPaymentMethodAsync(
                paymentAmount,
                remainingAfter,
                plan.GetPaymentTitle());
            if (method == OrderWeb.SharedUI.Payments.PaymentMethodChoice.Cancelled)
            {
                return;
            }

            await TakeWizardPaymentAsync(
                method,
                paymentAmount,
                tipThisAttempt,
                tip,
                settlesOrder: remainingAfter <= 0.009m);
        }
        finally
        {
            Session.ActionsBusy = false;
            PublishSession();
        }
    }

    private async Task TakeWizardPaymentAsync(
        OrderWeb.SharedUI.Payments.PaymentMethodChoice method,
        decimal amount,
        decimal tipAmount,
        decimal tipTotal,
        bool settlesOrder)
    {
        if (_currentOrder is null)
        {
            return;
        }

        string? giftCardNumber = null;
        string? loyaltyLookup = null;
        int? loyaltyPoints = null;
        var payAmount = amount;
        string methodKey;

        switch (method)
        {
            case OrderWeb.SharedUI.Payments.PaymentMethodChoice.Cash:
            {
                methodKey = "cash";
                var cash = await _ui.ShowPaymentCashAsync(amount);
                if (!cash.Success)
                {
                    return;
                }

                payAmount = cash.AmountPaid > 0 ? cash.AmountPaid : amount;
                break;
            }
            case OrderWeb.SharedUI.Payments.PaymentMethodChoice.Card:
                // Mother parity: CARD records immediately (no terminal confirm dialog).
                methodKey = "card";
                break;
            case OrderWeb.SharedUI.Payments.PaymentMethodChoice.GiftCard:
            {
                methodKey = "gift_card";
                var gift = await _ui.ShowPaymentGiftCardAsync(amount);
                if (!gift.Success || string.IsNullOrWhiteSpace(gift.CardNumber) || gift.AmountApplied <= 0)
                {
                    await _ui.ShowAlertAsync("Gift card", gift.Message ?? "Gift card payment cancelled. No balance was changed.");
                    return;
                }

                giftCardNumber = gift.CardNumber;
                payAmount = gift.AmountApplied;
                break;
            }
            case OrderWeb.SharedUI.Payments.PaymentMethodChoice.Loyalty:
                // Kept for when OrderPlaceLoyaltyEarnRules.OrderPlacePaymentLoyaltyEnabled flips on.
                // Do not remove — PaymentPage redeem path is the current supported surface.
                methodKey = "loyalty";
                var loyalty = await _ui.PromptLoyaltyPaymentAsync(amount);
                if (loyalty is null || !loyalty.Success || string.IsNullOrWhiteSpace(loyalty.Lookup) || loyalty.Points <= 0)
                {
                    await _ui.ShowAlertAsync("Loyalty", loyalty?.Message ?? "Loyalty payment cancelled. No points were changed.");
                    return;
                }

                loyaltyLookup = loyalty.Lookup;
                loyaltyPoints = loyalty.Points;
                payAmount = loyalty.AmountApplied;
                break;
            default:
                return;
        }

        Session.PaymentActionLabel = "PAYING…";
        RaiseChanged();
        var paymentService = new ClientPaymentService(_cache);
        var result = await paymentService.TakePaymentAsync(
            _currentOrder.OrderId,
            methodKey,
            payAmount,
            expectedOrderRevision: _currentOrder.Version,
            giftCardNumber: giftCardNumber,
            giftCardIdempotencyKey: giftCardNumber == null
                ? null
                : $"gift-card:{_currentOrder.OrderId}:{giftCardNumber}:{payAmount:F2}",
            loyaltyLookup: loyaltyLookup,
            loyaltyPoints: loyaltyPoints,
            loyaltyIdempotencyKey: loyaltyLookup == null || loyaltyPoints is null
                ? null
                : $"loyalty:{_currentOrder.OrderId}:{loyaltyLookup}:{loyaltyPoints}",
            tipAmount: tipAmount,
            tipTotal: tipTotal);

        if (giftCardNumber != null)
        {
            ClientGiftCardDiagnostics.Record(
                "order-pay",
                result.Approved,
                result.Message,
                result.Approved ? null : (result.IsUnknown ? GiftCardErrorCodes.OfflineMother : GiftCardErrorCodes.Unknown));
        }

        if (loyaltyLookup != null)
        {
            var queued = !result.Approved &&
                         result.Message?.Contains("queued", StringComparison.OrdinalIgnoreCase) == true;
            ClientLoyaltyDiagnostics.Record(
                "order-pay",
                result.Approved,
                result.Message,
                result.Approved
                    ? null
                    : (result.IsUnknown
                        ? LoyaltyErrorCodes.OfflineMother
                        : queued
                            ? LoyaltyErrorCodes.Queued
                            : LoyaltyErrorCodes.Unknown),
                queued);
        }

        if (!result.Approved)
        {
            await _ui.ShowAlertAsync(
                result.IsUnknown ? "Payment status unknown" : "Payment not approved",
                result.Message);
            return;
        }

        var fullyPaid = settlesOrder && payAmount + 0.009m >= amount;
        var paidByLabel = methodKey switch
        {
            "card" => "Paid by card",
            "cash" => "Paid by cash",
            "gift_card" => "Paid by gift card",
            "loyalty" => "Paid by loyalty",
            _ => "Payment approved"
        };
        await _ui.ShowToastAsync(
            fullyPaid ? paidByLabel : "Payment approved",
            fullyPaid
                ? $"£{payAmount:F2} paid. Printing receipt…"
                : result.Message,
            StatusKind.Success);

        if (fullyPaid)
        {
            // Mother completes order + prints receipt; Client leaves Order Place.
            await _ui.CloseOrderPageAsync();
            return;
        }

        try
        {
            var refreshed = await _orderClient.OpenOrderForEditAsync(_currentOrder.OrderId);
            _currentOrder = PreserveCustomer(refreshed.State);
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
        }
        catch
        {
            // Payment already succeeded; refresh is best-effort.
        }
    }

    private bool ShouldOfferTip()
    {
        if (!IsTableOrder() || _currentOrder is null)
        {
            return false;
        }

        var status = (_currentOrder.ServiceChargeStatus ?? "not_configured").Trim().ToLowerInvariant();
        return status is "not_configured" or "" or "none"
            && _currentOrder.ServiceChargePercent <= 0m
            && _currentOrder.ServiceCharge <= 0m;
    }

    private static decimal AllocateTip(decimal tipRemaining, decimal paymentAmount, decimal remainingBalance)
    {
        if (tipRemaining <= 0m || paymentAmount <= 0m || remainingBalance <= 0m)
        {
            return 0m;
        }

        if (paymentAmount >= remainingBalance - 0.009m)
        {
            return tipRemaining;
        }

        return Math.Min(
            tipRemaining,
            decimal.Round(tipRemaining * paymentAmount / remainingBalance, 2, MidpointRounding.AwayFromZero));
    }

    private static decimal GetDeliveryFee(MotherOrderState order) =>
        order.Lines
            .Where(line => string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase)
                || line.Name.Contains("delivery fee", StringComparison.OrdinalIgnoreCase))
            .Sum(line => line.UnitPrice * line.Quantity);

    private static IReadOnlyList<OrderWeb.SharedUI.Payments.PaymentPayByItemsLine> BuildPayByItemsLines(MotherOrderState order)
    {
        return order.Lines
            .Where(line => line.Quantity > 0
                && !string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase)
                && !line.Name.Contains("delivery fee", StringComparison.OrdinalIgnoreCase))
            .Select(line =>
            {
                var detailParts = new List<string>();
                if (line.Modifiers is { Count: > 0 })
                {
                    detailParts.Add(string.Join(", ", line.Modifiers));
                }

                if (!string.IsNullOrWhiteSpace(line.Notes))
                {
                    detailParts.Add(line.Notes!);
                }

                // Mother uses line total ÷ qty (VAT-inclusive unit). Client UnitPrice is already that unit.
                var unit = Math.Round(Math.Max(0m, line.UnitPrice), 2, MidpointRounding.AwayFromZero);
                return new OrderWeb.SharedUI.Payments.PaymentPayByItemsLine
                {
                    ItemId = line.Id,
                    Name = line.Name,
                    Detail = detailParts.Count == 0 ? null : string.Join(" · ", detailParts),
                    Quantity = line.Quantity,
                    UnitAmount = unit
                };
            })
            .Where(line => line.UnitAmount > 0)
            .ToList();
    }

    /// <summary>
    /// Reloads Mother copy when idle. Returns true when a newer Mother version was applied.
    /// </summary>
    public async Task<bool> ReloadFromMotherIfIdleAsync()
    {
        if (_currentOrder is null || _persistQueued || Session.ActionsBusy)
        {
            return false;
        }

        if (!await _motherWriteGate.WaitAsync(0))
        {
            return false;
        }

        _motherWriteGate.Release();

        try
        {
            var previousVersion = _currentOrder.Version;
            var previousUpdated = _currentOrder.UpdatedUtc;
            var result = await _orderClient.RefreshLatestAsync(_currentOrder);
            var changedElsewhere =
                result.State.Version != previousVersion ||
                !string.Equals(result.State.UpdatedUtc, previousUpdated, StringComparison.Ordinal);
            lock (_orderGate)
            {
                var preservedConflict = IsTableHeaderLabel(_currentOrder?.ConflictMessage)
                    ? null
                    : _currentOrder?.ConflictMessage;
                _currentOrder = result.State with
                {
                    ConflictMessage = changedElsewhere ? null : preservedConflict
                };
            }

            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            return changedElsewhere;
        }
        catch
        {
            // Paid/voided elsewhere — Live Order will drop it.
            return false;
        }
    }

    public void MarkRemoteConflict()
    {
        if (_currentOrder is null)
        {
            return;
        }

        _currentOrder = _currentOrder with
        {
            ConflictMessage = "Changed on another terminal — finish or discard local edits, then reopen."
        };
        Session.StatusMessage = _currentOrder.ConflictMessage;
        PublishSession();
    }

    private async Task EnsureOrderContextAsync()
    {
        if (_currentOrder != null || _table is null)
        {
            return;
        }

        var session = await _cache.GetCurrentLoginSessionAsync();
        if (!string.IsNullOrWhiteSpace(_table.CurrentOrderId))
        {
            var online = await _offlinePolicy.IsMotherOnlineAsync();
            var decision = _offlinePolicy.Evaluate(ClientOperation.OpenCollectionOrder, online);
            if (!decision.Allowed)
            {
                Session.StatusMessage = decision.Message;
                return;
            }

            var opened = await _orderClient.OpenOrderForEditAsync(_table.CurrentOrderId);
            _currentOrder = opened.State;
            await _cache.SaveOrderStateAsync(_currentOrder);
            return;
        }

        var createDecision = _offlinePolicy.Evaluate(ClientOperation.SaveCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
        if (!createDecision.Allowed)
        {
            Session.StatusMessage = createDecision.Message;
            return;
        }

        var result = await _orderClient.OpenOrCreateTableOrderAsync(_table, _covers, session);
        _currentOrder = result.State;
        await _cache.SaveOrderStateAsync(_currentOrder);
    }

    private async Task LoadMenuAsync()
    {
        var status = await _cache.GetStatusAsync();
        var sections = await _cache.GetOperationalSectionSyncStatusAsync();
        var hasWarmCache = status.Categories > 0 && status.Products > 0;
        var menuFresh = hasWarmCache &&
                        sections.MenuOk == true &&
                        IsMenuSyncFresh(sections.MenuUtc);

        if (!menuFresh)
        {
            var refreshed = await _menuClient.RefreshCacheAsync();
            if (!refreshed && !hasWarmCache)
            {
                try
                {
                    await new MotherOperationalSyncClient(_cache).PullAllAsync();
                }
                catch
                {
                    // Use whatever SQLite still has.
                }
            }
        }
        else
        {
            // Paint from cache now; refresh quietly after Order Place is open.
            _ = BackgroundRefreshMenuAsync();
        }

        await ReloadMenuFromCacheAsync();

        if (_categories.Count == 0)
        {
            Session.StatusMessage = "No menu synced — use Update All, then reopen.";
        }
    }

    private async Task BackgroundRefreshMenuAsync()
    {
        try
        {
            var refreshed = await _menuClient.RefreshCacheAsync();
            if (!refreshed)
            {
                return;
            }

            var previousCategoryId = _selectedCategory?.Id;
            await ReloadMenuFromCacheAsync();
            if (previousCategoryId is int id)
            {
                var still = _categories.FirstOrDefault(c => c.Id == id);
                if (still != null)
                {
                    _selectedCategory = still;
                    await LoadProductsForSelectedCategoryAsync();
                }
            }

            PublishSession();
        }
        catch
        {
            // Keep warm cache.
        }
    }

    private async Task ReloadMenuFromCacheAsync()
    {
        _categories.Clear();
        _categories.AddRange(await _cache.GetMenuCategoriesAsync());
        _mealDeals.Clear();
        _mealDeals.AddRange(await _cache.GetActiveMealDealsAsync());
        _tastingMenus.Clear();
        _tastingMenus.AddRange(await _cache.GetActiveTastingMenusAsync());
        _categoryIdsWithProducts.Clear();
        foreach (var id in await _cache.GetCategoryIdsWithProductsAsync())
        {
            _categoryIdsWithProducts.Add(id);
        }

        foreach (var category in _categories)
        {
            if (IsMealDealsCategory(category) && _mealDeals.Count > 0)
            {
                _categoryIdsWithProducts.Add(category.Id);
            }

            if (IsTastingMenusCategory(category) && _tastingMenus.Count > 0)
            {
                _categoryIdsWithProducts.Add(category.Id);
            }
        }

        _selectedTopCategory = TopLevelCategories().FirstOrDefault() ?? _categories.FirstOrDefault();
        await SelectTopCategoryAsync(_selectedTopCategory, publish: false);
    }

    private static bool IsMenuSyncFresh(string? menuUtc)
    {
        if (string.IsNullOrWhiteSpace(menuUtc) ||
            !DateTimeOffset.TryParse(menuUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - when.ToUniversalTime() <= WarmMenuTtl;
    }

    private async Task SelectTopCategoryAsync(CachedMenuCategory? category, bool publish = true)
    {
        _selectedTopCategory = category;
        if (category is null)
        {
            _selectedCategory = null;
            _products.Clear();
            if (publish)
            {
                PublishSession();
            }

            return;
        }

        var children = ChildCategories(category.Id).ToList();
        if (children.Count > 0)
        {
            _selectedCategory = CategoryHasVisibleItems(category.Id) ? category : children[0];
        }
        else
        {
            _selectedCategory = category;
        }

        await LoadProductsForSelectedCategoryAsync();
        if (publish)
        {
            PublishSession();
        }
    }

    private async Task LoadProductsForSelectedCategoryAsync()
    {
        _products.Clear();
        _specialProducts.Clear();
        if (_selectedCategory is null)
        {
            return;
        }

        if (IsMealDealsCategory(_selectedCategory))
        {
            foreach (var deal in _mealDeals)
            {
                _specialProducts.Add(new OrderPlaceProductItem(
                    $"md:{deal.MotherId}",
                    deal.Name,
                    deal.Price,
                    Badge: "DEAL",
                    PriceText: $"£{deal.Price:F2}",
                    IsAvailable: true));
            }

            return;
        }

        if (IsTastingMenusCategory(_selectedCategory))
        {
            foreach (var menu in _tastingMenus)
            {
                var priceText = menu.Options.Count == 0
                    ? "No prices"
                    : string.Join(", ", menu.Options.OrderBy(o => o.SortOrder).Select(o => $"{o.Name} £{o.Price:F2}"));
                _specialProducts.Add(new OrderPlaceProductItem(
                    $"tm:{menu.MotherId}",
                    menu.Name,
                    0m,
                    Badge: "TASTING",
                    PriceText: priceText,
                    IsAvailable: true));
            }

            return;
        }

        _products.AddRange(await _cache.GetProductsByCategoryAsync(_selectedCategory.Id));
    }

    private void PublishSession()
    {
        var takeaway = IsCollectionOrder() || IsDeliveryOrder();
        Session.Kind = IsDeliveryOrder()
            ? OrderPlaceOrderKind.Delivery
            : IsCollectionOrder()
                ? OrderPlaceOrderKind.Collection
                : OrderPlaceOrderKind.Table;

        Session.HeaderTitle = BuildHeaderTitle();
        Session.HeaderDetail = BuildHeaderDetail();
        Session.HeaderTable = BuildHeaderTable();
        Session.SelectedCategoryId = _selectedTopCategory?.Id.ToString(CultureInfo.InvariantCulture);
        Session.SelectedSubcategoryId = _selectedCategory?.Id.ToString(CultureInfo.InvariantCulture);

        Session.Categories.Clear();
        foreach (var category in TopLevelCategories())
        {
            Session.Categories.Add(new OrderPlaceCategoryItem(
                category.Id.ToString(CultureInfo.InvariantCulture),
                category.Name,
                IsSpecial: IsMealDealsCategory(category) || IsTastingMenusCategory(category)));
        }

        Session.Subcategories.Clear();
        if (_selectedTopCategory != null &&
            !IsMealDealsCategory(_selectedTopCategory) &&
            !IsTastingMenusCategory(_selectedTopCategory))
        {
            var children = ChildCategories(_selectedTopCategory.Id).ToList();
            if (children.Count > 0)
            {
                if (CategoryHasVisibleItems(_selectedTopCategory.Id))
                {
                    Session.Subcategories.Add(new OrderPlaceCategoryItem(
                        _selectedTopCategory.Id.ToString(CultureInfo.InvariantCulture),
                        "Main",
                        null));
                }

                foreach (var child in children)
                {
                    Session.Subcategories.Add(new OrderPlaceCategoryItem(
                        child.Id.ToString(CultureInfo.InvariantCulture),
                        child.Name,
                        _selectedTopCategory.Id.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }

        Session.Products.Clear();
        if (_specialProducts.Count > 0)
        {
            foreach (var product in _specialProducts)
            {
                Session.Products.Add(product);
            }
        }
        else
        {
            foreach (var product in FilteredProducts())
            {
                Session.Products.Add(new OrderPlaceProductItem(
                    product.Id.ToString(CultureInfo.InvariantCulture),
                    product.Name,
                    product.Price,
                    IsAvailable: true));
            }
        }

        Session.Lines.Clear();
        decimal deliveryFee = 0m;
        if (_currentOrder != null)
        {
            foreach (var line in _currentOrder.Lines)
            {
                if (string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase) ||
                    line.Name.Contains("delivery fee", StringComparison.OrdinalIgnoreCase))
                {
                    deliveryFee += line.UnitPrice * line.Quantity;
                    continue;
                }

                if (line.ProductMotherId?.StartsWith("tasting-course:", StringComparison.OrdinalIgnoreCase) == true)
                {
                    continue;
                }

                var isMealDeal = !string.IsNullOrWhiteSpace(line.MealDealId);
                var choiceText = line.MealDealChoices is { Count: > 0 }
                    ? string.Join(Environment.NewLine, line.MealDealChoices.Select(c => $"• {c}"))
                    : null;
                var details = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        choiceText,
                        !isMealDeal && line.Modifiers.Count > 0 ? string.Join(", ", line.Modifiers) : null,
                        line.Notes
                    }.Where(text => !string.IsNullOrWhiteSpace(text)));

                Session.Lines.Add(new OrderPlaceBasketLine(
                    line.Id,
                    line.Name,
                    line.UnitPrice * line.Quantity,
                    line.Quantity,
                    details,
                    IsSent: line.IsSent,
                    ShowNoteAction: !isMealDeal,
                    TrailingActionText: !string.IsNullOrWhiteSpace(line.TastingMenuId) ? "Fire" : null));
            }

            Session.Subtotal = _currentOrder.Subtotal;
            Session.Total = _currentOrder.Total;
            Session.Discount = _currentOrder.Discount;
            Session.ServiceCharge = _currentOrder.ServiceCharge;
        }
        else
        {
            Session.Subtotal = 0m;
            Session.Total = 0m;
            Session.Discount = 0m;
            Session.ServiceCharge = 0m;
        }

        Session.DeliveryFee = deliveryFee;
        Session.ShowServiceCharge = !takeaway;
        Session.ShowDeliveryFee = IsDeliveryOrder();
        Session.PrintActionLabel = takeaway ? "PRINT RECEIPT" : "PRINT BILL";
        if (!Session.ActionsBusy)
        {
            Session.SendActionLabel = "SEND TO KITCHEN";
            Session.PaymentActionLabel = $"PAYMENT £{Session.Total:F2}";
        }
        else if (!string.Equals(Session.PaymentActionLabel, "OPENING…", StringComparison.Ordinal))
        {
            Session.PaymentActionLabel = $"PAYMENT £{Session.Total:F2}";
        }

        // Real conflicts only. Never promote a "Table N" label into StatusMessage —
        // that duplicates the orange HeaderTable line (Mother shows table once).
        if (!string.IsNullOrWhiteSpace(_currentOrder?.ConflictMessage) &&
            !_currentOrder.ConflictMessage.Contains('·') &&
            !IsTableHeaderLabel(_currentOrder.ConflictMessage))
        {
            Session.StatusMessage = _currentOrder.ConflictMessage;
        }
        else if (IsTableHeaderLabel(Session.StatusMessage))
        {
            Session.StatusMessage = null;
        }

        RaiseChanged();
    }

    private void QueueMotherPersist()
    {
        _persistQueued = true;
        _ = DrainMotherPersistAsync();
    }

    private async Task FlushMotherPersistAsync()
    {
        _persistQueued = true;
        await DrainMotherPersistAsync();
    }

    private async Task DrainMotherPersistAsync()
    {
        if (!await _motherWriteGate.WaitAsync(0))
        {
            _persistQueued = true;
            return;
        }

        try
        {
            while (_persistQueued)
            {
                _persistQueued = false;
                MotherOrderState? snapshot;
                lock (_orderGate)
                {
                    snapshot = _currentOrder;
                }

                if (snapshot is null)
                {
                    continue;
                }

                try
                {
                    var result = await _orderClient.ReplaceLinesAsync(snapshot, snapshot.Lines);
                    lock (_orderGate)
                    {
                        if (_currentOrder is null)
                        {
                            _currentOrder = PreserveCustomer(result.State);
                        }
                        else if (SameBasketSignature(_currentOrder.Lines, snapshot.Lines))
                        {
                            _currentOrder = PreserveCustomer(result.State);
                        }
                        else
                        {
                            // Newer local edits exist — keep local lines, adopt Mother version + financials.
                            _currentOrder = _currentOrder with
                            {
                                Version = result.State.Version,
                                OrderNumber = result.State.OrderNumber,
                                UpdatedUtc = result.State.UpdatedUtc,
                                Status = result.State.Status,
                                Notes = result.State.Notes ?? _currentOrder.Notes,
                                Discount = result.State.Discount,
                                ServiceCharge = result.State.ServiceCharge,
                                ServiceChargeStatus = result.State.ServiceChargeStatus,
                                ServiceChargePercent = result.State.ServiceChargePercent,
                                LoyaltyPointsEarned = result.State.LoyaltyPointsEarned > 0
                                    ? result.State.LoyaltyPointsEarned
                                    : _currentOrder.LoyaltyPointsEarned,
                                Subtotal = result.State.Subtotal,
                                Total = result.State.Total,
                                Tax = result.State.Tax
                            };
                            _persistQueued = true;
                        }
                    }

                    if (_currentOrder != null)
                    {
                        await _cache.SaveOrderStateAsync(_currentOrder);
                    }

                    if (result.ConflictDetected)
                    {
                        Session.StatusMessage = result.Message;
                        await _ui.ShowToastAsync("Order conflict", result.Message, StatusKind.Warning);
                    }

                    PublishSession();
                }
                catch (Exception ex)
                {
                    Session.StatusMessage = "Could not save to Mother — tap Send to retry.";
                    RaiseChanged();
                    await _ui.ShowToastAsync("Save failed", ex.Message, StatusKind.Error);
                }
            }
        }
        finally
        {
            _motherWriteGate.Release();
            if (_persistQueued)
            {
                _ = DrainMotherPersistAsync();
            }
        }
    }

    private static bool SameBasketSignature(
        IReadOnlyList<MotherOrderLine> left,
        IReadOnlyList<MotherOrderLine> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Id, right[i].Id, StringComparison.Ordinal) ||
                left[i].Quantity != right[i].Quantity ||
                left[i].UnitPrice != right[i].UnitPrice)
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerable<CachedProduct> FilteredProducts() => _products;

    private IEnumerable<CachedMenuCategory> TopLevelCategories()
    {
        var tops = _categories
            .Where(category => category.ParentId is null)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToList();
        if (tops.Count > 0)
        {
            return tops;
        }

        return _categories
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name);
    }

    private IEnumerable<CachedMenuCategory> ChildCategories(int parentId) =>
        _categories
            .Where(category => category.ParentId == parentId)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name);

    private bool CategoryHasVisibleItems(int categoryId) =>
        _categoryIdsWithProducts.Contains(categoryId);

    private bool IsCollectionOrder() =>
        CustomerOrderHubRules.IsCollectionOrderType(_currentOrder?.OrderType);

    private bool IsDeliveryOrder() =>
        CustomerOrderHubRules.IsDeliveryOrderType(_currentOrder?.OrderType);

    private bool IsTableOrder() =>
        !IsCollectionOrder() && !IsDeliveryOrder();

    private static bool IsCustomerHubOrder(MotherOrderState order) =>
        CustomerOrderHubRules.IsCustomerHubOrderType(order.OrderType);

    private string BuildHeaderTitle()
    {
        var number = string.IsNullOrWhiteSpace(_currentOrder?.OrderNumber)
            ? string.Empty
            : _currentOrder.OrderNumber.Trim();
        var orderPart = string.IsNullOrWhiteSpace(number) ? "Order #" : $"Order #{number}";
        if (IsDeliveryOrder())
        {
            return $"{orderPart} · DELIVERY";
        }

        if (IsCollectionOrder())
        {
            return $"{orderPart} · COLLECTION";
        }

        var orderNote = TruncateHeaderNote(_currentOrder?.Notes, 15);
        return string.IsNullOrEmpty(orderNote) ? orderPart : $"{orderPart} | Note: {orderNote}";
    }

    private string BuildHeaderDetail()
    {
        if (IsCollectionOrder() || IsDeliveryOrder())
        {
            var name = FirstNonEmpty(_customerName, _currentOrder?.CustomerName);
            var phone = FirstNonEmpty(_customerPhone, _currentOrder?.CustomerPhone);
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(phone))
            {
                return $"{name} · {phone}";
            }

            return name ?? phone ?? (IsDeliveryOrder() ? "Delivery order" : "Collection order");
        }

        return $"{Math.Max(_currentOrder?.Guests ?? _covers, 1)} guests";
    }

    private string? BuildHeaderTable()
    {
        if (!IsTableOrder())
        {
            return null;
        }

        var table = FirstNonEmpty(_currentOrder?.TableNumber, _table?.TableNumber);
        if (string.IsNullOrWhiteSpace(table))
        {
            return null;
        }

        // Guard if TableNumber already includes the "Table " prefix.
        return table.StartsWith("Table ", StringComparison.OrdinalIgnoreCase)
            ? table
            : $"Table {table}";
    }

    /// <summary>Legacy drafts stuffed "Table N" into ConflictMessage; that is not a status line.</summary>
    private static bool IsTableHeaderLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("Table ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // "Table 11" / "Table 11A" only — not longer operational messages.
        return trimmed.Length <= 24 && !trimmed.Contains('·');
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? TruncateHeaderNote(string? note, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "..";
    }

    private static bool IsMealDealsCategory(CachedMenuCategory? category) =>
        string.Equals(category?.MotherId, MealDealsMotherId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(category?.Name, "Meal Deals", StringComparison.OrdinalIgnoreCase);

    private static bool IsTastingMenusCategory(CachedMenuCategory? category) =>
        string.Equals(category?.MotherId, TastingMenusMotherId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(category?.Name, "Tasting Menus", StringComparison.OrdinalIgnoreCase);

    private MotherOrderState PreserveCustomer(MotherOrderState state) =>
        state with
        {
            CustomerName = FirstNonEmpty(state.CustomerName, _customerName, _currentOrder?.CustomerName),
            CustomerPhone = FirstNonEmpty(state.CustomerPhone, _customerPhone, _currentOrder?.CustomerPhone),
            Notes = state.Notes ?? _currentOrder?.Notes,
            Discount = state.Discount,
            ServiceCharge = state.ServiceCharge,
            ServiceChargeStatus = state.ServiceChargeStatus ?? _currentOrder?.ServiceChargeStatus,
            ServiceChargePercent = state.ServiceChargePercent
        };

    private async Task AddMealDealProductAsync(string dealMotherId)
    {
        var deal = _mealDeals.FirstOrDefault(d =>
            string.Equals(d.MotherId, dealMotherId, StringComparison.OrdinalIgnoreCase));
        if (deal is null)
        {
            return;
        }

        if (_currentOrder is null)
        {
            await _ui.ShowAlertAsync("Order", "This order has not been created on Mother POS yet.");
            return;
        }

        if (deal.Choices.Count == 0)
        {
            await _ui.ShowAlertAsync("Meal Deal", "This deal has no choices configured.");
            return;
        }

        var selections = await _ui.PickMealDealChoicesAsync(deal.Name, deal.PickCount, deal.Choices);
        if (selections is null || selections.Count == 0)
        {
            return;
        }

        try
        {
            Session.IsBusy = true;
            RaiseChanged();
            var result = await _orderClient.AddMealDealAsync(_currentOrder, deal, selections);
            _currentOrder = PreserveCustomer(result.State);
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Meal Deal", ex.Message);
        }
        finally
        {
            Session.IsBusy = false;
            RaiseChanged();
        }
    }

    private async Task AddTastingMenuProductAsync(string menuMotherId)
    {
        var menu = _tastingMenus.FirstOrDefault(m =>
            string.Equals(m.MotherId, menuMotherId, StringComparison.OrdinalIgnoreCase));
        if (menu is null)
        {
            return;
        }

        if (_currentOrder is null)
        {
            await _ui.ShowAlertAsync("Order", "This order has not been created on Mother POS yet.");
            return;
        }

        if (menu.Options.Count == 0 || menu.Courses.Count == 0)
        {
            await _ui.ShowAlertAsync("Tasting Menu", "This tasting menu has no price options or courses configured.");
            return;
        }

        var optionChoices = menu.Options
            .OrderBy(o => o.SortOrder)
            .Select(o => new OrderPlaceVariantChoice(o.MotherId, o.Name, o.IncludesWine ? "Includes wine" : null, o.Price))
            .ToList();
        var picked = await _ui.PickVariantAsync(menu.Name, optionChoices);
        if (picked is null)
        {
            return;
        }

        var option = menu.Options.FirstOrDefault(o =>
            string.Equals(o.MotherId, picked.Id, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            return;
        }

        try
        {
            Session.IsBusy = true;
            RaiseChanged();
            var result = await _orderClient.AddTastingMenuAsync(_currentOrder, menu, option);
            _currentOrder = PreserveCustomer(result.State);
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Tasting Menu", ex.Message);
        }
        finally
        {
            Session.IsBusy = false;
            RaiseChanged();
        }
    }
}

/// <summary>UI bridge so the host stays free of Page inheritance.</summary>
public interface IClientOrderPlaceUi
{
    Task ShowAlertAsync(string title, string message);
    Task ShowToastAsync(string title, string message, StatusKind kind = StatusKind.Info);
    Task SetLoadingAsync(bool isLoading, string? message = null);
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
    Task<string?> PromptAsync(string title, string message, string accept, string cancel, string placeholder, string? initialText = null);
    Task<string?> PickActionAsync(string title, params string[] options);
    Task<OrderPlaceVariantChoice?> PickVariantAsync(string itemName, IReadOnlyList<OrderPlaceVariantChoice> variants);
    Task<OrderPlaceQuickNoteResult> PickQuickNoteAsync(string itemName, IReadOnlyList<string> notes);
    Task<IReadOnlyList<OrderPlaceAddonChoice>?> PickAddonsAsync(string itemName, IReadOnlyList<OrderPlaceAddonChoice> addons);
    Task<IReadOnlyList<string>?> PickMealDealChoicesAsync(string dealName, int pickCount, IReadOnlyList<string> choices);
    Task NavigateToPaymentAsync(
        decimal total,
        string orderId,
        int version,
        bool allowSplit = true,
        decimal tipAmount = 0m,
        decimal tipTotal = 0m);
    Task CloseOrderPageAsync();

    /// <summary>SharedUI tip dialog (Table). Null = cancelled.</summary>
    Task<decimal?> ShowPaymentTipAsync(decimal orderTotal);

    /// <summary>SharedUI Payment Setup (Table). Null = cancelled/back.</summary>
    Task<OrderWeb.SharedUI.Payments.PaymentSplitPlan?> ShowPaymentSetupPlanAsync(
        decimal totalDue,
        decimal remainingBalance,
        IReadOnlyList<OrderWeb.SharedUI.Payments.PaymentPayByItemsLine> payByItemsLines,
        decimal orderSubtotal,
        decimal serviceCharge,
        decimal deliveryFee,
        decimal discount);

    /// <summary>Mother SELECT PAYMENT METHOD (Cash / Card / Gift).</summary>
    Task<OrderWeb.SharedUI.Payments.PaymentMethodChoice> ShowPaymentMethodAsync(
        decimal amountDue,
        decimal remainingAfterThisPayment = 0,
        string? title = null);

    /// <summary>SharedUI CASH PAYMENT (Exact / change / confirm).</summary>
    Task<OrderWeb.SharedUI.Payments.PaymentCashResult> ShowPaymentCashAsync(decimal amountDue);

    /// <summary>SharedUI GIFT CARD PAYMENT (lookup via Mother; APPLY returns card+amount).</summary>
    Task<OrderWeb.SharedUI.Payments.PaymentGiftCardResult> ShowPaymentGiftCardAsync(decimal amountDue);

    Task<OrderWeb.Client.Dialogs.LoyaltyOrderPaymentResult?> PromptLoyaltyPaymentAsync(decimal amountDue);

    /// <summary>Mother MoreOptionsDialog parity: blue "i" circle, 3-column tile grid, Cancel.</summary>
    Task<string?> ShowMoreOptionsAsync(IReadOnlyList<OrderPlaceMoreOption> options);

    /// <summary>Mother DiscountDialog parity: Fixed/Percent, amount, reason chips, Remove/Apply.</summary>
    Task<OrderPlaceDiscountResult?> ShowDiscountAsync(decimal subtotal);

    /// <summary>Mother TableTransferDialog parity: current table + grid of available empty tables.</summary>
    Task<OrderPlaceTableOption?> ShowTableTransferAsync(string currentTableLabel, IReadOnlyList<OrderPlaceTableOption> availableTables);

    /// <summary>Mother FireCourseDialog parity: colored course tiles + Fire All (includes Drinks).</summary>
    Task<string?> ShowFireCourseAsync(bool includeDrinks = true);

    /// <summary>Order Place earn: SharedUI Add Loyalty Points (lookup → confirm → Mother add).</summary>
    Task<OrderPlaceLoyaltyAddOutcome?> ShowLoyaltyAddAsync(
        decimal billTotal,
        string orderId,
        Func<string, int, string>? resolveStickyIdempotencyKey = null);
}
