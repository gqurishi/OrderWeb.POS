using System.Net.Http.Headers;
using System.Text.Json;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

public sealed class MotherMenuClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
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

            var snapshot = new MenuSnapshotDto(
                envelope.Version ?? "1",
                envelope.Categories ?? [],
                envelope.Products ?? [],
                envelope.Prices ?? [],
                envelope.ModifierGroups ?? [],
                envelope.Modifiers ?? [],
                envelope.ProductModifiers ?? [],
                envelope.Variants ?? [],
                envelope.MealDeals ?? [],
                envelope.MealDealChoices ?? [],
                envelope.MealDealCategoryRules ?? [],
                envelope.TastingMenus ?? [],
                envelope.TastingMenuOptions ?? [],
                envelope.TastingMenuCourses ?? [],
                envelope.TastingMenuChoices ?? []);

            // Empty menu is still a successful HTTP payload; surface Mother's guidance as Error hint.
            if (snapshot.Categories.Count == 0)
            {
                return (snapshot, envelope.Message
                    ?? "Mother Food Menu has no categories for Client. Open Mother → Food Menu, add categories and items, then Retry sync.");
            }

            return (snapshot, null);
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
        IReadOnlyList<BootstrapProductModifier>? ProductModifiers,
        IReadOnlyList<BootstrapVariant>? Variants,
        IReadOnlyList<BootstrapMealDeal>? MealDeals,
        IReadOnlyList<BootstrapMealDealChoice>? MealDealChoices,
        IReadOnlyList<BootstrapMealDealCategoryRule>? MealDealCategoryRules,
        IReadOnlyList<BootstrapTastingMenu>? TastingMenus,
        IReadOnlyList<BootstrapTastingMenuOption>? TastingMenuOptions,
        IReadOnlyList<BootstrapTastingMenuCourse>? TastingMenuCourses,
        IReadOnlyList<BootstrapTastingMenuChoice>? TastingMenuChoices);
}

public sealed record MenuSnapshotDto(
    string Version,
    IReadOnlyList<BootstrapCategory> Categories,
    IReadOnlyList<BootstrapProduct> Products,
    IReadOnlyList<BootstrapPrice> Prices,
    IReadOnlyList<BootstrapModifierGroup> ModifierGroups,
    IReadOnlyList<BootstrapModifier> Modifiers,
    IReadOnlyList<BootstrapProductModifier> ProductModifiers,
    IReadOnlyList<BootstrapVariant> Variants,
    IReadOnlyList<BootstrapMealDeal> MealDeals,
    IReadOnlyList<BootstrapMealDealChoice> MealDealChoices,
    IReadOnlyList<BootstrapMealDealCategoryRule> MealDealCategoryRules,
    IReadOnlyList<BootstrapTastingMenu> TastingMenus,
    IReadOnlyList<BootstrapTastingMenuOption> TastingMenuOptions,
    IReadOnlyList<BootstrapTastingMenuCourse> TastingMenuCourses,
    IReadOnlyList<BootstrapTastingMenuChoice> TastingMenuChoices);
