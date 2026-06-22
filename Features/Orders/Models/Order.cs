using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace POS_in_NET.Models;

public class Order
{
    public int Id { get; set; }
    
    [Required]
    public string OrderId { get; set; } = string.Empty; // External order ID from online system
    
    // OrderWeb.net fields
    public string? OrderNumber { get; set; } // Human readable order number (KIT-7479)
    public string? CloudOrderId { get; set; } // UUID from OrderWeb.net
    
    [Required]
    public string CustomerName { get; set; } = string.Empty;
    
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerAddress { get; set; }
    
    // Financial fields
    public decimal TotalAmount { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal TaxAmount { get; set; }
    
    // Order details
    public string? OrderType { get; set; } // pickup/delivery/table
    public string SourceChannel { get; set; } = "local"; // local/web
    public int? TableSessionId { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime? ScheduledTime { get; set; }
    public string? SpecialInstructions { get; set; }

    // Local POS lifecycle fields (Phase 1)
    public LocalLifecycleState LocalLifecycleState { get; set; } = LocalLifecycleState.Draft;
    public bool IsOpen { get; set; } = true;
    public string? VoidReason { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public DateTime? PaidAt { get; set; }

    // Operational summary fields for fast dashboards (Phase 8)
    public DateTime? FirstSentAt { get; set; }
    public DateTime? LastSentAt { get; set; }
    public int SendAttemptCount { get; set; }
    public int SendFailureCount { get; set; }
    public int? SendLatencyMs { get; set; }
    public DateTime? FirstPaymentAttemptAt { get; set; }
    public int PaymentAttemptCount { get; set; }
    public int? PaymentCompletionSeconds { get; set; }
    public DateTime? OperationalLastEventAt { get; set; }
    public bool DraftAbandonedFlag { get; set; }
    public DateTime? DraftAbandonedAt { get; set; }
    
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    
    // Order timing for automatic status transitions
    public DateTime? KitchenTime { get; set; }        // When order goes to kitchen (auto after 0 min)
    public DateTime? PreparingTime { get; set; }      // When order starts preparing (auto after 2 min)
    public DateTime? ReadyTime { get; set; }          // When order is ready (auto after 10 min)
    public DateTime? DeliveringTime { get; set; }     // When delivery person takes order (manual)
    public DateTime? CompletedTime { get; set; }      // When order is completed (manual)
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpectedUpdatedAt { get; set; }
    
    // Order items
    public List<OrderItem> Items { get; set; } = new();
    
    // UI Display Properties
    public bool HasSpecialInstructions => !string.IsNullOrWhiteSpace(SpecialInstructions);
    public bool HasDeliveryFee => DeliveryFee > 0;
    public bool CanComplete => Status != OrderStatus.Completed && Status != OrderStatus.Cancelled;

    public string LocalLifecycleDisplay => LocalLifecycleState switch
    {
        LocalLifecycleState.Draft => "Draft",
        LocalLifecycleState.Active => "Active",
        LocalLifecycleState.SentPartial => "Sent Partial",
        LocalLifecycleState.SentFull => "Sent Full",
        LocalLifecycleState.PaymentPartial => "Payment Partial",
        LocalLifecycleState.Paid => "Paid",
        LocalLifecycleState.Voided => "Voided",
        _ => "Draft"
    };

    public string LocalLifecycleColor => LocalLifecycleState switch
    {
        LocalLifecycleState.Draft => "#0EA5E9",
        LocalLifecycleState.Active => "#2563EB",
        LocalLifecycleState.SentPartial => "#F59E0B",
        LocalLifecycleState.SentFull => "#D97706",
        LocalLifecycleState.PaymentPartial => "#8B5CF6",
        LocalLifecycleState.Paid => "#10B981",
        LocalLifecycleState.Voided => "#DC2626",
        _ => "#64748B"
    };

    public bool IsStaleDraft
    {
        get
        {
            var isDraftLifecycle = LocalLifecycleState is LocalLifecycleState.Draft or LocalLifecycleState.Active;
            if (!isDraftLifecycle || !IsOpen)
            {
                return false;
            }

            if (DraftAbandonedFlag)
            {
                return true;
            }

            return UpdatedAt != default
                && UpdatedAt <= DateTime.Now.AddHours(-2)
                && SendAttemptCount == 0
                && PaymentAttemptCount == 0;
        }
    }

    public string DraftHealthDisplay => IsStaleDraft ? "Stale draft" : "Healthy";

    public string DraftHealthColor => IsStaleDraft ? "#DC2626" : "#10B981";

    public string LifecycleSummary => $"{LocalLifecycleDisplay} · {OrderType ?? "order"}";
    
    // Additional data from online system (JSON)
    public string? OrderData { get; set; }
    
    // Payment information
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? TransactionId { get; set; }
    
    // Print tracking (NEW for acknowledgment system)
    public string PrintStatus { get; set; } = "pending"; // pending, sent_to_pos, printing, printed, failed
    public DateTime? PrintedAt { get; set; }
    public string? PrintError { get; set; }
    public string? PrintDeviceId { get; set; }
}

public class OrderItem
{
    public int Id { get; set; }
    public string OrderId { get; set; } = string.Empty; // Changed to string to match UUID
    public string? ClientItemId { get; set; }
    
    // OrderWeb.net fields
    public int? CloudItemId { get; set; }
    public string? MenuItemId { get; set; }
    public string? PrintGroupId { get; set; }
    
    [Required]
    public string ItemName { get; set; } = string.Empty;
    
    public int Quantity { get; set; }
    public decimal? ItemPrice { get; set; } // Renamed from UnitPrice
    public decimal TotalPrice => (ItemPrice ?? 0) * Quantity + Addons.Sum(a => (a.AddonPrice ?? 0) * Quantity);
    
    // UI Display Properties
    public string QuantityDisplay => $"{Quantity}x";
    
    public string? SpecialInstructions { get; set; }

    public string SendStatus { get; set; } = "queued";
    public string? SendBatchId { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public string? FailureReason { get; set; }
    
    // Addons for this item
    public List<OrderItemAddon> Addons { get; set; } = new();
    
    // Navigation property
    public Order? Order { get; set; }
}

public class OrderItemAddon
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    
    public string? AddonId { get; set; }
    public string AddonName { get; set; } = string.Empty;
    public decimal? AddonPrice { get; set; }
    public int Quantity { get; set; } = 1;
    
    // Navigation property
    public OrderItem? OrderItem { get; set; }
}

public class OrderPayment
{
    public int Id { get; set; }
    public int OrderDbId { get; set; }
    public int AttemptNo { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "GBP";
    public string Status { get; set; } = "attempted";
    public string? Reference { get; set; }
    public decimal TipAmount { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class OrderEvent
{
    public int Id { get; set; }
    public int OrderDbId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ActorType { get; set; } = "system";
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    public DateTime EventAt { get; set; }
    public string? PayloadJson { get; set; }
}

public class OrderItemSendTracking
{
    public int Id { get; set; }
    public int OrderDbId { get; set; }
    public int OrderItemDbId { get; set; }
    public string SendBatchId { get; set; } = string.Empty;
    public string StationType { get; set; } = "kitchen";
    public string? PrintGroupId { get; set; }
    public string? RouteTarget { get; set; }
    public string SendStatus { get; set; } = "queued";
    public DateTime? SentAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public string? FailureReason { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class OperationalMetricSummary
{
    public int SampleCount { get; set; }
    public double Average { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
}

public sealed class VoidAuditEntry
{
    public int OrderDbId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime EventAt { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class OperationalAnalyticsSnapshot
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public OperationalMetricSummary SendLatency { get; set; } = new();
    public OperationalMetricSummary PaymentCompletionTime { get; set; } = new();
    public int DraftAbandonmentCount { get; set; }
    public List<VoidAuditEntry> VoidAudits { get; set; } = new();
}

public sealed class SendSupportRow
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string LifecycleState { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? PrintGroupId { get; set; }
    public string? RouteTarget { get; set; }
    public string SendStatus { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum OrderStatus
{
    New,         // Just received from online system
    Kitchen,     // Sent to kitchen (auto after order received)
    Preparing,   // Being prepared (auto after 2 minutes)
    Ready,       // Ready for delivery (auto after 10 minutes)
    Delivering,  // Out for delivery (manual by delivery person)
    Completed,   // Delivered successfully (manual)
    Cancelled    // Order cancelled
}

public enum LocalLifecycleState
{
    Draft,
    Active,
    SentPartial,
    SentFull,
    PaymentPartial,
    Paid,
    Voided
}

public enum SyncStatus
{
    Synced,      // Successfully synced with online system
    Pending,     // Waiting to be synced
    Failed,      // Sync failed, will retry
    Conflict     // Conflict detected, needs manual resolution
}

public enum PaymentStatus
{
    Pending,     // Payment not yet processed
    Paid,        // Payment completed
    Failed,      // Payment failed
    Refunded     // Payment refunded
}
