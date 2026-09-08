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
        var now = DateTimeOffset.UtcNow.ToString("O");
        var orderId = string.IsNullOrWhiteSpace(table.CurrentOrderId)
            ? $"T{table.TableNumber}-{DateTimeOffset.UtcNow:HHmmss}"
            : table.CurrentOrderId;

        var state = new MotherOrderState(
            orderId,
            string.IsNullOrWhiteSpace(table.CurrentOrderId) ? $"Table {table.TableNumber}" : table.CurrentOrderId,
            "Table",
            table.Id,
            table.TableNumber,
            Math.Max(covers, table.Covers > 0 ? table.Covers : 1),
            "open",
            Array.Empty<MotherOrderLine>(),
            0m,
            0m,
            0m,
            table.Version + 1,
            now,
            session?.UserName ?? "Client User");

        return await UpsertOrderAsync(state);
    }

    public Task<MotherCommandResult> CreateCustomerOrderAsync(CustomerOrderDraft draft, LoginSession? session) =>
        UpsertOrderAsync(BuildDraftState(draft, session), draft.DeliveryFee, draft.Notes, draft.PickupTime ?? draft.ScheduledTime, draft.Customer);

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
        var orders = await GetOpenOrdersAsync();
        var latest = orders?.FirstOrDefault(order => string.Equals(order.OrderId, state.OrderId, StringComparison.OrdinalIgnoreCase));
        if (latest == null)
        {
            throw new InvalidOperationException("Mother POS could not reload this order.");
        }

        return new MotherCommandResult(latest, false, "Order refreshed from Mother POS.");
    }

    public async Task<IReadOnlyList<MotherOrderState>?> GetOpenOrdersAsync(string? orderType = null)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return null;
        }

        var query = string.IsNullOrWhiteSpace(orderType) ? string.Empty : $"?orderType={Uri.EscapeDataString(orderType)}";
        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync($"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders{query}");
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var envelope = JsonSerializer.Deserialize<OrderListEnvelope>(json, JsonOptions);
            if (envelope is null || !envelope.Success)
            {
                return null;
            }

            return (envelope.Orders ?? []).Select(ToState).ToList();
        }
        catch
        {
            return null;
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
            customer?.Name ?? SplitCustomer(state.ConflictMessage).Name,
            customer?.Phone ?? SplitCustomer(state.ConflictMessage).Phone,
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
            printKitchen,
            printReceipt,
            printDocuments,
            printKitchen || printReceipt || (printDocuments?.Count > 0) ? Guid.NewGuid().ToString("N") : null);

        using var client = CreateClient(auth);
        using var response = await client.PostAsJsonAsync($"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders", body, JsonOptions);
        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<OrderEnvelope>(json, JsonOptions);
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
        return new MotherCommandResult(ToState(envelope.Order), false, message);
    }

    private static MotherOrderState BuildDraftState(CustomerOrderDraft draft, LoginSession? session)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return new MotherOrderState(
            string.Empty,
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
            $"{draft.Customer.Name} · {draft.Customer.Phone}");
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

    private sealed record OrderEnvelope(bool Success, string? Message, OrderStateDto? Order, PrintState? Print);

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
