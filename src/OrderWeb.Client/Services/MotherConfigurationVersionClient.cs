using System.Net.Http.Json;
using System.Text.Json;

namespace OrderWeb.Client.Services;

/// <summary>Version comparison transport. It never applies payload data itself.</summary>
public sealed class MotherConfigurationVersionClient
{
    private readonly ClientCacheService _cache;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MotherConfigurationVersionClient(ClientCacheService cache) => _cache = cache;

    public async Task<bool> CompareAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings == null || session == null) return false;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
        try
        {
            var response = await client.GetFromJsonAsync<VersionEnvelope>($"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/sync/versions", JsonOptions, cancellationToken);
            if (response?.Success != true || response.Versions == null) return false;
            await _cache.RecordMotherConfigurationVersionsAsync(response.Versions);
            return true;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private sealed record VersionEnvelope(bool Success, Dictionary<string, string>? Versions);
}
