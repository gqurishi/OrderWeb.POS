using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using MySqlConnector;

namespace POS_in_NET.Pages
{
    public partial class RefundModal : ContentPage
    {
        private readonly DatabaseService _databaseService;
        private readonly int _orderId;
        private readonly string _orderNumber;
        private readonly decimal _originalAmount;
        private bool _isFullRefund = true;

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
            if (decimal.TryParse(e.NewTextValue, out decimal amount))
            {
                if (amount > _originalAmount)
                {
                    RefundAmountEntry.Text = _originalAmount.ToString("F2");
                    return;
                }
                RefundSummaryLabel.Text = $"£{amount:F2}";
            }
            else
            {
                RefundSummaryLabel.Text = "£0.00";
            }
        }

        private async void OnConfirmRefundClicked(object sender, EventArgs e)
        {
            decimal refundAmount;
            
            if (_isFullRefund)
            {
                refundAmount = _originalAmount;
            }
            else
            {
                if (!decimal.TryParse(RefundAmountEntry.Text, out refundAmount) || refundAmount <= 0)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Invalid Amount", "Please enter a valid refund amount");
                    return;
                }
                
                if (refundAmount > _originalAmount)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Invalid Amount", "Refund amount cannot exceed original amount");
                    return;
                }
            }

            var confirm = await DisplayAlert(
                "Confirm Refund",
                $"Process refund of £{refundAmount:F2} for {_orderNumber}?\n\nThis action cannot be undone.",
                "Confirm",
                "Cancel"
            );

            if (!confirm)
                return;

            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                // Create refund record
                var insertRefundQuery = @"
                    INSERT INTO order_refunds 
                    (order_id, refund_amount, refund_type, reason, refunded_at, refunded_by)
                    VALUES 
                    (@orderId, @refundAmount, @refundType, @reason, NOW(), 'Staff')";
                
                using var insertCommand = new MySqlCommand(insertRefundQuery, connection);
                insertCommand.Parameters.AddWithValue("@orderId", _orderId);
                insertCommand.Parameters.AddWithValue("@refundAmount", refundAmount);
                insertCommand.Parameters.AddWithValue("@refundType", _isFullRefund ? "full" : "partial");
                insertCommand.Parameters.AddWithValue("@reason", RefundReasonEditor.Text ?? "");
                
                await insertCommand.ExecuteNonQueryAsync();
                
                // Update order status if full refund
                if (_isFullRefund)
                {
                    var updateOrderQuery = @"
                        UPDATE orders 
                        SET status = 'refunded',
                            updated_by_terminal_name = @updatedByTerminalName,
                            updated_by_terminal_at = NOW(),
                            updated_at = NOW()
                        WHERE id = @orderId";
                    
                    using var updateCommand = new MySqlCommand(updateOrderQuery, connection);
                    updateCommand.Parameters.AddWithValue("@orderId", _orderId);
                    updateCommand.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
                    await updateCommand.ExecuteNonQueryAsync();
                }
                
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Refund of £{refundAmount:F2} processed successfully");
                
                RefundCompleted?.Invoke();
                await Navigation.PopModalAsync();
            }
            catch (MySqlException ex) when (ex.Message.Contains("order_refunds"))
            {
                // Table doesn't exist, create it
                await CreateRefundTableAsync();
                
                // Retry the refund
                OnConfirmRefundClicked(sender, e);
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to process refund: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task CreateRefundTableAsync()
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                var createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS order_refunds (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        order_id INT NOT NULL,
                        refund_amount DECIMAL(10,2) NOT NULL,
                        refund_type ENUM('full', 'partial') NOT NULL,
                        reason TEXT,
                        refunded_at DATETIME NOT NULL,
                        refunded_by VARCHAR(100),
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        INDEX idx_order_id (order_id)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
                
                using var command = new MySqlCommand(createTableQuery, connection);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to create refunds table: {ex.Message}");
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
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
}
