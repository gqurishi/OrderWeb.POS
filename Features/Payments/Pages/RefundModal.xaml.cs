using Microsoft.Maui.Controls;
using MySqlConnector;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System.Text.Json;

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
            var remainingRefundable = RefundAmountPolicy.Remaining(authoritativeOrderTotal, alreadyRefunded);

            const string duplicateReferenceQuery = """
                SELECT COUNT(*) FROM order_refunds
                WHERE order_id = @orderId AND external_reference = @externalReference
                """;
            using (var duplicateReferenceCommand = new MySqlCommand(duplicateReferenceQuery, connection, transaction))
            {
                duplicateReferenceCommand.Parameters.AddWithValue("@orderId", _orderId);
                duplicateReferenceCommand.Parameters.AddWithValue("@externalReference", externalReference);
                if (Convert.ToInt32(await duplicateReferenceCommand.ExecuteScalarAsync() ?? 0) > 0)
                {
                    await transaction.RollbackAsync();
                    await AppAlertService.ShowAlertAsync("Duplicate Refund", "This refund reference has already been recorded.");
                    return;
                }
            }

            if (_isFullRefund)
            {
                refundAmount = remainingRefundable;
            }

            var refundValidation = RefundAmountPolicy.Validate(refundAmount, authoritativeOrderTotal, alreadyRefunded);
            if (!refundValidation.IsValid)
            {
                await transaction.RollbackAsync();
                await AppAlertService.ShowAlertAsync(
                    "Invalid Amount",
                    refundValidation.Message);
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

            const string tipTotalsQuery = """
                SELECT
                    COALESCE(SUM(CASE WHEN payment_method = 'cash' AND status = 'approved' THEN tip_amount ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN payment_method = 'card' AND status = 'approved' THEN tip_amount ELSE 0 END), 0),
                    COALESCE(ABS(SUM(CASE WHEN payment_method = 'refund' AND status = 'approved' THEN tip_amount ELSE 0 END)), 0),
                    COALESCE(MAX(attempt_no), 0) + 1
                FROM order_payments
                WHERE order_id = @orderId
                """;
            decimal approvedTips;
            decimal approvedCashTips;
            decimal approvedCardTips;
            decimal alreadyReversedTips;
            int nextAttempt;
            using (var tipTotalsCommand = new MySqlCommand(tipTotalsQuery, connection, transaction))
            {
                tipTotalsCommand.Parameters.AddWithValue("@orderId", _orderId);
                using var tipReader = await tipTotalsCommand.ExecuteReaderAsync();
                await tipReader.ReadAsync();
                approvedCashTips = tipReader.GetDecimal(0);
                approvedCardTips = tipReader.GetDecimal(1);
                approvedTips = approvedCashTips + approvedCardTips;
                alreadyReversedTips = tipReader.GetDecimal(2);
                nextAttempt = tipReader.GetInt32(3);
            }

            var remainingTip = Math.Max(0m, approvedTips - alreadyReversedTips);
            var reversedTip = refundAmount >= remainingRefundable - 0.009m
                ? remainingTip
                : Math.Min(remainingTip, decimal.Round(approvedTips * refundAmount / authoritativeOrderTotal, 2, MidpointRounding.AwayFromZero));
            var cashTipReversed = approvedTips <= 0m
                ? 0m
                : decimal.Round(reversedTip * approvedCashTips / approvedTips, 2, MidpointRounding.AwayFromZero);
            var cardTipReversed = reversedTip - cashTipReversed;
            const string refundPaymentQuery = """
                INSERT INTO order_payments
                    (order_id, attempt_no, payment_method, amount, currency_code, status,
                     reference, tip_amount, metadata_json, created_at, created_by)
                VALUES
                    (@orderId, @attemptNo, 'refund', @amount, 'GBP', 'approved',
                     @reference, @tipAmount, @metadataJson, NOW(), @createdBy)
                """;
            using (var refundPaymentCommand = new MySqlCommand(refundPaymentQuery, connection, transaction))
            {
                refundPaymentCommand.Parameters.AddWithValue("@orderId", _orderId);
                refundPaymentCommand.Parameters.AddWithValue("@attemptNo", nextAttempt);
                refundPaymentCommand.Parameters.AddWithValue("@amount", -refundAmount);
                refundPaymentCommand.Parameters.AddWithValue("@reference", externalReference);
                refundPaymentCommand.Parameters.AddWithValue("@tipAmount", -reversedTip);
                refundPaymentCommand.Parameters.AddWithValue("@metadataJson", JsonSerializer.Serialize(new
                {
                    refundType = refundAmount >= remainingRefundable ? "full" : "partial",
                    tipReversed = reversedTip,
                    cashTipReversed,
                    cardTipReversed
                }));
                refundPaymentCommand.Parameters.AddWithValue("@createdBy", refundedBy);
                await refundPaymentCommand.ExecuteNonQueryAsync();
            }

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
