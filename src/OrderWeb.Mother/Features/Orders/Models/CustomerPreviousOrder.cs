namespace POS_in_NET.Models;

public sealed class CustomerPreviousOrder
{
    public int OrderDatabaseId { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string LocalLifecycleState { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public string? OrderNotes { get; set; }
    public string ItemsText { get; set; } = string.Empty;
    public bool IsMostRecent { get; set; }

    public string OrderReferenceDisplay => string.IsNullOrWhiteSpace(OrderNumber)
        ? $"Order #{OrderDatabaseId}"
        : $"Order #{OrderNumber}";

    public string DateDisplay => CreatedAt.ToString("ddd dd MMM yyyy · HH:mm");

    public string OrderTypeDisplay => OrderType.Equals("delivery", StringComparison.OrdinalIgnoreCase)
        ? "Delivery"
        : "Collection";

    public string TotalDisplay => $"£{TotalAmount:F2}";

    public bool HasOrderNotes => !string.IsNullOrWhiteSpace(OrderNotes);

    public string OrderNotesDisplay => HasOrderNotes ? $"Order note: {OrderNotes}" : string.Empty;

    public string StatusDisplay
    {
        get
        {
            if (RefundAmount > 0m)
            {
                return RefundAmount >= TotalAmount ? "Refunded" : "Partially refunded";
            }

            if (LocalLifecycleState.Equals("paid", StringComparison.OrdinalIgnoreCase))
            {
                return "Paid";
            }

            return Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
                ? "Completed"
                : "Sent";
        }
    }

    public string StatusColor => RefundAmount > 0m ? "#B91C1C" : "#047857";
}
