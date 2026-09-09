using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>Client → Mother gift-card API. No local gift-card ledger; cloud balances via Mother only.</summary>
public sealed class MotherGiftCardClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    private readonly ClientOfflinePolicy _offlinePolicy;

    public MotherGiftCardClient()
        : this(new ClientCacheService(), new ClientOfflinePolicy())
    {
    }

    public MotherGiftCardClient(ClientCacheService cache, ClientOfflinePolicy offlinePolicy)
    {
        _cache = cache;
        _offlinePolicy = offlinePolicy;
    }

    public Task<ClientGiftCardLookupResponseDto> LookupAsync(string cardNumber, string purpose, CancellationToken cancellationToken = default) =>
        SendLookupAsync(cardNumber, purpose, cancellationToken);

    public Task<ClientGiftCardTransactionResponseDto> SellAsync(
        string? cardNumber,
        decimal amount,
        string paymentMethod,
        string? orderId,
        string? description,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendMutateAsync(
            "/api/client/gift-cards/sell",
            new ClientGiftCardMutateRequestDto(cardNumber, amount, paymentMethod, orderId, description, idempotencyKey),
            cancellationToken);

    public Task<ClientGiftCardTransactionResponseDto> TopUpAsync(
        string cardNumber,
        decimal amount,
        string paymentMethod,
        string? orderId,
        string? description,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendMutateAsync(
            "/api/client/gift-cards/top-up",
            new ClientGiftCardMutateRequestDto(cardNumber, amount, paymentMethod, orderId, description, idempotencyKey),
            cancellationToken);

    public Task<ClientGiftCardRedeemResponseDto> RedeemAsync(
        string cardNumber,
        decimal amount,
        string? orderId,
        string? description,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendRedeemAsync(cardNumber, amount, orderId, description, idempotencyKey, cancellationToken);

    public Task<ClientGiftCardTransactionResponseDto> ActivateAsync(
        string cardNumber,
        decimal amount,
        string paymentMethod,
        string? orderId,
        string? description,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendMutateAsync(
            "/api/client/gift-cards/activate",
            new ClientGiftCardMutateRequestDto(cardNumber, amount, paymentMethod, orderId, description, idempotencyKey),
            cancellationToken);

    private async Task<ClientGiftCardLookupResponseDto> SendLookupAsync(
        string cardNumber,
        string purpose,
        CancellationToken cancellationToken)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return new ClientGiftCardLookupResponseDto(
                Success: false,
                Message: gate,
                Error: gate,
                ErrorCode: GiftCardErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return new ClientGiftCardLookupResponseDto(
                Success: false,
                Message: "Sign in and pair with Mother POS before using gift cards.",
                Error: "Sign in and pair with Mother POS before using gift cards.",
                ErrorCode: GiftCardErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/gift-cards/lookup",
                new ClientGiftCardLookupRequestDto(cardNumber, purpose, auth.Session.SessionToken),
                JsonOptions,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientGiftCardLookupResponseDto>(json, JsonOptions);
            if (dto != null)
            {
                return MapAccessDenied(response.StatusCode, dto with
                {
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                        ? MapHttpErrorCode(response.StatusCode, dto.Error ?? dto.Message)
                        : dto.ErrorCode
                });
            }

            return new ClientGiftCardLookupResponseDto(
                Success: false,
                Message: $"Mother POS gift-card lookup failed ({(int)response.StatusCode}).",
                Error: $"Mother POS gift-card lookup failed ({(int)response.StatusCode}).",
                ErrorCode: MapHttpErrorCode(response.StatusCode, null));
        }
        catch (Exception ex)
        {
            return new ClientGiftCardLookupResponseDto(
                Success: false,
                Message: $"Could not reach Mother POS: {ex.Message}",
                Error: $"Could not reach Mother POS: {ex.Message}",
                ErrorCode: GiftCardErrorCodes.OfflineMother);
        }
    }

    private async Task<ClientGiftCardTransactionResponseDto> SendMutateAsync(
        string path,
        ClientGiftCardMutateRequestDto body,
        CancellationToken cancellationToken)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: gate,
                Error: gate,
                ErrorCode: GiftCardErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "Sign in and pair with Mother POS before using gift cards.",
                Error: "Sign in and pair with Mother POS before using gift cards.",
                ErrorCode: GiftCardErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}{path}",
                body with { SessionToken = auth.Session.SessionToken },
                JsonOptions,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientGiftCardTransactionResponseDto>(json, JsonOptions);
            if (dto != null)
            {
                return MapAccessDenied(response.StatusCode, dto with
                {
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                        ? MapHttpErrorCode(response.StatusCode, dto.Error ?? dto.Message)
                        : dto.ErrorCode
                });
            }

            return new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: $"Mother POS gift-card request failed ({(int)response.StatusCode}).",
                Error: $"Mother POS gift-card request failed ({(int)response.StatusCode}).",
                ErrorCode: MapHttpErrorCode(response.StatusCode, null));
        }
        catch (Exception ex)
        {
            return new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: $"Could not reach Mother POS: {ex.Message}",
                Error: $"Could not reach Mother POS: {ex.Message}",
                ErrorCode: GiftCardErrorCodes.OfflineMother);
        }
    }

    private async Task<ClientGiftCardRedeemResponseDto> SendRedeemAsync(
        string cardNumber,
        decimal amount,
        string? orderId,
        string? description,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return new ClientGiftCardRedeemResponseDto(
                Success: false,
                Message: gate,
                Error: gate,
                ErrorCode: GiftCardErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return new ClientGiftCardRedeemResponseDto(
                Success: false,
                Message: "Sign in and pair with Mother POS before using gift cards.",
                Error: "Sign in and pair with Mother POS before using gift cards.",
                ErrorCode: GiftCardErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/gift-cards/redeem",
                new ClientGiftCardMutateRequestDto(cardNumber, amount, null, orderId, description, idempotencyKey, auth.Session.SessionToken),
                JsonOptions,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientGiftCardRedeemResponseDto>(json, JsonOptions);
            if (dto != null)
            {
                return MapAccessDenied(response.StatusCode, dto with
                {
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                        ? MapHttpErrorCode(response.StatusCode, dto.Error ?? dto.Message)
                        : dto.ErrorCode
                });
            }

            return new ClientGiftCardRedeemResponseDto(
                Success: false,
                Message: $"Mother POS gift-card redeem failed ({(int)response.StatusCode}).",
                Error: $"Mother POS gift-card redeem failed ({(int)response.StatusCode}).",
                ErrorCode: MapHttpErrorCode(response.StatusCode, null));
        }
        catch (Exception ex)
        {
            return new ClientGiftCardRedeemResponseDto(
                Success: false,
                Message: $"Could not reach Mother POS: {ex.Message}",
                Error: $"Could not reach Mother POS: {ex.Message}",
                ErrorCode: GiftCardErrorCodes.OfflineMother);
        }
    }

    private async Task<string?> EnsureOnlineAsync()
    {
        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var decision = _offlinePolicy.Evaluate(ClientOperation.GiftCard, online);
        return decision.Allowed ? null : decision.Message;
    }

    private async Task<MotherClientAuth?> GetAuthAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken) ||
            string.IsNullOrWhiteSpace(session?.SessionToken))
        {
            return null;
        }

        return new MotherClientAuth(settings, session);
    }

    private static HttpClient CreateClient(MotherClientAuth auth)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", auth.Settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", auth.Settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", auth.Session.SessionToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string MapHttpErrorCode(HttpStatusCode status, string? message)
    {
        if (status == HttpStatusCode.Forbidden || status == HttpStatusCode.Unauthorized)
        {
            return GiftCardErrorCodes.AccessDenied;
        }

        if (status == HttpStatusCode.ServiceUnavailable || status == HttpStatusCode.GatewayTimeout)
        {
            return GiftCardErrorCodes.CloudDown;
        }

        var text = (message ?? string.Empty).ToLowerInvariant();
        if (text.Contains("insufficient") || text.Contains("exceeds"))
        {
            return GiftCardErrorCodes.InsufficientBalance;
        }

        if (text.Contains("block") || text.Contains("expired"))
        {
            return GiftCardErrorCodes.BlockedCard;
        }

        return GiftCardErrorCodes.Unknown;
    }

    private static ClientGiftCardLookupResponseDto MapAccessDenied(HttpStatusCode status, ClientGiftCardLookupResponseDto dto) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? dto with { ErrorCode = GiftCardErrorCodes.AccessDenied }
            : dto;

    private static ClientGiftCardTransactionResponseDto MapAccessDenied(HttpStatusCode status, ClientGiftCardTransactionResponseDto dto) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? dto with { ErrorCode = GiftCardErrorCodes.AccessDenied }
            : dto;

    private static ClientGiftCardRedeemResponseDto MapAccessDenied(HttpStatusCode status, ClientGiftCardRedeemResponseDto dto) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? dto with { ErrorCode = GiftCardErrorCodes.AccessDenied }
            : dto;

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession Session);
}
