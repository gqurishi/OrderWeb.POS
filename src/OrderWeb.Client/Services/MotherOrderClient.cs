using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;

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
            $"Table {table.TableNumber}");

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
    /// Void/cancel a Collection, Delivery, or Table order on Mother (same OrderId).
    /// </summary>
    public async Task<MotherCommandResult> VoidCollectionOrderAsync(MotherOrderState state)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            throw new InvalidOperationException("This Client is not connected to Mother POS.");
        }

        if (string.IsNullOrWhiteSpace(state.OrderId))
        {
            throw new InvalidOperationException("A Mother order id is required to void.");
        }

        using var client = CreateClient(auth);
        using var response = await client.PostAsJsonAsync(
            $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders/void",
            new { orderId = state.OrderId },
            JsonOptions);
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<OrderEnvelope>(json, JsonOptions);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success || envelope.Order is null)
        {
            throw new InvalidOperationException(envelope?.Message ?? $"Mother POS could not void this order ({(int)response.StatusCode}).");
        }

        return new MotherCommandResult(ToState(envelope.Order), false, envelope.Message ?? "Order voided on Mother POS.");
    }

    public Task<MotherCommandResult> AddItemAsync(MotherOrderState state, CachedProduct product, IReadOnlyList<string> modifiers)
    {
        var lines = state.Lines.ToList();
        var existing = lines.FirstOrDefault(line =>
            string.Equals(line.Name, product.Name, StringComparison.OrdinalIgnoreCase) &&
            line.Modifiers.SequenceEqual(modifiers) &&
            !string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase));
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
                product.Name,
                1,
                product.Price + ModifierTotal(product, modifiers),
                null,
                modifiers,
                product.MotherId));
        }

        return UpsertOrderAsync(state with { Lines = lines });
    }

    public Task<MotherCommandResult> UpdateQuantityAsync(MotherOrderState state, MotherOrderLine line, int quantity)
    {
        var lines = state.Lines
            .Select(existing => existing.Id == line.Id ? existing with { Quantity = Math.Max(quantity, 1) } : existing)
            .ToList();
        return UpsertOrderAsync(state with { Lines = lines });
    }

    public Task<MotherCommandResult> RemoveItemAsync(MotherOrderState state, MotherOrderLine line)
    {
        var lines = state.Lines.Where(existing => existing.Id != line.Id).ToList();
        return UpsertOrderAsync(state with { Lines = lines });
    }

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

            return ((envelope.Orders ?? []).Select(ToState).ToList(), null);
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
            notes,
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
                line.Modifiers)).ToList(),
            // Existing orders: send version so Mother rejects stale concurrent edits.
            string.IsNullOrWhiteSpace(state.OrderId) ? null : state.Version,
            string.IsNullOrWhiteSpace(state.OrderId) ? null : state.UpdatedUtc,
            printKitchen,
            printReceipt,
            printDocuments,
            printKitchen || printReceipt || (printDocuments?.Count > 0) ? Guid.NewGuid().ToString("N") : null);

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
                line.ProductMotherId)).ToList(),
            order.Subtotal,
            order.Tax,
            order.Total,
            Math.Max(1, order.Version),
            order.UpdatedUtc ?? DateTimeOffset.UtcNow.ToString("O"),
            null,
            order.ConflictMessage);

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
        string? PrintRequestId);

    private sealed record OrderLineHttpRequest(
        string? Id,
        string? ProductId,
        string Name,
        int Quantity,
        decimal UnitPrice,
        string? Notes,
        IReadOnlyList<string> Modifiers);

    private sealed record OrderEnvelope(bool Success, string? Message, OrderStateDto? Order, PrintState? Print, bool? Conflict = null);

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
        string? ConflictMessage);

    private sealed record OrderLineDto(
        string Id,
        string? ProductMotherId,
        string Name,
        int Quantity,
        decimal UnitPrice,
        string? Notes,
        IReadOnlyList<string>? Modifiers);
}
