using System.Net.Http.Headers;
using System.Text.Json;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

public sealed class MotherMenuClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherMenuClient()
        : this(new ClientCacheService())
    {
    }

    public MotherMenuClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public async Task<bool> RefreshCacheAsync(CancellationToken cancellationToken = default)
    {
        var (snapshot, _) = await TryGetMenuAsync(cancellationToken);
        if (snapshot == null)
        {
            return false;
        }

        var replace = await _cache.ReplaceMenuAsync(snapshot);
        return replace.Applied && snapshot.Categories.Count > 0;
    }

    public async Task<MenuSnapshotDto?> GetMenuAsync(CancellationToken cancellationToken = default)
    {
        var (snapshot, _) = await TryGetMenuAsync(cancellationToken);
        return snapshot;
    }

    public async Task<(MenuSnapshotDto? Snapshot, string? Error)> TryGetMenuAsync(
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
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.GetAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/menu", cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<MenuEnvelope>(json, JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                return (null, envelope?.Message ?? $"Mother menu pull failed ({(int)response.StatusCode}).");
            }

            if (envelope is null || !envelope.Success)
            {
                return (null, envelope?.Message ?? "Mother POS returned an empty menu response.");
            }

            return (new MenuSnapshotDto(
                envelope.Version ?? "1",
                envelope.Categories ?? [],
                envelope.Products ?? [],
                envelope.Prices ?? [],
                envelope.ModifierGroups ?? [],
                envelope.Modifiers ?? [],
                envelope.ProductModifiers ?? []), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private sealed record MenuEnvelope(
        bool Success,
        string? Message,
        string? Version,
        IReadOnlyList<BootstrapCategory>? Categories,
        IReadOnlyList<BootstrapProduct>? Products,
        IReadOnlyList<BootstrapPrice>? Prices,
        IReadOnlyList<BootstrapModifierGroup>? ModifierGroups,
        IReadOnlyList<BootstrapModifier>? Modifiers,
        IReadOnlyList<BootstrapProductModifier>? ProductModifiers);
}

public sealed record MenuSnapshotDto(
    string Version,
    IReadOnlyList<BootstrapCategory> Categories,
    IReadOnlyList<BootstrapProduct> Products,
    IReadOnlyList<BootstrapPrice> Prices,
    IReadOnlyList<BootstrapModifierGroup> ModifierGroups,
    IReadOnlyList<BootstrapModifier> Modifiers,
    IReadOnlyList<BootstrapProductModifier> ProductModifiers);
