using System.ComponentModel.DataAnnotations;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Individual items within a customer order
    /// </summary>
    public class RestaurantOrderItem
    {
        public int Id { get; set; }
        
        public int OrderId { get; set; }
        
        public int MenuItemId { get; set; }
        
        public int Quantity { get; set; } = 1;
        
        public decimal UnitPrice { get; set; }
        
        public decimal TotalPrice { get; set; }
        
        public string? SpecialInstructions { get; set; }
        
        public RestaurantItemStatus ItemStatus { get; set; } = RestaurantItemStatus.Ordered;
        
        public DateTime CreatedDate { get; set; }

        // Navigation properties
        public CustomerOrder? Order { get; set; }
        
        public RestaurantMenuItem? MenuItem { get; set; }
        
        public List<RestaurantOrderItemModifier> AppliedModifiers { get; set; } = new List<RestaurantOrderItemModifier>();

        // Display properties
        public string ItemName => MenuItem?.Name ?? "Unknown Item";
        
        public string FormattedUnitPrice => $"${UnitPrice:F2}";
        
        public string FormattedTotalPrice => $"${TotalPrice:F2}";
        
        public string QuantityDisplay => Quantity == 1 ? "" : $"{Quantity}x ";
        
        public string DisplayName => $"{QuantityDisplay}{ItemName}";
        
        public string ItemStatusDisplay => ItemStatus.ToString();
        
        public Color ItemStatusColor => ItemStatus switch
        {
            RestaurantItemStatus.Ordered => Colors.Blue,
            RestaurantItemStatus.Preparing => Colors.Orange,
            RestaurantItemStatus.Ready => Colors.Green,
            RestaurantItemStatus.Served => Colors.Gray,
            _ => Colors.Black
        };
        
        public string StatusIcon => ItemStatus switch
        {
            RestaurantItemStatus.Ordered => "📝",
            RestaurantItemStatus.Preparing => "👨‍🍳",
            RestaurantItemStatus.Ready => "✅",
            RestaurantItemStatus.Served => "🍽️",
            _ => "❓"
        };
        
        public int ModifiersCount => AppliedModifiers?.Count ?? 0;
        
        public bool HasModifiers => ModifiersCount > 0;
        
        public bool HasInstructions => !string.IsNullOrWhiteSpace(SpecialInstructions);
        
        public decimal ModifierTotal => AppliedModifiers?.Sum(x => x.PriceAdjustment) ?? 0;
        
        public string ModifiersDisplay
        {
            get
            {
                if (!HasModifiers) return "";
                
                return string.Join(", ", AppliedModifiers.Select(x => x.ModifierName));
            }
        }
        
        public List<string> ModifiersList => AppliedModifiers?.Select(x => x.ModifierName).ToList() ?? new List<string>();
        
        public string PrepTime => MenuItem?.PrepTimeDisplay ?? "Unknown";
        
        public bool IsSpicy => MenuItem?.IsSpicy ?? false;
        
        public bool IsVegetarian => MenuItem?.IsVegetarian ?? false;
        
        public string DietaryInfo
        {
            get
            {
                var info = new List<string>();
                if (MenuItem?.IsSpicy == true) info.Add("🌶️");
                if (MenuItem?.IsVegan == true) info.Add("🌿");
                else if (MenuItem?.IsVegetarian == true) info.Add("🌱");
                if (MenuItem?.IsGlutenFree == true) info.Add("🚫🌾");
                return string.Join(" ", info);
            }
        }
    }

    /// <summary>
    /// Status of individual order items
    /// </summary>
    public enum RestaurantItemStatus
    {
        Ordered,   // Item ordered, not yet started
        Preparing, // Kitchen is preparing this item
        Ready,     // Item is ready to serve
        Served     // Item has been served to customer
    }

    /// <summary>
    /// Applied modifiers for order items
    /// </summary>
    public class RestaurantOrderItemModifier
    {
        public int Id { get; set; }
        
        public int OrderItemId { get; set; }
        
        public int ModifierId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string ModifierName { get; set; } = string.Empty; // Store name at time of order
        
        public decimal PriceAdjustment { get; set; } = 0.00m;

        // Navigation properties
        public RestaurantOrderItem? OrderItem { get; set; }
        
        public ItemModifier? Modifier { get; set; }

        // Display properties
        public string DisplayName
        {
            get
            {
                if (PriceAdjustment > 0)
                    return $"{ModifierName} (+${PriceAdjustment:F2})";
                else if (PriceAdjustment < 0)
                    return $"{ModifierName} (-${Math.Abs(PriceAdjustment):F2})";
                else
                    return ModifierName;
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
                    return "";
            }
        }
    }

    /// <summary>
    /// Payment transaction entity
    /// </summary>
    public class PaymentTransaction
    {
        public int Id { get; set; }
        
        public int OrderId { get; set; }
        
        public RestaurantTransactionType TransactionType { get; set; } = RestaurantTransactionType.Payment;
        
        public RestaurantPaymentMethod PaymentMethod { get; set; }
        
        public decimal Amount { get; set; }
        
        public decimal? AmountReceived { get; set; }
        
        public decimal? ChangeGiven { get; set; }
        
        public decimal TipAmount { get; set; } = 0.00m;
        
        [StringLength(4)]
        public string? CardLastFour { get; set; }
        
        [StringLength(100)]
        public string? TransactionReference { get; set; }
        
        [StringLength(100)]
        public string? ProcessedBy { get; set; }
        
        public DateTime TransactionDate { get; set; }
        
        public string? Notes { get; set; }

        // Navigation property
        public CustomerOrder? Order { get; set; }

        // Display properties
        public string FormattedAmount => $"${Amount:F2}";
        
        public string FormattedTip => TipAmount > 0 ? $"${TipAmount:F2}" : "";
        
        public string TransactionTypeDisplay => TransactionType.ToString();
        
        public string PaymentMethodDisplay => PaymentMethod.ToString();
        
        public string TransactionDateDisplay => TransactionDate.ToString("MMM dd, yyyy HH:mm");
        
        public string CardDisplay => !string.IsNullOrEmpty(CardLastFour) ? $"****{CardLastFour}" : "";
        
        public bool IsCashTransaction => PaymentMethod == RestaurantPaymentMethod.Cash;
        
        public string ChangeDisplay => ChangeGiven.HasValue && ChangeGiven > 0 ? $"${ChangeGiven:F2}" : "";
        
        public string ReceivedDisplay => AmountReceived.HasValue ? $"${AmountReceived:F2}" : "";
    }

    /// <summary>
    /// Transaction type enumeration
    /// </summary>
    public enum RestaurantTransactionType
    {
        Payment,
        Refund,
        Tip
    }
}