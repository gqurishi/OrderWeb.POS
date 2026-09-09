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
            if (string.IsNullOrWhiteSpace(category.Name))
            {
                continue;
            }

            // Some payloads omit motherId; fall back to the snapshot int id so we still commit.
            var motherKey = string.IsNullOrWhiteSpace(category.MotherId)
                ? category.Id.ToString()
                : category.MotherId.Trim();
            if (string.IsNullOrWhiteSpace(motherKey))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"cat:{motherKey}", used);
            categoryMap[category.Id] = stableId;
            categories.Add(category with
            {
                Id = stableId,
                MotherId = motherKey,
                ParentMotherId = string.IsNullOrWhiteSpace(category.ParentMotherId)
                    ? null
                    : category.ParentMotherId.Trim()
            });
        }

        // Resolve parent Mother ids to stable local parent ids after all categories are mapped.
        var motherIdToStable = categories.ToDictionary(
            category => category.MotherId,
            category => category.Id,
            StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            if (string.IsNullOrWhiteSpace(category.ParentMotherId) ||
                !motherIdToStable.TryGetValue(category.ParentMotherId, out var parentId))
            {
                categories[i] = category with { ParentId = null };
                continue;
            }

            categories[i] = category with { ParentId = parentId };
        }

        var productMap = new Dictionary<int, int>();
        var products = new List<BootstrapProduct>();
        foreach (var product in snapshot.Products)
        {
            var productMotherKey = string.IsNullOrWhiteSpace(product.MotherId)
                ? product.Id.ToString()
                : product.MotherId.Trim();
            if (string.IsNullOrWhiteSpace(productMotherKey) ||
                string.IsNullOrWhiteSpace(product.Name) ||
                !categoryMap.TryGetValue(product.CategoryId, out var categoryId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"prod:{productMotherKey}", used);
            productMap[product.Id] = stableId;
            products.Add(product with { Id = stableId, MotherId = productMotherKey, CategoryId = categoryId });
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

        var variants = new List<BootstrapVariant>();
        foreach (var variant in snapshot.Variants)
        {
            if (string.IsNullOrWhiteSpace(variant.MotherId) ||
                string.IsNullOrWhiteSpace(variant.Name) ||
                !productMap.TryGetValue(variant.ProductId, out var productId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"var:{variant.MotherId}", used);
            variants.Add(variant with { Id = stableId, ProductId = productId });
        }

        var quickNotes = new List<BootstrapQuickNote>();
        foreach (var note in snapshot.QuickNotes ?? Array.Empty<BootstrapQuickNote>())
        {
            if (string.IsNullOrWhiteSpace(note.MotherId) ||
                string.IsNullOrWhiteSpace(note.NoteText) ||
                !productMap.TryGetValue(note.ProductId, out var productId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"qnote:{note.MotherId}", used);
            quickNotes.Add(note with { Id = stableId, ProductId = productId, NoteText = note.NoteText.Trim() });
        }

        var mealDealMap = new Dictionary<int, int>();
        var mealDeals = new List<BootstrapMealDeal>();
        foreach (var deal in snapshot.MealDeals)
        {
            if (string.IsNullOrWhiteSpace(deal.MotherId) || string.IsNullOrWhiteSpace(deal.Name))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"mealdeal:{deal.MotherId}", used);
            mealDealMap[deal.Id] = stableId;
            mealDeals.Add(deal with { Id = stableId });
        }

        var mealDealChoices = new List<BootstrapMealDealChoice>();
        foreach (var choice in snapshot.MealDealChoices)
        {
            if (string.IsNullOrWhiteSpace(choice.MotherId) ||
                string.IsNullOrWhiteSpace(choice.Name) ||
                !mealDealMap.TryGetValue(choice.MealDealId, out var mealDealId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"mealdeal-choice:{choice.MotherId}", used);
            mealDealChoices.Add(choice with { Id = stableId, MealDealId = mealDealId });
        }

        var mealDealRules = new List<BootstrapMealDealCategoryRule>();
        foreach (var rule in snapshot.MealDealCategoryRules)
        {
            if (string.IsNullOrWhiteSpace(rule.MotherId) ||
                string.IsNullOrWhiteSpace(rule.Name) ||
                !mealDealMap.TryGetValue(rule.MealDealId, out var mealDealId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"mealdeal-rule:{rule.MotherId}", used);
            mealDealRules.Add(rule with
            {
                Id = stableId,
                MealDealId = mealDealId,
                MenuItemMotherIds = rule.MenuItemMotherIds ?? Array.Empty<string>()
            });
        }

        var tastingMap = new Dictionary<int, int>();
        var tastingMenus = new List<BootstrapTastingMenu>();
        foreach (var tasting in snapshot.TastingMenus)
        {
            if (string.IsNullOrWhiteSpace(tasting.MotherId) || string.IsNullOrWhiteSpace(tasting.Name))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"tasting:{tasting.MotherId}", used);
            tastingMap[tasting.Id] = stableId;
            tastingMenus.Add(tasting with { Id = stableId });
        }

        var tastingOptions = new List<BootstrapTastingMenuOption>();
        foreach (var option in snapshot.TastingMenuOptions)
        {
            if (string.IsNullOrWhiteSpace(option.MotherId) ||
                !tastingMap.TryGetValue(option.TastingMenuId, out var tastingMenuId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"tasting-opt:{option.MotherId}", used);
            tastingOptions.Add(option with { Id = stableId, TastingMenuId = tastingMenuId });
        }

        var courseMap = new Dictionary<int, int>();
        var tastingCourses = new List<BootstrapTastingMenuCourse>();
        foreach (var course in snapshot.TastingMenuCourses)
        {
            if (string.IsNullOrWhiteSpace(course.MotherId) ||
                string.IsNullOrWhiteSpace(course.Name) ||
                !tastingMap.TryGetValue(course.TastingMenuId, out var tastingMenuId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"tasting-course:{course.MotherId}", used);
            courseMap[course.Id] = stableId;
            tastingCourses.Add(course with { Id = stableId, TastingMenuId = tastingMenuId });
        }

        var tastingChoices = new List<BootstrapTastingMenuChoice>();
        foreach (var choice in snapshot.TastingMenuChoices)
        {
            if (string.IsNullOrWhiteSpace(choice.MotherId) ||
                string.IsNullOrWhiteSpace(choice.Name) ||
                !courseMap.TryGetValue(choice.CourseId, out var courseId))
            {
                continue;
            }

            var stableId = StableEntityId.FromKey($"tasting-choice:{choice.MotherId}", used);
            tastingChoices.Add(choice with { Id = stableId, CourseId = courseId });
        }

        return new MenuSnapshotDto(
            snapshot.Version,
            categories,
            products,
            prices,
            groups,
            modifiers,
            productModifiers,
            variants,
            mealDeals,
            mealDealChoices,
            mealDealRules,
            tastingMenus,
            tastingOptions,
            tastingCourses,
            tastingChoices,
            quickNotes);
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
