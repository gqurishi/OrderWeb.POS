using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyFirstMauiApp.Models.FoodMenu
{
    public class MenuCategory
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ParentId { get; set; }
        public string? ParentCategoryName { get; set; }
        public bool IsSelected { get; set; }
        public int DisplayOrder { get; set; }
        public bool Active { get; set; } = true;
        public string Color { get; set; } = "#3B82F6";
        public string Icon { get; set; } = "🍽️";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class Addon
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    public class MenuItemAddon : Addon
    {
    }

    public class MenuItemComponent
    {
        public int Id { get; set; }
        public string MenuItemId { get; set; } = string.Empty;
        public string ComponentName { get; set; } = string.Empty;
        public decimal ComponentPrice { get; set; }
        public decimal VatRate { get; set; }
        public string ComponentType { get; set; } = "HotFood";
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class ItemComponent
    {
        public string Id { get; set; } = string.Empty;
        public string MenuItemId { get; set; } = string.Empty;
        public string ComponentName { get; set; } = string.Empty;
        public decimal ComponentCost { get; set; }
        public decimal VatRate { get; set; }
        public string ComponentType { get; set; } = "HotFood";
        public int DisplayOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class MenuItemQuickNote
    {
        public string Id { get; set; } = string.Empty;
        public string MenuItemId { get; set; } = string.Empty;
        public string NoteText { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class PredefinedComment
    {
        public string Id { get; set; } = string.Empty;
        public string CommentText { get; set; } = string.Empty;
        public string? Category { get; set; }
        public int DisplayOrder { get; set; }
        public bool Active { get; set; } = true;
        public string Color { get; set; } = "#64748B";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class PredefinedNote
    {
        public string Id { get; set; } = string.Empty;
        public string NoteText { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string Priority { get; set; } = "normal";
        public int DisplayOrder { get; set; }
        public bool Active { get; set; } = true;
        public string Color { get; set; } = "#64748B";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class MealDealCategoryRule
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public int MinSelections { get; set; }
        public int MaxSelections { get; set; } = 1;
        public List<string> MenuItemIds { get; set; } = new();
    }

    public class MealDeal
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string Color { get; set; } = "#3B82F6";
        public bool Active { get; set; } = true;
        public int DisplayOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public List<MealDealCategoryRule> Categories { get; set; } = new();

        public string CategoriesJson => JsonSerializer.Serialize(Categories);

        public static List<MealDealCategoryRule> ParseCategories(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<MealDealCategoryRule>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<MealDealCategoryRule>>(json) ?? new List<MealDealCategoryRule>();
            }
            catch
            {
                return new List<MealDealCategoryRule>();
            }
        }
    }

    public class FoodMenuItem
    {
        public string Id { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public decimal? PriceDineIn { get; set; }
        public decimal? PriceTakeaway { get; set; }
        public string Color { get; set; } = "#3B82F6";
        public int DisplayOrder { get; set; }
        public bool IsFeatured { get; set; }
        public int? PreparationTime { get; set; }
        public decimal VatRate { get; set; }
        public string VatType { get; set; } = "Standard";
        public bool IsVatExempt { get; set; }
        public string? VatNotes { get; set; }
        public bool PrintInRed { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string VatConfigType { get; set; } = "standard";
        public string VatCategory { get; set; } = "HotFood";
        public decimal CalculatedVatRate { get; set; }
        public string? LabelText { get; set; }
        public bool PrintComponentLabels { get; set; }
        public string? ComponentLabelsJson { get; set; }
        public string? PrintGroupId { get; set; }

        public List<Addon> Addons { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public List<MenuItemComponent> Components { get; set; } = new();

        [JsonIgnore]
        public bool HasComponents => Components.Count > 0;

        [JsonIgnore]
        public bool IsMixedVat => string.Equals(VatConfigType, "component", StringComparison.OrdinalIgnoreCase);

        public decimal GetEffectivePrice(string? orderType)
        {
            if (string.IsNullOrWhiteSpace(orderType))
            {
                return PriceDineIn ?? PriceTakeaway ?? Price;
            }

            var normalizedOrderType = orderType.Trim().ToLowerInvariant();
            var isTakeawayFamily = normalizedOrderType is "takeaway" or "collection" or "pickup" or "delivery";

            if (isTakeawayFamily)
            {
                return PriceTakeaway ?? PriceDineIn ?? Price;
            }

            // Default to dine-in/table/in-house pricing for all other order types.
            return PriceDineIn ?? PriceTakeaway ?? Price;
        }

        public string? AddonsJson => Addons.Count > 0 ? JsonSerializer.Serialize(Addons) : null;
        public string? TagsJson => Tags.Count > 0 ? JsonSerializer.Serialize(Tags) : null;

        public static List<Addon> ParseAddons(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<Addon>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<Addon>>(json) ?? new List<Addon>();
            }
            catch
            {
                return new List<Addon>();
            }
        }

        public static List<string> ParseTags(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }
    }

    public class ComponentVATBreakdown
    {
        public string ComponentName { get; set; } = string.Empty;
        public decimal NetPrice { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrossPrice { get; set; }
    }

    public class VATBreakdown
    {
        public decimal NetPrice { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrossPrice { get; set; }
        public List<ComponentVATBreakdown>? Components { get; set; }

        [JsonIgnore]
        public bool IsMixedVat => Components != null && Components.Count > 0;
    }
}
