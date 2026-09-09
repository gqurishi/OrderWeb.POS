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

    /// <summary>Missing/invalid request fields.</summary>
    public const string Validation = "loyalty.validation";

    /// <summary>Mother queued a cloud retry after OrderWeb was unreachable (do not double-submit).</summary>
    public const string Queued = "loyalty.queued";

    public const string Unknown = "loyalty.unknown";
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
