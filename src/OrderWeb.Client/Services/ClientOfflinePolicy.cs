using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OrderWeb.Client.Services;

public enum ClientOperation
{
    ViewCachedMenu, ViewCachedFloor, CreateUnsentDraft, SubmitFinalOrder,
    CardPayment, Refund, ChangePermissions, UpdateMenu, ViewCachedOpenOrders
}

public enum MotherProbeStatus
{
    Unreachable,
    Online,
    PairingInvalid,
    TerminalDisabled
}

public sealed record OfflineDecision(bool Allowed, bool RequiresMother, bool DataMayBeStale, string Message);

public sealed record MotherProbeResult(MotherProbeStatus Status, string Message);

/// <summary>Single, explicit offline policy for Client POS operations.</summary>
public sealed class ClientOfflinePolicy
{
    private readonly ClientCacheService _cache;
    public ClientOfflinePolicy() : this(new ClientCacheService()) { }
    public ClientOfflinePolicy(ClientCacheService cache) => _cache = cache;

    public OfflineDecision Evaluate(ClientOperation operation, bool motherOnline) => operation switch
    {
        ClientOperation.ViewCachedMenu => new(true, false, !motherOnline, motherOnline ? "Mother online" : "Mother offline — cached menu may be outdated."),
        ClientOperation.ViewCachedFloor => new(true, false, !motherOnline, motherOnline ? "Mother online" : "Mother offline — cached floor may be outdated."),
        ClientOperation.CreateUnsentDraft => new(true, false, !motherOnline, motherOnline ? "Draft available" : "Offline draft only — it has not been sent to Mother."),
        ClientOperation.ViewCachedOpenOrders => new(true, false, !motherOnline, motherOnline ? "Open orders current" : "Mother offline — open orders may be outdated."),
        ClientOperation.SubmitFinalOrder => OnlineOnly(motherOnline, "Final orders require Mother confirmation. No order was submitted."),
        ClientOperation.CardPayment => OnlineOnly(motherOnline, "Card payment requires Mother and the payment provider. No payment was taken."),
        ClientOperation.Refund => OnlineOnly(motherOnline, "Refunds require Mother approval. No refund was created."),
        ClientOperation.ChangePermissions => OnlineOnly(motherOnline, "Permission changes require Mother POS."),
        ClientOperation.UpdateMenu => OnlineOnly(motherOnline, "Menu updates require Mother POS."),
        _ => OnlineOnly(motherOnline, "This action requires Mother POS.")
    };

    public Task<bool> IsMotherOnlineAsync(CancellationToken cancellationToken = default) =>
        IsMotherOnlineAtAsync(null, cancellationToken);

    public async Task<bool> IsMotherOnlineAtAsync(string? apiBaseUrl, CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var url = string.IsNullOrWhiteSpace(apiBaseUrl) ? settings?.ApiBaseUrl : apiBaseUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync($"{url.TrimEnd('/')}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    public Task<MotherProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(null, cancellationToken);

    public async Task<MotherProbeResult> ProbeAsync(string? apiBaseUrl, CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var url = string.IsNullOrWhiteSpace(apiBaseUrl) ? settings?.ApiBaseUrl : apiBaseUrl;
        var address = FormatAddress(url);
        if (string.IsNullOrWhiteSpace(url))
        {
            return new MotherProbeResult(
                MotherProbeStatus.Unreachable,
                "This terminal is not paired with Mother POS yet. Enter the Mother IP and a pairing code.");
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var health = await client.GetAsync($"{url.TrimEnd('/')}/health", cancellationToken);
            if (!health.IsSuccessStatusCode)
            {
                return Unreachable(address);
            }

            if (string.IsNullOrWhiteSpace(settings?.TerminalToken) ||
                string.IsNullOrWhiteSpace(settings.TerminalId))
            {
                return new MotherProbeResult(MotherProbeStatus.Online, $"Mother POS is reachable at {address}.");
            }

            return await ProbePairingAsync(client, url, address, settings, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Unreachable(address);
        }
        catch (TaskCanceledException)
        {
            return Unreachable(address);
        }
    }

    private static async Task<MotherProbeResult> ProbePairingAsync(
        HttpClient client,
        string apiBaseUrl,
        string address,
        MotherConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            var body = JsonSerializer.Serialize(new
            {
                terminalId = settings.TerminalId,
                terminal_id = settings.TerminalId,
                terminalToken = settings.TerminalToken,
                terminal_token = settings.TerminalToken,
                status = "online"
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{apiBaseUrl.TrimEnd('/')}/terminals/heartbeat", content, cancellationToken);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return new MotherProbeResult(MotherProbeStatus.Online, $"Mother POS is reachable at {address}.");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = ReadMessage(payload);
            if (response.StatusCode == HttpStatusCode.Forbidden &&
                !string.IsNullOrWhiteSpace(message) &&
                message.Contains("disabled", StringComparison.OrdinalIgnoreCase))
            {
                return new MotherProbeResult(
                    MotherProbeStatus.TerminalDisabled,
                    string.IsNullOrWhiteSpace(message)
                        ? "This Client POS has been disabled by the Mother POS."
                        : message);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new MotherProbeResult(
                    MotherProbeStatus.PairingInvalid,
                    $"Mother POS answered at {address}, but this terminal is no longer paired. Enter a new pairing code from Mother.");
            }

            return new MotherProbeResult(MotherProbeStatus.Online, $"Mother POS is reachable at {address}.");
        }
        catch (HttpRequestException)
        {
            return new MotherProbeResult(MotherProbeStatus.Online, $"Mother POS is reachable at {address}.");
        }
        catch (TaskCanceledException)
        {
            return new MotherProbeResult(MotherProbeStatus.Online, $"Mother POS is reachable at {address}.");
        }
    }

    private static MotherProbeResult Unreachable(string address) =>
        new(
            MotherProbeStatus.Unreachable,
            string.IsNullOrWhiteSpace(address)
                ? "Cannot reach Mother POS. Check the network, that Mother is running, and that the IP is correct."
                : $"Cannot reach Mother POS at {address}. Check the network, that Mother is running, and that the IP is correct.");

    private static string FormatAddress(string? apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }

        return apiBaseUrl;
    }

    private static string? ReadMessage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static OfflineDecision OnlineOnly(bool online, string offlineMessage) =>
        online ? new(true, true, false, "Mother online") : new(false, true, true, offlineMessage);
}
