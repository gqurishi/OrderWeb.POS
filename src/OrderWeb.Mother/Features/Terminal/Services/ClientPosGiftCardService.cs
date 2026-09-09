using OrderWeb.Contracts.Dtos;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother-facing gift card operations for Client POS.
/// Cloud remains the balance source of truth; this only wraps OrderWeb via Mother.
/// </summary>
public sealed class ClientPosGiftCardService
{
    private readonly OrderWebGiftCardApiService _giftCardApi;
    private readonly GiftCardActivationQueueService _activationQueue;

    public ClientPosGiftCardService(
        OrderWebGiftCardApiService giftCardApi,
        GiftCardActivationQueueService activationQueue)
    {
        _giftCardApi = giftCardApi;
        _activationQueue = activationQueue;
    }

    public static ClientPosGiftCardService? TryResolve()
    {
        var api = ServiceHelper.GetService<OrderWebGiftCardApiService>();
        var queue = ServiceHelper.GetService<GiftCardActivationQueueService>();
        if (api == null || queue == null)
        {
            return null;
        }

        return new ClientPosGiftCardService(api, queue);
    }

    public Task<GiftCardLookupResponse> LookupAsync(string cardNumber, string purpose) =>
        _giftCardApi.LookupAsync(cardNumber, purpose);

    public Task<GiftCardTransactionResponse> SellAsync(
        string? cardNumber,
        decimal amount,
        string paymentMethod,
        string? orderId,
        string? description,
        string? idempotencyKey)
    {
        var request = new GiftCardSellRequest
        {
            CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? null : cardNumber.Trim(),
            Amount = amount,
            PaymentMethod = NormalizePaymentMethod(paymentMethod),
            OrderId = orderId,
            TillOrderId = orderId,
            Description = string.IsNullOrWhiteSpace(description) ? "Client POS gift card sale" : description.Trim()
        };
        return _giftCardApi.SellAsync(request, ResolveTransactionId(idempotencyKey, orderId));
    }

    public Task<GiftCardTransactionResponse> TopUpAsync(
        string cardNumber,
        decimal amount,
        string paymentMethod,
        string? orderId,
        string? description,
        string? idempotencyKey)
    {
        var request = new GiftCardTopUpRequest
        {
            CardNumber = cardNumber.Trim(),
            Amount = amount,
            PaymentMethod = NormalizePaymentMethod(paymentMethod),
            OrderId = orderId,
            TillOrderId = orderId,
            Description = string.IsNullOrWhiteSpace(description) ? "Client POS gift card top-up" : description.Trim()
        };
        return _giftCardApi.TopUpAsync(request, ResolveTransactionId(idempotencyKey, orderId));
    }

    public Task<GiftCardRedeemResponse> RedeemAsync(
        string cardNumber,
        decimal amount,
        string? orderId,
        string? description,
        string? idempotencyKey)
    {
        var request = new GiftCardRedeemRequest
        {
            CardNumber = cardNumber.Trim(),
            Amount = amount,
            OrderId = orderId,
            Description = string.IsNullOrWhiteSpace(description) ? "Client POS gift card redeem" : description.Trim()
        };
        return _giftCardApi.RedeemAsync(request, ResolveTransactionId(idempotencyKey, orderId));
    }

    public async Task<ClientGiftCardActivateResult> ActivateAsync(
        string cardNumber,
        decimal amount,
        string paymentMethod,
        string? orderId,
        string? description,
        string? idempotencyKey,
        bool queueOnRetryableFailure)
    {
        var transactionId = ResolveTransactionId(idempotencyKey, orderId)
            ?? $"client-activate:{Guid.NewGuid():N}";
        var request = new GiftCardActivateRequest
        {
            CardNumber = cardNumber.Trim(),
            Amount = amount,
            PaymentMethod = NormalizePaymentMethod(paymentMethod),
            OrderId = orderId ?? transactionId,
            TillOrderId = orderId ?? transactionId,
            Description = string.IsNullOrWhiteSpace(description) ? "Client POS gift card activation" : description.Trim()
        };

        var result = await _giftCardApi.ActivateAsync(request, transactionId);
        if (result.Success || !queueOnRetryableFailure || !result.CanQueueForRetry)
        {
            return new ClientGiftCardActivateResult(result, Queued: false, QueueMessage: null);
        }

        var queueResult = await _activationQueue.QueueAsync(request, transactionId, result.Error ?? result.Message);
        return new ClientGiftCardActivateResult(
            result,
            Queued: queueResult.Success,
            QueueMessage: queueResult.Message);
    }

    public static ClientGiftCardLookupResponseDto ToLookupDto(GiftCardLookupResponse result)
    {
        var card = result.GiftCard;
        var ok = result.Success || result.CanProceed;
        return new ClientGiftCardLookupResponseDto(
            Success: ok,
            Found: result.Found,
            CanProceed: result.CanProceed,
            CanUse: result.CanUse ?? card?.IsUsable,
            Message: result.StatusMessage,
            Error: ok ? null : result.Error ?? result.StatusMessage,
            ErrorCode: ok ? null : ClassifyLookupError(result),
            CanQueueForRetry: result.CanQueueForRetry,
            SuggestedAmounts: (IReadOnlyList<decimal>?)result.Till?.SuggestedAmounts ?? Array.Empty<decimal>(),
            GiftCard: card == null ? null : ToCardDto(card));
    }

    public static ClientGiftCardTransactionResponseDto ToTransactionDto(
        GiftCardTransactionResponse result,
        bool queued = false,
        string? queueMessage = null)
    {
        var ok = result.Success || queued;
        return new ClientGiftCardTransactionResponseDto(
            Success: ok,
            Queued: queued,
            QueueMessage: queueMessage,
            Message: result.Message ?? queueMessage,
            Error: ok ? null : result.Error ?? result.Message,
            ErrorCode: ok
                ? queued ? GiftCardErrorCodes.Queued : null
                : ClassifyTransactionError(result),
            CanQueueForRetry: result.CanQueueForRetry,
            TransactionId: result.TransactionId,
            Balance: result.EffectiveBalance,
            GiftCard: result.GiftCard == null ? null : ToCardDto(result.GiftCard),
            ReceiptLines: (IReadOnlyList<string>?)result.Receipt?.Lines ?? Array.Empty<string>());
    }

    public static ClientGiftCardRedeemResponseDto ToRedeemDto(GiftCardRedeemResponse result)
    {
        return new ClientGiftCardRedeemResponseDto(
            Success: result.Success,
            Message: result.Message,
            Error: result.Success ? null : result.Error ?? result.Message,
            ErrorCode: result.Success ? null : ClassifyRedeemError(result),
            AmountRedeemed: result.EffectiveAmountRedeemed,
            RemainingBalance: result.EffectiveRemainingBalance,
            PreviousBalance: result.PreviousBalance,
            IsFullyRedeemed: result.IsFullyRedeemed);
    }

    public static ClientGiftCardDto ToCardDto(GiftCard card) =>
        new(
            CardNumberMasked: MaskCardNumber(card.CardNumber),
            CardNumber: card.CardNumber,
            Balance: card.Balance,
            Status: card.Status,
            CardType: card.CardType,
            CanUse: card.IsUsable,
            IsExpired: card.IsExpired,
            ExpiryDate: card.ExpiryDate);

    public static string MaskCardNumber(string? cardNumber)
    {
        var trimmed = (cardNumber ?? string.Empty).Trim();
        if (trimmed.Length <= 4)
        {
            return trimmed;
        }

        return new string('*', Math.Min(trimmed.Length - 4, 12)) + trimmed[^4..];
    }

    private static string ClassifyLookupError(GiftCardLookupResponse result)
    {
        if (result.CanQueueForRetry)
        {
            return GiftCardErrorCodes.CloudDown;
        }

        var text = $"{result.Error} {result.StatusMessage} {result.Message}".ToLowerInvariant();
        if (text.Contains("block") || text.Contains("cannot be") || text.Contains("expired") || text.Contains("not usable"))
        {
            return GiftCardErrorCodes.BlockedCard;
        }

        if (text.Contains("not found") || text.Contains("no gift card"))
        {
            return GiftCardErrorCodes.NotFound;
        }

        if (text.Contains("enter or scan") || text.Contains("required"))
        {
            return GiftCardErrorCodes.Validation;
        }

        return GiftCardErrorCodes.Unknown;
    }

    private static string ClassifyTransactionError(GiftCardTransactionResponse result)
    {
        if (result.CanQueueForRetry)
        {
            return GiftCardErrorCodes.CloudDown;
        }

        var text = $"{result.Error} {result.Message}".ToLowerInvariant();
        if (text.Contains("insufficient") || text.Contains("exceeds"))
        {
            return GiftCardErrorCodes.InsufficientBalance;
        }

        if (text.Contains("block") || text.Contains("cannot"))
        {
            return GiftCardErrorCodes.BlockedCard;
        }

        if (text.Contains("not configured") || text.Contains("orderweb") || text.Contains("cloud"))
        {
            return GiftCardErrorCodes.CloudDown;
        }

        return GiftCardErrorCodes.Unknown;
    }

    private static string ClassifyRedeemError(GiftCardRedeemResponse result)
    {
        var text = $"{result.Error} {result.Message}".ToLowerInvariant();
        if (text.Contains("exceeds") || text.Contains("insufficient"))
        {
            return GiftCardErrorCodes.InsufficientBalance;
        }

        if (text.Contains("cannot") || text.Contains("block") || text.Contains("expired"))
        {
            return GiftCardErrorCodes.BlockedCard;
        }

        if (text.Contains("orderweb") || text.Contains("cloud") || text.Contains("configured"))
        {
            return GiftCardErrorCodes.CloudDown;
        }

        return GiftCardErrorCodes.Unknown;
    }

    private static string NormalizePaymentMethod(string? paymentMethod)
    {
        var value = (paymentMethod ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "card" or "cash" or "gift_card" or "other" => value,
            "gift card" => "gift_card",
            "" => "cash",
            _ => value
        };
    }

    private static string? ResolveTransactionId(string? idempotencyKey, string? orderId)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return idempotencyKey.Trim();
        }

        return string.IsNullOrWhiteSpace(orderId) ? null : orderId.Trim();
    }
}

public sealed record ClientGiftCardActivateResult(
    GiftCardTransactionResponse Result,
    bool Queued,
    string? QueueMessage);
