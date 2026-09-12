using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>Client → Mother loyalty API. No local points ledger; cloud balances via Mother only.</summary>
public sealed class MotherLoyaltyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    private readonly ClientOfflinePolicy _offlinePolicy;

    public MotherLoyaltyClient()
        : this(new ClientCacheService(), new ClientOfflinePolicy())
    {
    }

    public MotherLoyaltyClient(ClientCacheService cache, ClientOfflinePolicy offlinePolicy)
    {
        _cache = cache;
        _offlinePolicy = offlinePolicy;
    }

    public Task<ClientLoyaltyLookupResponseDto> SearchAsync(string lookup, CancellationToken cancellationToken = default) =>
        SendLookupAsync("/api/client/loyalty/search", lookup, cancellationToken);

    public Task<ClientLoyaltyLookupResponseDto> HistoryAsync(string lookup, CancellationToken cancellationToken = default) =>
        SendLookupAsync("/api/client/loyalty/history", lookup, cancellationToken);

    public async Task<ClientLoyaltyLookupResponseDto> CreateCustomerAsync(
        string phone,
        string name,
        string? email,
        CancellationToken cancellationToken = default)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return OfflineLookup(gate);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return AccessDeniedLookup("Sign in and pair with Mother POS before using loyalty.");
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/loyalty/customers",
                new ClientLoyaltyCreateRequestDto(phone, name, email, auth.Session.SessionToken),
                JsonOptions,
                cancellationToken);
            return await ReadLookupResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return OfflineLookup($"Could not reach Mother POS: {ex.Message}");
        }
    }

    public Task<ClientLoyaltyLookupResponseDto> AddPointsAsync(
        string lookup,
        int points,
        string? reason,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendMutateAsync("/api/client/loyalty/add", lookup, points, reason, idempotencyKey, cancellationToken);

    public Task<ClientLoyaltyLookupResponseDto> RedeemPointsAsync(
        string lookup,
        int points,
        string? reason,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendMutateAsync("/api/client/loyalty/redeem", lookup, points, reason, idempotencyKey, cancellationToken);

    /// <summary>
    /// Order Place earn — <c>POST /api/client/orders/loyalty-add</c>.
    /// Uses shared <see cref="ClientOrderLoyaltyAddRequestDto"/> / <see cref="ClientOrderLoyaltyAddResponseDto"/>.
    /// </summary>
    public async Task<ClientOrderLoyaltyAddResponseDto> AddPointsForOrderAsync(
        string orderId,
        string lookup,
        int? points = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return OfflineOrderAdd(gate);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return AccessDeniedOrderAdd("Sign in and pair with Mother POS before using loyalty.");
        }

        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(lookup))
        {
            return new ClientOrderLoyaltyAddResponseDto(
                Success: false,
                Message: "Order id and customer lookup are required.",
                Error: "Order id and customer lookup are required.",
                ErrorCode: LoyaltyErrorCodes.Validation);
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/orders/loyalty-add",
                new ClientOrderLoyaltyAddRequestDto(
                    OrderId: orderId.Trim(),
                    Lookup: lookup.Trim(),
                    Points: points,
                    IdempotencyKey: idempotencyKey,
                    SessionToken: auth.Session.SessionToken),
                JsonOptions,
                cancellationToken);
            return await ReadOrderAddResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return OfflineOrderAdd($"Could not reach Mother POS: {ex.Message}");
        }
    }

    public async Task<ClientLoyaltyTestResponseDto> TestConnectionAsync(
        string? lookup,
        CancellationToken cancellationToken = default)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return new ClientLoyaltyTestResponseDto(
                Success: false,
                Message: gate,
                Error: gate,
                ErrorCode: LoyaltyErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return new ClientLoyaltyTestResponseDto(
                Success: false,
                Message: "Sign in and pair with Mother POS before using loyalty.",
                Error: "Sign in and pair with Mother POS before using loyalty.",
                ErrorCode: LoyaltyErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/loyalty/test",
                new ClientLoyaltyLookupRequestDto(lookup, auth.Session.SessionToken),
                JsonOptions,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientLoyaltyTestResponseDto>(json, JsonOptions);
            if (dto != null)
            {
                return MapAccessDenied(response.StatusCode, dto with
                {
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                        ? MapHttpErrorCode(response.StatusCode, dto.Error ?? dto.Message)
                        : dto.ErrorCode
                });
            }

            return new ClientLoyaltyTestResponseDto(
                Success: false,
                Message: $"Mother POS loyalty test failed ({(int)response.StatusCode}).",
                Error: $"Mother POS loyalty test failed ({(int)response.StatusCode}).",
                ErrorCode: MapHttpErrorCode(response.StatusCode, null));
        }
        catch (Exception ex)
        {
            return new ClientLoyaltyTestResponseDto(
                Success: false,
                Message: $"Could not reach Mother POS: {ex.Message}",
                Error: $"Could not reach Mother POS: {ex.Message}",
                ErrorCode: LoyaltyErrorCodes.OfflineMother);
        }
    }

    private async Task<ClientLoyaltyLookupResponseDto> SendLookupAsync(
        string path,
        string lookup,
        CancellationToken cancellationToken)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return OfflineLookup(gate);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return AccessDeniedLookup("Sign in and pair with Mother POS before using loyalty.");
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}{path}",
                new ClientLoyaltyLookupRequestDto(lookup, auth.Session.SessionToken),
                JsonOptions,
                cancellationToken);
            return await ReadLookupResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return OfflineLookup($"Could not reach Mother POS: {ex.Message}");
        }
    }

    private async Task<ClientLoyaltyLookupResponseDto> SendMutateAsync(
        string path,
        string lookup,
        int points,
        string? reason,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return OfflineLookup(gate);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return AccessDeniedLookup("Sign in and pair with Mother POS before using loyalty.");
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}{path}",
                new ClientLoyaltyMutateRequestDto(lookup, points, reason, idempotencyKey, auth.Session.SessionToken),
                JsonOptions,
                cancellationToken);
            return await ReadLookupResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return OfflineLookup($"Could not reach Mother POS: {ex.Message}");
        }
    }

    private static async Task<ClientLoyaltyLookupResponseDto> ReadLookupResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<ClientLoyaltyLookupResponseDto>(json, JsonOptions);
        if (dto != null)
        {
            return MapAccessDenied(response.StatusCode, dto with
            {
                ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                    ? MapHttpErrorCode(response.StatusCode, dto.Error ?? dto.Message)
                    : dto.ErrorCode
            });
        }

        return new ClientLoyaltyLookupResponseDto(
            Success: false,
            Message: $"Mother POS loyalty request failed ({(int)response.StatusCode}).",
            Error: $"Mother POS loyalty request failed ({(int)response.StatusCode}).",
            ErrorCode: MapHttpErrorCode(response.StatusCode, null));
    }

    private static async Task<ClientOrderLoyaltyAddResponseDto> ReadOrderAddResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<ClientOrderLoyaltyAddResponseDto>(json, JsonOptions);
        if (dto != null)
        {
            var code = string.IsNullOrWhiteSpace(dto.ErrorCode)
                ? MapHttpErrorCode(response.StatusCode, dto.Error ?? dto.Message)
                : dto.ErrorCode;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                code = LoyaltyErrorCodes.AccessDenied;
            }

            return dto with
            {
                Success = response.IsSuccessStatusCode && dto.Success,
                ErrorCode = response.IsSuccessStatusCode ? null : code,
                Error = response.IsSuccessStatusCode ? null : (dto.Error ?? dto.Message)
            };
        }

        var fallback = $"Mother POS order loyalty-add failed ({(int)response.StatusCode}).";
        return new ClientOrderLoyaltyAddResponseDto(
            Success: false,
            Message: fallback,
            Error: fallback,
            ErrorCode: MapHttpErrorCode(response.StatusCode, fallback));
    }

    private async Task<string?> EnsureOnlineAsync()
    {
        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var decision = _offlinePolicy.Evaluate(ClientOperation.Loyalty, online);
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

    private static ClientLoyaltyLookupResponseDto OfflineLookup(string message) =>
        new(
            Success: false,
            Message: message,
            Error: message,
            ErrorCode: LoyaltyErrorCodes.OfflineMother);

    private static ClientOrderLoyaltyAddResponseDto OfflineOrderAdd(string message) =>
        new(
            Success: false,
            Message: message,
            Error: message,
            ErrorCode: LoyaltyErrorCodes.OfflineMother);

    private static ClientLoyaltyLookupResponseDto AccessDeniedLookup(string message) =>
        new(
            Success: false,
            Message: message,
            Error: message,
            ErrorCode: LoyaltyErrorCodes.AccessDenied);

    private static ClientOrderLoyaltyAddResponseDto AccessDeniedOrderAdd(string message) =>
        new(
            Success: false,
            Message: message,
            Error: message,
            ErrorCode: LoyaltyErrorCodes.AccessDenied);

    private static string MapHttpErrorCode(HttpStatusCode status, string? message)
    {
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return LoyaltyErrorCodes.AccessDenied;
        }

        if (status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            return LoyaltyErrorCodes.CloudDown;
        }

        if (status == HttpStatusCode.Conflict)
        {
            return LoyaltyErrorCodes.AlreadyEarned;
        }

        if (status == HttpStatusCode.NotFound)
        {
            return LoyaltyErrorCodes.CustomerNotFound;
        }

        var text = (message ?? string.Empty).ToLowerInvariant();
        if (text.Contains("queued") && text.Contains("retry"))
        {
            return LoyaltyErrorCodes.Queued;
        }

        if (text.Contains("already") && (text.Contains("added") || text.Contains("earn")))
        {
            return LoyaltyErrorCodes.AlreadyEarned;
        }

        if (text.Contains("not found") || text.Contains("no customer"))
        {
            return LoyaltyErrorCodes.CustomerNotFound;
        }

        if (text.Contains("insufficient") || text.Contains("not enough") || text.Contains("exceed"))
        {
            return LoyaltyErrorCodes.InsufficientPoints;
        }

        return LoyaltyErrorCodes.Unknown;
    }

    private static ClientLoyaltyLookupResponseDto MapAccessDenied(HttpStatusCode status, ClientLoyaltyLookupResponseDto dto) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? dto with { ErrorCode = LoyaltyErrorCodes.AccessDenied }
            : dto;

    private static ClientLoyaltyTestResponseDto MapAccessDenied(HttpStatusCode status, ClientLoyaltyTestResponseDto dto) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? dto with { ErrorCode = LoyaltyErrorCodes.AccessDenied }
            : dto;

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession Session);
}
