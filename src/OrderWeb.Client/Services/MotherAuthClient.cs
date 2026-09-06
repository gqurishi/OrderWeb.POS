using OrderWeb.Client.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderWeb.Client.Services;

public sealed class MotherAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherAuthClient()
        : this(new ClientCacheService())
    {
    }

    public MotherAuthClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public async Task<(bool Success, string Message, LoginSession? User)> ValidatePinAsync(string pin)
    {
        try
        {
            var user = await LoginAsync(new LoginRequest("PIN", pin));
            return (true, string.Empty, user);
        }
        catch (LoginException ex)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<LoginSession> LoginAsync(LoginRequest request)
    {
        if (!string.Equals(request.Method, "PIN", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.Pin))
        {
            throw new LoginException("Enter a 4 digit PIN.");
        }

        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken))
        {
            throw new LoginException("Client POS is not paired with Mother POS.");
        }

        var body = new ClientLoginHttpRequest(
            request.Pin.Trim(),
            settings.TerminalId,
            settings.TerminalToken,
            string.Empty,
            AppInfo.VersionString);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        ClientCompatibilityHeaders.Apply(client);
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        var endpoints = new[]
        {
            $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/login",
            $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/auth/login"
        };

        HttpResponseMessage? response = null;
        string? attemptedEndpoint = null;

        foreach (var endpoint in endpoints)
        {
            try
            {
                attemptedEndpoint = endpoint;
                response = await client.PostAsync(endpoint, content).WaitAsync(TimeSpan.FromSeconds(12));
                if (response.IsSuccessStatusCode)
                {
                    break;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                break;
            }
            catch (TaskCanceledException ex)
            {
                if (endpoint == endpoints[^1])
                {
                    throw new LoginException($"Mother POS login did not respond at {endpoint}.", ex);
                }
            }
            catch (TimeoutException ex)
            {
                if (endpoint == endpoints[^1])
                {
                    throw new LoginException($"Mother POS login did not respond at {endpoint}.", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                if (endpoint == endpoints[^1])
                {
                    throw new LoginException($"Could not reach Mother POS login at {endpoint}. {ex.Message}", ex);
                }
            }
        }

        if (response is null)
        {
            throw new LoginException($"Mother POS login did not respond at {attemptedEndpoint ?? endpoints[0]}.");
        }

        var json = await response.Content.ReadAsStringAsync();
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new LoginException($"Mother POS returned an invalid login response from {attemptedEndpoint}.", ex);
        }

        var hasSuccess = root.TryGetProperty("success", out _);
        var success = !hasSuccess || TryReadBool(root, "success");
        var message = TryReadString(root, "message");
        var payload = GetLoginPayload(root);

        var userId = TryReadString(payload, "userId", "user_id");
        var userName = TryReadString(payload, "userName", "user_name", "username", "name");
        var role = TryReadString(payload, "role");
        var sessionToken = TryReadString(payload, "sessionToken", "session_token", "token", "accessToken", "access_token")
            ?? TryReadString(root, "sessionToken", "session_token", "token", "accessToken", "access_token");
        var expiresAtUtc = TryReadString(payload, "expiresAtUtc", "expires_at", "expiresAt", "expires_at_utc")
            ?? TryReadString(root, "expiresAtUtc", "expires_at", "expiresAt", "expires_at_utc");
        var permissions = payload.TryGetProperty("permissions", out var permissionsElement) && permissionsElement.ValueKind == JsonValueKind.Array
            ? permissionsElement.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList()
            : new List<string>();
        var capabilities = payload.TryGetProperty("capabilities", out var capabilitiesElement) && capabilitiesElement.ValueKind == JsonValueKind.Array
            ? capabilitiesElement.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList()
            : new List<string>();

        if (!response.IsSuccessStatusCode || !success || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionToken))
        {
            var finalMessage = message;
            if (string.IsNullOrWhiteSpace(finalMessage))
            {
                finalMessage = response.StatusCode == HttpStatusCode.NotFound
                    ? "Mother POS login route was not found. Update and restart Mother POS."
                    : response.IsSuccessStatusCode && success
                        ? "Mother POS accepted the PIN but did not return a user session."
                    : $"Mother POS login failed with status {(int)response.StatusCode}.";
            }

            throw new LoginException(finalMessage);
        }

        return new LoginSession(
            userId,
            userName ?? userId,
            role ?? "User",
            permissions.Concat(capabilities).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            sessionToken,
            DateTimeOffset.TryParse(expiresAtUtc, out var expiresAt)
                ? expiresAt
                : DateTimeOffset.UtcNow.AddHours(12));
    }

    private static bool TryReadBool(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind == JsonValueKind.True ||
                   (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
        }

        return false;
    }

    private static JsonElement GetLoginPayload(JsonElement root)
    {
        foreach (var propertyName in new[] { "payload", "user", "data", "result" })
        {
            if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                return value;
            }
        }

        return root;
    }

    private static string? TryReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value) &&
                value.ValueKind != JsonValueKind.Null)
            {
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private sealed record ClientLoginHttpRequest(
        [property: JsonPropertyName("pin")] string Pin,
        [property: JsonPropertyName("terminal_id")] string TerminalId,
        [property: JsonPropertyName("terminal_token")] string TerminalToken,
        [property: JsonPropertyName("restaurant_slug")] string RestaurantSlug,
        [property: JsonPropertyName("app_version")] string AppVersion);

    private sealed record ClientLoginHttpResponse(
        bool Success,
        string? Message,
        ClientLoginPayload? Payload);

    private sealed record ClientLoginPayload(
        string UserId,
        string UserName,
        string Role,
        IReadOnlyList<string> Permissions,
        string SessionToken,
        string ExpiresAtUtc,
        string? RestaurantName);
}

public sealed class LoginException : Exception
{
    public LoginException(string message)
        : base(message)
    {
    }

    public LoginException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
