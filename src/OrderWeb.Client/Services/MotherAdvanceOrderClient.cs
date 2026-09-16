using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client → Mother advance-orders API. Online-only; Mother owns the clock and kitchen print.
/// WS <c>advance.reminder</c> is handled by the host (Phase 5) — this client is list + print.
/// </summary>
public sealed class MotherAdvanceOrderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    private readonly ClientOfflinePolicy _offlinePolicy;

    public MotherAdvanceOrderClient()
        : this(new ClientCacheService(), new ClientOfflinePolicy())
    {
    }

    public MotherAdvanceOrderClient(ClientCacheService cache, ClientOfflinePolicy offlinePolicy)
    {
        _cache = cache;
        _offlinePolicy = offlinePolicy;
    }

    /// <summary><c>GET /api/client/advance-orders?range=today|tomorrow|7d</c></summary>
    public async Task<AdvanceOrderListResponseDto> ListAsync(
        string? range = AdvanceOrderRanges.Today,
        CancellationToken cancellationToken = default)
    {
        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return FailList(gate, AdvanceOrderErrorCodes.OfflineMother, range);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return FailList(
                "Sign in and pair with Mother POS before viewing Advance Orders.",
                AdvanceOrderErrorCodes.AccessDenied,
                range);
        }

        var normalized = NormalizeRange(range);
        try
        {
            using var client = CreateClient(auth);
            var url =
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/advance-orders?range={Uri.EscapeDataString(normalized)}";
            using var response = await client.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<AdvanceOrderListResponseDto>(json, JsonOptions);
            if (dto != null)
            {
                return dto with
                {
                    Success = response.IsSuccessStatusCode && dto.Success,
                    Range = string.IsNullOrWhiteSpace(dto.Range) ? normalized : dto.Range,
                    Orders = dto.Orders ?? Array.Empty<AdvanceOrderDto>(),
                    ErrorCode = response.IsSuccessStatusCode
                        ? null
                        : (string.IsNullOrWhiteSpace(dto.ErrorCode)
                            ? MapHttpError(response.StatusCode)
                            : dto.ErrorCode)
                };
            }

            return FailList(
                $"Mother POS advance orders failed ({(int)response.StatusCode}).",
                MapHttpError(response.StatusCode),
                normalized);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailList($"Could not reach Mother POS: {ex.Message}", AdvanceOrderErrorCodes.OfflineMother, normalized);
        }
    }

    /// <summary><c>POST /api/client/advance-orders/{id}/print-kitchen</c></summary>
    public async Task<AdvanceOrderPrintResponseDto> PrintKitchenAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return new AdvanceOrderPrintResponseDto(
                false, "Order id is required.", ErrorCode: AdvanceOrderErrorCodes.Validation);
        }

        var gate = await EnsureOnlineAsync();
        if (gate != null)
        {
            return new AdvanceOrderPrintResponseDto(false, gate, orderId.Trim(), ErrorCode: AdvanceOrderErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return new AdvanceOrderPrintResponseDto(
                false,
                "Sign in and pair with Mother POS before printing Advance Orders.",
                orderId.Trim(),
                ErrorCode: AdvanceOrderErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            var url =
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/advance-orders/{Uri.EscapeDataString(orderId.Trim())}/print-kitchen";
            using var response = await client.PostAsync(url, content: null, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<AdvanceOrderPrintResponseDto>(json, JsonOptions);
            if (dto != null)
            {
                return dto with
                {
                    Success = response.IsSuccessStatusCode && dto.Success,
                    OrderId = string.IsNullOrWhiteSpace(dto.OrderId) ? orderId.Trim() : dto.OrderId,
                    ErrorCode = response.IsSuccessStatusCode
                        ? null
                        : (string.IsNullOrWhiteSpace(dto.ErrorCode)
                            ? MapHttpError(response.StatusCode)
                            : dto.ErrorCode)
                };
            }

            return new AdvanceOrderPrintResponseDto(
                false,
                $"Mother POS kitchen print failed ({(int)response.StatusCode}).",
                orderId.Trim(),
                ErrorCode: MapHttpError(response.StatusCode));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AdvanceOrderPrintResponseDto(
                false,
                $"Could not reach Mother POS: {ex.Message}",
                orderId.Trim(),
                ErrorCode: AdvanceOrderErrorCodes.OfflineMother);
        }
    }

    private async Task<string?> EnsureOnlineAsync()
    {
        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var decision = _offlinePolicy.Evaluate(ClientOperation.AdvanceOrders, online);
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

    private static string NormalizeRange(string? range)
    {
        var value = (range ?? AdvanceOrderRanges.Today).Trim().ToLowerInvariant();
        return value switch
        {
            AdvanceOrderRanges.Tomorrow or "tommorrow" or "tmr" => AdvanceOrderRanges.Tomorrow,
            AdvanceOrderRanges.Next7Days or "7" or "week" => AdvanceOrderRanges.Next7Days,
            _ => AdvanceOrderRanges.Today
        };
    }

    private static AdvanceOrderListResponseDto FailList(string message, string code, string? range) =>
        new(
            false,
            message,
            NormalizeRange(range),
            null,
            null,
            Array.Empty<AdvanceOrderDto>(),
            code);

    private static string MapHttpError(HttpStatusCode status) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? AdvanceOrderErrorCodes.AccessDenied
            : AdvanceOrderErrorCodes.Unknown;

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession Session);
}
