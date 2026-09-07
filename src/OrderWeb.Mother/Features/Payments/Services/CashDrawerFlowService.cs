using MySqlConnector;
using POS_in_NET.Models;
using POS_in_NET.Views;

namespace POS_in_NET.Services;

public sealed class CashDrawerFlowContext
{
    public string SourceArea { get; set; } = "pos";
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public int? TableSessionId { get; set; }
    public string? TableNumber { get; set; }
}

public sealed class CashDrawerFlowService
{
    private readonly CashDrawerService _cashDrawerService;
    private readonly TillExpenseService _tillExpenseService;
    private readonly AuthenticationService _authenticationService;
    private readonly InactivityService? _inactivityService;

    public CashDrawerFlowService(
        CashDrawerService cashDrawerService,
        TillExpenseService tillExpenseService,
        AuthenticationService authenticationService,
        InactivityService? inactivityService = null)
    {
        _cashDrawerService = cashDrawerService;
        _tillExpenseService = tillExpenseService;
        _authenticationService = authenticationService;
        _inactivityService = inactivityService;
    }

    public async Task RunAsync(CashDrawerFlowContext? context = null)
    {
        context ??= new CashDrawerFlowContext();
        _inactivityService?.ResetActivity();

        if (!await RequireManagerApprovalForBasicUserAsync())
        {
            return;
        }

        var reasonDialog = new ModernActionSheetDialog();
        reasonDialog.SetActionSheetGrid(
            "Cash Drawer Reason",
            new List<string> { "No Sale", "Shopping", "Delivery", "Refund", "Cash Count", "Other" },
            "£",
            "#0F766E");

        var reason = await reasonDialog.ShowAsync();
        if (reason == null)
        {
            return;
        }

        switch (reason)
        {
            case "No Sale":
                await OpenSimpleAsync("No Sale", context);
                break;
            case "Shopping":
                await RunShoppingFlowAsync(context);
                break;
            case "Delivery":
                await RunDeliveryFlowAsync(context);
                break;
            case "Refund":
                await OpenSimpleAsync("Refund", context);
                break;
            case "Cash Count":
                await RunCashCountFlowAsync(context);
                break;
            case "Other":
                await RunOtherFlowAsync(context);
                break;
        }
    }

    private async Task RunShoppingFlowAsync(CashDrawerFlowContext context)
    {
        var subDialog = new ModernActionSheetDialog();
        subDialog.SetActionSheet(
            "Shopping",
            new List<string> { "Take from till", "Settle pending trip" },
            "£",
            "#0F766E");

        var action = await subDialog.ShowAsync();
        if (action == null)
        {
            return;
        }

        if (action == "Take from till")
        {
            await RunShoppingTakeAsync(context);
            return;
        }

        await RunShoppingSettleAsync(context);
    }

    private async Task RunShoppingTakeAsync(CashDrawerFlowContext context)
    {
        var hostPage = GetHostPage();
        if (hostPage == null)
        {
            return;
        }

        var takeDialog = new TillShoppingTakeDialog(context.SourceArea, context.OrderId, context.OrderNumber);
        var request = await takeDialog.ShowAsync(hostPage);
        if (request == null)
        {
            return;
        }

        try
        {
            var expense = await _tillExpenseService.CreateShoppingTakeAsync(request);
            await OpenDrawerAsync(
                $"Shopping take · {expense.Description} · £{expense.AmountTaken:F2}",
                context,
                expense.Id);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex.Message);
        }
    }

    private async Task RunShoppingSettleAsync(CashDrawerFlowContext context)
    {
        var userId = _authenticationService.CurrentUser?.Id;
        var pending = await _tillExpenseService.GetPendingShoppingAsync(userId);
        if (pending.Count == 0)
        {
            await ShowInfoAsync("No pending trips", "There are no shopping trips waiting to be settled.");
            return;
        }

        var picker = new ModernActionSheetDialog();
        picker.SetActionSheet(
            "Settle shopping trip",
            pending.Select(t => $"{t.Description} · £{t.AmountTaken:F2} out").ToList(),
            "£",
            "#0F766E");

        var picked = await picker.ShowAsync();
        if (picked == null)
        {
            return;
        }

        var trip = pending.FirstOrDefault(t =>
            string.Equals($"{t.Description} · £{t.AmountTaken:F2} out", picked, StringComparison.Ordinal));
        if (trip == null)
        {
            return;
        }

        var hostPage = GetHostPage();
        if (hostPage == null)
        {
            return;
        }

        var settleDialog = new TillShoppingSettleDialog(trip);
        var settleRequest = await settleDialog.ShowAsync(hostPage);
        if (settleRequest == null)
        {
            return;
        }

        try
        {
            var expense = await _tillExpenseService.SettleShoppingAsync(settleRequest);
            var change = expense.AmountReturned ?? 0;
            await OpenDrawerAsync(
                $"Shopping settle · {expense.Description} · spent £{expense.AmountSpent:F2} · return £{change:F2}",
                context,
                expense.Id);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex.Message);
        }
    }

    private async Task RunDeliveryFlowAsync(CashDrawerFlowContext context)
    {
        var hostPage = GetHostPage();
        if (hostPage == null)
        {
            return;
        }

        var amountDialog = new TillAmountDialog(
            "Delivery payout",
            "Record cash paid out for delivery before opening the drawer.",
            "Amount out (£)",
            "Pay & Open");
        var amount = await amountDialog.ShowAsync(hostPage);
        if (!amount.HasValue || amount.Value <= 0)
        {
            return;
        }

        try
        {
            var expense = await _tillExpenseService.CreateDeliveryPayoutAsync(new DeliveryPayoutRequest
            {
                Amount = amount.Value,
                OrderId = context.OrderId,
                OrderNumber = context.OrderNumber,
                SourceArea = context.SourceArea
            });

            await OpenDrawerAsync(
                $"Delivery · £{expense.NetAmount:F2}",
                context,
                expense.Id);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex.Message);
        }
    }

    private async Task RunCashCountFlowAsync(CashDrawerFlowContext context)
    {
        var hostPage = GetHostPage();
        if (hostPage == null)
        {
            return;
        }

        var amountDialog = new TillAmountDialog(
            "Cash count",
            "Enter the cash counted in the drawer.",
            "Counted cash (£)",
            "Record & Open");
        var amount = await amountDialog.ShowAsync(hostPage);
        if (!amount.HasValue || amount.Value < 0)
        {
            return;
        }

        try
        {
            var expense = await _tillExpenseService.CreateCashCountAsync(new CashCountRequest
            {
                CountedCash = amount.Value,
                SourceArea = context.SourceArea
            });

            await OpenDrawerAsync(
                $"Cash count · £{expense.CountedCash:F2}",
                context,
                expense.Id);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex.Message);
        }
    }

    private async Task RunOtherFlowAsync(CashDrawerFlowContext context)
    {
        var reasonDialog = new StyledPromptDialog();
        reasonDialog.SetDialog(
            "Other till expense",
            "Enter the reason for opening the cash drawer:",
            "Reason",
            null,
            string.Empty,
            true);

        var reason = await reasonDialog.ShowAsync();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var hostPage = GetHostPage();
        if (hostPage == null)
        {
            return;
        }

        var amountDialog = new TillAmountDialog(
            "Amount out (optional)",
            "Leave as 0 if no cash is leaving the till.",
            "Amount out (£)",
            "Continue");
        var amount = await amountDialog.ShowAsync(hostPage);
        if (!amount.HasValue)
        {
            return;
        }

        int? expenseId = null;
        if (amount.Value > 0)
        {
            try
            {
                var expense = await _tillExpenseService.CreateOtherExpenseAsync(new OtherTillExpenseRequest
                {
                    Reason = reason.Trim(),
                    AmountOut = amount.Value,
                    SourceArea = context.SourceArea
                });
                expenseId = expense?.Id;
            }
            catch (Exception ex)
            {
                await ShowFailureAsync(ex.Message);
                return;
            }
        }

        var drawerReason = amount.Value > 0
            ? $"Other · {reason.Trim()} · £{amount.Value:F2}"
            : $"Other · {reason.Trim()}";

        await OpenDrawerAsync(drawerReason, context, expenseId);
    }

    private async Task OpenSimpleAsync(string reason, CashDrawerFlowContext context)
    {
        var confirmDialog = new ModernConfirmDialog();
        confirmDialog.SetConfirm(
            "Open Cash Drawer",
            $"Open the cash drawer for: {reason}?",
            "Open",
            "No",
            "£");

        if (!await confirmDialog.ShowAsync())
        {
            return;
        }

        await OpenDrawerAsync(reason, context);
    }

    private async Task OpenDrawerAsync(string reason, CashDrawerFlowContext context, int? tillExpenseId = null)
    {
        var result = await _cashDrawerService.OpenAsync(new CashDrawerOpenRequest
        {
            Reason = reason,
            SourceArea = context.SourceArea,
            OrderId = context.OrderId,
            OrderNumber = context.OrderNumber,
            TableSessionId = context.TableSessionId,
            TableNumber = context.TableNumber,
            TillExpenseId = tillExpenseId
        });

        if (result.Success)
        {
            await ShowSuccessAsync($"Cash drawer opened on {result.PrinterName}.");
        }
        else
        {
            await ShowFailureAsync(result.Message);
        }
    }

    private async Task<bool> RequireManagerApprovalForBasicUserAsync()
    {
        var currentUser = _authenticationService.CurrentUser;
        if (currentUser?.Role == UserRole.Cashier)
        {
            // This gate lives in the Mother-side cash-drawer workflow; hiding
            // the button in a client cannot grant drawer access by itself.
            if (!CashierCapabilities.IsGrantedTo(UserRole.Cashier, CashierCapabilities.OpenDrawer))
            {
                await ShowFailureAsync("Your Cashier account cannot open the cash drawer.");
                return false;
            }

            return true;
        }

        if (currentUser?.Role is UserRole.Manager or UserRole.Admin)
        {
            return true;
        }

        if (currentUser?.Role != UserRole.User)
        {
            await ShowFailureAsync("You do not have permission to use the cash drawer.");
            return false;
        }

        var prompt = new StyledPromptDialog();
        prompt.SetDialog(
            "Manager PIN Required",
            "Enter a manager or admin PIN to use the cash drawer.",
            "Manager/Admin PIN",
            Keyboard.Numeric,
            string.Empty,
            true);
        prompt.SetIsPassword(true);
        prompt.SetOkText("Approve");

        var pin = await prompt.ShowAsync();
        if (string.IsNullOrWhiteSpace(pin))
        {
            return false;
        }

        var approver = await ValidateManagerOrAdminPinAsync(pin);
        if (approver == null)
        {
            await ShowFailureAsync("Manager or admin approval could not be verified.");
            return false;
        }

        _inactivityService?.ResetActivity();
        return true;
    }

    private static async Task<CashDrawerApproval?> ValidateManagerOrAdminPinAsync(string pin)
    {
        try
        {
            var databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
            await using var connection = await databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(@"
                SELECT id, name, username, password_hash, role
                FROM users
                WHERE role IN ('admin', 'manager')", connection);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var hash = reader.IsDBNull(reader.GetOrdinal("password_hash"))
                    ? string.Empty
                    : reader.GetString("password_hash");

                if (string.IsNullOrWhiteSpace(hash))
                {
                    continue;
                }

                bool isValid;
                try
                {
                    isValid = BCrypt.Net.BCrypt.Verify(pin, hash);
                }
                catch
                {
                    continue;
                }

                if (!isValid)
                {
                    continue;
                }

                var name = reader.IsDBNull(reader.GetOrdinal("name"))
                    ? string.Empty
                    : reader.GetString("name");
                var username = reader.IsDBNull(reader.GetOrdinal("username"))
                    ? string.Empty
                    : reader.GetString("username");

                return new CashDrawerApproval(
                    reader.GetInt32(reader.GetOrdinal("id")),
                    !string.IsNullOrWhiteSpace(name) ? name : username,
                    reader.IsDBNull(reader.GetOrdinal("role"))
                        ? string.Empty
                        : reader.GetString("role"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cash drawer manager approval failed: {ex.Message}");
        }

        return null;
    }

    private static Page? GetHostPage()
    {
        if (Application.Current?.MainPage is Shell shell)
        {
            return shell.CurrentPage;
        }

        return Application.Current?.MainPage;
    }

    private static async Task ShowSuccessAsync(string message)
    {
        var dialog = new ModernAlertDialog();
        dialog.SetAlert("Cash Drawer", message, "OK", "#10B981", "White");
        await dialog.ShowAsync();
    }

    private static async Task ShowFailureAsync(string message)
    {
        var dialog = new ModernAlertDialog();
        dialog.SetAlert("Cash Drawer Failed", message, "!", "#EF4444", "White");
        await dialog.ShowAsync();
    }

    private static async Task ShowInfoAsync(string title, string message)
    {
        var dialog = new ModernAlertDialog();
        dialog.SetAlert(title, message, "i", "#3B82F6", "White");
        await dialog.ShowAsync();
    }

    private sealed record CashDrawerApproval(int UserId, string Name, string Role);
}
