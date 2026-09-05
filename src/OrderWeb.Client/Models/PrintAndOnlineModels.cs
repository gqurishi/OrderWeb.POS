namespace OrderWeb.Client.Models;

/// <summary>
/// Local cache of a Mother-authoritative print result. Status/message come only from Mother.
/// </summary>
public sealed record PrintRequestState(
    string Id,
    string PrintType,
    string? OrderId,
    string Status,
    string Message,
    string CreatedUtc,
    string UpdatedUtc,
    string? AuditId = null,
    string? JobIds = null,
    string? FailedRoutesJson = null);

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
