namespace OrderWeb.SharedUI.Payments;

/// <summary>Host-neutral models for the SharedUI payment wizard (Mother chrome).</summary>
public enum PaymentSplitMode
{
    Full,
    EqualSplit,
    CustomAmount,
    PayByItems
}

public sealed class PaymentPayByItemsSelection
{
    public string ItemId { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
}

public sealed class PaymentPayByItemsLine
{
    public string ItemId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public int Quantity { get; init; }
    /// <summary>Unit price including VAT when available.</summary>
    public decimal UnitAmount { get; init; }
}

public sealed class PaymentPayByItemsResult
{
    public bool Success { get; init; }
    public decimal Amount { get; init; }
    public IReadOnlyList<PaymentPayByItemsSelection> SelectedItems { get; init; } =
        Array.Empty<PaymentPayByItemsSelection>();
}

/// <summary>
/// Split / portion plan after Payment Setup. Shared by Mother (payment loop) and Client (single tender).
/// </summary>
public sealed class PaymentSplitPlan
{
    private PaymentSplitPlan(
        decimal totalAmount,
        int totalParts,
        PaymentSplitMode mode,
        decimal? customAmount = null,
        IReadOnlyList<PaymentPayByItemsSelection>? selectedItems = null)
    {
        TotalAmount = totalAmount;
        TotalParts = Math.Max(1, totalParts);
        CurrentPart = 1;
        Mode = mode;
        CustomAmount = customAmount;
        SelectedItems = selectedItems ?? Array.Empty<PaymentPayByItemsSelection>();
        AmountPerPart = TotalParts <= 1
            ? totalAmount
            : Math.Round(totalAmount / TotalParts, 2, MidpointRounding.AwayFromZero);
    }

    public decimal TotalAmount { get; }
    public int TotalParts { get; }
    public int CurrentPart { get; private set; }
    public decimal AmountPerPart { get; }
    public PaymentSplitMode Mode { get; }
    public decimal? CustomAmount { get; }
    public IReadOnlyList<PaymentPayByItemsSelection> SelectedItems { get; }

    public bool IsSplit => Mode == PaymentSplitMode.EqualSplit;
    public bool IsCustomAmount => Mode == PaymentSplitMode.CustomAmount;
    public bool IsPayByItems => Mode == PaymentSplitMode.PayByItems;
    public bool RequiresPaymentLine => Mode != PaymentSplitMode.Full;
    public bool TracksPartRemaining => Mode == PaymentSplitMode.EqualSplit;
    public bool StopsAfterOnePartialPayment =>
        Mode is PaymentSplitMode.CustomAmount or PaymentSplitMode.PayByItems;

    public static PaymentSplitPlan Full(decimal totalAmount) =>
        new(totalAmount, 1, PaymentSplitMode.Full);

    public static PaymentSplitPlan Equal(decimal totalAmount, int totalParts) =>
        new(totalAmount, totalParts, PaymentSplitMode.EqualSplit);

    public static PaymentSplitPlan Custom(decimal totalAmount, decimal customAmount) =>
        new(totalAmount, 1, PaymentSplitMode.CustomAmount, customAmount);

    public static PaymentSplitPlan PayByItems(
        decimal totalAmount,
        decimal amount,
        IReadOnlyList<PaymentPayByItemsSelection> selectedItems) =>
        new(totalAmount, 1, PaymentSplitMode.PayByItems, amount, selectedItems);

    /// <summary>Client one-shot tender amount (no multi-part tracking).</summary>
    public decimal GetThisPaymentAmount(decimal remainingBalance)
    {
        if (Mode is PaymentSplitMode.CustomAmount or PaymentSplitMode.PayByItems)
        {
            var planned = CustomAmount ?? 0m;
            return Math.Min(planned, remainingBalance);
        }

        if (Mode == PaymentSplitMode.EqualSplit && TotalParts > 1)
        {
            return Math.Round(remainingBalance / TotalParts, 2, MidpointRounding.AwayFromZero);
        }

        return remainingBalance;
    }

    /// <summary>Mother payment-loop amount for the current split part.</summary>
    public decimal GetNextAmount(decimal remainingBalance)
    {
        if (Mode is PaymentSplitMode.CustomAmount or PaymentSplitMode.PayByItems)
        {
            return Math.Min(CustomAmount ?? 0m, remainingBalance);
        }

        if (Mode == PaymentSplitMode.Full)
        {
            return remainingBalance;
        }

        var remainingParts = TotalParts - CurrentPart + 1;
        return remainingParts <= 1
            ? remainingBalance
            : Math.Min(remainingBalance, AmountPerPart);
    }

    public void MarkPartComplete()
    {
        if (Mode == PaymentSplitMode.EqualSplit && CurrentPart < TotalParts)
        {
            CurrentPart++;
        }
    }

    public void ApplyExistingPaid(decimal totalPaid)
    {
        if (Mode != PaymentSplitMode.EqualSplit || totalPaid <= 0 || AmountPerPart <= 0)
        {
            return;
        }

        var paidRemaining = totalPaid;
        CurrentPart = 1;
        while (CurrentPart < TotalParts && paidRemaining >= AmountPerPart - 0.009m)
        {
            paidRemaining -= AmountPerPart;
            CurrentPart++;
        }
    }

    public string GetPaymentTitle() => Mode switch
    {
        PaymentSplitMode.EqualSplit => $"SPLIT PAYMENT {CurrentPart} OF {TotalParts}",
        PaymentSplitMode.CustomAmount => "CUSTOM PAYMENT",
        PaymentSplitMode.PayByItems => "PAY BY ITEMS",
        _ => "SELECT PAYMENT METHOD"
    };

    public string DisplayLabel => Mode switch
    {
        PaymentSplitMode.EqualSplit => $"SPLIT {TotalParts}",
        PaymentSplitMode.CustomAmount => "CUSTOM",
        PaymentSplitMode.PayByItems => "PAY BY ITEMS",
        _ => "FULL"
    };

    public object ToMetadata() => new
    {
        isSplit = IsSplit,
        mode = Mode.ToString(),
        totalParts = TotalParts,
        currentPart = CurrentPart,
        amountPerPart = AmountPerPart,
        customAmount = CustomAmount ?? 0m,
        selectedItems = SelectedItems.Select(item => new
        {
            item.ItemId,
            item.ItemName,
            item.Quantity,
            item.Amount
        }).ToList(),
        totalAmount = TotalAmount
    };
}
