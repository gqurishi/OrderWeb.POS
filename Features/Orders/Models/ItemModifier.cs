using System.ComponentModel.DataAnnotations;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Modifier/Add-on options for menu items
    /// </summary>
    public class ItemModifier
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [StringLength(255)]
        public string? Description { get; set; }
        
        public decimal PriceAdjustment { get; set; } = 0.00m;
        
        public ModifierType ModifierType { get; set; } = ModifierType.Addition;
        
        public bool IsRequired { get; set; } = false;
        
        public int SortOrder { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedDate { get; set; }

        // Display properties
        public string DisplayName
        {
            get
            {
                if (PriceAdjustment > 0)
                    return $"{Name} (+${PriceAdjustment:F2})";
                else if (PriceAdjustment < 0)
                    return $"{Name} (-${Math.Abs(PriceAdjustment):F2})";
                else
                    return Name;
            }
        }
        
        public string PriceDisplay
        {
            get
            {
                if (PriceAdjustment > 0)
                    return $"+${PriceAdjustment:F2}";
                else if (PriceAdjustment < 0)
                    return $"-${Math.Abs(PriceAdjustment):F2}";
                else
                    return "No charge";
            }
        }
        
        public string TypeDisplay => ModifierType.ToString();
        
        public Color TypeColor => ModifierType switch
        {
            ModifierType.Addition => Colors.Green,
            ModifierType.Substitution => Colors.Orange,
            ModifierType.Removal => Colors.Red,
            ModifierType.Size => Colors.Blue,
            _ => Colors.Gray
        };
        
        public string TypeIcon => ModifierType switch
        {
            ModifierType.Addition => "➕",
            ModifierType.Substitution => "🔄",
            ModifierType.Removal => "➖",
            ModifierType.Size => "📏",
            _ => "⚙️"
        };
        
        public string RequiredDisplay => IsRequired ? "Required" : "Optional";
    }

    /// <summary>
    /// Types of modifiers available
    /// </summary>
    public enum ModifierType
    {
        Addition,      // Extra cheese, bacon
        Substitution,  // Gluten-free bread, side salad instead of fries
        Removal,       // No onions, no tomatoes
        Size          // Small, medium, large
    }

    /// <summary>
    /// Link between menu items and their available modifiers
    /// </summary>
    public class MenuItemModifier
    {
        public int Id { get; set; }
        
        public int MenuItemId { get; set; }
        
        public int ModifierId { get; set; }
        
        public bool IsDefault { get; set; } = false;

        // Navigation properties
        public RestaurantMenuItem? MenuItem { get; set; }
        
        public ItemModifier? Modifier { get; set; }

        // Display properties
        public string ModifierName => Modifier?.Name ?? "Unknown";
        
        public decimal PriceAdjustment => Modifier?.PriceAdjustment ?? 0;
        
        public ModifierType ModifierType => Modifier?.ModifierType ?? ModifierType.Addition;
        
        public bool IsRequired => Modifier?.IsRequired ?? false;
        
        public string DisplayName => Modifier?.DisplayName ?? "Unknown";
    }
}