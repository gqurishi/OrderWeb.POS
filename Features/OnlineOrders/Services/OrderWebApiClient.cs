using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;

namespace POS_in_NET.Services;

public sealed record OrderWebApiConfig(
    string ApiBaseUrl,
    string TenantSlug,
    string ApiKey,
    bool IsEnabled);

/// <summary>
/// Shared OrderWeb POS API helper. Keeps URL normalization, auth headers,
/// device identity, idempotency, and offline queue wiring consistent.
/// </summary>
public sealed class OrderWebApiClient
{
    private const string DeviceIdSettingKey = "orderweb_device_id";

    private readonly DatabaseService _databaseService;
    private readonly OfflineQueueService? _offlineQueueService;
    private readonly HttpClient _httpClient;
    private string? _deviceId;

    public OrderWebApiClient(
        DatabaseService databaseService,
        OfflineQueueService? offlineQueueService = null)
    {
        _databaseService = databaseService;
        _offlineQueueService = offlineQueueService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<TerminalRoleCheck> CanRunCloudJobsAsync()
    {
        return await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
    }

    public async Task<OrderWebApiConfig?> GetConfigAsync()
    {
        var config = await _databaseService.GetCloudConfigAsync();
        var tenant = config.GetValueOrDefault("tenant_slug", string.Empty).Trim();
        var apiKey = config.GetValueOrDefault("api_key", string.Empty).Trim();
        var enabled = string.Equals(config.GetValueOrDefault("is_enabled"), "True", StringComparison.OrdinalIgnoreCase);
        var baseUrl = NormalizeApiBaseUrl(
            config.GetValueOrDefault("api_base_url", config.GetValueOrDefault("cloud_url", string.Empty)));

        if (!enabled || string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new OrderWebApiConfig(baseUrl, tenant, apiKey, enabled);
    }

    public static string NormalizeApiBaseUrl(string? configuredUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredUrl)
            ? "https://orderweb.net/api"
            : configuredUrl.Trim();

        baseUrl = baseUrl.TrimEnd('/');

        var suffixes = new[]
        {
            "/pos/pull-orders",
            "/pos/reservations",
            "/pos",
            "/api/pos",
            "/api"
        };

        foreach (var suffix in suffixes)
        {
            if (baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        baseUrl = StripTenantSegmentFromApiBase(baseUrl);

        if (!baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"{baseUrl}/api";
        }

        return string.IsNullOrWhiteSpace(baseUrl)
            ? "https://orderweb.net/api"
            : baseUrl;
    }

    private static string StripTenantSegmentFromApiBase(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return baseUrl;
        }

        var pathSegments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (pathSegments.Length == 2 &&
            pathSegments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            !pathSegments[1].Equals("pos", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Path = "api",
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri.ToString().TrimEnd('/');
        }

        return baseUrl;
    }

    public static string BuildUrl(OrderWebApiConfig config, string path)
    {
        path = path.StartsWith('/') ? path : $"/{path}";
        return $"{config.ApiBaseUrl}{path}";
    }

    public async Task<string> GetDeviceIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(_deviceId))
        {
            return _deviceId;
        }

        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var select = new MySqlCommand(
                "SELECT setting_value FROM settings WHERE setting_key = @key LIMIT 1",
                connection);
            select.Parameters.AddWithValue("@key", DeviceIdSettingKey);
            var existing = Convert.ToString(await select.ExecuteScalarAsync());
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _deviceId = existing;
                return _deviceId;
            }

            _deviceId = $"POS_{Environment.MachineName}_{Guid.NewGuid().ToString("N")[..8]}";
            await using var insert = new MySqlCommand(
                """
                INSERT INTO settings (setting_key, setting_value)
                VALUES (@key, @value)
                ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)
                """,
                connection);
            insert.Parameters.AddWithValue("@key", DeviceIdSettingKey);
            insert.Parameters.AddWithValue("@value", _deviceId);
            await insert.ExecuteNonQueryAsync();
            return _deviceId;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"OrderWeb device id fallback: {ex.Message}");
            return $"POS_{Environment.MachineName}";
        }
    }

    public HttpRequestMessage CreateRequest(
        OrderWebApiConfig config,
        HttpMethod method,
        string path,
        object? payload = null,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, BuildUrl(config, path));
        ApplyAuthHeaders(request, config.ApiKey, idempotencyKey);

        if (payload != null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    public void ApplyAuthHeaders(HttpRequestMessage request, string apiKey, string? idempotencyKey = null)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);
        }
    }

    public Dictionary<string, string> BuildQueueHeaders(string apiKey, string? idempotencyKey = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {apiKey}",
            ["X-API-Key"] = apiKey,
            ["Accept"] = "application/json"
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            headers["Idempotency-Key"] = idempotencyKey;
            headers["X-Idempotency-Key"] = idempotencyKey;
        }

        return headers;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        return await _httpClient.SendAsync(request);
    }

    public async Task<bool> EnqueueAsync(
        string operationType,
        string endpoint,
        object payload,
        string apiKey,
        string? idempotencyKey = null,
        string httpMethod = "POST",
        int priority = 5)
    {
        if (_offlineQueueService == null)
        {
            return false;
        }

        return await _offlineQueueService.EnqueueAsync(
            operationType,
            endpoint,
            payload,
            httpMethod,
            priority,
            BuildQueueHeaders(apiKey, idempotencyKey));
    }

    public static string BuildIdempotencyKey(string operation, params object?[] parts)
    {
        var raw = $"{operation}:{string.Join(":", parts.Select(p => Convert.ToString(p)?.Trim() ?? string.Empty))}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"{operation}:{Convert.ToHexString(bytes)[..32].ToLowerInvariant()}";
    }
}
