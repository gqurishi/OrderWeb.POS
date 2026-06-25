using System;
using System.ComponentModel.DataAnnotations;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Enhanced table session status tracking
    /// </summary>
    public enum TableSessionStatus
    {
        Occupied,       // Customers just seated, haven't ordered yet
        Ordering,       // Taking order, deciding what to eat
        FoodServed,     // Food delivered, customers eating
        Payment,        // Requesting bill, processing payment
        Cleaning,       // Table being cleaned after customers leave
        Closed          // Session completed and closed
    }

    /// <summary>
    /// Represents a dining session at a restaurant table
    /// </summary>
    public class TableSession
    {
        public int Id { get; set; }
        public int TableId { get; set; }
        public string? CurrentOrderId { get; set; }
        public int? ParentSessionId { get; set; }
        public int? MergedIntoSessionId { get; set; }
            public int? LinkedOrderDbId { get; set; }
            public string? LinkedOrderId { get; set; }
            public string? LinkedOrderNumber { get; set; }
            public decimal? LinkedOrderTotalAmount { get; set; }
            public string? LinkedOrderLifecycleState { get; set; }
            public DateTime? LinkedOrderUpdatedAt { get; set; }
            public bool LinkedOrderIsOpen { get; set; }
            public bool LinkedOrderDraftAbandonedFlag { get; set; }
        
        [Required]
        [StringLength(20)]
        public string SessionNumber { get; set; } = string.Empty; // S001, S002, etc.
        
        [Range(1, 20)]
        public int PartySize { get; set; } = 1;
        
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        
        public TableSessionStatus Status { get; set; } = TableSessionStatus.Occupied;
        
        public string? CustomerNotes { get; set; }
        
        [StringLength(100)]
        public string? SpecialOccasion { get; set; } // Birthday, Anniversary, etc.
        
        public int EstimatedDuration { get; set; } = 60; // Minutes
        public int? ActualDuration { get; set; } // Calculated when closed
        
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime UpdatedDate { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
        
        // Navigation properties
        public RestaurantTable? Table { get; set; }
        
        // Calculated properties
        public int MinutesOccupied => EndTime.HasValue 
            ? (int)(EndTime.Value - StartTime).TotalMinutes 
            : (int)(DateTime.Now - StartTime).TotalMinutes;
            
        public string StatusDisplay => Status switch
        {
            TableSessionStatus.Occupied => " Just Seated",
            TableSessionStatus.Ordering => " Taking Order", 
            TableSessionStatus.FoodServed => " Dining",
            TableSessionStatus.Payment => " Ready to Pay",
            TableSessionStatus.Cleaning => " Cleaning",
            TableSessionStatus.Closed => " Closed",
            _ => " Unknown"
        };
        
        public string StatusColor => Status switch
        {
            TableSessionStatus.Occupied => "#FFF3CD",      // Light yellow
            TableSessionStatus.Ordering => "#CCE5FF",      // Light blue
            TableSessionStatus.FoodServed => "#FFE0CC",    // Light orange  
            TableSessionStatus.Payment => "#E6CCFF",       // Light purple
            TableSessionStatus.Cleaning => "#FFCCCC",      // Light red
            TableSessionStatus.Closed => "#D4EDDA",        // Light green
            _ => "#F8F9FA"                                 // Light gray
        };
        
        public string TimeDisplay
        {
            get
            {
                var minutes = MinutesOccupied;
                if (minutes < 60)
                    return $"{minutes}m";
                else
                    return $"{minutes / 60}h {minutes % 60}m";
            }
        }
        
        public bool IsOvertime => MinutesOccupied > EstimatedDuration;
        
        public string OvertimeWarning => IsOvertime 
            ? $" {MinutesOccupied - EstimatedDuration} min over" 
            : "";

        public bool HasLinkedOpenOrder => LinkedOrderDbId.HasValue && !string.IsNullOrWhiteSpace(LinkedOrderId);

        public string TableDisplay => Table?.TableNumber ?? TableId.ToString();

        public string LinkedOrderDisplay => HasLinkedOpenOrder
            ? (string.IsNullOrWhiteSpace(LinkedOrderNumber) ? LinkedOrderId ?? string.Empty : LinkedOrderNumber)
            : "No open order";

        public string LinkedOrderLifecycleDisplay => string.IsNullOrWhiteSpace(LinkedOrderLifecycleState)
            ? "Unknown"
            : LinkedOrderLifecycleState.Replace("_", " ");

        public bool LinkedOrderIsStaleDraft
        {
            get
            {
                if (!HasLinkedOpenOrder)
                {
                    return false;
                }

                if (LinkedOrderDraftAbandonedFlag)
                {
                    return true;
                }

                return LinkedOrderUpdatedAt.HasValue && LinkedOrderUpdatedAt.Value <= DateTime.Now.AddHours(-2);
            }
        }

        public string LinkedOrderHealthDisplay => !HasLinkedOpenOrder
            ? "No linked order"
            : LinkedOrderIsStaleDraft ? "Needs cleanup" : "Healthy";

        public string LinkedOrderHealthColor => !HasLinkedOpenOrder
            ? "#64748B"
            : LinkedOrderIsStaleDraft ? "#DC2626" : "#10B981";

        public string SessionHealthDisplay => HasLinkedOpenOrder
            ? $"{StatusDisplay} · {LinkedOrderHealthDisplay}"
            : StatusDisplay;
    }
}