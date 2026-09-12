using MySqlConnector;
using OrderWeb.SharedUI.Views;
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

        var page = GetContentPage();
        if (page is null)
        {
            return;
        }

        using var idleGuard = _inactivityService?.BeginCriticalActivity();
        var choice = await CashDrawerDialogFlow.CollectAsync(page);
        if (choice is null)
        {
            return;
        }

        switch (choice.Kind)
        {
            case CashDrawerUiKind.NoSale:
                await OpenDrawerAsync("No Sale", context);
                break;
            case CashDrawerUiKind.Refund:
                await OpenDrawerAsync("Refund", context);
                break;
            case CashDrawerUiKind.ShoppingTake:
                await RecordShoppingTakeAsync(choice, context);
                break;
            case CashDrawerUiKind.ShoppingSettle:
                await RunShoppingSettleAsync(context);
                break;
            case CashDrawerUiKind.Delivery:
                await RecordDeliveryAsync(choice, context);
                break;
            case CashDrawerUiKind.CashCount:
                await RecordCashCountAsync(choice, context);
                break;
            case CashDrawerUiKind.Other:
                await RecordOtherAsync(choice, context);
                break;
        }
    }

    private async Task RecordShoppingTakeAsync(CashDrawerUiResult choice, CashDrawerFlowContext context)
    {
        try
        {
            var expense = await _tillExpenseService.CreateShoppingTakeAsync(new ShoppingTakeRequest
            {
                ItemName = string.IsNullOrWhiteSpace(choice.Details) ? "Shopping" : choice.Details.Trim(),
                AmountTaken = choice.Amount ?? 0,
                SourceArea = context.SourceArea,
                OrderId = context.OrderId,
                OrderNumber = context.OrderNumber
            });
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

    private async Task RecordDeliveryAsync(CashDrawerUiResult choice, CashDrawerFlowContext context)
    {
        if (choice.Amount is not > 0)
        {
            return;
        }

        try
        {
            var expense = await _tillExpenseService.CreateDeliveryPayoutAsync(new DeliveryPayoutRequest
            {
                Amount = choice.Amount.Value,
                OrderId = context.OrderId,
                OrderNumber = context.OrderNumber,
                SourceArea = context.SourceArea
            });
            await OpenDrawerAsync($"Delivery · £{expense.NetAmount:F2}", context, expense.Id);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex.Message);
        }
    }

    private async Task RecordCashCountAsync(CashDrawerUiResult choice, CashDrawerFlowContext context)
    {
        if (choice.Amount is not >= 0)
        {
            return;
        }

        try
        {
            var expense = await _tillExpenseService.CreateCashCountAsync(new CashCountRequest
            {
                CountedCash = choice.Amount.Value,
                SourceArea = context.SourceArea
            });
            await OpenDrawerAsync($"Cash count · £{expense.CountedCash:F2}", context, expense.Id);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex.Message);
        }
    }

    private async Task RecordOtherAsync(CashDrawerUiResult choice, CashDrawerFlowContext context)
    {
        var reason = choice.Details?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason) || choice.Amount is null)
        {
            return;
        }

        int? expenseId = null;
        if (choice.Amount.Value > 0)
        {
            try
            {
                var expense = await _tillExpenseService.CreateOtherExpenseAsync(new OtherTillExpenseRequest
                {
                    Reason = reason,
                    AmountOut = choice.Amount.Value,
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

        var drawerReason = choice.Amount.Value > 0
            ? $"Other · {reason} · £{choice.Amount.Value:F2}"
            : $"Other · {reason}";
        await OpenDrawerAsync(drawerReason, context, expenseId);
    }

    private async Task RunShoppingSettleAsync(CashDrawerFlowContext context)
    {
        var page = GetContentPage();
        if (page is null)
        {
            return;
        }

        var pending = await _tillExpenseService.GetPendingShoppingAsync(_authenticationService.CurrentUser?.Id);
        var trips = pending.Select(trip => new CashDrawerPendingTrip(
            trip.Id,
            $"{trip.Description} · £{trip.AmountTaken:F2} out",
            $"{trip.Description} · £{trip.AmountTaken:F2} taken · by {trip.RecordedByName}",
            trip.AmountTaken,
            trip.Description)).ToList();

        var choice = await CashDrawerDialogFlow.CollectSettleAsync(page, trips);
        if (choice?.ExpenseId is not int expenseId || choice.Amount is not decimal spent)
        {
            return;
        }

        try
        {
            var expense = await _tillExpenseService.SettleShoppingAsync(new ShoppingSettleRequest
            {
                TillExpenseId = expenseId,
                AmountSpent = spent
            });
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

    private static ContentPage? GetContentPage()
    {
        var page = Application.Current?.MainPage is Shell shell
            ? shell.CurrentPage
            : Application.Current?.MainPage;
        return page as ContentPage;
    }

    private static async Task ShowSuccessAsync(string message)
    {
        var page = GetContentPage();
        if (page is null)
        {
            return;
        }

        await CashDrawerDialogFlow.ShowNoticeAsync(page, "Cash Drawer", message, "OK", "#10B981");
    }

    private static async Task ShowFailureAsync(string message)
    {
        var page = GetContentPage();
        if (page is null)
        {
            return;
        }

        await CashDrawerDialogFlow.ShowNoticeAsync(page, "Cash Drawer Failed", message, "!", "#EF4444");
    }

    private static async Task ShowInfoAsync(string title, string message)
    {
        var page = GetContentPage();
        if (page is null)
        {
            return;
        }

        await CashDrawerDialogFlow.ShowNoticeAsync(page, title, message, "i", "#3B82F6");
    }

    private sealed record CashDrawerApproval(int UserId, string Name, string Role);
}
