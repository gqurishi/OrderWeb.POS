using System.ComponentModel.DataAnnotations;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Customer order entity
    /// </summary>
    public class CustomerOrder
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(20)]
        public string OrderNumber { get; set; } = string.Empty;
        
        public int? TableSessionId { get; set; }
        
        public RestaurantOrderType OrderType { get; set; } = RestaurantOrderType.DineIn;
        
        [StringLength(100)]
        public string? CustomerName { get; set; }
        
        [StringLength(20)]
        public string? CustomerPhone { get; set; }
        
        public decimal Subtotal { get; set; } = 0.00m;
        
        public decimal TaxAmount { get; set; } = 0.00m;
        
        public decimal TaxRate { get; set; } = 0.0000m;
        
        public decimal DiscountAmount { get; set; } = 0.00m;
        
        public decimal TotalAmount { get; set; } = 0.00m;
        
        public RestaurantOrderStatus OrderStatus { get; set; } = RestaurantOrderStatus.New;
        
        public RestaurantPaymentStatus PaymentStatus { get; set; } = RestaurantPaymentStatus.Pending;
        
        public RestaurantPaymentMethod? PaymentMethod { get; set; }
        
        public string? SpecialInstructions { get; set; }
        
        public DateTime OrderDate { get; set; }
        
        public DateTime? EstimatedReadyTime { get; set; }
        
        public DateTime? ActualReadyTime { get; set; }
        
        public DateTime? ServedTime { get; set; }
        
        public DateTime? PaidTime { get; set; }
        
        [StringLength(100)]
        public string? CreatedBy { get; set; }

        // Navigation properties
        public TableSession? TableSession { get; set; }
        
        public List<RestaurantOrderItem> OrderItems { get; set; } = new List<RestaurantOrderItem>();
        
        public List<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();

        // Display properties
        public string DisplayOrderNumber => $"#{OrderNumber}";
        
        public string FormattedTotal => $"${TotalAmount:F2}";
        
        public string FormattedSubtotal => $"${Subtotal:F2}";
        
        public string FormattedTax => $"${TaxAmount:F2}";
        
        public string FormattedDiscount => DiscountAmount > 0 ? $"-${DiscountAmount:F2}" : "";
        
        public string TableDisplay => TableSession != null ? $"Table {TableSession.TableId}" : "Takeout";
        
        public string OrderTypeDisplay => OrderType.ToString().Replace("DineIn", "Dine-in");
        
        public string OrderStatusDisplay => OrderStatus.ToString();
        
        public string PaymentStatusDisplay => PaymentStatus.ToString();
        
        public Color OrderStatusColor => OrderStatus switch
        {
            RestaurantOrderStatus.New => Colors.Blue,
            RestaurantOrderStatus.Confirmed => Colors.Orange,
            RestaurantOrderStatus.Preparing => Colors.Purple,
            RestaurantOrderStatus.Ready => Colors.Green,
            RestaurantOrderStatus.Served => Colors.DarkGreen,
            RestaurantOrderStatus.Paid => Colors.Gray,
            RestaurantOrderStatus.Cancelled => Colors.Red,
            _ => Colors.Black
        };
        
        public Color PaymentStatusColor => PaymentStatus switch
        {
            RestaurantPaymentStatus.Pending => Colors.Orange,
            RestaurantPaymentStatus.Paid => Colors.Green,
            RestaurantPaymentStatus.Partial => Colors.Yellow,
            RestaurantPaymentStatus.Refunded => Colors.Red,
            _ => Colors.Gray
        };
        
        public string OrderTimeDisplay => OrderDate.ToString("MMM dd, yyyy HH:mm");
        
        public string EstimatedReadyDisplay => EstimatedReadyTime?.ToString("HH:mm") ?? "TBD";
        
        public int ItemsCount => OrderItems?.Count ?? 0;
        
        public int TotalQuantity => OrderItems?.Sum(x => x.Quantity) ?? 0;
        
        public TimeSpan? EstimatedPrepTime
        {
            get
            {
                if (EstimatedReadyTime.HasValue)
                    return EstimatedReadyTime.Value - OrderDate;
                return null;
            }
        }
        
        public string PrepTimeDisplay => EstimatedPrepTime?.TotalMinutes.ToString("F0") + " mins" ?? "TBD";
        
        public bool IsOverdue => EstimatedReadyTime.HasValue && 
                               DateTime.Now > EstimatedReadyTime.Value && 
                               OrderStatus != RestaurantOrderStatus.Ready && 
                               OrderStatus != RestaurantOrderStatus.Served && 
                               OrderStatus != RestaurantOrderStatus.Paid;
        
        public string CustomerDisplay
        {
            get
            {
                if (!string.IsNullOrEmpty(CustomerName))
                    return CustomerName;
                if (!string.IsNullOrEmpty(CustomerPhone))
                    return CustomerPhone;
                return "Guest";
            }
        }
    }

    /// <summary>
    /// Order type enumeration
    /// </summary>
    public enum RestaurantOrderType
    {
        DineIn,
        Takeout,
        Delivery
    }

    /// <summary>
    /// Order status enumeration
    /// </summary>
    public enum RestaurantOrderStatus
    {
        New,        // Order just placed
        Confirmed,  // Order confirmed by staff
        Preparing,  // Kitchen is preparing the order
        Ready,      // Order is ready for pickup/serving
        Served,     // Order has been served to customer
        Paid,       // Order has been paid for
        Cancelled   // Order was cancelled
    }

    /// <summary>
    /// Payment status enumeration
    /// </summary>
    public enum RestaurantPaymentStatus
    {
        Pending,  // Payment not yet received
        Paid,     // Fully paid
        Partial,  // Partially paid
        Refunded  // Payment was refunded
    }

    /// <summary>
    /// Payment method enumeration
    /// </summary>
    public enum RestaurantPaymentMethod
    {
        Cash,
        Card,
        GiftCard,
        Split  // Multiple payment methods
    }
}