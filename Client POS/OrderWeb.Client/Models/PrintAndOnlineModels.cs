namespace OrderWeb.Client.Models;

public sealed record PrintRequestState(
    string Id,
    string PrintType,
    string? OrderId,
    string Status,
    string Message,
    string CreatedUtc,
    string UpdatedUtc);

public sealed record CachedOnlineOrder(
    string Id,
    string MotherId,
    string OrderNumber,
    string CustomerName,
    string OrderType,
    string DueTime,
    string Status,
    decimal Total,
    string PayloadJson,
    string UpdatedUtc);
