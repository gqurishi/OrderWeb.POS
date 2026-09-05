namespace OrderWeb.Contracts.Orders;

/// <summary>
/// Required on every modifying order request for idempotency and optimistic concurrency.
/// </summary>
public sealed record OrderMutationContext(
    string RequestId,
    string TerminalId,
    string SessionId,
    string? OrderId,
    long OrderRevision,
    DateTimeOffset TimestampUtc);

public static class OrderMutationContextFactory
{
    public static OrderMutationContext Create(
        string terminalId,
        string sessionId,
        string? orderId,
        long orderRevision) =>
        new(
            Guid.NewGuid().ToString("N"),
            terminalId,
            sessionId,
            orderId,
            orderRevision,
            DateTimeOffset.UtcNow);
}

public sealed record OrderLineModifierDto(string Id, string Name, decimal PriceDelta);

public sealed record OrderLineDto(
    string Id,
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? Notes,
    IReadOnlyList<OrderLineModifierDto> Modifiers,
    bool IsVoided = false);

public sealed record OrderTotalsDto(
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    bool IsDisplayEstimate = false);

public sealed record OrderDto(
    string Id,
    string? TableId,
    string? TableName,
    int GuestCount,
    string Status,
    long Revision,
    IReadOnlyList<OrderLineDto> Lines,
    OrderTotalsDto Totals,
    DateTimeOffset UpdatedAtUtc,
    string? ServerName = null);

public sealed record MenuCategoryDto(string Id, string Name, int SortOrder);

public sealed record MenuProductDto(
    string Id,
    string CategoryId,
    string Name,
    decimal Price,
    bool IsAvailable,
    int SortOrder = 0,
    string? Colour = null,
    IReadOnlyList<ProductModifierGroupDto>? ModifierGroups = null);

public sealed record ProductModifierGroupDto(
    string Id,
    string Name,
    int MinSelect,
    int MaxSelect,
    IReadOnlyList<OrderLineModifierDto> Options);

public sealed record OpenOrderRequest(
    OrderMutationContext Context,
    string? TableId,
    int GuestCount,
    string OrderType);

public sealed record AddOrderLineRequest(
    OrderMutationContext Context,
    string ProductId,
    int Quantity,
    string? Notes,
    IReadOnlyList<string> SelectedModifierIds);

public sealed record UpdateOrderLineQuantityRequest(
    OrderMutationContext Context,
    string LineId,
    int Quantity);

public sealed record UpdateOrderLineNotesRequest(
    OrderMutationContext Context,
    string LineId,
    string Notes);

public sealed record VoidOrderLineRequest(
    OrderMutationContext Context,
    string LineId,
    string? Reason,
    string? ManagerApprovalCode);

public sealed record ApplyDiscountRequest(
    OrderMutationContext Context,
    decimal Amount,
    bool IsPercent,
    string? Reason,
    string? ManagerApprovalCode);

public sealed record SendOrderRequest(OrderMutationContext Context);
