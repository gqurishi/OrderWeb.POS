namespace OrderWeb.Contracts.Dtos;

/// <summary>
/// Shared Client ↔ Mother advance-order contracts.
/// Mother owns the clock and kitchen print; Client lists + receives <c>advance.reminder</c> WS tips.
/// Access: Manager session (Admin is Mother-only on Client login).
/// </summary>
public static class AdvanceOrderErrorCodes
{
    public const string OfflineMother = "advance.offline_mother";
    public const string AccessDenied = "advance.access_denied";
    public const string NotFound = "advance.not_found";
    public const string Validation = "advance.validation";
    public const string PrintFailed = "advance.print_failed";
    public const string AlreadyPrinted = "advance.already_printed";
    public const string Unknown = "advance.unknown";
}

/// <summary>Query ranges for <c>GET /api/client/advance-orders?range=</c>.</summary>
public static class AdvanceOrderRanges
{
    public const string Today = "today";
    public const string Tomorrow = "tomorrow";
    public const string Next7Days = "7d";
}

/// <summary>One advance Collection/Delivery row for Manager list.</summary>
public sealed record AdvanceOrderDto(
    string OrderId,
    string? OrderNumber,
    string OrderType,
    string? ScheduledTimeUtc,
    string? ScheduledDisplay,
    string CustomerName,
    string? CustomerPhone,
    decimal TotalAmount,
    bool KitchenPrinted,
    string Status);

/// <summary><c>GET /api/client/advance-orders</c> envelope.</summary>
public sealed record AdvanceOrderListResponseDto(
    bool Success,
    string? Message,
    string Range,
    string? FromUtc,
    string? ToUtc,
    IReadOnlyList<AdvanceOrderDto>? Orders,
    string? ErrorCode = null);

/// <summary><c>POST /api/client/advance-orders/{id}/print-kitchen</c> envelope.</summary>
public sealed record AdvanceOrderPrintResponseDto(
    bool Success,
    string? Message,
    string? OrderId = null,
    bool KitchenPrinted = false,
    string? ErrorCode = null);

/// <summary>
/// WebSocket event <c>advance.reminder</c> — Mother tips tills when advance kitchen print runs
/// (T−3h or inside-window). Clients must not schedule locally.
///
/// Frame shape (fan-out JSON + terminal_events payload):
/// <code>
/// {
///   "type": "advance.reminder",
///   "eventId": 123,
///   "restaurantId": "slug",
///   "version": "{orderId}",
///   "timestamp": "2026-09-15T12:00:00Z",
///   "correlationId": "...",
///   "orderId": "...",
///   "orderNumber": "#…",
///   "orderType": "Collection|Delivery",
///   "scheduledTimeUtc": "…",
///   "scheduledDisplay": "dd MMM HH:mm",
///   "customerName": "…",
///   "customerPhone": "…",
///   "totalAmount": 0.00,
///   "kitchenPrinted": true
/// }
/// </code>
/// Stored payload matches <see cref="AdvanceOrderReminderDto"/>.
/// </summary>
public sealed record AdvanceOrderReminderDto(
    string OrderId,
    string? OrderNumber,
    string OrderType,
    string? ScheduledTimeUtc,
    string? ScheduledDisplay,
    string CustomerName,
    string? CustomerPhone,
    decimal TotalAmount,
    bool KitchenPrinted);
