namespace OrderWeb.Contracts.Dtos;

/// <summary>
/// Shared Client ↔ Mother order-history API contracts.
/// Mother owns history query rules; Client fetches on demand.
/// Feature gate: <see cref="Features.PosFeatureKeys.Payments"/> (route: orderhistory).
/// </summary>
public static class OrderHistoryErrorCodes
{
    public const string OfflineMother = "order_history.offline_mother";
    public const string AccessDenied = "order_history.access_denied";
    public const string Validation = "order_history.validation";
    public const string NotFound = "order_history.not_found";
    public const string Unknown = "order_history.unknown";
    /// <summary>Offline view of a previously cached day snapshot (may be stale).</summary>
    public const string CachedSnapshot = "order_history.cached_snapshot";
}

public sealed record ClientOrderHistoryItemDto(
    int Id = 0,
    string? OrderId = null,
    string? OrderNumber = null,
    string? OrderType = null,
    string? OrderTypeDisplay = null,
    decimal TotalAmount = 0m,
    string? CreatedAtUtc = null,
    string? OrderDateTime = null,
    string? Status = null,
    string? StatusDisplay = null,
    string? CustomerDisplay = null,
    string? PaymentDisplay = null,
    string? SourceChannel = null,
    bool IsWebOrder = false,
    string? HistoryGroup = null);

public sealed record ClientOrderHistoryResponseDto(
    bool Success,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    IReadOnlyList<ClientOrderHistoryItemDto>? Completed = null,
    IReadOnlyList<ClientOrderHistoryItemDto>? Voided = null,
    bool HasNextPage = false,
    int Page = 1,
    int PageSize = 20,
    bool FromCache = false);

/// <summary>Read-only closed/open history order for Client View. No pay/edit/reopen.</summary>
public sealed record ClientOrderHistoryDetailLineDto(
    string? Name = null,
    int Quantity = 0,
    decimal UnitPrice = 0m,
    decimal TotalPrice = 0m,
    string? Details = null);

public sealed record ClientOrderHistoryDetailDto(
    int Id = 0,
    string? OrderId = null,
    string? OrderNumber = null,
    string? OrderType = null,
    string? OrderTypeDisplay = null,
    string? Status = null,
    string? StatusDisplay = null,
    string? HistoryGroup = null,
    string? CreatedAtUtc = null,
    string? OrderDateTime = null,
    string? CustomerName = null,
    string? CustomerPhone = null,
    string? CustomerEmail = null,
    string? CustomerAddress = null,
    string? PaymentMethod = null,
    string? PaymentDisplay = null,
    string? PaymentStatusDisplay = null,
    decimal? AmountPaid = null,
    string? PaymentProvider = null,
    string? PaymentReference = null,
    decimal SubtotalAmount = 0m,
    decimal TaxAmount = 0m,
    decimal DiscountAmount = 0m,
    decimal DeliveryFee = 0m,
    decimal ServiceChargeAmount = 0m,
    decimal TipsAmount = 0m,
    decimal TotalAmount = 0m,
    string? ScheduledDisplay = null,
    string? SpecialInstructions = null,
    string? PromoCode = null,
    string? GiftCardDisplay = null,
    string? LoyaltyDisplay = null,
    string? SourceChannel = null,
    bool IsWebOrder = false,
    bool CanReprint = true,
    IReadOnlyList<ClientOrderHistoryDetailLineDto>? Lines = null);

public sealed record ClientOrderHistoryDetailResponseDto(
    bool Success,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    ClientOrderHistoryDetailDto? Order = null);
