using System.Globalization;
using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Payments;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Access;
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
    private readonly HashSet<int> _categoryIdsWithProducts = new();
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
        await EnsureOrderContextAsync();
        await LoadMenuAsync();
        PublishSession();
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

        var note = await _ui.PromptAsync("Notes", $"Note for {motherLine.Name}", "Save", "Cancel", motherLine.Notes ?? "Kitchen note");
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
        var lines = _currentOrder?.Lines;
        if (lines is null || lines.Count == 0)
        {
            await _ui.ShowAlertAsync("Notes", "Add an item before adding notes.");
            return;
        }

        await EditLineNoteAsync(lines[0].Id, cancellationToken);
    }

    public async Task VoidAsync(CancellationToken cancellationToken = default)
    {
        if (_currentOrder is null || _currentOrder.Lines.Count == 0)
        {
            await _ui.ShowAlertAsync("Void", "There are no items to void.");
            return;
        }

        if (!await _ui.ConfirmAsync("Void Order", "Void this order on Mother POS?", "Void", "Cancel"))
        {
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
            var result = await _orderClient.VoidCollectionOrderAsync(_currentOrder);
            _currentOrder = result.State;
            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
            await _ui.ShowAlertAsync("Mother POS", result.Message);
            await _ui.CloseOrderPageAsync();
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Void failed", ex.Message);
        }
    }

    public async Task MoreAsync(CancellationToken cancellationToken = default)
    {
        var takeaway = IsCollectionOrder() || IsDeliveryOrder();
        var options = new List<string> { "Discount", "Loyalty Points", "Cash Drawer" };
        if (takeaway)
        {
            options.Insert(0, "Previous Orders");
        }

        var selected = await _ui.PickActionAsync("More", options.ToArray());
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        await _ui.ShowAlertAsync(
            selected,
            takeaway && selected == "Previous Orders"
                ? "Open Live Order / Order History for this customer phone on Mother when available."
                : $"{selected} is handled on Mother POS for this Client release.");
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
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
            await _ui.ShowAlertAsync(result.ConflictDetected ? "Order conflict" : "Mother POS", result.Message);
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Send to Kitchen failed", ex.Message);
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
                await _ui.ShowAlertAsync(result.ConflictDetected ? "Order conflict" : "Print", result.Message);
            }
            else
            {
                var session = await _cache.GetCurrentLoginSessionAsync();
                var request = await _printClient.RequestPrintAsync("bill", _currentOrder.OrderId, session);
                await _cache.SavePrintRequestAsync(request);
                await _ui.ShowAlertAsync("Print", request.Message);
            }
        }
        catch (Exception ex)
        {
            await _ui.ShowAlertAsync("Print failed", ex.Message);
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

            await _ui.NavigateToPaymentAsync(_currentOrder.Total, _currentOrder.OrderId, _currentOrder.Version);
        }
        finally
        {
            Session.ActionsBusy = false;
            PublishSession();
        }
    }

    public async Task ReloadFromMotherIfIdleAsync()
    {
        if (_currentOrder is null || _persistQueued || Session.ActionsBusy)
        {
            return;
        }

        if (!await _motherWriteGate.WaitAsync(0))
        {
            return;
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
                _currentOrder = result.State with
                {
                    ConflictMessage = changedElsewhere ? null : _currentOrder?.ConflictMessage
                };
            }

            await _cache.SaveOrderStateAsync(_currentOrder);
            PublishSession();
        }
        catch
        {
            // Paid/voided elsewhere — Live Order will drop it.
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
        _categoryIdsWithProducts.Clear();
        foreach (var id in await _cache.GetCategoryIdsWithProductsAsync())
        {
            _categoryIdsWithProducts.Add(id);
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
        if (_selectedCategory is null)
        {
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
        Session.SelectedCategoryId = _selectedTopCategory?.Id.ToString(CultureInfo.InvariantCulture);
        Session.SelectedSubcategoryId = _selectedCategory?.Id.ToString(CultureInfo.InvariantCulture);

        Session.Categories.Clear();
        foreach (var category in TopLevelCategories())
        {
            Session.Categories.Add(new OrderPlaceCategoryItem(
                category.Id.ToString(CultureInfo.InvariantCulture),
                category.Name));
        }

        Session.Subcategories.Clear();
        if (_selectedTopCategory != null)
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
        foreach (var product in FilteredProducts())
        {
            Session.Products.Add(new OrderPlaceProductItem(
                product.Id.ToString(CultureInfo.InvariantCulture),
                product.Name,
                product.Price,
                IsAvailable: true));
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

                var details = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        line.Modifiers.Count > 0 ? string.Join(", ", line.Modifiers) : null,
                        line.Notes
                    }.Where(text => !string.IsNullOrWhiteSpace(text)));

                Session.Lines.Add(new OrderPlaceBasketLine(
                    line.Id,
                    line.Name,
                    line.UnitPrice * line.Quantity,
                    line.Quantity,
                    details,
                    IsSent: string.Equals(_currentOrder.Status, "sent_to_kitchen", StringComparison.OrdinalIgnoreCase),
                    ShowNoteAction: true));
            }

            Session.Subtotal = _currentOrder.Subtotal;
            Session.Total = _currentOrder.Total;
        }
        else
        {
            Session.Subtotal = 0m;
            Session.Total = 0m;
        }

        Session.Discount = 0m;
        Session.ServiceCharge = 0m;
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

        if (!string.IsNullOrWhiteSpace(_currentOrder?.ConflictMessage) &&
            !_currentOrder.ConflictMessage.Contains('·'))
        {
            Session.StatusMessage = _currentOrder.ConflictMessage;
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
                            _currentOrder = result.State;
                        }
                        else if (SameBasketSignature(_currentOrder.Lines, snapshot.Lines))
                        {
                            _currentOrder = result.State;
                        }
                        else
                        {
                            // Newer local edits exist — keep local lines, adopt Mother version for next write.
                            _currentOrder = _currentOrder with
                            {
                                Version = result.State.Version,
                                OrderNumber = result.State.OrderNumber,
                                UpdatedUtc = result.State.UpdatedUtc,
                                Status = result.State.Status
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
                    }

                    PublishSession();
                }
                catch (Exception ex)
                {
                    Session.StatusMessage = "Could not save to Mother — tap Send to retry.";
                    RaiseChanged();
                    await _ui.ShowAlertAsync("Mother POS", ex.Message);
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

        var table = FirstNonEmpty(_currentOrder?.TableNumber, _table?.TableNumber) ?? "?";
        return $"{orderPart} · TABLE {table}";
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

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

/// <summary>UI bridge so the host stays free of Page inheritance.</summary>
public interface IClientOrderPlaceUi
{
    Task ShowAlertAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
    Task<string?> PromptAsync(string title, string message, string accept, string cancel, string placeholder);
    Task<string?> PickActionAsync(string title, params string[] options);
    Task<OrderPlaceVariantChoice?> PickVariantAsync(string itemName, IReadOnlyList<OrderPlaceVariantChoice> variants);
    Task<OrderPlaceQuickNoteResult> PickQuickNoteAsync(string itemName, IReadOnlyList<string> notes);
    Task<IReadOnlyList<OrderPlaceAddonChoice>?> PickAddonsAsync(string itemName, IReadOnlyList<OrderPlaceAddonChoice> addons);
    Task NavigateToPaymentAsync(decimal total, string orderId, int version);
    Task CloseOrderPageAsync();
}
