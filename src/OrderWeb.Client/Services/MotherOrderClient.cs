using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Access;

namespace OrderWeb.Client.Services;

public sealed class MotherOrderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherOrderClient()
        : this(new ClientCacheService())
    {
    }

    public MotherOrderClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public async Task<MotherCommandResult> OpenOrCreateTableOrderAsync(CachedTable table, int covers, LoginSession? session)
    {
        // Occupied table: load Mother copy — never upsert empty lines (would wipe the basket).
        if (!string.IsNullOrWhiteSpace(table.CurrentOrderId))
        {
            return await OpenOrderForEditAsync(table.CurrentOrderId);
        }

        // Free table: open order-create with empty basket (same UX as Collection/Delivery).
        // Mother occupies the table session; ledger row is created on the first item save.
        var now = DateTimeOffset.UtcNow.ToString("O");
        var orderId = Guid.NewGuid().ToString("N");
        var guests = Math.Max(covers, table.Covers > 0 ? table.Covers : 1);
        // ConflictMessage must stay null/real conflicts only — stuffing "Table N" here made
        // Order Place show the table twice (HeaderTable + StatusMessage).
        var state = new MotherOrderState(
            orderId,
            string.Empty,
            "Table",
            table.Id,
            table.TableNumber,
            guests,
            "open",
            Array.Empty<MotherOrderLine>(),
            0m,
            0m,
            0m,
            0,
            now,
            session?.UserName ?? "Client User",
            ConflictMessage: null);

        try
        {
            return await UpsertOrderAsync(state);
        }
        catch
        {
            // Still open the Client order-create section with a local draft.
            await _cache.SaveOrderStateAsync(state);
            return new MotherCommandResult(state, false, "Table ready — add items to create the order.");
        }
    }

    public Task<MotherCommandResult> CreateCustomerOrderAsync(CustomerOrderDraft draft, LoginSession? session, string? orderId = null) =>
        UpsertOrderAsync(BuildDraftState(draft, session, orderId), draft.DeliveryFee, draft.Notes, draft.PickupTime ?? draft.ScheduledTime, draft.Customer);

    public Task<MotherCommandResult> ReplaceLinesAsync(MotherOrderState state, IReadOnlyList<MotherOrderLine> lines) =>
        UpsertOrderAsync(state with { Lines = lines.ToList() });

    /// <summary>
    /// Void/cancel a Collection, Delivery, or Table order on Mother (reason + optional manager PIN).
    /// Pass tableId to release an uncommitted table session when no ledger row exists yet.
    /// </summary>
    public async Task<MotherCommandResult> VoidCollectionOrderAsync(
        MotherOrderState state,
        string? reason = null,
        string? approvingPin = null,
        int? tableId = null)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            throw new InvalidOperationException("This Client is not connected to Mother POS.");
        }

        if (string.IsNullOrWhiteSpace(state.OrderId) && tableId is null or <= 0)
        {
            throw new InvalidOperationException("A Mother order id is required to void.");
        }

        using var client = CreateClient(auth);
        using var response = await client.PostAsJsonAsync(
            $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders/void",
            new
            {
                orderId = state.OrderId,
                reason,
                approvingPin,
                tableId = tableId ?? state.TableId
            },
            JsonOptions);
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<OrderEnvelope>(json, JsonOptions);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success || envelope.Order is null)
        {
            throw new InvalidOperationException(envelope?.Message ?? $"Mother POS could not void this order ({(int)response.StatusCode}).");
        }

        return new MotherCommandResult(ToState(envelope.Order), false, envelope.Message ?? "Order voided on Mother POS.");
    }

    public Task<MotherCommandResult> ApplyDiscountAsync(
        MotherOrderState state,
        decimal amount,
        decimal percent,
        string? reason,
        string discountType,
        string? approvingPin) =>
        PostOrderActionAsync(
            "/api/client/orders/discount",
            new
            {
                orderId = state.OrderId,
                amount,
                percent,
                reason,
                discountType,
                approvingPin
            });

    public Task<MotherCommandResult> SetServiceChargeAsync(
        MotherOrderState state,
        string action,
        string? reason,
        string? approvingPin) =>
        PostOrderActionAsync(
            "/api/client/orders/service-charge",
            new { orderId = state.OrderId, action, reason, approvingPin });

    public Task<MotherCommandResult> TransferTableAsync(MotherOrderState state, int targetTableId) =>
        PostOrderActionAsync(
            "/api/client/orders/transfer-table",
            new { orderId = state.OrderId, targetTableId });

    public Task<MotherCommandResult> MergeTablesAsync(MotherOrderState state, string childTableNumber) =>
        PostOrderActionAsync(
            "/api/client/orders/merge-tables",
            new { orderId = state.OrderId, childTableNumber });

    public Task<MotherCommandResult> FireCourseAsync(MotherOrderState state, string course) =>
        PostOrderActionAsync(
            "/api/client/orders/fire-course",
            new { orderId = state.OrderId, course });

    public Task<MotherCommandResult> ApplyLoyaltyRedeemAsync(
        MotherOrderState state,
        string lookup,
        int points,
        string? idempotencyKey) =>
        PostOrderActionAsync(
            "/api/client/orders/loyalty-redeem",
            new { orderId = state.OrderId, lookup, points, idempotencyKey });

    public async Task<(bool Success, string Message, IReadOnlyList<MotherPreviousOrder> Orders)> GetPreviousOrdersAsync(string customerPhone)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return (false, "This Client is not connected to Mother POS.", Array.Empty<MotherPreviousOrder>());
        }

        using var client = CreateClient(auth);
        var url =
            $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders/previous?phone={Uri.EscapeDataString(customerPhone.Trim())}";
        using var response = await client.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<PreviousOrdersEnvelope>(json, JsonOptions);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            return (false, envelope?.Message ?? "Previous orders unavailable.", Array.Empty<MotherPreviousOrder>());
        }

        var orders = (envelope.Orders ?? Array.Empty<PreviousOrderDto>())
            .Select(order => new MotherPreviousOrder(
                order.OrderDatabaseId,
                order.OrderNumber,
                order.CreatedAtUtc,
                order.OrderType,
                order.TotalAmount,
                order.Status,
                order.ItemsText,
                order.OrderNotes,
                order.IsMostRecent))
            .ToList();
        return (true, string.Empty, orders);
    }

    public async Task<(bool Success, string Message)> OpenOrderPlaceCashDrawerAsync(
        MotherOrderState? state,
        string? reason,
        decimal? amount = null,
        string? details = null)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return (false, "This Client is not connected to Mother POS.");
        }

        using var client = CreateClient(auth);
        using var response = await client.PostAsJsonAsync(
            $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders/cash-drawer/open",
            new
            {
                reason,
                orderId = state?.OrderId,
                orderNumber = state?.OrderNumber,
                amount,
                details
            },
            JsonOptions);
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<SimpleActionEnvelope>(json, JsonOptions);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            return (false, envelope?.Message ?? "Mother POS could not open the cash drawer.");
        }

        return (true, envelope.Message ?? "Cash drawer opened.");
    }

    private async Task<MotherCommandResult> PostOrderActionAsync(string path, object body)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            throw new InvalidOperationException("This Client is not connected to Mother POS.");
        }

        using var client = CreateClient(auth);
        using var response = await client.PostAsJsonAsync(
            $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}{path}",
            body,
            JsonOptions);
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<OrderEnvelope>(json, JsonOptions);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success || envelope.Order is null)
        {
            throw new InvalidOperationException(envelope?.Message ?? $"Mother POS action failed ({(int)response.StatusCode}).");
        }

        return new MotherCommandResult(ToState(envelope.Order), false, envelope.Message ?? "Updated on Mother POS.");
    }

    public Task<MotherCommandResult> AddItemAsync(MotherOrderState state, CachedProduct product, IReadOnlyList<string> modifiers) =>
        AddItemAsync(state, product, modifiers, notes: null, variant: null);

    /// <summary>Local basket mutation only — no Mother HTTP (optimistic UI).</summary>
    public MotherOrderState BuildStateWithAddItem(
        MotherOrderState state,
        CachedProduct product,
        IReadOnlyList<string> modifiers,
        string? notes = null,
        CachedProductVariant? variant = null)
    {
        var takeaway = CustomerOrderHubRules.IsCollectionOrderType(state.OrderType)
            || CustomerOrderHubRules.IsDeliveryOrderType(state.OrderType);
        var basePrice = variant?.PriceFor(takeaway) ?? product.Price;
        var unitPrice = basePrice + ModifierTotal(product, modifiers);
        var displayName = variant == null || string.IsNullOrWhiteSpace(variant.Name)
            ? product.Name
            : $"{product.Name} ({variant.Name})";
        var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        var modifierList = modifiers?.ToList() ?? new List<string>();

        var lines = state.Lines.ToList();
        var existing = lines.FirstOrDefault(line =>
            LinesMatchForMerge(line, product, variant, normalizedNotes, modifierList));
        if (existing != null)
        {
            var index = lines.IndexOf(existing);
            lines[index] = existing with { Quantity = existing.Quantity + 1 };
        }
        else
        {
            lines.Add(new MotherOrderLine(
                Guid.NewGuid().ToString("N"),
                product.Id,
                displayName,
                1,
                unitPrice,
                normalizedNotes,
                modifierList,
                product.MotherId,
                variant?.MotherId,
                variant?.Name,
                variant?.PriceFor(takeaway)));
        }

        return RecalcOrderMoney(state with { Lines = lines });
    }

    public MotherOrderState BuildStateWithQuantity(MotherOrderState state, MotherOrderLine line, int quantity)
    {
        IReadOnlyList<MotherOrderLine> lines;
        if (quantity <= 0)
        {
            lines = state.Lines.Where(existing => existing.Id != line.Id).ToList();
        }
        else
        {
            lines = state.Lines
                .Select(existing => existing.Id == line.Id ? existing with { Quantity = Math.Max(quantity, 1) } : existing)
                .ToList();
        }

        return RecalcOrderMoney(state with { Lines = lines });
    }

    public static MotherOrderState RecalcOrderMoney(MotherOrderState state)
    {
        var productTotal = state.Lines
            .Where(line => !string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase)
                && !line.Name.Contains("delivery fee", StringComparison.OrdinalIgnoreCase))
            .Sum(line => line.UnitPrice * line.Quantity);
        var delivery = state.Lines
            .Where(line => string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase)
                || line.Name.Contains("delivery fee", StringComparison.OrdinalIgnoreCase))
            .Sum(line => line.UnitPrice * line.Quantity);
        var discount = Math.Max(0m, state.Discount);
        var serviceCharge = Math.Max(0m, state.ServiceCharge);
        var subtotal = productTotal;
        var total = Math.Max(0m, productTotal - discount) + serviceCharge + delivery + state.Tax;
        return state with { Subtotal = subtotal, Total = total };
    }

    public Task<MotherCommandResult> AddItemAsync(
        MotherOrderState state,
        CachedProduct product,
        IReadOnlyList<string> modifiers,
        string? notes,
        CachedProductVariant? variant)
    {
        return UpsertOrderAsync(BuildStateWithAddItem(state, product, modifiers, notes, variant));
    }

    private static bool LinesMatchForMerge(
        MotherOrderLine line,
        CachedProduct product,
        CachedProductVariant? variant,
        string? notes,
        IReadOnlyList<string> modifiers)
    {
        if (string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sameProduct =
            (!string.IsNullOrWhiteSpace(product.MotherId) &&
             string.Equals(line.ProductMotherId, product.MotherId, StringComparison.OrdinalIgnoreCase))
            || (line.ProductId == product.Id);

        if (!sameProduct)
        {
            // Fallback: base name without variant suffix
            var baseName = product.Name;
            if (!string.Equals(line.Name, baseName, StringComparison.OrdinalIgnoreCase) &&
                !line.Name.StartsWith(baseName + " (", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var lineVariant = line.VariantId ?? string.Empty;
        var pickVariant = variant?.MotherId ?? string.Empty;
        if (!string.Equals(lineVariant, pickVariant, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(line.Notes?.Trim() ?? string.Empty, notes ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        return line.Modifiers.SequenceEqual(modifiers, StringComparer.OrdinalIgnoreCase);
    }

    public Task<MotherCommandResult> UpdateQuantityAsync(MotherOrderState state, MotherOrderLine line, int quantity) =>
        UpsertOrderAsync(BuildStateWithQuantity(state, line, Math.Max(quantity, 1)));

    public Task<MotherCommandResult> RemoveItemAsync(MotherOrderState state, MotherOrderLine line) =>
        UpsertOrderAsync(BuildStateWithQuantity(state, line, 0));

    public Task<MotherCommandResult> AddNoteAsync(MotherOrderState state, MotherOrderLine line, string note)
    {
        var lines = state.Lines
            .Select(existing => existing.Id == line.Id ? existing with { Notes = note } : existing)
            .ToList();
        return UpsertOrderAsync(state with { Lines = lines });
    }

    public async Task<MotherCommandResult> SendToKitchenAsync(MotherOrderState state)
    {
        var saved = await UpsertOrderCoreAsync(
            state,
            null,
            null,
            null,
            null,
            printKitchen: true,
            printReceipt: false,
            printDocuments: ["kitchen_ticket"]);
        return new MotherCommandResult(
            saved.State with { Status = "sent_to_kitchen" },
            saved.ConflictDetected,
            saved.Message);
    }

    public async Task<MotherCommandResult> SendToKitchenAndReceiptAsync(MotherOrderState state)
    {
        var saved = await UpsertOrderCoreAsync(
            state,
            null,
            null,
            null,
            null,
            printKitchen: true,
            printReceipt: true,
            printDocuments: ["kitchen_ticket", "customer_receipt"]);
        return new MotherCommandResult(
            saved.State with { Status = "sent_to_kitchen" },
            saved.ConflictDetected,
            saved.Message);
    }

    public async Task<MotherCommandResult> RefreshLatestAsync(MotherOrderState state)
    {
        var latest = await GetOrderByIdAsync(state.OrderId);
        if (latest == null)
        {
            throw new InvalidOperationException("Mother POS could not reload this order.");
        }

        return new MotherCommandResult(latest, false, "Order refreshed from Mother POS.");
    }

    /// <summary>
    /// Phase 2: load one Collection (or other) order fresh from Mother for edit on any Client terminal.
    /// </summary>
    public async Task<MotherCommandResult> OpenOrderForEditAsync(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new InvalidOperationException("A Mother order id is required.");
        }

        var latest = await GetOrderByIdAsync(orderId.Trim());
        if (latest == null)
        {
            throw new InvalidOperationException("Mother POS could not load this order. Check that Mother is online and the order is still open.");
        }

        await _cache.SaveOrderStateAsync(latest);
        return new MotherCommandResult(latest, false, "Order opened from Mother POS.");
    }

    public async Task<MotherOrderState?> GetOrderByIdAsync(string orderId)
    {
        var auth = await GetAuthAsync();
        if (auth is null || string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders/{Uri.EscapeDataString(orderId.Trim())}");
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var envelope = JsonSerializer.Deserialize<OrderEnvelope>(json, JsonOptions);
            if (envelope is null || !envelope.Success || envelope.Order is null)
            {
                return null;
            }

            return ToState(envelope.Order);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<MotherOrderState>?> GetOpenOrdersAsync(string? orderType = null)
    {
        var (orders, _) = await TryGetOpenOrdersAsync(orderType);
        return orders;
    }

    public async Task<(IReadOnlyList<MotherOrderState>? Orders, string? Error)> TryGetOpenOrdersAsync(
        string? orderType = null,
        CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return (null, "Client is not logged in to Mother POS.");
        }

        var query = string.IsNullOrWhiteSpace(orderType) ? string.Empty : $"?orderType={Uri.EscapeDataString(orderType)}";
        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders{query}",
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<OrderListEnvelope>(json, JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                return (null, envelope?.Message ?? $"Mother open-orders pull failed ({(int)response.StatusCode}).");
            }

            if (envelope is null || !envelope.Success)
            {
                return (null, envelope?.Message ?? "Mother POS returned an empty open-orders response.");
            }

            // Mother list is kitchen-sent only. Stamp so drafts saved locally stay off the board
            // and a later cache read still treats these rows as Live Orders.
            return ((envelope.Orders ?? []).Select(order => ClientLiveOrderPresentation.AsKitchenBoardOrder(ToState(order))).ToList(), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private async Task<MotherCommandResult> UpsertOrderAsync(
        MotherOrderState state,
        decimal? deliveryFee = null,
        string? notes = null,
        string? scheduledTime = null,
        CachedCustomer? customer = null)
        => await UpsertOrderCoreAsync(state, deliveryFee, notes, scheduledTime, customer, false, false, null);

    private async Task<MotherCommandResult> UpsertOrderCoreAsync(
        MotherOrderState state,
        decimal? deliveryFee,
        string? notes,
        string? scheduledTime,
        CachedCustomer? customer,
        bool printKitchen,
        bool printReceipt,
        IReadOnlyList<string>? printDocuments)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            throw new InvalidOperationException("This Client is not connected to Mother POS.");
        }

        var fee = deliveryFee ?? state.Lines
            .Where(line => string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(line.Name, "Delivery Fee", StringComparison.OrdinalIgnoreCase))
            .Sum(line => line.UnitPrice * line.Quantity);
        var body = new OrderUpsertHttpRequest(
            state.OrderId,
            state.OrderType,
            customer?.Name ?? state.CustomerName ?? SplitCustomer(state.ConflictMessage).Name,
            customer?.Phone ?? state.CustomerPhone ?? SplitCustomer(state.ConflictMessage).Phone,
            customer?.Email,
            customer?.Address,
            fee,
            notes ?? state.Notes,
            scheduledTime,
            state.TableId,
            state.TableNumber,
            state.Guests,
            state.Lines.Select(line => new OrderLineHttpRequest(
                line.Id,
                line.ProductMotherId ?? line.ProductId?.ToString(),
                line.Name,
                line.Quantity,
                line.UnitPrice,
                line.Notes,
                line.Modifiers,
                line.VariantId,
                line.VariantName,
                line.VariantPrice,
                line.MealDealId,
                line.MealDealChoices,
                line.TastingMenuId)).ToList(),
            // Existing orders: send version so Mother rejects stale concurrent edits.
            string.IsNullOrWhiteSpace(state.OrderId) ? null : state.Version,
            string.IsNullOrWhiteSpace(state.OrderId) ? null : state.UpdatedUtc,
            printKitchen,
            printReceipt,
            printDocuments,
            printKitchen || printReceipt || (printDocuments?.Count > 0) ? Guid.NewGuid().ToString("N") : null,
            state.Discount);

        using var client = CreateClient(auth);
        using var response = await client.PostAsJsonAsync($"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders", body, JsonOptions);
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<OrderEnvelope>(json, JsonOptions);
        if (envelope?.Order is not null &&
            (response.StatusCode == System.Net.HttpStatusCode.Conflict || envelope.Conflict == true))
        {
            var conflictState = ToState(envelope.Order);
            await _cache.SaveOrderStateAsync(conflictState);
            return new MotherCommandResult(
                conflictState,
                true,
                string.IsNullOrWhiteSpace(envelope.Message)
                    ? "Order updated elsewhere — reload"
                    : envelope.Message);
        }

        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success || envelope.Order is null)
        {
            throw new InvalidOperationException(envelope?.Message ?? $"Mother POS could not save this order ({(int)response.StatusCode}).");
        }

        if (envelope.Print is not null)
        {
            await _cache.SavePrintRequestAsync(new PrintRequestState(
                envelope.Print.PrintJobId ?? Guid.NewGuid().ToString("N"),
                envelope.Print.DocumentType ?? (printKitchen ? "kitchen ticket" : "receipt"),
                envelope.Order.Id,
                NormalizePrintStatus(envelope.Print.Status),
                envelope.Print.Message ?? "Mother print request accepted.",
                DateTimeOffset.UtcNow.ToString("O"),
                DateTimeOffset.UtcNow.ToString("O")));
        }

        var message = envelope.Print is null
            ? envelope.Message ?? "Order saved on Mother POS."
            : $"{envelope.Message ?? "Order saved on Mother POS."} Print status: {NormalizePrintStatus(envelope.Print.Status)}. {envelope.Print.Message}".Trim();
        var saved = WithCustomer(
            ToState(envelope.Order),
            customer?.Name ?? state.CustomerName,
            customer?.Phone ?? state.CustomerPhone);
        await _cache.SaveOrderStateAsync(saved);
        return new MotherCommandResult(saved, false, message);
    }

    private static MotherOrderState BuildDraftState(CustomerOrderDraft draft, LoginSession? session, string? orderId = null)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        // Stable Mother order id before first POST so retries cannot mint a second Collection row.
        var stableOrderId = string.IsNullOrWhiteSpace(orderId) ? Guid.NewGuid().ToString("N") : orderId.Trim();
        return new MotherOrderState(
            stableOrderId,
            draft.OrderType,
            draft.OrderType,
            null,
            null,
            0,
            "open",
            Array.Empty<MotherOrderLine>(),
            0m,
            0m,
            draft.DeliveryFee,
            1,
            now,
            session?.UserName ?? "Client User",
            null,
            draft.Customer.Name,
            draft.Customer.Phone);
    }

    private async Task<MotherClientAuth?> GetAuthAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken) ||
            string.IsNullOrWhiteSpace(session?.SessionToken))
        {
            return null;
        }

        return new MotherClientAuth(settings, session);
    }

    private static HttpClient CreateClient(MotherClientAuth auth)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", auth.Settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", auth.Settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", auth.Session.SessionToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static MotherOrderState ToState(OrderStateDto order) =>
        new(
            order.Id,
            order.OrderNumber ?? order.Id,
            order.OrderType ?? "Collection",
            order.TableId,
            order.TableNumber,
            order.Guests,
            order.Status ?? "open",
            (order.Lines ?? []).Select(line => new MotherOrderLine(
                line.Id,
                null,
                line.Name,
                line.Quantity,
                line.UnitPrice,
                line.Notes,
                line.Modifiers ?? Array.Empty<string>(),
                line.ProductMotherId,
                line.VariantId,
                line.VariantName,
                line.VariantPrice,
                line.MealDealId,
                line.MealDealChoices,
                line.TastingMenuId,
                line.IsSent,
                line.SendStatus)).ToList(),
            order.Subtotal,
            order.Tax,
            order.Total,
            Math.Max(1, order.Version),
            order.UpdatedUtc ?? DateTimeOffset.UtcNow.ToString("O"),
            null,
            order.ConflictMessage,
            null,
            null,
            order.Notes,
            order.Discount,
            order.ServiceCharge,
            order.ServiceChargeStatus,
            order.ServiceChargePercent,
            order.LoyaltyPointsEarned);

    public Task<MotherCommandResult> SetOrderNotesAsync(MotherOrderState state, string? notes) =>
        UpsertOrderAsync(state with { Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim() });

    public Task<MotherCommandResult> AddMealDealAsync(
        MotherOrderState state,
        CachedMealDeal deal,
        IReadOnlyList<string> choices)
    {
        var choiceList = choices.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        var notes = choiceList.Count == 0
            ? null
            : string.Join("\n", choiceList.Select(c => $"• {c}"));
        var lines = state.Lines.ToList();
        var existing = lines.FirstOrDefault(line =>
            string.Equals(line.MealDealId, deal.MotherId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(line.Notes?.Trim() ?? string.Empty, notes ?? string.Empty, StringComparison.Ordinal) &&
            !line.IsSent);
        if (existing != null)
        {
            var index = lines.IndexOf(existing);
            lines[index] = existing with { Quantity = existing.Quantity + 1 };
        }
        else
        {
            lines.Add(new MotherOrderLine(
                Guid.NewGuid().ToString("N"),
                null,
                deal.Name,
                1,
                deal.Price,
                notes,
                choiceList,
                $"mealdeal:{deal.MotherId}",
                MealDealId: deal.MotherId,
                MealDealChoices: choiceList));
        }

        return UpsertOrderAsync(RecalcOrderMoney(state with { Lines = lines }));
    }

    public Task<MotherCommandResult> AddTastingMenuAsync(
        MotherOrderState state,
        CachedTastingMenu menu,
        CachedTastingMenuOption option)
    {
        var lines = state.Lines.ToList();
        lines.Add(new MotherOrderLine(
            Guid.NewGuid().ToString("N"),
            null,
            $"{menu.Name} ({option.Name})",
            1,
            option.Price,
            null,
            Array.Empty<string>(),
            $"tasting:{menu.MotherId}:{option.MotherId}",
            VariantId: option.MotherId,
            VariantName: option.Name,
            VariantPrice: option.Price,
            TastingMenuId: menu.MotherId));
        return UpsertOrderAsync(RecalcOrderMoney(state with { Lines = lines }));
    }

    private static MotherOrderState WithCustomer(MotherOrderState state, string? name, string? phone)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone))
        {
            return state;
        }

        return state with
        {
            CustomerName = string.IsNullOrWhiteSpace(name) ? state.CustomerName : name.Trim(),
            CustomerPhone = string.IsNullOrWhiteSpace(phone) ? state.CustomerPhone : phone.Trim()
        };
    }

    private static (string? Name, string? Phone) SplitCustomer(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return (null, null);
        }

        var parts = summary.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return (parts.ElementAtOrDefault(0), parts.ElementAtOrDefault(1));
    }

    private static decimal ModifierTotal(CachedProduct product, IReadOnlyList<string> selectedModifiers)
    {
        return product.ModifierGroups
            .SelectMany(group => group.Modifiers)
            .Where(modifier => selectedModifiers.Contains(modifier.Name))
            .Sum(modifier => modifier.PriceDelta);
    }

    private static string NormalizePrintStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "printed" => "printed",
        "partial" => "partial",
        "failed" => "failed",
        _ => "queued"
    };

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession Session);

    private sealed record OrderUpsertHttpRequest(
        string? OrderId,
        string? OrderType,
        string? CustomerName,
        string? CustomerPhone,
        string? CustomerEmail,
        string? CustomerAddress,
        decimal DeliveryFee,
        string? Notes,
        string? ScheduledTime,
        int? TableId,
        string? TableNumber,
        int Guests,
        IReadOnlyList<OrderLineHttpRequest> Lines,
        int? ExpectedVersion,
        string? ExpectedUpdatedUtc,
        bool PrintKitchen,
        bool PrintReceipt,
        IReadOnlyList<string>? PrintDocuments,
        string? PrintRequestId,
        decimal? Discount = null);

    private sealed record OrderLineHttpRequest(
        string? Id,
        string? ProductId,
        string Name,
        int Quantity,
        decimal UnitPrice,
        string? Notes,
        IReadOnlyList<string> Modifiers,
        string? VariantId = null,
        string? VariantName = null,
        decimal? VariantPrice = null,
        string? MealDealId = null,
        IReadOnlyList<string>? MealDealChoices = null,
        string? TastingMenuId = null);

    private sealed record OrderEnvelope(bool Success, string? Message, OrderStateDto? Order, PrintState? Print, bool? Conflict = null);

    private sealed record PreviousOrdersEnvelope(bool Success, string? Message, IReadOnlyList<PreviousOrderDto>? Orders);

    private sealed record PreviousOrderDto(
        int OrderDatabaseId,
        string? OrderNumber,
        string CreatedAtUtc,
        string OrderType,
        decimal TotalAmount,
        string Status,
        string ItemsText,
        string? OrderNotes,
        bool IsMostRecent);

    private sealed record SimpleActionEnvelope(bool Success, string? Message, string? PrinterName = null);

    private sealed record PrintState(string? PrintJobId, string? Status, string? Message, string? DocumentType = null);

    private sealed record OrderListEnvelope(bool Success, string? Message, IReadOnlyList<OrderStateDto>? Orders);

    private sealed record OrderStateDto(
        string Id,
        string? OrderNumber,
        string? OrderType,
        string? Status,
        int? TableId,
        string? TableNumber,
        int Guests,
        IReadOnlyList<OrderLineDto>? Lines,
        decimal Subtotal,
        decimal Tax,
        decimal Total,
        int Version,
        string? UpdatedUtc,
        string? ConflictMessage,
        string? Notes = null,
        decimal Discount = 0m,
        decimal ServiceCharge = 0m,
        string? ServiceChargeStatus = null,
        decimal ServiceChargePercent = 0m,
        int LoyaltyPointsEarned = 0);

    private sealed record OrderLineDto(
        string Id,
        string? ProductMotherId,
        string Name,
        int Quantity,
        decimal UnitPrice,
        string? Notes,
        IReadOnlyList<string>? Modifiers,
        string? VariantId = null,
        string? VariantName = null,
        decimal? VariantPrice = null,
        string? MealDealId = null,
        IReadOnlyList<string>? MealDealChoices = null,
        string? TastingMenuId = null,
        bool IsSent = false,
        string? SendStatus = null);
}
