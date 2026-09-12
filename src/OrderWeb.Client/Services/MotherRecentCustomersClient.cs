using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>Client → Mother Recent Customers (7-day cache) API. Online-only.</summary>
public sealed class MotherRecentCustomersClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    private readonly ClientOfflinePolicy _offlinePolicy;

    public MotherRecentCustomersClient()
        : this(new ClientCacheService(), new ClientOfflinePolicy())
    {
    }

    public MotherRecentCustomersClient(ClientCacheService cache, ClientOfflinePolicy offlinePolicy)
    {
        _cache = cache;
        _offlinePolicy = offlinePolicy;
    }

    public async Task<ClientRecentCustomersResponseDto> GetRecentAsync(
        string? search,
        string? orderType,
        CancellationToken cancellationToken = default)
    {
        var online = await _offlinePolicy.IsMotherOnlineAsync(cancellationToken);
        var gate = _offlinePolicy.Evaluate(ClientOperation.RecentCustomers, online);
        if (!gate.Allowed)
        {
            return Fail(gate.Message, RecentCustomersErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return Fail("Sign in and pair with Mother POS before viewing recent customers.", RecentCustomersErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            var endpoint = BuildUrl(auth.Settings.ApiBaseUrl, "/api/client/customers/recent", new Dictionary<string, string?>
            {
                ["search"] = search,
                ["orderType"] = string.IsNullOrWhiteSpace(orderType) ? "all" : orderType
            });
            using var response = await client.GetAsync(endpoint, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientRecentCustomersResponseDto>(json, JsonOptions)
                      ?? Fail($"Mother POS recent customers failed ({(int)response.StatusCode}).", MapHttp(response.StatusCode));

            if (!response.IsSuccessStatusCode || !dto.Success)
            {
                return dto with
                {
                    Success = false,
                    Error = dto.Error ?? dto.Message ?? $"Mother POS recent customers failed ({(int)response.StatusCode}).",
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode) ? MapHttp(response.StatusCode) : dto.ErrorCode,
                    Customers = dto.Customers ?? Array.Empty<ClientRecentCustomerItemDto>()
                };
            }

            return dto with
            {
                Success = true,
                Customers = dto.Customers ?? Array.Empty<ClientRecentCustomerItemDto>(),
                Summary = dto.Summary ?? new ClientRecentCustomersSummaryDto()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail($"Could not reach Mother POS: {ex.Message}", RecentCustomersErrorCodes.OfflineMother);
        }
    }

    public async Task<ClientRecentCustomerMutateResponseDto> RetrySyncAsync(CancellationToken cancellationToken = default)
    {
        var online = await _offlinePolicy.IsMotherOnlineAsync(cancellationToken);
        var gate = _offlinePolicy.Evaluate(ClientOperation.RecentCustomers, online);
        if (!gate.Allowed)
        {
            return MutateFail(gate.Message, RecentCustomersErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return MutateFail("Sign in and pair with Mother POS before retrying sync.", RecentCustomersErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            var endpoint = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/customers/retry-sync";
            using var response = await client.PostAsJsonAsync(
                endpoint,
                new ClientRecentCustomerMutateRequestDto(auth.Session?.SessionToken),
                JsonOptions,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientRecentCustomerMutateResponseDto>(json, JsonOptions)
                      ?? MutateFail($"Mother POS sync retry failed ({(int)response.StatusCode}).", MapHttp(response.StatusCode));

            if (!response.IsSuccessStatusCode || !dto.Success)
            {
                return dto with
                {
                    Success = false,
                    Error = dto.Error ?? dto.Message ?? $"Mother POS sync retry failed ({(int)response.StatusCode}).",
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode) ? MapHttp(response.StatusCode) : dto.ErrorCode
                };
            }

            return dto;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MutateFail($"Could not reach Mother POS: {ex.Message}", RecentCustomersErrorCodes.OfflineMother);
        }
    }

    public async Task<ClientRecentCustomerMutateResponseDto> DeleteCacheAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return MutateFail("A valid customer cache id is required.", RecentCustomersErrorCodes.Validation);
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync(cancellationToken);
        var gate = _offlinePolicy.Evaluate(ClientOperation.RecentCustomers, online);
        if (!gate.Allowed)
        {
            return MutateFail(gate.Message, RecentCustomersErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return MutateFail("Sign in and pair with Mother POS before removing cache.", RecentCustomersErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            var endpoint = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/customers/delete-cache";
            using var response = await client.PostAsJsonAsync(
                endpoint,
                new ClientRecentCustomerDeleteCacheRequestDto(id, auth.Session?.SessionToken),
                JsonOptions,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<ClientRecentCustomerMutateResponseDto>(json, JsonOptions)
                      ?? MutateFail($"Mother POS delete cache failed ({(int)response.StatusCode}).", MapHttp(response.StatusCode));

            if (!response.IsSuccessStatusCode || !dto.Success)
            {
                return dto with
                {
                    Success = false,
                    Error = dto.Error ?? dto.Message ?? $"Mother POS delete cache failed ({(int)response.StatusCode}).",
                    ErrorCode = string.IsNullOrWhiteSpace(dto.ErrorCode) ? MapHttp(response.StatusCode) : dto.ErrorCode
                };
            }

            return dto;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MutateFail($"Could not reach Mother POS: {ex.Message}", RecentCustomersErrorCodes.OfflineMother);
        }
    }

    private async Task<MotherClientAuth?> GetAuthAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken))
        {
            return null;
        }

        return new MotherClientAuth(settings, await _cache.GetCurrentLoginSessionAsync());
    }

    private static HttpClient CreateClient(MotherClientAuth auth)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", auth.Settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", auth.Settings.TerminalToken);
        if (!string.IsNullOrWhiteSpace(auth.Session?.SessionToken))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", auth.Session.SessionToken);
        }

        client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
        return client;
    }

    private static string BuildUrl(string apiBaseUrl, string path, IReadOnlyDictionary<string, string?> query)
    {
        var parts = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        var queryString = string.Join("&", parts);
        return $"{apiBaseUrl.TrimEnd('/')}{path}{(queryString.Length == 0 ? string.Empty : $"?{queryString}")}";
    }

    private static string MapHttp(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => RecentCustomersErrorCodes.AccessDenied,
        HttpStatusCode.NotFound => RecentCustomersErrorCodes.NotFound,
        HttpStatusCode.BadRequest => RecentCustomersErrorCodes.Validation,
        _ => RecentCustomersErrorCodes.Unknown
    };

    private static ClientRecentCustomersResponseDto Fail(string message, string code) =>
        new(false, message, message, code, Array.Empty<ClientRecentCustomerItemDto>());

    private static ClientRecentCustomerMutateResponseDto MutateFail(string message, string code) =>
        new(false, message, message, code);

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession? Session);
}
