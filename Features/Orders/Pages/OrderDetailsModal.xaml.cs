using Microsoft.Maui.Controls;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace POS_in_NET.Pages
{
    public partial class OrderDetailsModal : ContentPage
    {
        private readonly OrderService _orderService;
        private readonly ReceiptService _receiptService;
        private readonly int _orderId;
        private ObservableCollection<OrderItemDisplay> OrderItems { get; set; } = new();
        
        public OrderDetailsModal(int orderId)
        {
            InitializeComponent();
            
            _orderService = ServiceHelper.GetService<OrderService>() ?? new OrderService();
            _receiptService = ServiceHelper.GetService<ReceiptService>()
                ?? throw new InvalidOperationException("Receipt printing service is unavailable.");
            _orderId = orderId;
            
            OrderItemsCollection.ItemsSource = OrderItems;
            
            LoadOrderDetails();
        }

        private async void LoadOrderDetails()
        {
            try
            {
                var order = await _orderService.GetOrderByDatabaseIdAsync(_orderId);
                if (order == null)
                {
                    await AppAlertService.ShowAlertAsync("Order Not Found", "The selected order could not be loaded.");
                    return;
                }

                OrderNumberLabel.Text = string.IsNullOrWhiteSpace(order.OrderNumber)
                    ? order.OrderId
                    : order.OrderNumber;
                OrderDateLabel.Text = $"{order.CreatedAt:dd/MM/yyyy h:mm tt}";

                var orderType = order.OrderType ?? string.Empty;
                OrderTypeLabel.Text = orderType.Trim().ToLowerInvariant() switch
                {
                    "pickup" or "collection" or "col" => "Collection",
                    "delivery" or "del" => "Delivery",
                    "table" or "tbl" or "dine_in" or "dine-in" => "Table",
                    _ => string.IsNullOrWhiteSpace(orderType) ? "Order" : orderType
                };
                OrderStatusLabel.Text = order.LocalLifecycleState == LocalLifecycleState.Voided
                    ? "VOIDED"
                    : order.Status.ToString().ToUpperInvariant();

                SubtotalLabel.Text = $"£{order.SubtotalAmount:F2}";
                VatLabel.Text = $"£{order.TaxAmount:F2}";
                DiscountLabel.Text = order.DiscountAmount > 0 ? $"-£{order.DiscountAmount:F2}" : "£0.00";
                TotalLabel.Text = $"£{order.TotalAmount:F2}";

                OrderItems.Clear();
                foreach (var item in order.Items.Where(item =>
                             !(item.MenuItemId ?? string.Empty).StartsWith("tasting-course:", StringComparison.OrdinalIgnoreCase)))
                {
                    OrderItems.Add(new OrderItemDisplay
                    {
                        ItemName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.ItemName : item.DisplayName,
                        Quantity = item.Quantity,
                        UnitPrice = item.ItemPrice ?? 0m,
                        TotalPrice = item.TotalPrice
                    });
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load order details: {ex.Message}");
            }
        }

        private async void OnPrintClicked(object sender, EventArgs e)
        {
            var order = await _orderService.GetOrderByDatabaseIdAsync(_orderId);
            if (order == null)
            {
                await AppAlertService.ShowAlertAsync("Print Failed", "The selected order could not be found.");
                return;
            }

            var printed = await _receiptService.PrintFullCustomerReceiptAsync(order);
            if (!printed)
            {
                await AppAlertService.ShowAlertAsync(
                    "Print Failed",
                    "The receipt could not be queued. Check the receipt printer and Manage Queue.");
            }
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }

    public class OrderItemDisplay
    {
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
        public string QuantityDisplay => $"{Quantity}x";
    }
}
