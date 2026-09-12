using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Services;

/// <summary>Maps the SharedUI cash-drawer choice onto Mother's Client open payload.</summary>
public sealed record ClientCashDrawerOpen(string Reason, decimal? Amount, string? Details, string DrawerReason)
{
    public static ClientCashDrawerOpen From(CashDrawerUiResult choice) => choice.Kind switch
    {
        CashDrawerUiKind.Refund => new("Refund", null, null, "Refund"),
        CashDrawerUiKind.ShoppingTake => new(
            "Shopping",
            choice.Amount,
            choice.Details,
            $"Shopping take · {choice.Details} · £{choice.Amount:F2}"),
        CashDrawerUiKind.Delivery => new("Delivery", choice.Amount, null, $"Delivery · £{choice.Amount:F2}"),
        CashDrawerUiKind.CashCount => new("Cash Count", choice.Amount, null, $"Cash count · £{choice.Amount:F2}"),
        CashDrawerUiKind.Other => new(
            "Other",
            choice.Amount,
            choice.Details,
            choice.Amount is > 0
                ? $"Other · {choice.Details} · £{choice.Amount:F2}"
                : $"Other · {choice.Details}"),
        _ => new("No Sale", null, null, "No Sale")
    };

    /// <summary>Mother manager sidebar: same SharedUI dialogs, no extra Client page.</summary>
    public static async Task RunAsync(ContentPage page, ClientCacheService cache, bool cashier)
    {
        var online = await new ClientOfflinePolicy().IsMotherOnlineAsync();
        if (!online)
        {
            await CashDrawerDialogFlow.ShowNoticeAsync(page, "Cash Drawer Failed", "Cash drawer opening requires a live Mother POS connection.", "!", "#EF4444");
            return;
        }

        var choice = await CashDrawerDialogFlow.CollectAsync(page);
        if (choice is null)
        {
            return;
        }

        if (choice.Kind == CashDrawerUiKind.ShoppingSettle)
        {
            var settled = await SettleShoppingAsync(page, cache);
            if (settled is null)
            {
                return;
            }

            await ShowResultAsync(page, settled.Success, settled.Message);
            return;
        }

        var open = From(choice);
        if (cashier)
        {
            var result = await new MotherCashierClient(cache).OpenCashDrawerAsync(open.Reason, open.Amount, open.Details);
            await ShowResultAsync(page, result.Success, result.Message);
            return;
        }

        var drawer = await new MotherOrderClient().OpenOrderPlaceCashDrawerAsync(null, open.Reason, open.Amount, open.Details);
        await ShowResultAsync(page, drawer.Success, drawer.Message);
    }

    private static Task ShowResultAsync(ContentPage page, bool success, string message) =>
        CashDrawerDialogFlow.ShowNoticeAsync(
            page,
            success ? "Cash Drawer" : "Cash Drawer Failed",
            message,
            success ? "OK" : "!",
            success ? "#10B981" : "#EF4444");

    public static async Task<CashierActionResult?> SettleShoppingAsync(ContentPage page, ClientCacheService cache)
    {
        var client = new MotherCashierClient(cache);
        var pending = await client.GetPendingShoppingAsync();
        var choice = await CashDrawerDialogFlow.CollectSettleAsync(page, pending);
        if (choice?.ExpenseId is not int expenseId || choice.Amount is not decimal spent)
        {
            return null;
        }

        return await client.SettleShoppingAsync(expenseId, spent);
    }
}
