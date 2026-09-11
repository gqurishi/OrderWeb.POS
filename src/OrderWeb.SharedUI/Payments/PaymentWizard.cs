using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Payments;

/// <summary>
/// SharedUI payment wizard entry points (Mother chrome).
/// Hosts call these from Order Place PAYMENT; COL/DEL should skip tip + setup and use Full only.
/// </summary>
public static class PaymentWizard
{
    /// <summary>ADD TIP. Returns tip amount, or null if cancelled (-1 from dialog).</summary>
    public static async Task<decimal?> ShowTipAsync(decimal orderTotal, ContentPage? hostPage = null)
    {
        var dialog = new PaymentTipDialog();
        dialog.SetOrderTotal(orderTotal);
        var tip = await dialog.ShowAsync(hostPage);
        return tip < 0 ? null : tip;
    }

    /// <summary>Mother SELECT PAYMENT METHOD (Cash / Card / Gift [+ Loyalty]).</summary>
    public static async Task<PaymentMethodChoice> ShowMethodAsync(
        decimal amountDue,
        decimal remainingAfterThisPayment = 0,
        string? title = null,
        bool showLoyalty = false,
        ContentPage? hostPage = null)
    {
        var dialog = new PaymentMethodDialog(showLoyalty);
        dialog.SetAmountDue(amountDue, remainingAfterThisPayment, title);
        return await dialog.ShowAsync(hostPage);
    }

    /// <summary>Mother CASH PAYMENT (Exact / quick / change / confirm).</summary>
    public static async Task<PaymentCashResult> ShowCashAsync(
        decimal amountDue,
        ContentPage? hostPage = null)
    {
        var dialog = new PaymentCashDialog();
        dialog.SetAmountDue(amountDue);
        return await dialog.ShowAsync(hostPage);
    }

    /// <summary>
    /// Mother GIFT CARD PAYMENT chrome. Host supplies lookup; APPLY returns card+amount
    /// (Client posts redeem via Mother payments — SharedUI does not redeem).
    /// </summary>
    public static async Task<PaymentGiftCardResult> ShowGiftCardAsync(
        decimal amountDue,
        Func<string, CancellationToken, Task<PaymentGiftCardLookupResult>> lookupAsync,
        ContentPage? hostPage = null)
    {
        var dialog = new PaymentGiftCardDialog();
        dialog.SetAmountDue(amountDue);
        return await dialog.ShowAsync(lookupAsync, hostPage);
    }

    /// <summary>Payment Setup → optional Split / Pay By Items / Custom Amount.</summary>
    public static async Task<PaymentSplitPlan?> ShowSetupPlanAsync(
        decimal totalDue,
        decimal remainingBalance,
        IReadOnlyList<PaymentPayByItemsLine>? payByItemsLines = null,
        decimal orderSubtotal = 0,
        decimal serviceCharge = 0,
        decimal deliveryFee = 0,
        decimal discount = 0,
        ContentPage? hostPage = null)
    {
        while (true)
        {
            var setup = new PaymentActionSheetDialog();
            setup.SetActionSheetGrid(
                "Payment Setup",
                new[] { "Pay Full", "Split Evenly", "Pay By Items", "Custom Amount" },
                "£",
                "#059669");
            setup.SetCancelText("Back");
            setup.HighlightGridOption("Pay Full", "#DCFCE7", "#065F46", "#10B981");

            var selected = await setup.ShowAsync(hostPage);
            if (selected is null)
            {
                return null;
            }

            if (selected == "Pay Full")
            {
                return PaymentSplitPlan.Full(totalDue);
            }

            if (selected == "Custom Amount")
            {
                var custom = await ShowCustomAmountAsync(totalDue, remainingBalance, hostPage);
                if (custom is not null)
                {
                    return custom;
                }

                continue;
            }

            if (selected == "Pay By Items")
            {
                var items = await ShowPayByItemsAsync(
                    totalDue,
                    remainingBalance,
                    payByItemsLines ?? Array.Empty<PaymentPayByItemsLine>(),
                    orderSubtotal,
                    serviceCharge,
                    deliveryFee,
                    discount,
                    hostPage);
                if (items is not null)
                {
                    return items;
                }

                continue;
            }

            if (selected == "Split Evenly")
            {
                var split = await ShowEvenSplitAsync(remainingBalance, hostPage);
                if (split is not null)
                {
                    return split;
                }
            }
        }
    }

    public static async Task<PaymentSplitPlan?> ShowEvenSplitAsync(
        decimal remainingBalance,
        ContentPage? hostPage = null)
    {
        var splitDialog = new PaymentActionSheetDialog();
        splitDialog.SetActionSheetGrid(
            "Split Evenly",
            new[] { "Split by 2", "Split by 3", "Split by 4", "Split by 5", "Split by 6", "Custom Split" },
            "÷",
            "#2563EB");
        splitDialog.SetCancelText("Back");

        var selected = await splitDialog.ShowAsync(hostPage);
        if (selected is null)
        {
            return null;
        }

        if (selected == "Custom Split")
        {
            var parts = await ShowCustomSplitCountAsync(hostPage);
            if (parts is null)
            {
                return null;
            }

            return PaymentSplitPlan.Equal(remainingBalance, parts.Value);
        }

        var splitCount = selected switch
        {
            "Split by 2" => 2,
            "Split by 3" => 3,
            "Split by 4" => 4,
            "Split by 5" => 5,
            "Split by 6" => 6,
            _ => 1
        };

        return splitCount <= 1
            ? PaymentSplitPlan.Full(remainingBalance)
            : PaymentSplitPlan.Equal(remainingBalance, splitCount);
    }

    public static async Task<PaymentSplitPlan?> ShowCustomAmountAsync(
        decimal totalDue,
        decimal remainingBalance,
        ContentPage? hostPage = null)
    {
        var keyboard = new NumericKeyboardDialog();
        var value = await keyboard.ShowCurrencyAsync(null, "Custom Amount", hostPage);
        if (!value.HasValue)
        {
            return null;
        }

        var customAmount = value.Value;
        if (customAmount <= 0 || customAmount > remainingBalance + 0.009m)
        {
            await ShowSimpleAlertAsync(
                "Invalid Amount",
                $"Enter an amount between £0.01 and £{remainingBalance:F2}.",
                hostPage);
            return null;
        }

        return PaymentSplitPlan.Custom(totalDue, Math.Min(customAmount, remainingBalance));
    }

    public static async Task<PaymentSplitPlan?> ShowPayByItemsAsync(
        decimal totalDue,
        decimal remainingBalance,
        IReadOnlyList<PaymentPayByItemsLine> lines,
        decimal orderSubtotal,
        decimal serviceCharge,
        decimal deliveryFee,
        decimal discount,
        ContentPage? hostPage = null)
    {
        var payable = (lines ?? Array.Empty<PaymentPayByItemsLine>())
            .Where(line => line.Quantity > 0 && line.UnitAmount > 0)
            .ToList();
        if (payable.Count == 0)
        {
            await ShowSimpleAlertAsync(
                "Pay By Items",
                "There are no payable items on this order.",
                hostPage);
            return null;
        }

        var dialog = new PaymentPayByItemsDialog();
        dialog.SetOrder(payable, orderSubtotal, serviceCharge, deliveryFee, discount, remainingBalance);
        var result = await dialog.ShowAsync(hostPage);
        if (!result.Success || result.Amount <= 0)
        {
            return null;
        }

        return PaymentSplitPlan.PayByItems(
            totalDue,
            Math.Min(result.Amount, remainingBalance),
            result.SelectedItems);
    }

    private static async Task<int?> ShowCustomSplitCountAsync(ContentPage? hostPage)
    {
        var keyboard = new NumericKeyboardDialog();
        var digits = await keyboard.ShowDigitsAsync(null, "How many ways to split?", 2, hostPage);
        if (string.IsNullOrWhiteSpace(digits) ||
            !int.TryParse(digits.Trim(), out var parts) ||
            parts is < 2 or > 20)
        {
            if (digits is not null)
            {
                await ShowSimpleAlertAsync("Invalid Split", "Enter a split number between 2 and 20.", hostPage);
            }

            return null;
        }

        return parts;
    }

    private static async Task ShowSimpleAlertAsync(string title, string message, ContentPage? hostPage)
    {
        var page = hostPage ?? PaymentOverlayHost.FindPage();
        var dialog = new PaymentAlertDialog();
        var tone = title.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
            ? PaymentAlertTone.Warning
            : PaymentAlertTone.Info;
        dialog.SetAlert(title, message, "OK", tone);
        await dialog.ShowAsync(page);
    }
}
