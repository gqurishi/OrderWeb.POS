using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

public sealed class MotherHeartbeatClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private readonly ClientCacheService _cache;
    private readonly Func<LoginSession?> _sessionProvider;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;

    public MotherHeartbeatClient(ClientCacheService cache, Func<LoginSession?> sessionProvider)
    {
        _cache = cache;
        _sessionProvider = sessionProvider;
    }

    public bool IsRunning => _heartbeatTask is { IsCompleted: false };

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var settings = await _cache.GetMotherConnectionAsync();
        if (!CanHeartbeat(settings))
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _heartbeatTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_heartbeatTask is not null)
        {
            try
            {
                await _heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _heartbeatTask = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SendOnceAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Heartbeat remains best-effort, but keep the failure visible to
                // diagnostics so a connected WebSocket cannot hide auth errors.
                System.Diagnostics.Debug.WriteLine($"Mother heartbeat failed: {ex.Message}");
            }

            await Task.Delay(HeartbeatInterval, cancellationToken);
        }
    }

    private async Task SendOnceAsync(CancellationToken cancellationToken)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        if (!CanHeartbeat(settings))
        {
            return;
        }

        var session = _sessionProvider();
        var endpoint = $"{settings!.ApiBaseUrl.TrimEnd('/')}/terminals/heartbeat";
        var body = new HeartbeatRequest(
            settings.TerminalId,
            settings.TerminalToken,
            string.Empty,
            AppInfo.VersionString,
            DeviceInfo.Platform.ToString(),
            string.IsNullOrWhiteSpace(DeviceInfo.Name) ? "Client POS" : DeviceInfo.Name,
            int.TryParse(session?.UserId, out var userId) ? userId : null,
            null,
            GetBatteryStatus(),
            "online");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static bool CanHeartbeat(MotherConnectionSettings? settings) =>
        settings is not null &&
        !string.IsNullOrWhiteSpace(settings.ApiBaseUrl) &&
        !string.IsNullOrWhiteSpace(settings.TerminalId) &&
        !string.IsNullOrWhiteSpace(settings.TerminalToken);

    private static string? GetBatteryStatus()
    {
        try
        {
            return $"{Battery.ChargeLevel:P0} {Battery.State}";
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private sealed record HeartbeatRequest(
        [property: JsonPropertyName("terminal_id")] string TerminalId,
        [property: JsonPropertyName("terminal_token")] string TerminalToken,
        [property: JsonPropertyName("restaurant_slug")] string RestaurantSlug,
        [property: JsonPropertyName("app_version")] string AppVersion,
        [property: JsonPropertyName("platform")] string Platform,
        [property: JsonPropertyName("device_name")] string DeviceName,
        [property: JsonPropertyName("current_user_id")] int? CurrentUserId,
        [property: JsonPropertyName("last_sync_event_id")] long? LastSyncEventId,
        [property: JsonPropertyName("battery_status")] string? BatteryStatus,
        [property: JsonPropertyName("status")] string Status);
}
