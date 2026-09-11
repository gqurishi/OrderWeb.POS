using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

/// <summary>Client-side proxy: it never decides that a payment succeeded.</summary>
public sealed class ClientPaymentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    private readonly MotherCompatibilityClient _compatibility;

    public ClientPaymentService() : this(new ClientCacheService()) { }

    public ClientPaymentService(ClientCacheService cache)
    {
        _cache = cache;
        _compatibility = new MotherCompatibilityClient(cache);
    }

    public async Task<ClientPaymentResult> TakePaymentAsync(
        string orderId,
        string method,
        decimal amount,
        string? requestId = null,
        long? expectedOrderRevision = null,
        string? correlationId = null,
        string? giftCardNumber = null,
        string? giftCardIdempotencyKey = null,
        string? loyaltyLookup = null,
        int? loyaltyPoints = null,
        string? loyaltyIdempotencyKey = null,
        decimal tipAmount = 0m,
        decimal tipTotal = 0m)
    {
        var normalizedMethod = NormalizeMethod(method);
        if (amount <= 0m)
        {
            return new ClientPaymentResult(false, "Enter an amount greater than zero. No payment was submitted.", null, IsUnknown: false);
        }

        var offline = new ClientOfflinePolicy(_cache);
        var motherOnline = await offline.IsMotherOnlineAsync();
        var operation = normalizedMethod switch
        {
            "card" => ClientOperation.CardPayment,
            "gift_card" => ClientOperation.GiftCard,
            "loyalty" => ClientOperation.Loyalty,
            _ => ClientOperation.SubmitFinalOrder
        };
        var policy = offline.Evaluate(operation, motherOnline);
        if (!policy.Allowed)
        {
            return new ClientPaymentResult(false, policy.Message, null, IsUnknown: false);
        }

        var compatibility = await _compatibility.CheckAsync();
        if (compatibility.Status == OrderWeb.Contracts.Compatibility.ClientCompatibilityStatus.UpdateRequired)
        {
            return new ClientPaymentResult(false, compatibility.Message ?? "Update is required before taking payments.", null);
        }
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings == null || session == null || string.IsNullOrWhiteSpace(orderId))
        {
            return new ClientPaymentResult(false, "Payment requires a paired Mother POS, active staff session, and Mother order.", null);
        }

        if (normalizedMethod == "gift_card" && string.IsNullOrWhiteSpace(giftCardNumber))
        {
            return new ClientPaymentResult(false, "A gift card number is required for gift card payment.", null);
        }

        if (normalizedMethod == "loyalty" &&
            (string.IsNullOrWhiteSpace(loyaltyLookup) || loyaltyPoints is null or <= 0))
        {
            return new ClientPaymentResult(false, "A loyalty customer lookup and positive points amount are required.", null);
        }

        requestId ??= Guid.NewGuid().ToString("N");
        correlationId ??= Guid.NewGuid().ToString("N");
        giftCardIdempotencyKey ??= normalizedMethod == "gift_card"
            ? $"gift-card:{orderId}:{requestId}:{amount:F2}"
            : null;
        loyaltyIdempotencyKey ??= normalizedMethod == "loyalty"
            ? $"loyalty:{orderId}:{loyaltyLookup}:{loyaltyPoints}"
            : null;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);

        try
        {
            using var response = await client.PostAsJsonAsync(
                $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/payments",
                new
                {
                    requestId,
                    orderId,
                    method = normalizedMethod,
                    amount,
                    expectedOrderRevision,
                    correlationId,
                    giftCardNumber,
                    giftCardIdempotencyKey,
                    loyaltyLookup,
                    loyaltyPoints,
                    loyaltyIdempotencyKey,
                    tipAmount = Math.Max(0m, tipAmount),
                    tipTotal = Math.Max(Math.Max(0m, tipAmount), Math.Max(0m, tipTotal)),
                    sessionToken = session.SessionToken
                },
                JsonOptions);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PaymentEnvelope>(body, JsonOptions);
            var message = result?.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = response.IsSuccessStatusCode && result?.Success == true
                    ? "Payment approved by Mother."
                    : (response.IsSuccessStatusCode
                        ? "Mother did not return a payment result."
                        : "Mother could not approve the payment.");
            }
            if (result?.Payment?.GiftCardNumberMasked is { Length: > 0 } masked)
            {
                message = $"{message} Card {masked}";
                if (result.Payment.GiftCardRemainingBalance is decimal remaining)
                {
                    message = $"{message}, remaining £{remaining:F2}.";
                }
            }

            if (result?.Payment?.LoyaltyPointsRedeemed is int pts)
            {
                var name = result.Payment.LoyaltyCustomerName;
                message = string.IsNullOrWhiteSpace(name)
                    ? $"{message} Redeemed {pts:N0} pts"
                    : $"{message} Redeemed {pts:N0} pts for {name}";
                if (result.Payment.LoyaltyPointsRemaining is int left)
                {
                    message = $"{message}, remaining {left:N0} pts.";
                }
            }

            return new ClientPaymentResult(
                response.IsSuccessStatusCode && result?.Success == true,
                message,
                result?.Payment?.ProviderReference,
                IsUnknown: false);
        }
        catch (HttpRequestException)
        {
            return new ClientPaymentResult(false, "Mother POS could not be reached. Payment status is unknown; check Mother before retrying.", null, IsUnknown: true);
        }
        catch (TaskCanceledException)
        {
            return new ClientPaymentResult(false, "Mother POS did not respond. Payment status is unknown; check Mother before retrying.", null, IsUnknown: true);
        }
    }

    public async Task<ClientPaymentResult> GetPaymentStatusAsync(string requestId)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings == null || session == null || string.IsNullOrWhiteSpace(requestId))
            return new ClientPaymentResult(false, "Payment status requires a paired Mother POS and active session.", null, IsUnknown: true);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
        try
        {
            using var response = await client.GetAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/payments/{Uri.EscapeDataString(requestId)}");
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PaymentEnvelope>(body, JsonOptions);
            return new ClientPaymentResult(
                response.IsSuccessStatusCode && result?.Success == true && string.Equals(result.Payment?.Status, "approved", StringComparison.OrdinalIgnoreCase),
                result?.Message ?? (response.IsSuccessStatusCode ? "Mother returned a non-final payment result." : "Mother has no final payment result yet."),
                result?.Payment?.ProviderReference,
                IsUnknown: !response.IsSuccessStatusCode || result?.Payment == null);
        }
        catch (HttpRequestException) { return new ClientPaymentResult(false, "Mother could not be reached to check payment status.", null, IsUnknown: true); }
        catch (TaskCanceledException) { return new ClientPaymentResult(false, "Mother did not respond to the payment status check.", null, IsUnknown: true); }
    }

    private static string NormalizeMethod(string? method)
    {
        var value = (method ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        return value switch
        {
            "gift" or "giftcard" or "gift_card" => "gift_card",
            "loyalty" or "points" or "loyalty_points" or "customer_points" => "loyalty",
            "cash" => "cash",
            "card" => "card",
            "split" => "split",
            _ => value
        };
    }

    private sealed record PaymentEnvelope(bool Success, string? Message, PaymentState? Payment);
    private sealed record PaymentState(
        string? PaymentId,
        string? Status,
        decimal ConfirmedAmount,
        string? ProviderReference,
        string? GiftCardNumberMasked = null,
        decimal? GiftCardRemainingBalance = null,
        string? LoyaltyCustomerName = null,
        int? LoyaltyPointsRedeemed = null,
        int? LoyaltyPointsRemaining = null);
}

public sealed record ClientPaymentResult(bool Approved, string Message, string? Reference, bool IsUnknown = false);
