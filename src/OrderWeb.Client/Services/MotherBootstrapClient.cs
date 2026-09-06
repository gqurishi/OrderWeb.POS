using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

public sealed class MotherBootstrapClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(8)
    };

    public BootstrapDiagnostics? LastDiagnostics { get; private set; }

    public async Task<BootstrapPayload> RequestBootstrapAsync(BootstrapRequest request, IProgress<BootstrapProgress>? progress = null)
    {
        BootstrapException? lastError = null;
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                return await RequestBootstrapOnceAsync(request, progress);
            }
            catch (BootstrapException ex) when (attempt < RetryDelays.Length && IsRetryable(ex))
            {
                lastError = ex;
                var delay = RetryDelays[attempt];
                progress?.Report(new BootstrapProgress(BootstrapStage.Connecting, $"Mother connection failed. Retrying in {delay.TotalSeconds:0} seconds ({attempt + 1}/{RetryDelays.Length})...", 0.10));
                await Task.Delay(delay);
            }
        }

        throw lastError ?? new BootstrapException("Mother POS bootstrap failed.");
    }

    private static bool IsRetryable(BootstrapException exception) =>
        exception.Diagnostics?.ErrorType is "Timeout" or "HTTP request error";

    private async Task<BootstrapPayload> RequestBootstrapOnceAsync(BootstrapRequest request, IProgress<BootstrapProgress>? progress = null)
    {
        progress?.Report(new BootstrapProgress(BootstrapStage.Connecting, "Connecting to Mother POS...", 0.10));

        var endpoint = BuildBootstrapEndpoint(request.MotherIpAddress);
        var body = new BootstrapHttpRequest(
            request.PairingCode,
            request.TerminalName,
            GetOrCreateDeviceId(),
            "Client POS",
            DeviceInfo.Platform.ToString(),
            AppInfo.VersionString);
        var requestJson = JsonSerializer.Serialize(body, JsonOptions);
        LastDiagnostics = new BootstrapDiagnostics(endpoint, requestJson, body.DeviceId, null, null, null);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            progress?.Report(new BootstrapProgress(BootstrapStage.DownloadingMenu, $"Pairing with Mother POS at {endpoint}...", 0.22));
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (TaskCanceledException ex)
        {
            LastDiagnostics = LastDiagnostics with { ErrorType = "Timeout" };
            throw new BootstrapException($"Mother POS did not respond within 2 minutes at {endpoint}. Check the IP address, port 5055, firewall, and Mother bootstrap logs.", LastDiagnostics, ex);
        }
        catch (HttpRequestException ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? string.Empty : $" Detail: {ex.Message}";
            LastDiagnostics = LastDiagnostics with { ErrorType = "HTTP request error" };
            throw new BootstrapException($"Could not reach Mother POS at {endpoint}. Check both devices are on the same network and that local HTTP traffic is allowed.{detail}", LastDiagnostics, ex);
        }

        progress?.Report(new BootstrapProgress(BootstrapStage.DownloadingTables, "Reading bootstrap response...", 0.55));
        var json = await response.Content.ReadAsStringAsync();
        LastDiagnostics = LastDiagnostics with
        {
            StatusCode = (int)response.StatusCode,
            RawResponseBody = json
        };
        BootstrapHttpResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<BootstrapHttpResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            LastDiagnostics = LastDiagnostics with { ErrorType = "Invalid JSON response" };
            throw new BootstrapException("Mother POS returned an invalid bootstrap response.", LastDiagnostics, ex);
        }

        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var message = envelope?.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = response.StatusCode == HttpStatusCode.NotFound
                    ? "Mother POS bootstrap route was not found. Restart Mother POS after updating."
                    : $"Mother POS bootstrap failed with status {(int)response.StatusCode}.";
            }

            LastDiagnostics = LastDiagnostics with { ErrorType = $"HTTP {(int)response.StatusCode}" };
            throw new BootstrapException(message, LastDiagnostics);
        }

        // Current Mother POS versions return the terminal activation fields at
        // the response root. Older Client POS versions expected those fields
        // inside a `payload` envelope. Accept both contracts so a successful
        // pairing is not reported as a failed HTTP 200 response.
        var payload = envelope.Payload ?? CreatePayloadFromFlatResponse(envelope, endpoint);
        if (payload is null)
        {
            LastDiagnostics = LastDiagnostics with { ErrorType = "Incomplete HTTP 200 response" };
            throw new BootstrapException(
                "Mother POS accepted the pairing but did not return the terminal connection details.",
                LastDiagnostics);
        }

        ValidateBootstrapPayload(payload);

        progress?.Report(new BootstrapProgress(BootstrapStage.DownloadingOpenOrders, "Downloading open orders...", 0.78));
        progress?.Report(new BootstrapProgress(BootstrapStage.PreparingPos, "Preparing local POS cache...", 0.92));
        return payload;
    }

    private static string BuildBootstrapEndpoint(string motherIpAddress)
    {
        var cleanIp = string.IsNullOrWhiteSpace(motherIpAddress)
            ? "127.0.0.1"
            : motherIpAddress.Trim().TrimEnd('/');

        if (cleanIp.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            cleanIp.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"{cleanIp}/api/client/bootstrap";
        }

        if (cleanIp.Contains(':', StringComparison.Ordinal))
        {
            return $"http://{cleanIp}/api/client/bootstrap";
        }

        return $"http://{cleanIp}:5055/api/client/bootstrap";
    }

    public static string GetDeviceId() => GetOrCreateDeviceId();

    public static string ResetDeviceId()
    {
        const string key = "client_pos_device_id";
        var created = Guid.NewGuid().ToString("D");
        Preferences.Set(key, created);
        return created;
    }

    private static void ValidateBootstrapPayload(BootstrapPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Terminal.TerminalId) ||
            string.IsNullOrWhiteSpace(payload.Terminal.TerminalToken) ||
            string.IsNullOrWhiteSpace(payload.Terminal.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(payload.Terminal.WebSocketUrl))
        {
            throw new BootstrapException("Mother POS bootstrap response did not include terminalId, terminalToken, apiBaseUrl, and webSocketUrl.");
        }
    }

    private static BootstrapPayload? CreatePayloadFromFlatResponse(
        BootstrapHttpResponse response,
        string endpoint)
    {
        var terminalId = response.TerminalId ?? response.TerminalIdentity?.TerminalId;
        var terminalToken = response.TerminalToken;
        if (string.IsNullOrWhiteSpace(terminalId) || string.IsNullOrWhiteSpace(terminalToken))
        {
            return null;
        }

        var endpointUri = new Uri(endpoint);
        var apiBaseUrl = endpointUri.GetLeftPart(UriPartial.Authority);
        var motherIp = response.TerminalConfig?.MotherIp ?? endpointUri.Host;
        var apiPort = response.ApiPort > 0
            ? response.ApiPort
            : response.TerminalConfig?.ApiPort > 0
                ? response.TerminalConfig.ApiPort
                : endpointUri.Port;
        var websocketPath = string.IsNullOrWhiteSpace(response.TerminalConfig?.WebsocketPath)
            ? "/ws"
            : response.TerminalConfig.WebsocketPath;
        var websocketUrl = !string.IsNullOrWhiteSpace(response.WebsocketUrl)
            ? response.WebsocketUrl
            : $"ws://{motherIp}:{apiPort}{websocketPath}";
        var restaurantName = string.IsNullOrWhiteSpace(response.Restaurant?.Name)
            ? "Mother POS"
            : response.Restaurant.Name;
        var restaurantId = string.IsNullOrWhiteSpace(response.Restaurant?.Slug)
            ? restaurantName
            : response.Restaurant.Slug;
        var generatedUtc = response.ServerTime?.ToUniversalTime().ToString("O")
            ?? DateTimeOffset.UtcNow.ToString("O");
        var checkpoint = response.TerminalConfig?.SyncCheckpoint ?? 0;

        return new BootstrapPayload(
            new BootstrapRestaurantInfo(restaurantId, restaurantName, string.Empty, "GBP", "Europe/London"),
            new BootstrapTerminalConfig(
                terminalId,
                response.TerminalName ?? response.TerminalIdentity?.TerminalName ?? "Client POS",
                response.TerminalIdentity?.DeviceType ?? "Client",
                apiBaseUrl,
                websocketUrl,
                string.Empty,
                terminalToken),
            Array.Empty<BootstrapCategory>(),
            Array.Empty<BootstrapProduct>(),
            Array.Empty<BootstrapPrice>(),
            Array.Empty<BootstrapModifierGroup>(),
            Array.Empty<BootstrapModifier>(),
            Array.Empty<BootstrapProductModifier>(),
            Array.Empty<BootstrapTaxRate>(),
            Array.Empty<BootstrapFloor>(),
            Array.Empty<BootstrapTable>(),
            Array.Empty<BootstrapOpenOrder>(),
            Array.Empty<BootstrapOrderItem>(),
            Array.Empty<BootstrapOnlineOrder>(),
            Array.Empty<BootstrapReservation>(),
            Array.Empty<BootstrapCustomer>(),
            Array.Empty<BootstrapPermission>(),
            new BootstrapSyncVersions(
                1,
                terminalId,
                checkpoint.ToString(),
                generatedUtc,
                checkpoint.ToString(),
                checkpoint.ToString(),
                checkpoint.ToString(),
                checkpoint.ToString()));
    }

    private static string GetOrCreateDeviceId()
    {
        const string key = "client_pos_device_id";
        var existing = Preferences.Get(key, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var created = Guid.NewGuid().ToString("D");
        Preferences.Set(key, created);
        return created;
    }

    private sealed record BootstrapHttpRequest(
        [property: JsonPropertyName("pairingCode")] string PairingCode,
        [property: JsonPropertyName("terminalName")] string TerminalName,
        [property: JsonPropertyName("deviceId")] string DeviceId,
        [property: JsonPropertyName("deviceType")] string DeviceType,
        [property: JsonPropertyName("platform")] string Platform,
        [property: JsonPropertyName("appVersion")] string AppVersion);

    private sealed record BootstrapHttpResponse(
        bool Success,
        string? Message,
        BootstrapPayload? Payload,
        string? TerminalId,
        string? TerminalToken,
        string? TerminalName,
        int ApiPort,
        string? WebsocketUrl,
        FlatRestaurantInfo? Restaurant,
        FlatTerminalIdentity? TerminalIdentity,
        FlatTerminalConfig? TerminalConfig,
        DateTimeOffset? ServerTime);

    private sealed record FlatRestaurantInfo(string? Name, string? Slug);

    private sealed record FlatTerminalIdentity(
        string? TerminalId,
        string? TerminalName,
        string? DeviceType);

    private sealed record FlatTerminalConfig(
        string? MotherIp,
        int ApiPort,
        string? WebsocketPath,
        long SyncCheckpoint);
}

public sealed record BootstrapProgress(BootstrapStage Stage, string Message, double Progress);

public sealed record BootstrapDiagnostics(
    string Endpoint,
    string RequestJson,
    string DeviceId,
    int? StatusCode,
    string? RawResponseBody,
    string? ErrorType);

public enum BootstrapStage
{
    Connecting,
    DownloadingMenu,
    DownloadingTables,
    DownloadingOpenOrders,
    PreparingPos,
    Complete,
    Failed
}

public sealed class BootstrapException : Exception
{
    public BootstrapDiagnostics? Diagnostics { get; }

    public BootstrapException(string message)
        : base(message)
    {
    }

    public BootstrapException(string message, BootstrapDiagnostics? diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }

    public BootstrapException(string message, BootstrapDiagnostics? diagnostics, Exception innerException)
        : base(message, innerException)
    {
        Diagnostics = diagnostics;
    }

    public BootstrapException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
