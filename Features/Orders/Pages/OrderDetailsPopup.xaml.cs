using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class OrderDetailsPopup : ContentPage
{
    private Order _order;
    
    public OrderDetailsPopup(Order order)
    {
        InitializeComponent();
        _order = order;
        LoadOrderDetails();
    }
    
    private void LoadOrderDetails()
    {
        // Header
        OrderTitleLabel.Text = $"Order Details: {_order.OrderNumber}";
        OrderDateLabel.Text = $"Placed on: {_order.CreatedAt:MMM dd, yyyy, h:mm:ss tt}";
        
        // Details Section - Only Type, Payment Method, Voucher
        TypeLabel.Text = _order.OrderType ?? "Pickup";
        
        // Payment Method
        SetPaymentMethod(_order.PaymentMethod);
        
        // Voucher
        VoucherLabel.Text = "N/A"; // Order model doesn't have VoucherCode field
        
        // Customer Section
        CustomerNameLabel.Text = string.IsNullOrWhiteSpace(_order.CustomerName) ? "N/A" : _order.CustomerName.Trim();
        CustomerPhoneLabel.Text = string.IsNullOrWhiteSpace(_order.CustomerPhone) ? "N/A" : _order.CustomerPhone.Trim();
        CustomerEmailLabel.Text = string.IsNullOrWhiteSpace(_order.CustomerEmail) ? "N/A" : _order.CustomerEmail.Trim();
        CustomerAddressLabel.Text = string.IsNullOrWhiteSpace(_order.CustomerAddress) ? "Collection" : _order.CustomerAddress;
        
        // Order Items - Debug logging
        System.Diagnostics.Debug.WriteLine($" Order {_order.OrderNumber} has {_order.Items?.Count ?? 0} items");
        if (_order.Items != null)
        {
            foreach (var item in _order.Items)
            {
                System.Diagnostics.Debug.WriteLine($"   {item.Quantity}x {item.ItemName} - £{item.ItemPrice:F2}");
            }
        }
        OrderItemsCollection.ItemsSource = _order.Items;
        
        // Payment Summary
        SubtotalLabel.Text = $"£{_order.SubtotalAmount:F2}";
        DeliveryFeeLabel.Text = $"£{_order.DeliveryFee:F2}";
        TotalLabel.Text = $"£{_order.TotalAmount:F2}";
    }
    
    private void SetPaymentMethod(string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            PaymentIcon.Text = "";
            PaymentMethodLabel.Text = "N/A";
            PaymentMethodLabel.TextColor = Color.FromArgb("#6b7280");
            return;
        }
        
        var method = paymentMethod.ToLower();
        
        if (method.Contains("cash"))
        {
            PaymentIcon.Text = "";
            PaymentMethodLabel.Text = "Cash";
            PaymentMethodLabel.TextColor = Color.FromArgb("#10b981");
        }
        else if (method.Contains("card"))
        {
            PaymentIcon.Text = "";
            PaymentMethodLabel.Text = "Card";
            PaymentMethodLabel.TextColor = Color.FromArgb("#3b82f6");
        }
        else if (method.Contains("gift") || method.Contains("voucher"))
        {
            PaymentIcon.Text = "";
            PaymentMethodLabel.Text = "Gift Card";
            PaymentMethodLabel.TextColor = Color.FromArgb("#a855f7");
        }
        else
        {
            PaymentIcon.Text = "";
            PaymentMethodLabel.Text = paymentMethod;
            PaymentMethodLabel.TextColor = Color.FromArgb("#111827");
        }
    }
    
    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
    
    private async void OnPrintClicked(object sender, EventArgs e)
    {
        try
        {
            var receiptService = ServiceHelper.GetService<ReceiptService>();
            if (receiptService == null)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Receipt service not available");
                return;
            }
            
            await receiptService.PrintReceiptAsync(_order);
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Receipt sent to printer");
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Print Error", $"Failed to print: {ex.Message}");
        }
    }
}
