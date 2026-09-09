namespace OrderWeb.Contracts.Dtos;

/// <summary>Shared Client ↔ Mother gift-card API contracts. Cloud remains balance source of truth.</summary>
public static class GiftCardErrorCodes
{
    public const string OfflineMother = "gift_card.offline_mother";
    public const string CloudDown = "gift_card.cloud_down";
    public const string BlockedCard = "gift_card.blocked";
    public const string InsufficientBalance = "gift_card.insufficient_balance";
    public const string AccessDenied = "gift_card.access_denied";
    public const string Validation = "gift_card.validation";
    public const string NotFound = "gift_card.not_found";
    public const string Queued = "gift_card.queued";
    public const string Unknown = "gift_card.unknown";
}

public static class GiftCardLookupPurposes
{
    public const string Activate = "activate";
    public const string TopUp = "topup";
    public const string Redeem = "redeem";
    public const string Sell = "sell";
}

public sealed record ClientGiftCardLookupRequestDto(
    string CardNumber,
    string? Purpose = null,
    string? SessionToken = null);

public sealed record ClientGiftCardMutateRequestDto(
    string? CardNumber,
    decimal Amount,
    string? PaymentMethod = null,
    string? OrderId = null,
    string? Description = null,
    string? IdempotencyKey = null,
    string? SessionToken = null);

public sealed record ClientGiftCardDto(
    string? CardNumberMasked,
    string? CardNumber,
    decimal Balance,
    string? Status,
    string? CardType,
    bool CanUse,
    bool IsExpired,
    DateTime? ExpiryDate);

public sealed record ClientGiftCardLookupResponseDto(
    bool Success,
    bool? Found = null,
    bool CanProceed = false,
    bool? CanUse = null,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    bool CanQueueForRetry = false,
    IReadOnlyList<decimal>? SuggestedAmounts = null,
    ClientGiftCardDto? GiftCard = null);

public sealed record ClientGiftCardTransactionResponseDto(
    bool Success,
    bool Queued = false,
    string? QueueMessage = null,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    bool CanQueueForRetry = false,
    string? TransactionId = null,
    decimal? Balance = null,
    ClientGiftCardDto? GiftCard = null,
    IReadOnlyList<string>? ReceiptLines = null);

public sealed record ClientGiftCardRedeemResponseDto(
    bool Success,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    decimal? AmountRedeemed = null,
    decimal? RemainingBalance = null,
    decimal? PreviousBalance = null,
    bool? IsFullyRedeemed = null);
