using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>Client → Mother order-history API. Online fetch; optional day snapshot via <see cref="ClientCacheService"/>.</summary>
public sealed class MotherOrderHistoryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    private readonly ClientOfflinePolicy _offlinePolicy;

    public MotherOrderHistoryClient()
        : this(new ClientCacheService(), new ClientOfflinePolicy())
    {
    }

    public MotherOrderHistoryClient(ClientCacheService cache, ClientOfflinePolicy offlinePolicy)
    {
        _cache = cache;
        _offlinePolicy = offlinePolicy;
    }

    public async Task<ClientOrderHistoryResponseDto> SearchAsync(
        DateTime? date,
        string? orderType,
        string? search,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var historyDate = (date ?? DateTime.Today).Date;
        var type = string.IsNullOrWhiteSpace(orderType) ? "ALL" : orderType.Trim().ToUpperInvariant();
        var searchText = (search ?? string.Empty).Trim();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, 100);

        var online = await _offlinePolicy.IsMotherOnlineAsync(cancellationToken);
        var liveGate = _offlinePolicy.Evaluate(ClientOperation.OrderHistory, online);
        if (!liveGate.Allowed)
        {
            return await TryCachedOrOfflineAsync(historyDate, type, searchText, page, liveGate.Message);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return AccessDenied("Sign in and pair with Mother POS before viewing order history.");
        }

        try
        {
            using var client = CreateClient(auth);
            var url = BuildUrl(auth.Settings.ApiBaseUrl, historyDate, type, searchText, page, pageSize);
            using var response = await client.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientOrderHistoryResponseDto>(json, JsonOptions)
                      ?? Failed($"Mother POS order history failed ({(int)response.StatusCode}).", MapHttpErrorCode(response.StatusCode));

            if (!response.IsSuccessStatusCode || !dto.Success)
            {
                return MapAccessDenied(response.StatusCode, dto with
                {
                    Success = false,
                    Error = dto.Error ?? dto.Message ?? $"Mother POS order history failed ({(int)response.StatusCode}).",
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                        ? MapHttpErrorCode(response.StatusCode)
                        : dto.ErrorCode,
                    Completed = dto.Completed ?? Array.Empty<ClientOrderHistoryItemDto>(),
                    Voided = dto.Voided ?? Array.Empty<ClientOrderHistoryItemDto>()
                });
            }

            var success = dto with
            {
                Success = true,
                FromCache = false,
                Completed = dto.Completed ?? Array.Empty<ClientOrderHistoryItemDto>(),
                Voided = dto.Voided ?? Array.Empty<ClientOrderHistoryItemDto>(),
                Page = dto.Page > 0 ? dto.Page : page,
                PageSize = dto.PageSize > 0 ? dto.PageSize : pageSize
            };

            if (page == 1 && string.IsNullOrWhiteSpace(searchText))
            {
                try
                {
                    await _cache.SaveOrderHistoryDaySnapshotAsync(historyDate, type, success);
                }
                catch
                {
                    // Snapshot is best-effort; live result still succeeds.
                }
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await TryCachedOrOfflineAsync(
                historyDate,
                type,
                searchText,
                page,
                $"Could not reach Mother POS: {ex.Message}");
        }
    }

    private async Task<ClientOrderHistoryResponseDto> TryCachedOrOfflineAsync(
        DateTime historyDate,
        string orderType,
        string search,
        int page,
        string offlineMessage)
    {
        var cacheDecision = _offlinePolicy.Evaluate(ClientOperation.ViewCachedOrderHistory, motherOnline: false);
        if (cacheDecision.Allowed &&
            page == 1 &&
            string.IsNullOrWhiteSpace(search))
        {
            var snapshot = await _cache.GetOrderHistoryDaySnapshotAsync(historyDate, orderType);
            if (snapshot != null)
            {
                return snapshot;
            }
        }

        return Failed(offlineMessage, OrderHistoryErrorCodes.OfflineMother);
    }

    private static string BuildUrl(
        string apiBaseUrl,
        DateTime date,
        string orderType,
        string search,
        int page,
        int pageSize)
    {
        var query = new StringBuilder();
        query.Append("date=").Append(Uri.EscapeDataString(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        query.Append("&orderType=").Append(Uri.EscapeDataString(orderType));
        query.Append("&page=").Append(page.ToString(CultureInfo.InvariantCulture));
        query.Append("&pageSize=").Append(pageSize.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Append("&search=").Append(Uri.EscapeDataString(search));
        }

        return $"{apiBaseUrl.TrimEnd('/')}/api/client/order-history?{query}";
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

    public async Task<ClientOrderHistoryDetailResponseDto> GetDetailAsync(
        string? orderId,
        int? databaseId = null,
        CancellationToken cancellationToken = default)
    {
        var key = (orderId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key) && databaseId is > 0)
        {
            key = databaseId.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return DetailFailed("An order id is required.", OrderHistoryErrorCodes.Validation);
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync(cancellationToken);
        var liveGate = _offlinePolicy.Evaluate(ClientOperation.OrderHistory, online);
        if (!liveGate.Allowed)
        {
            return DetailFailed(liveGate.Message, OrderHistoryErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return DetailFailed(
                "Sign in and pair with Mother POS before viewing order details.",
                OrderHistoryErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            var url = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/order-history/{Uri.EscapeDataString(key)}";
            using var response = await client.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientOrderHistoryDetailResponseDto>(json, JsonOptions)
                      ?? DetailFailed(
                          $"Mother POS order detail failed ({(int)response.StatusCode}).",
                          MapHttpErrorCode(response.StatusCode));

            if (!response.IsSuccessStatusCode || !dto.Success || dto.Order is null)
            {
                var mapped = dto with
                {
                    Success = false,
                    Error = dto.Error ?? dto.Message ?? $"Mother POS order detail failed ({(int)response.StatusCode}).",
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode)
                        ? (response.StatusCode == HttpStatusCode.NotFound
                            ? OrderHistoryErrorCodes.NotFound
                            : MapHttpErrorCode(response.StatusCode))
                        : dto.ErrorCode
                };
                return response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                    ? mapped with { ErrorCode = OrderHistoryErrorCodes.AccessDenied }
                    : mapped;
            }

            return dto with { Success = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DetailFailed($"Could not reach Mother POS: {ex.Message}", OrderHistoryErrorCodes.OfflineMother);
        }
    }

    private static ClientOrderHistoryResponseDto Failed(string message, string errorCode) =>
        new(
            Success: false,
            Message: message,
            Error: message,
            ErrorCode: errorCode,
            Completed: Array.Empty<ClientOrderHistoryItemDto>(),
            Voided: Array.Empty<ClientOrderHistoryItemDto>());

    private static ClientOrderHistoryDetailResponseDto DetailFailed(string message, string errorCode) =>
        new(
            Success: false,
            Message: message,
            Error: message,
            ErrorCode: errorCode);

    private static ClientOrderHistoryResponseDto AccessDenied(string message) =>
        Failed(message, OrderHistoryErrorCodes.AccessDenied);

    private static ClientOrderHistoryResponseDto MapAccessDenied(HttpStatusCode status, ClientOrderHistoryResponseDto dto) =>
        status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? dto with { ErrorCode = OrderHistoryErrorCodes.AccessDenied }
            : dto;

    private static string MapHttpErrorCode(HttpStatusCode status) =>
        status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OrderHistoryErrorCodes.AccessDenied,
            _ => OrderHistoryErrorCodes.Unknown
        };

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession Session);
}
