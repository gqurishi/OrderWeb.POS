using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// OrderWeb cloud customer API (search, upsert, sync-batch). Cloud is master.
/// </summary>
public class OrderWebCustomerCloudService
{
    public const string DefaultBaseUrl = "https://orderweb.net";

    private readonly DatabaseService _databaseService;
    private readonly HttpClient _httpClient;
    private readonly OrderWebApiClient? _orderWebApiClient;

    public OrderWebCustomerCloudService(DatabaseService databaseService, OrderWebApiClient orderWebApiClient)
        : this(databaseService, null, orderWebApiClient)
    {
    }

    public OrderWebCustomerCloudService(
        DatabaseService? databaseService = null,
        HttpClient? httpClient = null,
        OrderWebApiClient? orderWebApiClient = null)
    {
        _databaseService = databaseService ?? new DatabaseService();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _orderWebApiClient = orderWebApiClient;
    }

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("44", StringComparison.Ordinal) && digits.Length >= 12)
        {
            digits = "0" + digits[2..];
        }

        return digits;
    }

    public async Task<CustomerDataRecord?> SearchByPhoneAsync(string phone)
    {
        if (!await CanRunCustomerCloudAsync())
        {
            return null;
        }

        var config = await LoadConfigAsync();
        if (config == null)
        {
            return null;
        }

        var normalized = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var url = $"{config.BaseUrl}/pos/customers/search?tenant={Uri.EscapeDataString(config.TenantSlug)}&phone={Uri.EscapeDataString(normalized)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request, config.ApiKey);

        try
        {
            var response = _orderWebApiClient != null
                ? await _orderWebApiClient.SendAsync(request)
                : await _httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[CustomerCloud] Search failed: {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return MapCloudCustomer(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomerCloud] Search error: {ex.Message}");
            return null;
        }
    }

    public async Task<(bool Success, CustomerDataRecord? Customer, string? Error)> UpsertAsync(CustomerCloudUpsertPayload payload)
    {
        if (!await CanRunCustomerCloudAsync())
        {
            return (false, null, "Customer cloud sync runs on the mother/master terminal only.");
        }

        var config = await LoadConfigAsync();
        if (config == null)
        {
            return (false, null, "OrderWeb is not configured.");
        }

        var url = $"{config.BaseUrl}/pos/customers/upsert";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(request, config.ApiKey);

        var body = new
        {
            tenant = config.TenantSlug,
            phone = payload.PhoneNormalized,
            name = payload.Name,
            order_type = payload.OrderType,
            address = payload.Address,
            local_id = payload.LocalId
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            var response = _orderWebApiClient != null
                ? await _orderWebApiClient.SendAsync(request)
                : await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return (false, null, "Invalid OrderWeb API key.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"Cloud upsert failed ({(int)response.StatusCode}).");
            }

            var customer = MapCloudCustomer(json);
            return customer == null
                ? (false, null, "Cloud returned an unexpected response.")
                : (true, customer, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<(int Succeeded, int Failed, string? Error)> SyncBatchAsync(IReadOnlyList<CustomerCloudUpsertPayload> items)
    {
        if (!await CanRunCustomerCloudAsync())
        {
            return (0, items.Count, "Customer cloud sync runs on the mother/master terminal only.");
        }

        var config = await LoadConfigAsync();
        if (config == null)
        {
            return (0, items.Count, "OrderWeb is not configured.");
        }

        if (items.Count == 0)
        {
            return (0, 0, null);
        }

        var url = $"{config.BaseUrl}/pos/customers/sync-batch";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(request, config.ApiKey);

        var body = new
        {
            tenant = config.TenantSlug,
            customers = items.Select(item => new
            {
                phone = item.PhoneNormalized,
                name = item.Name,
                order_type = item.OrderType,
                address = item.Address,
                local_id = item.LocalId
            })
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            var response = _orderWebApiClient != null
                ? await _orderWebApiClient.SendAsync(request)
                : await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (0, items.Count, $"Batch sync failed ({(int)response.StatusCode}).");
            }

            // Treat whole batch as success when API accepts — per-row results can be refined later.
            return (items.Count, 0, null);
        }
        catch (Exception ex)
        {
            return (0, items.Count, ex.Message);
        }
    }

    public async Task<bool> EnqueueUpsertAsync(CustomerCloudUpsertPayload payload)
    {
        if (_orderWebApiClient == null || !await CanRunCustomerCloudAsync())
        {
            return false;
        }

        var config = await LoadConfigAsync();
        if (config == null)
        {
            return false;
        }

        var endpoint = $"{config.BaseUrl}/pos/customers/upsert";
        var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey(
            "customer-upsert",
            config.TenantSlug,
            payload.LocalId,
            payload.PhoneNormalized);

        var body = new
        {
            tenant = config.TenantSlug,
            phone = payload.PhoneNormalized,
            name = payload.Name,
            order_type = payload.OrderType,
            address = payload.Address,
            local_id = payload.LocalId
        };

        return await _orderWebApiClient.EnqueueAsync(
            "customer_upsert",
            endpoint,
            body,
            config.ApiKey,
            idempotencyKey,
            priority: 5);
    }

    private async Task<CloudConfig?> LoadConfigAsync()
    {
        var sharedConfig = _orderWebApiClient != null
            ? await _orderWebApiClient.GetConfigAsync()
            : null;

        if (sharedConfig != null)
        {
            return new CloudConfig(sharedConfig.TenantSlug, sharedConfig.ApiKey, sharedConfig.ApiBaseUrl);
        }

        var config = await _databaseService.GetCloudConfigAsync();
        var tenant = config.GetValueOrDefault("tenant_slug", string.Empty).Trim();
        var apiKey = config.GetValueOrDefault("api_key", string.Empty).Trim();
        var enabled = string.Equals(config.GetValueOrDefault("is_enabled", "False"), "True", StringComparison.OrdinalIgnoreCase);
        var baseUrl = OrderWebApiClient.NormalizeApiBaseUrl(
            config.GetValueOrDefault("api_base_url", config.GetValueOrDefault("cloud_url", DefaultBaseUrl)));

        if (!enabled || string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = OrderWebApiClient.NormalizeApiBaseUrl(DefaultBaseUrl);
        }

        return new CloudConfig(tenant, apiKey, baseUrl);
    }

    private async Task<bool> CanRunCustomerCloudAsync()
    {
        if (_orderWebApiClient != null)
        {
            return (await _orderWebApiClient.CanRunCloudJobsAsync()).Allowed;
        }

        return TerminalRoleService.CanRunMotherJobs;
    }

    private static void ApplyAuth(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    private static CustomerDataRecord? MapCloudCustomer(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var nested))
            {
                root = nested;
            }

            if (!root.TryGetProperty("name", out _) && !root.TryGetProperty("phone", out _))
            {
                return null;
            }

            var phone = root.TryGetProperty("phone", out var phoneEl) ? phoneEl.GetString() ?? string.Empty : string.Empty;
            var orderType = root.TryGetProperty("default_order_type", out var typeEl)
                ? typeEl.GetString() ?? "collection"
                : root.TryGetProperty("order_type", out var otEl) ? otEl.GetString() ?? "collection" : "collection";

            var record = new CustomerDataRecord
            {
                CloudCustomerId = root.TryGetProperty("customer_id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                Name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                PhoneNumber = phone,
                PhoneNormalized = NormalizePhone(phone),
                OrderTypes = orderType,
                PointsBalance = root.TryGetProperty("points_balance", out var ptsEl) && ptsEl.TryGetInt32(out var pts) ? pts : 0,
                TierLevel = root.TryGetProperty("tier_level", out var tierEl) ? tierEl.GetString() ?? string.Empty : string.Empty,
                SyncStatus = "synced",
                LastSyncAt = DateTime.Now,
                CachedAt = DateTime.Now,
                LastUsedAt = DateTime.Now
            };

            if (root.TryGetProperty("last_address", out var addrEl) && addrEl.ValueKind == JsonValueKind.Object)
            {
                record.FullAddress = addrEl.TryGetProperty("line1", out var l1) ? l1.GetString() ?? string.Empty : string.Empty;
                record.City = addrEl.TryGetProperty("city", out var city) ? city.GetString() ?? string.Empty : string.Empty;
                record.County = addrEl.TryGetProperty("county", out var county) ? county.GetString() ?? string.Empty : string.Empty;
                record.Postcode = addrEl.TryGetProperty("postcode", out var pc) ? pc.GetString() ?? string.Empty : string.Empty;
            }

            return record;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomerCloud] Map error: {ex.Message}");
            return null;
        }
    }

    private sealed record CloudConfig(string TenantSlug, string ApiKey, string BaseUrl);
}

public sealed class CustomerCloudUpsertPayload
{
    public int LocalId { get; set; }

    public string PhoneNormalized { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string OrderType { get; set; } = "collection";

    public object? Address { get; set; }
}
