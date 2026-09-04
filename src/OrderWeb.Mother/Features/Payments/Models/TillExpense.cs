namespace POS_in_NET.Models;

public enum TillExpenseCategory
{
    Shopping,
    Delivery,
    CashCount,
    Other
}

public enum TillExpenseStatus
{
    Pending,
    Settled,
    Void
}

public sealed class TillExpense
{
    public int Id { get; set; }
    public TillExpenseCategory Category { get; set; }
    public TillExpenseStatus Status { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal AmountTaken { get; set; }
    public decimal? AmountSpent { get; set; }
    public decimal? AmountReturned { get; set; }
    public decimal NetAmount { get; set; }
    public decimal? CountedCash { get; set; }
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string SourceArea { get; set; } = string.Empty;
    public int? RecordedByUserId { get; set; }
    public string RecordedByName { get; set; } = string.Empty;
    public int? SettledByUserId { get; set; }
    public string? SettledByName { get; set; }
    public DateTime? SettledAt { get; set; }
    public bool Voided { get; set; }
    public string? VoidReason { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string CategoryDisplay => Category switch
    {
        TillExpenseCategory.Shopping => "Shopping",
        TillExpenseCategory.Delivery => "Delivery",
        TillExpenseCategory.CashCount => "Cash Count",
        TillExpenseCategory.Other => "Other",
        _ => Category.ToString()
    };

    public string StatusDisplay => Status switch
    {
        TillExpenseStatus.Pending => "Pending",
        TillExpenseStatus.Settled => "Settled",
        TillExpenseStatus.Void => "Void",
        _ => Status.ToString()
    };

    public string SummaryDisplay => Status == TillExpenseStatus.Pending
        ? $"{Description} · £{AmountTaken:F2} out"
        : Category == TillExpenseCategory.CashCount && CountedCash.HasValue
            ? $"Counted £{CountedCash.Value:F2}"
            : NetAmount > 0
                ? $"{Description} · net £{NetAmount:F2}"
                : Description;
}

public sealed class ShoppingTakeRequest
{
    public string ItemName { get; set; } = string.Empty;
    public decimal AmountTaken { get; set; }
    public string SourceArea { get; set; } = "pos";
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
}

public sealed class ShoppingSettleRequest
{
    public int TillExpenseId { get; set; }
    public decimal AmountSpent { get; set; }
}

public sealed class DeliveryPayoutRequest
{
    public decimal Amount { get; set; }
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string SourceArea { get; set; } = "pos";
}

public sealed class CashCountRequest
{
    public decimal CountedCash { get; set; }
    public string? Notes { get; set; }
    public string SourceArea { get; set; } = "pos";
}

public sealed class OtherTillExpenseRequest
{
    public string Reason { get; set; } = string.Empty;
    public decimal? AmountOut { get; set; }
    public string SourceArea { get; set; } = "pos";
}
