using Microsoft.Maui.Controls;
using MySqlConnector;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class RefundModal : ContentPage
{
    private readonly DatabaseService _databaseService;
    private readonly int _orderId;
    private readonly string _orderNumber;
    private readonly decimal _originalAmount;
    private bool _isFullRefund = true;
    private bool _isProcessing;

    public event Action? RefundCompleted;

    public RefundModal(int orderId, string orderNumber, decimal originalAmount)
    {
        InitializeComponent();

        _databaseService = new DatabaseService();
        _orderId = orderId;
        _orderNumber = orderNumber;
        _originalAmount = originalAmount;

        OrderNumberLabel.Text = orderNumber;
        OriginalAmountLabel.Text = $"£{originalAmount:F2}";
        RefundSummaryLabel.Text = $"£{originalAmount:F2}";
        RefundAmountEntry.TextChanged += OnRefundAmountChanged;
    }

    private void OnFullRefundSelected(object sender, EventArgs e)
    {
        _isFullRefund = true;
        FullRefundBorder.BackgroundColor = Color.FromArgb("#10B981");
        PartialRefundBorder.BackgroundColor = Color.FromArgb("#F3F4F6");
        PartialRefundLabel.TextColor = Color.FromArgb("#6B7280");
        PartialAmountLayout.IsVisible = false;
        RefundSummaryLabel.Text = $"£{_originalAmount:F2}";
    }

    private void OnPartialRefundSelected(object sender, EventArgs e)
    {
        _isFullRefund = false;
        FullRefundBorder.BackgroundColor = Color.FromArgb("#F3F4F6");
        PartialRefundBorder.BackgroundColor = Color.FromArgb("#10B981");
        PartialRefundLabel.TextColor = Colors.White;
        PartialAmountLayout.IsVisible = true;
        RefundSummaryLabel.Text = "£0.00";
    }

    private void OnRefundAmountChanged(object sender, TextChangedEventArgs e)
    {
        if (decimal.TryParse(
                e.NewTextValue,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture,
                out var amount))
        {
            if (amount > _originalAmount)
            {
                RefundAmountEntry.Text = _originalAmount.ToString("F2");
                return;
            }

            RefundSummaryLabel.Text = $"£{amount:F2}";
            return;
        }

        RefundSummaryLabel.Text = "£0.00";
    }

    private async void OnConfirmRefundClicked(object sender, EventArgs e)
    {
        if (_isProcessing)
        {
            return;
        }

        var currentUser = AuthenticationService.Instance.CurrentUser;
        if (currentUser is not { IsActive: true, Role: UserRole.Admin })
        {
            await AppAlertService.ShowAlertAsync(
                "Administrator Required",
                "Only a signed-in Administrator can record a manual refund.");
            return;
        }

        var reason = RefundReasonEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            await AppAlertService.ShowAlertAsync("Reason Required", "Enter the reason for this refund.");
            return;
        }

        var externalReference = ExternalReferenceEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            await AppAlertService.ShowAlertAsync(
                "Reference Required",
                "Enter the card terminal receipt or manual refund reference.");
            return;
        }

        decimal refundAmount;
        if (_isFullRefund)
        {
            refundAmount = _originalAmount;
        }
        else if (!decimal.TryParse(
                     RefundAmountEntry.Text,
                     System.Globalization.NumberStyles.Number,
                     System.Globalization.CultureInfo.CurrentCulture,
                     out refundAmount) || refundAmount <= 0)
        {
            await AppAlertService.ShowAlertAsync("Invalid Amount", "Please enter a valid refund amount.");
            return;
        }

        var confirm = await DisplayAlert(
            "Confirm Manual Refund",
            $"Confirm that £{refundAmount:F2} was refunded outside the POS for {_orderNumber}?",
            "Record Refund",
            "Cancel");
        if (!confirm)
        {
            return;
        }

        _isProcessing = true;
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var transaction = connection.BeginTransaction();

            const string lockOrderQuery = """
                SELECT total_amount
                FROM orders
                WHERE id = @orderId
                FOR UPDATE
                """;
            using var lockOrderCommand = new MySqlCommand(lockOrderQuery, connection, transaction);
            lockOrderCommand.Parameters.AddWithValue("@orderId", _orderId);
            var orderTotalValue = await lockOrderCommand.ExecuteScalarAsync();
            if (orderTotalValue == null || orderTotalValue == DBNull.Value)
            {
                await transaction.RollbackAsync();
                await AppAlertService.ShowAlertAsync("Order Not Found", "The order no longer exists.");
                return;
            }

            var authoritativeOrderTotal = Convert.ToDecimal(orderTotalValue);
            const string refundedTotalQuery = """
                SELECT COALESCE(SUM(refund_amount), 0)
                FROM order_refunds
                WHERE order_id = @orderId
                """;
            using var refundedTotalCommand = new MySqlCommand(refundedTotalQuery, connection, transaction);
            refundedTotalCommand.Parameters.AddWithValue("@orderId", _orderId);
            var alreadyRefunded = Convert.ToDecimal(await refundedTotalCommand.ExecuteScalarAsync() ?? 0m);
            var remainingRefundable = Math.Max(0m, authoritativeOrderTotal - alreadyRefunded);

            if (_isFullRefund)
            {
                refundAmount = remainingRefundable;
            }

            if (refundAmount <= 0 || refundAmount > remainingRefundable)
            {
                await transaction.RollbackAsync();
                await AppAlertService.ShowAlertAsync(
                    "Invalid Amount",
                    remainingRefundable <= 0
                        ? "This order has already been fully refunded."
                        : $"Only £{remainingRefundable:F2} remains available to refund.");
                return;
            }

            var terminalName = GetCurrentTerminalName();
            var refundedBy = !string.IsNullOrWhiteSpace(currentUser.Name)
                ? currentUser.Name
                : currentUser.Username;
            const string insertRefundQuery = """
                INSERT INTO order_refunds
                    (order_id, refund_amount, refund_type, reason, refunded_at, refunded_by,
                     refunded_by_user_id, refunded_by_role, terminal_name, external_reference)
                VALUES
                    (@orderId, @refundAmount, @refundType, @reason, NOW(), @refundedBy,
                     @refundedByUserId, @refundedByRole, @terminalName, @externalReference)
                """;
            using var insertCommand = new MySqlCommand(insertRefundQuery, connection, transaction);
            insertCommand.Parameters.AddWithValue("@orderId", _orderId);
            insertCommand.Parameters.AddWithValue("@refundAmount", refundAmount);
            insertCommand.Parameters.AddWithValue("@refundType", refundAmount >= remainingRefundable ? "full" : "partial");
            insertCommand.Parameters.AddWithValue("@reason", reason);
            insertCommand.Parameters.AddWithValue("@refundedBy", refundedBy);
            insertCommand.Parameters.AddWithValue("@refundedByUserId", currentUser.Id);
            insertCommand.Parameters.AddWithValue("@refundedByRole", currentUser.Role.ToString());
            insertCommand.Parameters.AddWithValue("@terminalName", terminalName);
            insertCommand.Parameters.AddWithValue("@externalReference", externalReference);
            await insertCommand.ExecuteNonQueryAsync();

            const string updateOrderQuery = """
                UPDATE orders
                SET updated_by_terminal_name = @terminalName,
                    updated_by_terminal_at = NOW(),
                    updated_at = NOW()
                WHERE id = @orderId
                """;
            using var updateCommand = new MySqlCommand(updateOrderQuery, connection, transaction);
            updateCommand.Parameters.AddWithValue("@orderId", _orderId);
            updateCommand.Parameters.AddWithValue("@terminalName", terminalName);
            await updateCommand.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
            await AppAlertService.ShowAlertAsync(
                "Refund Recorded",
                $"Manual refund of £{refundAmount:F2} was recorded with reference {externalReference}.");

            RefundCompleted?.Invoke();
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("Record manual refund", ex);
            await AppAlertService.ShowAlertAsync(
                "Refund Not Recorded",
                "The refund could not be recorded. No local refund entry was completed. Please contact an administrator.");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        if (!_isProcessing)
        {
            await Navigation.PopModalAsync();
        }
    }

    private static string GetCurrentTerminalName()
    {
        try
        {
            return TerminalConfigurationService.GetConfiguration().TerminalName;
        }
        catch
        {
            return "Terminal";
        }
    }
}
