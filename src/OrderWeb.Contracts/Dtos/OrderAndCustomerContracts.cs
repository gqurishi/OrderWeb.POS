namespace OrderWeb.Contracts.Dtos;

public sealed record OrderLineRequest(
    string ProductId,
    decimal Quantity,
    string? Notes = null,
    IReadOnlyList<string>? ModifierIds = null);

public sealed record SubmitOrderRequest(
    string RequestId,
    string TerminalId,
    string SessionId,
    string? OrderId,
    long ExpectedRevision,
    string OrderType,
    string? TableId,
    IReadOnlyList<OrderLineRequest> Lines,
    DateTimeOffset RequestedAtUtc,
    string? CorrelationId = null);

public sealed record OrderLineDto(
    string Id,
    string ProductId,
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    string? Notes = null);

public sealed record OrderDto(
    string Id,
    string OrderNumber,
    string OrderType,
    string Status,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    long Revision,
    IReadOnlyList<OrderLineDto> Lines,
    string? TableId = null,
    string? CustomerId = null);

public sealed record CustomerSearchRequest(string? Name, string? Phone, string? AddressOrPostcode);

public sealed record CustomerDto(
    string Id,
    string Name,
    string Phone,
    string? Email,
    string? Address,
    string? Postcode);
