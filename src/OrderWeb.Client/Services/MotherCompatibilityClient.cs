using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Contracts.Compatibility;

namespace OrderWeb.Client.Services;

/// <summary>Mother-authoritative compatibility gate for Client operations.</summary>
public sealed class MotherCompatibilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    public MotherCompatibilityClient() : this(new ClientCacheService()) { }
    public MotherCompatibilityClient(ClientCacheService cache) => _cache = cache;

    public async Task<ClientCompatibilityResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings == null) return new(ClientCompatibilityStatus.UpdateRequired, "unknown", "Mother POS is not paired.");
        var request = new ClientCompatibilityRequest(
            AppInfo.VersionString,
            AppInfo.BuildString,
            DeviceInfo.Platform.ToString(),
            settings.TerminalId,
            PayloadVersion: 1,
            SchemaVersion: ClientCacheService.CurrentSchemaVersion);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            using var response = await client.PostAsJsonAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/api/pos/v1/compatibility", request, JsonOptions, cancellationToken);
            var envelope = JsonSerializer.Deserialize<Envelope>(await response.Content.ReadAsStringAsync(cancellationToken), JsonOptions);
            return response.IsSuccessStatusCode && envelope?.Result != null
                ? envelope.Result
                : new ClientCompatibilityResult(ClientCompatibilityStatus.UpdateRequired, "unknown", envelope?.Message ?? "Mother compatibility check failed.");
        }
        catch (HttpRequestException) { return new(ClientCompatibilityStatus.UpdateRequired, "unknown", "Mother compatibility check could not be reached."); }
        catch (TaskCanceledException) { return new(ClientCompatibilityStatus.UpdateRequired, "unknown", "Mother compatibility check timed out."); }
    }

    private sealed record Envelope(bool Success, string? Message, ClientCompatibilityResult? Result);
}
