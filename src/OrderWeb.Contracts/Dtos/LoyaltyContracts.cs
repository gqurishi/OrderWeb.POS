namespace OrderWeb.Contracts.Dtos;

/// <summary>
/// Shared Client ↔ Mother loyalty API contracts.
/// Cloud remains the points source of truth; Mother is the hub.
/// Feature gate: <see cref="Features.PosFeatureKeys.CustomerPoints"/> (Terminal Access label: Loyalty).
/// </summary>
public static class LoyaltyErrorCodes
{
    /// <summary>Client cannot reach Mother (offline / no session path).</summary>
    public const string OfflineMother = "loyalty.offline_mother";

    /// <summary>Mother cannot reach OrderWeb cloud, or cloud loyalty is not configured.</summary>
    public const string CloudDown = "loyalty.cloud_down";

    /// <summary>No loyalty customer for the lookup (phone / card).</summary>
    public const string CustomerNotFound = "loyalty.customer_not_found";

    /// <summary>Redeem requested more points than the cloud balance.</summary>
    public const string InsufficientPoints = "loyalty.insufficient_points";

    /// <summary>Session invalid or Terminal Access Loyalty (<c>pos.customer_points</c>) not granted.</summary>
    public const string AccessDenied = "loyalty.access_denied";

    /// <summary>Missing/invalid request fields (order id, lookup, £0 bill, no items, etc.).</summary>
    public const string Validation = "loyalty.validation";

    /// <summary>Mother queued a cloud retry after OrderWeb was unreachable (do not double-submit).</summary>
    public const string Queued = "loyalty.queued";

    /// <summary>
    /// Order Place earn already applied for this order (<c>loyalty_points_earned &gt; 0</c>).
    /// One earn per order — do not add again.
    /// </summary>
    public const string AlreadyEarned = "loyalty.already_earned";

    public const string Unknown = "loyalty.unknown";
}

/// <summary>
/// Locked v1 rules for Order Place → More → Loyalty Points (add / earn for the bill).
/// Endpoint: <c>POST /api/client/orders/loyalty-add</c>.
/// <para>
/// Explicitly deferred (do later — do not remove forever):
/// Payment → Loyalty redeem / pay-with-points (100 pts = £1). Keep existing PaymentPage /
/// payment API paths; do not expand Order Place tender Loyalty in the Add track.
/// </para>
/// <para>
/// Do not auto-earn on Send to Kitchen or Print — that would conflict with manual More → Add.
/// </para>
/// </summary>
public static class OrderPlaceLoyaltyEarnRules
{
    /// <summary>Earn rate: £1 of current order total = 1 point.</summary>
    public const int PointsPerPound = 1;

    /// <summary>Same order cannot earn twice; Mother blocks with <see cref="LoyaltyErrorCodes.AlreadyEarned"/>.</summary>
    public const bool OneEarnPerOrder = true;

    /// <summary>
    /// Auto-earn on Send/Print is deferred / off. Earn only via More → Add Loyalty Points.
    /// </summary>
    public const bool AutoEarnOnSendOrPrint = false;

    /// <summary>
    /// Order Place PAYMENT method dialog Loyalty button is deferred for polish.
    /// Existing PaymentPage / Mother payment loyalty redeem stays available elsewhere.
    /// </summary>
    public const bool OrderPlacePaymentLoyaltyEnabled = false;

    /// <summary>Points from open bill total (floor of pounds). Example: £15.80 → 15 points.</summary>
    public static int ComputePointsFromBillTotal(decimal orderTotal)
    {
        if (orderTotal <= 0m)
        {
            return 0;
        }

        return (int)decimal.Floor(orderTotal) * PointsPerPound;
    }
}

public sealed record ClientLoyaltyLookupRequestDto(
    string? Lookup = null,
    string? SessionToken = null);

public sealed record ClientLoyaltyCreateRequestDto(
    string? Phone = null,
    string? Name = null,
    string? Email = null,
    string? SessionToken = null);

/// <summary>Mutating loyalty calls. Prefer a sticky <see cref="IdempotencyKey"/> on retries.</summary>
public sealed record ClientLoyaltyMutateRequestDto(
    string? Lookup = null,
    int Points = 0,
    string? Reason = null,
    string? IdempotencyKey = null,
    string? SessionToken = null);

public sealed record ClientLoyaltyCustomerDto(
    string? Id,
    string? Phone,
    string? DisplayPhone,
    string? LoyaltyCardNumber,
    string? Name,
    string? Email,
    int PointsBalance,
    int TotalPointsEarned,
    int TotalPointsRedeemed,
    string? TierLevel);

public sealed record ClientLoyaltyHistoryItemDto(
    int Id,
    int PointsChange,
    string? TransactionType,
    string? Description,
    DateTime CreatedAt,
    decimal? OrderValue);

public sealed record ClientLoyaltyLookupResponseDto(
    bool Success,
    bool CustomerExists = false,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    ClientLoyaltyCustomerDto? Customer = null,
    int? PointsBalance = null,
    IReadOnlyList<ClientLoyaltyHistoryItemDto>? History = null);

public sealed record ClientLoyaltyTestResponseDto(
    bool Success,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null);

/// <summary>
/// Order Place earn request — <c>POST /api/client/orders/loyalty-add</c>.
/// <para>
/// <b>Rate (v1):</b> £1 spent = 1 point = <see cref="OrderPlaceLoyaltyEarnRules.ComputePointsFromBillTotal"/>.
/// Omit <see cref="Points"/> to use server calc from current order total; positive override allowed.
/// </para>
/// <para>
/// <b>One earn per order:</b> Mother refuses a second successful earn with
/// <see cref="LoyaltyErrorCodes.AlreadyEarned"/>.
/// </para>
/// <para>
/// Online only. Customer must already exist (no auto-create). Prefer sticky
/// <see cref="IdempotencyKey"/> on retries.
/// </para>
/// </summary>
public sealed record ClientOrderLoyaltyAddRequestDto(
    string? OrderId = null,
    string? Lookup = null,
    int? Points = null,
    string? IdempotencyKey = null,
    string? SessionToken = null);

/// <summary>
/// Order Place earn response for <c>POST /api/client/orders/loyalty-add</c>.
/// On success: <see cref="PointsAdded"/>, <see cref="PointsBalance"/>, <see cref="Customer"/>, <see cref="OrderId"/>.
/// On failure: <see cref="ErrorCode"/> from <see cref="LoyaltyErrorCodes"/>
/// (e.g. <c>already_earned</c>, <c>customer_not_found</c>, <c>cloud_down</c>, <c>queued</c>,
/// <c>offline_mother</c>, <c>access_denied</c>, <c>validation</c>).
/// </summary>
public sealed record ClientOrderLoyaltyAddResponseDto(
    bool Success,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    string? OrderId = null,
    decimal? BillTotal = null,
    int? PointsAdded = null,
    int? PointsBalance = null,
    ClientLoyaltyCustomerDto? Customer = null);
