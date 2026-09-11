namespace OrderWeb.Contracts.Dtos;

public sealed record PaymentRequest(
    string RequestId,
    string TerminalId,
    string SessionId,
    string OrderId,
    string Method,
    decimal Amount,
    DateTimeOffset RequestedAtUtc,
    long? ExpectedOrderRevision = null,
    string? CorrelationId = null,
    string? GiftCardNumber = null,
    string? GiftCardIdempotencyKey = null,
    string? LoyaltyLookup = null,
    int? LoyaltyPoints = null,
    string? LoyaltyIdempotencyKey = null,
    /// <summary>Tip allocated to this payment line.</summary>
    decimal TipAmount = 0m,
    /// <summary>Full tip for the order (ceiling for approved totals). Defaults to TipAmount when 0.</summary>
    decimal TipTotal = 0m);

public sealed record PaymentResultDto(
    string PaymentId,
    string OrderId,
    string Status,
    decimal ConfirmedAmount,
    string? ProviderReference = null,
    string? GiftCardNumberMasked = null,
    decimal? GiftCardRemainingBalance = null,
    string? LoyaltyCustomerName = null,
    int? LoyaltyPointsRedeemed = null,
    int? LoyaltyPointsRemaining = null);

public sealed record PrintRequest(
    string RequestId,
    string TerminalId,
    string SessionId,
    string DocumentType,
    string EntityId,
    int Copies = 1);

public sealed record PrintResultDto(string PrintJobId, string Status, string? Message = null);

public sealed record TerminalIdentityDto(
    string TerminalId,
    string TerminalName,
    string RestaurantId,
    string MotherId,
    string Platform,
    string AppVersion,
    bool IsPaired);
