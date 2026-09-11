using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
        RedeemAsync(cardNumber, amount, orderId, description, idempotencyKey, balanceBefore: null, cancellationToken);

    public Task<ClientGiftCardRedeemResponseDto> RedeemAsync(
        string cardNumber,
        decimal amount,
        string? orderId,
        string? description,
        string idempotencyKey,
        decimal? balanceBefore,
        CancellationToken cancellationToken = default) =>
        SendRedeemAsync(cardNumber, amount, orderId, description, idempotencyKey, balanceBefore, cancellationToken);

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
            var (status, json) = await PostJsonAsync(
                auth,
                "/api/client/gift-cards/lookup",
                new ClientGiftCardLookupRequestDto(cardNumber, purpose, auth.Session.SessionToken),
                cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientGiftCardLookupResponseDto>(json, JsonOptions);
            if (dto != null)
            {
                return MapAccessDenied(status, dto with
                {
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                        ? MapHttpErrorCode(status, dto.Error ?? dto.Message)
                        : dto.ErrorCode
                });
            }

            return new ClientGiftCardLookupResponseDto(
                Success: false,
                Message: $"Mother POS gift-card lookup failed ({(int)status}).",
                Error: $"Mother POS gift-card lookup failed ({(int)status}).",
                ErrorCode: MapHttpErrorCode(status, null));
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
            var (status, json) = await PostJsonAsync(
                auth,
                path,
                body with { SessionToken = auth.Session.SessionToken },
                cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientGiftCardTransactionResponseDto>(json, JsonOptions);
            if (dto != null)
            {
                return MapAccessDenied(status, dto with
                {
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                        ? MapHttpErrorCode(status, dto.Error ?? dto.Message)
                        : dto.ErrorCode
                });
            }

            return new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: $"Mother POS gift-card request failed ({(int)status}).",
                Error: $"Mother POS gift-card request failed ({(int)status}).",
                ErrorCode: MapHttpErrorCode(status, null));
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
        decimal? balanceBefore,
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

        var body = new ClientGiftCardMutateRequestDto(
            cardNumber,
            amount,
            null,
            orderId,
            description,
            idempotencyKey,
            auth.Session.SessionToken);

        // Redeem = Mother lookup + cloud redeem; allow one transient retry (sticky idempotency key).
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var (status, json) = await PostJsonAsync(
                    auth,
                    "/api/client/gift-cards/redeem",
                    body,
                    cancellationToken);
                var dto = JsonSerializer.Deserialize<ClientGiftCardRedeemResponseDto>(json, JsonOptions);
                if (dto != null)
                {
                    return MapAccessDenied(status, dto with
                    {
                        ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                            ? MapHttpErrorCode(status, dto.Error ?? dto.Message)
                            : dto.ErrorCode
                    });
                }

                return new ClientGiftCardRedeemResponseDto(
                    Success: false,
                    Message: $"Mother POS gift-card redeem failed ({(int)status}).",
                    Error: $"Mother POS gift-card redeem failed ({(int)status}).",
                    ErrorCode: MapHttpErrorCode(status, null));
            }
            catch (Exception ex) when (attempt == 0 && IsTransientTransportError(ex))
            {
                lastError = ex;
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        // Mother often finishes redeem on cloud before the success JSON reaches Client.
        // Confirm via lookup so we do not show a false "could not reach Mother" error.
        var confirmed = await TryConfirmRedeemAfterTransportLossAsync(
            cardNumber,
            amount,
            balanceBefore,
            cancellationToken);
        if (confirmed != null)
        {
            return confirmed;
        }

        var message =
            "Could not confirm the redeem reply from Mother. Check balance before trying again — "
            + "if the balance already dropped, the redeem worked."
            + (lastError is null ? string.Empty : $" ({lastError.Message})");
        return new ClientGiftCardRedeemResponseDto(
            Success: false,
            Message: message,
            Error: message,
            ErrorCode: GiftCardErrorCodes.OfflineMother);
    }

    /// <summary>
    /// After a lost HTTP reply, re-check cloud balance via Mother.
    /// If the balance already dropped by this redeem amount, treat as success.
    /// </summary>
    private async Task<ClientGiftCardRedeemResponseDto?> TryConfirmRedeemAfterTransportLossAsync(
        string cardNumber,
        decimal amount,
        decimal? balanceBefore,
        CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await SendLookupAsync(cardNumber, GiftCardLookupPurposes.Redeem, cancellationToken);
            if (!lookup.Success || lookup.GiftCard is null)
            {
                return null;
            }

            var remaining = lookup.GiftCard.Balance;
            if (balanceBefore is { } before)
            {
                var expected = before - amount;
                var dropped = before - remaining;
                // Allow 1p rounding; require a real drop matching this redeem.
                if (dropped >= amount - 0.01m && remaining <= expected + 0.01m)
                {
                    return new ClientGiftCardRedeemResponseDto(
                        Success: true,
                        Message: $"Redeemed GBP {amount:F2}. Remaining GBP {remaining:F2}. (Confirmed after a lost Mother reply.)",
                        AmountRedeemed: Math.Min(amount, dropped),
                        RemainingBalance: remaining,
                        PreviousBalance: before,
                        IsFullyRedeemed: remaining <= 0.009m);
                }

                return null;
            }

            // No prior balance on Client — still surface current balance instead of a hard fail.
            return null;
        }
        catch
        {
            return null;
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

    private static async Task<(HttpStatusCode Status, string Json)> PostJsonAsync(
        MotherClientAuth auth,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(auth);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}{path}")
        {
            // Buffer JSON ourselves — avoids Expect:100-continue / stream-copy failures on WinUI.
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.StatusCode, json);
    }

    private static HttpClient CreateClient(MotherClientAuth auth)
    {
        // Redeem waits on Mother → OrderWeb (lookup + redeem). Keep headroom above Mother's cloud timeout.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.ExpectContinue = false;
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", auth.Settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", auth.Settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", auth.Session.SessionToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static bool IsTransientTransportError(Exception ex)
    {
        if (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return true;
        }

        var text = ex.Message ?? string.Empty;
        return text.Contains("copying content", StringComparison.OrdinalIgnoreCase)
               || text.Contains("transport connection", StringComparison.OrdinalIgnoreCase)
               || text.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase);
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
