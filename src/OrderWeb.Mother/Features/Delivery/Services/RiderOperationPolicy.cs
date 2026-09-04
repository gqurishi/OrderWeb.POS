namespace POS_in_NET.Services;

public static class RiderOperationPolicy
{
    public const string AwaitingKitchen = "awaiting_kitchen";
    public const string ReadyForRider = "ready_for_rider";
    public const string QuoteRequested = "quote_requested";
    public const string QuoteAvailable = "quote_available";
    public const string QuoteExpired = "quote_expired";
    public const string QuoteFailed = "quote_failed";
    public const string FindingRider = "finding_rider";
    public const string RiderAssigned = "rider_assigned";
    public const string Collected = "collected";
    public const string Delivering = "delivering";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";
    public const string DispatchCheckRequired = "dispatch_check_required";

    public static readonly TimeSpan DayStartTime = new(2, 0, 0);

    public static DateTime GetBusinessDate(DateTime localNow) =>
        localNow.TimeOfDay < DayStartTime ? localNow.Date.AddDays(-1) : localNow.Date;

    public static DateTime GetDayStart(DateTime localNow) => GetBusinessDate(localNow).Add(DayStartTime);
    public static DateTime GetDayEnd(DateTime localNow) => GetDayStart(localNow).AddDays(1);

    public static bool IsTerminal(string? status) => Normalize(status) is Delivered or Cancelled;

    public static bool IsProblem(string? status) => Normalize(status) is QuoteFailed or QuoteExpired or DispatchCheckRequired;

    public static bool CanRequestQuote(string? status) => Normalize(status) is
        AwaitingKitchen or ReadyForRider or QuoteExpired or QuoteFailed;

    public static bool CanConfirmQuote(string? status, string? quoteId, DateTime? expiresAt, DateTime now) =>
        Normalize(status) == QuoteAvailable &&
        !string.IsNullOrWhiteSpace(quoteId) &&
        (!expiresAt.HasValue || expiresAt.Value > now);

    public static decimal CalculateCashCollection(
        decimal total,
        decimal? amountPaid,
        string? paymentMethod,
        string? paymentStatus)
    {
        if (OnlineOrderPaymentHelper.IsPaidFromSource(paymentMethod, paymentStatus)) return 0m;
        var paid = Math.Max(0m, amountPaid ?? 0m);
        return Math.Max(0m, total - paid);
    }

    public static string GetDisplayStatus(string? operationStatus, string? orderStatus, DateTime? firstSentAt)
    {
        var status = Normalize(operationStatus);
        if (status == AwaitingKitchen && (firstSentAt.HasValue || Normalize(orderStatus) is "ready" or "preparing" or "kitchen"))
            status = Normalize(orderStatus) == "ready" ? ReadyForRider : AwaitingKitchen;

        return status switch
        {
            AwaitingKitchen => "Awaiting kitchen",
            ReadyForRider => "Ready for rider",
            QuoteRequested => "Requesting quote",
            QuoteAvailable => "Quote available",
            QuoteExpired => "Quote expired",
            QuoteFailed => "Quote failed",
            FindingRider => "Finding rider",
            RiderAssigned => "Rider assigned",
            Collected => "Collected",
            Delivering => "Delivering",
            Delivered => "Delivered",
            Cancelled => "Cancelled",
            DispatchCheckRequired => "Checking dispatch",
            _ => "Awaiting kitchen"
        };
    }

    public static string GetStatusColor(string? operationStatus, string? orderStatus, DateTime? firstSentAt) =>
        GetDisplayStatus(operationStatus, orderStatus, firstSentAt) switch
        {
            "Delivered" => "#16A34A",
            "Rider assigned" or "Collected" or "Delivering" => "#2563EB",
            "Quote available" or "Ready for rider" => "#0F766E",
            "Quote failed" or "Quote expired" or "Checking dispatch" => "#DC2626",
            "Cancelled" => "#64748B",
            _ => "#D97706"
        };

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
