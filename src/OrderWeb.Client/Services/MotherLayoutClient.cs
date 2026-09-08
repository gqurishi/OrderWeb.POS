using System.Net.Http.Headers;
using System.Text.Json;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

public sealed class MotherLayoutClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherLayoutClient()
        : this(new ClientCacheService())
    {
    }

    public MotherLayoutClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public async Task<RestaurantLayoutSnapshotDto?> GetLayoutAsync(CancellationToken cancellationToken = default)
    {
        var (layout, _) = await TryGetLayoutAsync(cancellationToken);
        return layout;
    }

    public async Task<(RestaurantLayoutSnapshotDto? Layout, string? Error)> TryGetLayoutAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken) ||
            string.IsNullOrWhiteSpace(session?.SessionToken))
        {
            return (null, "Client is not logged in to Mother POS.");
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.GetAsync(
                $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/layout",
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<LayoutEnvelope>(json, JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                return (null, envelope?.Message ?? $"Mother layout pull failed ({(int)response.StatusCode}).");
            }

            if (envelope is null || !envelope.Success)
            {
                return (null, envelope?.Message ?? "Mother POS returned an empty layout response.");
            }

            var floors = envelope.Floors ?? [];
            var tables = envelope.Tables ?? [];
            return (new RestaurantLayoutSnapshotDto(envelope.Version ?? "1", floors, tables), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private sealed record LayoutEnvelope(
        bool Success,
        string? Message,
        string? Version,
        IReadOnlyList<FloorDto>? Floors,
        IReadOnlyList<RestaurantTableDto>? Tables);
}
