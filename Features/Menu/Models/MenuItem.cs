using System.ComponentModel.DataAnnotations;
using MyFirstMauiApp.Models.FoodMenu;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Individual menu item entity (dishes, products)
    /// </summary>
    public class RestaurantMenuItem
    {
        public int Id { get; set; }
        
        public int CategoryId { get; set; }
        
        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public decimal Price { get; set; }
        
        [StringLength(255)]
        public string? ImagePath { get; set; }
        
        public int PrepTime { get; set; } = 15; // Minutes
        
        public int? Calories { get; set; }
        
        public bool IsSpicy { get; set; } = false;
        
        public bool IsVegetarian { get; set; } = false;
        
        public bool IsVegan { get; set; } = false;
        
        public bool IsGlutenFree { get; set; } = false;
        
        public bool IsAvailable { get; set; } = true;
        
        public int SortOrder { get; set; }
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime UpdatedDate { get; set; }

        // Navigation properties
        public MenuCategory? Category { get; set; }
        
        public List<MenuItemModifier> Modifiers { get; set; } = new List<MenuItemModifier>();

        // Display properties
        public string DisplayName
        {
            get
            {
                var indicators = "";
                if (IsSpicy) indicators += "🌶️ ";
                if (IsVegan) indicators += "🌿 ";
                else if (IsVegetarian) indicators += "🌱 ";
                if (IsGlutenFree) indicators += "🚫🌾 ";
                
                return indicators + Name;
            }
        }
        
        public string FormattedPrice => $"${Price:F2}";
        
        public string PrepTimeDisplay => PrepTime == 1 ? "1 min" : $"{PrepTime} mins";
        
        public string CategoryName => Category?.Name ?? "Unknown";
        
        public string CategoryIcon => Category?.Icon ?? "🍽️";
        
        public Color StatusColor => IsAvailable ? Colors.Green : Colors.Red;
        
        public string StatusText => IsAvailable ? "Available" : "Out of Stock";
        
        public string CaloriesDisplay => Calories.HasValue ? $"{Calories} cal" : "";
        
        public bool HasDietaryInfo => IsSpicy || IsVegetarian || IsVegan || IsGlutenFree;
        
        public List<string> DietaryTags
        {
            get
            {
                var tags = new List<string>();
                if (IsSpicy) tags.Add("Spicy 🌶️");
                if (IsVegan) tags.Add("Vegan 🌿");
                else if (IsVegetarian) tags.Add("Vegetarian 🌱");
                if (IsGlutenFree) tags.Add("Gluten-Free 🚫🌾");
                return tags;
            }
        }
        
        public int ModifiersCount => Modifiers?.Count ?? 0;
        
        public bool HasModifiers => ModifiersCount > 0;
        
        public string ShortDescription => Description?.Length > 50 ? 
            Description.Substring(0, 47) + "..." : 
            Description ?? "";
    }
}