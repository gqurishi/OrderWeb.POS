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

        var endpoint = $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/login";
        var body = new ClientLoginHttpRequest(
            request.Pin.Trim(),
            settings.TerminalId,
            settings.TerminalToken,
            string.Empty,
            AppInfo.VersionString);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(endpoint, content).WaitAsync(TimeSpan.FromSeconds(12));
        }
        catch (TaskCanceledException ex)
        {
            throw new LoginException($"Mother POS login did not respond at {endpoint}.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new LoginException($"Mother POS login did not respond at {endpoint}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LoginException($"Could not reach Mother POS login at {endpoint}. {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync();
        ClientLoginHttpResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ClientLoginHttpResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new LoginException("Mother POS returned an invalid login response.", ex);
        }

        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success || envelope.Payload is null)
        {
            var message = envelope?.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = response.StatusCode == HttpStatusCode.NotFound
                    ? "Mother POS login route was not found. Update and restart Mother POS."
                    : $"Mother POS login failed with status {(int)response.StatusCode}.";
            }

            throw new LoginException(message);
        }

        return new LoginSession(
            envelope.Payload.UserId,
            envelope.Payload.UserName,
            envelope.Payload.Role,
            envelope.Payload.Permissions,
            envelope.Payload.SessionToken,
            DateTimeOffset.TryParse(envelope.Payload.ExpiresAtUtc, out var expiresAt)
                ? expiresAt
                : DateTimeOffset.UtcNow.AddHours(12));
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
