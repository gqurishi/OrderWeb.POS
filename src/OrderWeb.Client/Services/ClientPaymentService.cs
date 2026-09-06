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
        string? correlationId = null)
    {
        var offline = new ClientOfflinePolicy(_cache);
        var motherOnline = await offline.IsMotherOnlineAsync();
        var policy = offline.Evaluate(
            string.Equals(method, "card", StringComparison.OrdinalIgnoreCase) ? ClientOperation.CardPayment : ClientOperation.SubmitFinalOrder,
            motherOnline);
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

        requestId ??= Guid.NewGuid().ToString("N");
        correlationId ??= Guid.NewGuid().ToString("N");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);

        try
        {
            using var response = await client.PostAsJsonAsync(
                $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/payments",
                new { requestId, orderId, method, amount, expectedOrderRevision, correlationId }, JsonOptions);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PaymentEnvelope>(body, JsonOptions);
            return new ClientPaymentResult(
                response.IsSuccessStatusCode && result?.Success == true,
                result?.Message ?? (response.IsSuccessStatusCode ? "Mother did not return a payment result." : "Mother could not approve the payment."),
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

    private sealed record PaymentEnvelope(bool Success, string? Message, PaymentState? Payment);
    private sealed record PaymentState(string? PaymentId, string? Status, decimal ConfirmedAmount, string? ProviderReference);
}

public sealed record ClientPaymentResult(bool Approved, string Message, string? Reference, bool IsUnknown = false);
