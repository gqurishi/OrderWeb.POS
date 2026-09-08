using OrderWeb.Client.Models;
using OrderWeb.Contracts.Synchronization;

namespace OrderWeb.Client.Services;

/// <summary>
/// Remaps Mother menu snapshot int ids to stable ids derived from MotherId,
/// so category/product FKs do not drift when Mother reorders or uses sequential counters.
/// </summary>
public static class MenuSnapshotStabilizer
{
    public static MenuSnapshotDto Stabilize(MenuSnapshotDto snapshot)
    {
        var used = new HashSet<int>();
        var categoryMap = new Dictionary<int, int>();
        var categories = new List<BootstrapCategory>();

        foreach (var category in snapshot.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.MotherId) || string.IsNullOrWhiteSpace(category.Name))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"cat:{category.MotherId}", used);
            categoryMap[category.Id] = stableId;
            categories.Add(category with { Id = stableId });
        }

        var productMap = new Dictionary<int, int>();
        var products = new List<BootstrapProduct>();
        foreach (var product in snapshot.Products)
        {
            if (string.IsNullOrWhiteSpace(product.MotherId) ||
                string.IsNullOrWhiteSpace(product.Name) ||
                !categoryMap.TryGetValue(product.CategoryId, out var categoryId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"prod:{product.MotherId}", used);
            productMap[product.Id] = stableId;
            products.Add(product with { Id = stableId, CategoryId = categoryId });
        }

        var prices = new List<BootstrapPrice>();
        foreach (var price in snapshot.Prices)
        {
            if (!productMap.TryGetValue(price.ProductId, out var productId) ||
                string.IsNullOrWhiteSpace(price.PriceType))
            {
                continue;
            }

            var motherProduct = products.First(p => p.Id == productId).MotherId;
            var stableId = StableEntityId.FromKey($"price:{motherProduct}:{price.PriceType}", used);
            prices.Add(price with { Id = stableId, ProductId = productId });
        }

        var groupMap = new Dictionary<int, int>();
        var groups = new List<BootstrapModifierGroup>();
        foreach (var group in snapshot.ModifierGroups)
        {
            if (string.IsNullOrWhiteSpace(group.MotherId) || string.IsNullOrWhiteSpace(group.Name))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"mgroup:{group.MotherId}", used);
            groupMap[group.Id] = stableId;
            groups.Add(group with { Id = stableId });
        }

        var modifiers = new List<BootstrapModifier>();
        foreach (var modifier in snapshot.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifier.MotherId) ||
                string.IsNullOrWhiteSpace(modifier.Name) ||
                !groupMap.TryGetValue(modifier.ModifierGroupId, out var groupId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"mod:{modifier.MotherId}", used);
            modifiers.Add(modifier with { Id = stableId, ModifierGroupId = groupId });
        }

        var productModifiers = new List<BootstrapProductModifier>();
        foreach (var link in snapshot.ProductModifiers)
        {
            if (!productMap.TryGetValue(link.ProductId, out var productId) ||
                !groupMap.TryGetValue(link.ModifierGroupId, out var groupId))
            {
                continue;
            }

            productModifiers.Add(link with { ProductId = productId, ModifierGroupId = groupId });
        }

        return new MenuSnapshotDto(
            snapshot.Version,
            categories,
            products,
            prices,
            groups,
            modifiers,
            productModifiers);
    }

    public static string? ValidateForCommit(MenuSnapshotDto original, MenuSnapshotDto stabilized)
    {
        if (original.Categories.Count > 0 && stabilized.Categories.Count == 0)
        {
            return "Menu snapshot had categories but none were valid to commit.";
        }

        if (original.Products.Count > 0 &&
            stabilized.Categories.Count > 0 &&
            stabilized.Products.Count == 0)
        {
            return "Menu products could not be linked to categories — kept previous menu.";
        }

        var orphanRatio = original.Products.Count == 0
            ? 0d
            : 1d - (stabilized.Products.Count / (double)original.Products.Count);
        if (original.Products.Count >= 5 && orphanRatio > 0.5d)
        {
            return $"Menu replace rejected — {stabilized.Products.Count}/{original.Products.Count} products linked (too many orphans).";
        }

        return null;
    }
}

public sealed record CacheReplaceResult(bool Applied, string? Message)
{
    public static CacheReplaceResult Ok(string? message = null) => new(true, message);

    public static CacheReplaceResult Kept(string message) => new(false, message);
}
