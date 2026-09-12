using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Live OrderWeb gift card API client. OrderWeb owns gift card state; the POS
/// should use this service instead of local gift card balance/state.
/// </summary>
public sealed class OrderWebGiftCardApiService
{
    private const string GiftCardApiBaseUrl = "https://orderweb.net/api";
    private const string MissingIdempotencyError = "Gift card POST is missing an Idempotency-Key. Retry with the same transaction id.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OrderWebApiClient _orderWebApiClient;

    public OrderWebGiftCardApiService(OrderWebApiClient orderWebApiClient)
    {
        _orderWebApiClient = orderWebApiClient;
    }

    public async Task<GiftCardLookupResponse> LookupAsync(string cardNumber, string purpose)
    {
        var normalizedPurpose = GiftCardLookupPurpose.Normalize(purpose);
        var trimmedCardNumber = NormalizeCardNumber(cardNumber);

        if (string.IsNullOrWhiteSpace(trimmedCardNumber))
        {
            return new GiftCardLookupResponse
            {
                Success = false,
                Error = "Enter or scan a gift card number."
            };
        }

        var config = await GetGiftCardConfigAsync();
        if (config == null)
        {
            return MissingConfigurationLookupResponse();
        }

        var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var path = "/pos/gift-card/lookup"
            + $"?tenant={Uri.EscapeDataString(config.TenantSlug)}"
            + $"&cardNumber={Uri.EscapeDataString(trimmedCardNumber)}"
            + $"&purpose={Uri.EscapeDataString(normalizedPurpose)}"
            + $"&_={cacheBuster}";

        using var request = _orderWebApiClient.CreateRequest(config, HttpMethod.Get, path);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");

        AppDiagnostics.Log("Gift card lookup requested");
        try
        {
            using var response = await _orderWebApiClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            AppDiagnostics.Log($"Gift card lookup status: {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                return new GiftCardLookupResponse
                {
                    Success = false,
                    Error = ParseGiftCardError(content, $"Gift card lookup failed: {response.StatusCode}"),
                    CanQueueForRetry = IsRetryableStatusCode(response.StatusCode)
                };
            }

            var result = ParseGiftCardLookupResponse(content);
            return NormalizeLookupResult(result, normalizedPurpose)
                ?? new GiftCardLookupResponse { Success = false, Error = "Invalid gift card lookup response.", CanQueueForRetry = true };
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Gift card lookup transport failed: {ex.Message}");
            return new GiftCardLookupResponse
            {
                Success = false,
                Error = $"Gift card lookup could not reach OrderWeb: {ex.Message}",
                CanQueueForRetry = true
            };
        }
    }

    public async Task<GiftCardTransactionResponse> ActivateAsync(GiftCardActivateRequest request, string? transactionId = null)
    {
        var lookup = await LookupAsync(request.CardNumber, GiftCardLookupPurpose.Activate);
        if (!LookupCanProceed(lookup))
        {
            return TransactionBlockedByLookup(lookup, "Gift card cannot be activated.");
        }

        var config = await GetGiftCardConfigAsync();
        if (config == null)
        {
            return MissingConfigurationTransactionResponse();
        }

        var cardNumber = NormalizeCardNumber(request.CardNumber).ToUpperInvariant();
        var idempotencyKey = BuildRequiredIdempotencyKey(
            "gift-card-activate",
            transactionId,
            cardNumber,
            request.Amount,
            request.PaymentMethod,
            request.OrderId,
            request.TillOrderId);
        var payload = new
        {
            tenant = config.TenantSlug,
            cardNumber,
            amount = request.Amount,
            paymentMethod = request.PaymentMethod,
            orderId = request.OrderId ?? request.TillOrderId,
            tillOrderId = request.TillOrderId ?? request.OrderId,
            description = request.Description,
            idempotency_key = idempotencyKey
        };

        return await PostTransactionAsync(config, "/pos/gift-card/activate", payload, idempotencyKey);
    }

    public async Task<GiftCardTransactionResponse> SellAsync(GiftCardSellRequest request, string? transactionId = null)
    {
        var config = await GetGiftCardConfigAsync();
        if (config == null)
        {
            return MissingConfigurationTransactionResponse();
        }

        var cardNumber = string.IsNullOrWhiteSpace(request.CardNumber)
            ? null
            : NormalizeCardNumber(request.CardNumber).ToUpperInvariant();
        var idempotencyKey = BuildRequiredIdempotencyKey(
            "gift-card-sell",
            transactionId,
            cardNumber,
            request.Amount,
            request.PaymentMethod,
            request.OrderId,
            request.TillOrderId);
        var payload = new
        {
            tenant = config.TenantSlug,
            cardNumber,
            amount = request.Amount,
            paymentMethod = request.PaymentMethod,
            orderId = request.OrderId ?? request.TillOrderId,
            tillOrderId = request.TillOrderId ?? request.OrderId,
            description = request.Description,
            idempotency_key = idempotencyKey
        };

        return await PostTransactionAsync(config, "/pos/gift-card/sell", payload, idempotencyKey);
    }

    public async Task<GiftCardTransactionResponse> TopUpAsync(GiftCardTopUpRequest request, string? transactionId = null)
    {
        var lookup = await LookupAsync(request.CardNumber, GiftCardLookupPurpose.TopUp);
        if (!LookupCanProceed(lookup))
        {
            return TransactionBlockedByLookup(lookup, "Gift card cannot be topped up.");
        }

        var config = await GetGiftCardConfigAsync();
        if (config == null)
        {
            return MissingConfigurationTransactionResponse();
        }

        var cardNumber = NormalizeCardNumber(request.CardNumber).ToUpperInvariant();
        var idempotencyKey = BuildRequiredIdempotencyKey(
            "gift-card-top-up",
            transactionId,
            cardNumber,
            request.Amount,
            request.PaymentMethod,
            request.OrderId,
            request.TillOrderId);
        var payload = new
        {
            tenant = config.TenantSlug,
            cardNumber,
            amount = request.Amount,
            paymentMethod = request.PaymentMethod,
            orderId = request.OrderId ?? request.TillOrderId,
            tillOrderId = request.TillOrderId ?? request.OrderId,
            description = request.Description,
            idempotency_key = idempotencyKey
        };

        return await PostTransactionAsync(config, "/pos/gift-card/top-up", payload, idempotencyKey);
    }

    public async Task<GiftCardRedeemResponse> RedeemAsync(GiftCardRedeemRequest request, string? transactionId = null)
    {
        if (request.Amount <= 0)
        {
            return new GiftCardRedeemResponse
            {
                Success = false,
                Error = "Enter a valid redemption amount."
            };
        }

        var lookup = await LookupAsync(request.CardNumber, GiftCardLookupPurpose.Redeem);
        if (!LookupCanProceed(lookup))
        {
            return new GiftCardRedeemResponse
            {
                Success = false,
                Error = lookup.StatusMessage ?? "Gift card cannot be redeemed."
            };
        }

        var latestCard = lookup.GiftCard;
        if (latestCard == null)
        {
            return new GiftCardRedeemResponse
            {
                Success = false,
                Error = lookup.StatusMessage ?? "OrderWeb did not return the latest gift card balance."
            };
        }

        if (!latestCard.IsUsable)
        {
            return new GiftCardRedeemResponse
            {
                Success = false,
                Error = lookup.StatusMessage ?? $"Gift card cannot be redeemed: {latestCard.StatusDisplay.Trim()}."
            };
        }

        if (request.Amount > latestCard.Balance)
        {
            return new GiftCardRedeemResponse
            {
                Success = false,
                Error = $"Amount exceeds latest OrderWeb balance (GBP {latestCard.Balance:F2}).",
                RemainingBalance = latestCard.Balance
            };
        }

        var config = await GetGiftCardConfigAsync();
        if (config == null)
        {
            return new GiftCardRedeemResponse
            {
                Success = false,
                Error = "API not configured. Please check Cloud Settings."
            };
        }

        var cardNumber = NormalizeCardNumber(
            string.IsNullOrWhiteSpace(latestCard.CardNumber)
                ? request.CardNumber
                : latestCard.CardNumber).ToUpperInvariant();
        var idempotencyKey = BuildRequiredIdempotencyKey(
            "gift-card-redeem",
            transactionId,
            cardNumber,
            request.Amount,
            request.OrderId,
            request.Description);
        var payload = new
        {
            tenant = config.TenantSlug,
            cardNumber,
            amount = request.Amount,
            orderId = request.OrderId,
            description = request.Description,
            idempotency_key = idempotencyKey
        };

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new GiftCardRedeemResponse
            {
                Success = false,
                Error = MissingIdempotencyError
            };
        }

        using var httpRequest = _orderWebApiClient.CreateRequest(
            config,
            HttpMethod.Post,
            "/pos/gift-card/redeem",
            payload,
            idempotencyKey);

        AppDiagnostics.Log("Gift card redeem requested");
        try
        {
            using var response = await _orderWebApiClient.SendAsync(httpRequest);
            var content = await response.Content.ReadAsStringAsync();
            AppDiagnostics.Log($"Gift card redeem status: {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                return new GiftCardRedeemResponse
                {
                    Success = false,
                    Error = ParseGiftCardError(content, $"Failed to redeem gift card: {response.StatusCode}")
                };
            }

            return ParseGiftCardRedeemResponse(content)
                ?? new GiftCardRedeemResponse { Success = false, Error = "Invalid gift card redemption response." };
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Gift card redeem transport failed: {ex.Message}");
            return new GiftCardRedeemResponse
            {
                Success = false,
                Error = $"Gift card redeem could not reach OrderWeb: {ex.Message}"
            };
        }
    }

    public async Task<GiftCardTransactionResponse> FlushActivationsAsync(
        IEnumerable<GiftCardActivateRequest> activations,
        string? transactionId = null)
    {
        var config = await GetGiftCardConfigAsync();
        if (config == null)
        {
            return MissingConfigurationTransactionResponse();
        }

        var items = activations
            .Where(a => !string.IsNullOrWhiteSpace(a.CardNumber))
            .Take(50)
            .Select(a => new
            {
                cardNumber = NormalizeCardNumber(a.CardNumber).ToUpperInvariant(),
                amount = a.Amount,
                paymentMethod = a.PaymentMethod,
                orderId = a.OrderId ?? a.TillOrderId,
                tillOrderId = a.TillOrderId ?? a.OrderId,
                description = a.Description
            })
            .ToList();

        if (items.Count == 0)
        {
            return new GiftCardTransactionResponse
            {
                Success = false,
                Error = "No queued activations to flush."
            };
        }

        var idempotencyKey = BuildRequiredIdempotencyKey(
            "gift-card-activate-flush",
            transactionId,
            string.Join(",", items.Select(i => $"{i.cardNumber}:{i.amount}:{i.orderId}:{i.tillOrderId}")));
        var payload = new
        {
            tenant = config.TenantSlug,
            activations = items,
            idempotency_key = idempotencyKey
        };

        return await PostTransactionAsync(config, "/pos/gift-card/activate/flush", payload, idempotencyKey);
    }

    private async Task<OrderWebApiConfig?> GetGiftCardConfigAsync()
    {
        var config = await _orderWebApiClient.GetConfigAsync();
        return config == null
            ? null
            : config with { ApiBaseUrl = GiftCardApiBaseUrl };
    }

    private async Task<GiftCardTransactionResponse> PostTransactionAsync(
        OrderWebApiConfig config,
        string path,
        object payload,
        string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new GiftCardTransactionResponse
            {
                Success = false,
                Error = MissingIdempotencyError
            };
        }

        using var request = _orderWebApiClient.CreateRequest(config, HttpMethod.Post, path, payload, idempotencyKey);

        AppDiagnostics.Log("Gift card operation requested");
        try
        {
            using var response = await _orderWebApiClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            AppDiagnostics.Log($"Gift card POST status: {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                return new GiftCardTransactionResponse
                {
                    Success = false,
                    Error = ParseGiftCardError(content, $"Gift card operation failed: {response.StatusCode}"),
                    CanQueueForRetry = IsRetryableStatusCode(response.StatusCode)
                };
            }

            try
            {
                return ParseGiftCardTransactionResponse(content)
                    ?? new GiftCardTransactionResponse
                    {
                        Success = false,
                        Error = "Invalid gift card operation response.",
                        CanQueueForRetry = true
                    };
            }
            catch (Exception parseEx)
            {
                // Never treat a parse bug as "cloud unreachable" — the POST may already have succeeded.
                AppDiagnostics.Log($"Gift card POST parse failed: {parseEx.Message}");
                return BuildTransactionSuccessFromRawJson(content)
                    ?? new GiftCardTransactionResponse
                    {
                        Success = true,
                        Message = "Gift card updated on OrderWeb. Receipt could not be read.",
                        Error = null
                    };
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Gift card POST transport failed: {ex.Message}");
            return new GiftCardTransactionResponse
            {
                Success = false,
                Error = $"Gift card operation could not reach OrderWeb: {ex.Message}",
                CanQueueForRetry = true
            };
        }
    }

    /// <summary>Best-effort success when JSON shape is unexpected but HTTP 200 was returned.</summary>
    private static GiftCardTransactionResponse? BuildTransactionSuccessFromRawJson(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var data = TryGetProperty(root, out var dataElement, "data", "result") ? dataElement : root;
            var card = FindGiftCardElement(root);
            var success = GetBool(root, "success") ?? GetBool(data, "success") ?? true;
            if (success == false && !string.IsNullOrWhiteSpace(GetString(root, "error") ?? GetString(data, "error")))
            {
                return new GiftCardTransactionResponse
                {
                    Success = false,
                    Error = GetString(root, "error") ?? GetString(data, "error")
                };
            }

            return new GiftCardTransactionResponse
            {
                Success = true,
                Message = GetString(root, "message") ?? GetString(data, "message") ?? "Gift card updated on OrderWeb.",
                TransactionId = GetString(root, "transaction_id", "transactionId", "id")
                    ?? GetString(data, "transaction_id", "transactionId", "id"),
                RemainingBalance = GetNullableDecimal(root, "remaining_balance", "remainingBalance", "balance")
                    ?? GetNullableDecimal(data, "remaining_balance", "remainingBalance", "balance"),
                NewBalance = GetNullableDecimal(root, "new_balance", "newBalance")
                    ?? GetNullableDecimal(data, "new_balance", "newBalance"),
                GiftCard = card.ValueKind == JsonValueKind.Undefined ? null : ParseGiftCard(card, null, null),
                Receipt = ParseReceipt(root) ?? ParseReceipt(data)
            };
        }
        catch
        {
            return null;
        }
    }

    private static GiftCardLookupResponse MissingConfigurationLookupResponse()
    {
        return new GiftCardLookupResponse
        {
            Success = false,
            Error = "API not configured. Please check Cloud Settings."
        };
    }

    private static GiftCardTransactionResponse MissingConfigurationTransactionResponse()
    {
        return new GiftCardTransactionResponse
        {
            Success = false,
            Error = "API not configured. Please check Cloud Settings."
        };
    }

    private static GiftCardTransactionResponse TransactionBlockedByLookup(GiftCardLookupResponse lookup, string fallback)
    {
        return new GiftCardTransactionResponse
        {
            Success = false,
            Error = lookup.StatusMessage ?? fallback,
            GiftCard = lookup.GiftCard,
            CanQueueForRetry = lookup.CanQueueForRetry
        };
    }

    private static bool LookupCanProceed(GiftCardLookupResponse lookup)
    {
        return lookup.Till?.CanProceed ?? lookup.Success;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code == 408 || code == 429 || code >= 500;
    }

    private static string BuildStableTransactionId(string operation, string? explicitTransactionId, params object?[] fallbackParts)
    {
        if (!string.IsNullOrWhiteSpace(explicitTransactionId))
        {
            return explicitTransactionId.Trim();
        }

        return OrderWebApiClient.BuildIdempotencyKey(operation, fallbackParts);
    }

    private static string BuildRequiredIdempotencyKey(string operation, string? explicitTransactionId, params object?[] fallbackParts)
    {
        var stableTransactionId = BuildStableTransactionId(operation, explicitTransactionId, fallbackParts);
        var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey(operation, stableTransactionId);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOperationException(MissingIdempotencyError);
        }

        return idempotencyKey;
    }

    private static string NormalizeCardNumber(string? cardNumber)
    {
        return (cardNumber ?? string.Empty).Trim();
    }

    private static GiftCardLookupResponse? ParseGiftCardLookupResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<GiftCardLookupResponse>(content, JsonOptions);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var cardElement = FindGiftCardElement(root);
        var tillElement = TryGetProperty(root, out var till, "till") ? till : default;
        var found = GetBool(root, "found") ?? GetBool(cardElement, "found");
        var canUse = GetBool(root, "can_use", "canUse") ?? GetBool(cardElement, "can_use", "canUse");
        var isExpired = GetBool(root, "is_expired", "isExpired") ?? GetBool(cardElement, "is_expired", "isExpired");

        result ??= new GiftCardLookupResponse();
        result.Found ??= found;
        result.CanUse ??= canUse;
        result.IsExpired ??= isExpired;
        result.Error ??= GetString(root, "error");
        result.Message ??= GetString(root, "message");
        result.Till = MergeTill(result.Till, tillElement);

        if (result.GiftCard == null && cardElement.ValueKind != JsonValueKind.Undefined)
        {
            result.GiftCard = ParseGiftCard(cardElement, canUse, isExpired);
        }

        if (result.GiftCard != null)
        {
            result.GiftCard.CanUse ??= canUse;
            result.GiftCard.IsExpiredFlag ??= isExpired;
        }

        return result;
    }

    private static GiftCardLookupResponse? NormalizeLookupResult(GiftCardLookupResponse? result, string purpose)
    {
        if (result == null)
        {
            return null;
        }

        if (result.Till?.CanProceed is bool canProceed)
        {
            result.Success = canProceed;
            if (!canProceed)
            {
                result.Error = result.Till.StatusMessage
                    ?? result.Message
                    ?? result.Error
                    ?? "Gift card cannot proceed.";
            }

            return result;
        }

        if (result.Found == false)
        {
            result.Success = false;
            result.Error ??= result.StatusMessage ?? "Card not found";
            return result;
        }

        if (result.GiftCard == null)
        {
            result.Success = false;
            result.Error ??= result.StatusMessage ?? "Gift card details were not returned";
            return result;
        }

        if (purpose == GiftCardLookupPurpose.Redeem && !result.GiftCard.IsUsable)
        {
            result.Success = false;
            result.Error ??= result.GiftCard.IsExpired
                ? "Gift card is expired"
                : result.GiftCard.CanUse == false
                    ? "Gift card cannot be used"
                    : result.GiftCard.Balance <= 0
                        ? "Gift card has no remaining balance"
                        : "Gift card is not active";
            return result;
        }

        result.Success = result.Success || result.Found != false;
        return result;
    }

    private static GiftCardRedeemResponse? ParseGiftCardRedeemResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<GiftCardRedeemResponse>(content, JsonOptions);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var data = TryGetProperty(root, out var dataElement, "data", "result") ? dataElement : root;
        var redemption = TryGetProperty(data, out var redemptionElement, "redemption", "transaction", "gift_card", "giftCard", "card")
            ? redemptionElement
            : data;
        var card = FindGiftCardElement(root);

        result ??= new GiftCardRedeemResponse();
        result.Success = GetBool(root, "success") ?? result.Success;
        result.Error ??= GetString(root, "error") ?? GetString(data, "error");
        result.Message ??= GetString(root, "message") ?? GetString(data, "message");
        result.PreviousBalance ??= GetFirstNullableDecimal("previous_balance", "previousBalance");
        result.NewBalance ??= GetFirstNullableDecimal("new_balance", "newBalance");
        result.RemainingBalance ??= GetFirstNullableDecimal("remaining_balance", "remainingBalance", "balance");
        result.RedeemedAmount ??= GetFirstNullableDecimal("redeemed_amount", "redeemedAmount");
        result.AmountRedeemed ??= GetFirstNullableDecimal("amount_redeemed", "amountRedeemed", "amount");
        result.IsFullyRedeemed ??= GetFirstBool("is_fully_redeemed", "isFullyRedeemed");

        return result;

        decimal? GetFirstNullableDecimal(params string[] names)
        {
            return GetNullableDecimal(root, names)
                ?? GetNullableDecimal(data, names)
                ?? GetNullableDecimal(redemption, names)
                ?? GetNullableDecimal(card, names);
        }

        bool? GetFirstBool(params string[] names)
        {
            return GetBool(root, names)
                ?? GetBool(data, names)
                ?? GetBool(redemption, names)
                ?? GetBool(card, names);
        }
    }

    private static GiftCardTransactionResponse? ParseGiftCardTransactionResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        // Do not JsonSerializer.Deserialize the full DTO — OrderWeb receipt.lines may be
        // objects and that used to abort top-up/sell after the cloud write already succeeded.
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var data = TryGetProperty(root, out var dataElement, "data", "result") ? dataElement : root;
        var card = FindGiftCardElement(root);

        var result = new GiftCardTransactionResponse
        {
            Success = GetBool(root, "success") ?? GetBool(data, "success") ?? false,
            Error = GetString(root, "error") ?? GetString(data, "error"),
            Message = GetString(root, "message") ?? GetString(data, "message"),
            TransactionId = GetString(root, "transaction_id", "transactionId", "id")
                ?? GetString(data, "transaction_id", "transactionId", "id"),
            RemainingBalance = GetNullableDecimal(root, "remaining_balance", "remainingBalance", "balance")
                ?? GetNullableDecimal(data, "remaining_balance", "remainingBalance", "balance")
                ?? GetNullableDecimal(card, "remaining_balance", "remainingBalance", "balance"),
            NewBalance = GetNullableDecimal(root, "new_balance", "newBalance")
                ?? GetNullableDecimal(data, "new_balance", "newBalance"),
            GiftCard = card.ValueKind == JsonValueKind.Undefined ? null : ParseGiftCard(card, null, null),
            Receipt = ParseReceipt(root) ?? ParseReceipt(data)
        };

        if (!result.Success &&
            string.IsNullOrWhiteSpace(result.Error) &&
            (result.GiftCard != null || result.NewBalance.HasValue || result.RemainingBalance.HasValue || result.Receipt != null))
        {
            result.Success = true;
        }

        return result;
    }

    private static GiftCardReceipt? ParseReceipt(JsonElement element)
    {
        if (!TryGetProperty(element, out var receiptElement, "receipt") || receiptElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var receipt = new GiftCardReceipt();
        if (TryGetProperty(receiptElement, out var linesElement, "lines") && linesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesElement.EnumerateArray())
            {
                var text = line.ValueKind switch
                {
                    JsonValueKind.String => line.GetString(),
                    JsonValueKind.Number => line.ToString(),
                    JsonValueKind.Object =>
                        GetString(line, "text", "line", "content", "value", "message", "label")
                        ?? line.GetRawText(),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(text))
                {
                    receipt.Lines.Add(text);
                }
            }
        }

        return receipt;
    }

    private static GiftCardTillInstructions? MergeTill(GiftCardTillInstructions? resultTill, JsonElement tillElement)
    {
        if (resultTill == null && tillElement.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        resultTill ??= new GiftCardTillInstructions();
        resultTill.CanProceed ??= GetBool(tillElement, "can_proceed", "canProceed");
        resultTill.StatusMessage ??= GetString(tillElement, "status_message", "statusMessage", "message");

        if (resultTill.SuggestedAmounts.Count == 0)
        {
            resultTill.SuggestedAmounts = GetDecimalList(tillElement, "suggested_amounts", "suggestedAmounts");
        }

        return resultTill;
    }

    private static GiftCard? ParseGiftCard(JsonElement cardElement, bool? canUse, bool? isExpired)
    {
        if (cardElement.ValueKind == JsonValueKind.Undefined || cardElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new GiftCard
        {
            CardNumber = GetString(cardElement, "card_number", "cardNumber", "number", "code") ?? string.Empty,
            Balance = GetDecimal(cardElement, "balance", "remaining_balance", "remainingBalance", "amount"),
            Status = GetString(cardElement, "status", "state") ?? "active",
            CardType = GetString(cardElement, "card_type", "cardType", "type") ?? string.Empty,
            CreatedAt = GetDateTime(cardElement, "created_at", "createdAt"),
            ExpiryDate = GetDateTime(cardElement, "expiry_date", "expiryDate", "expires_at", "expiresAt"),
            CanUse = canUse ?? GetBool(cardElement, "can_use", "canUse"),
            IsExpiredFlag = isExpired ?? GetBool(cardElement, "is_expired", "isExpired")
        };
    }

    private static JsonElement FindGiftCardElement(JsonElement root)
    {
        if (TryGetProperty(root, out var cardElement, "gift_card", "giftCard", "card", "giftCardDetails"))
        {
            return cardElement;
        }

        if (TryGetProperty(root, out var dataElement, "data", "result")
            && TryGetProperty(dataElement, out cardElement, "gift_card", "giftCard", "card", "giftCardDetails"))
        {
            return cardElement;
        }

        return HasAnyProperty(root, "balance", "card_number", "cardNumber")
            ? root
            : default;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool HasAnyProperty(JsonElement element, params string[] names)
    {
        return names.Any(name => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _));
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static decimal GetDecimal(JsonElement element, params string[] names)
    {
        return GetNullableDecimal(element, names) ?? 0;
    }

    private static decimal? GetNullableDecimal(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static List<decimal> GetDecimalList(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind != JsonValueKind.Array)
        {
            return new List<decimal>();
        }

        var values = new List<decimal>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var number))
            {
                values.Add(number);
            }
            else if (item.ValueKind == JsonValueKind.String
                && decimal.TryParse(item.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                values.Add(parsed);
            }
        }

        return values;
    }

    private static bool? GetBool(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            _ => null
        };
    }

    private static DateTime? GetDateTime(JsonElement element, params string[] names)
    {
        var value = GetString(element, names);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static string ParseGiftCardError(string content, string fallback)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return fallback;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fallback}. OrderWeb returned a web page instead of API JSON. Please check the API base URL and gift card endpoint configuration.";
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            return GetString(root, "error", "message")
                ?? (TryGetProperty(root, out var till, "till")
                    ? GetString(till, "status_message", "statusMessage", "message")
                    : null)
                ?? fallback;
        }
        catch
        {
            return trimmed.Length > 180 ? $"{trimmed[..180]}..." : trimmed;
        }
    }
}
