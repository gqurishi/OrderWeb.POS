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
        private Order? _order;
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

        public OrderDetailsModal(Order order)
        {
            InitializeComponent();
            _orderService = ServiceHelper.GetService<OrderService>() ?? new OrderService();
            _receiptService = ServiceHelper.GetService<ReceiptService>()
                ?? throw new InvalidOperationException("Receipt printing service is unavailable.");
            _order = order ?? throw new ArgumentNullException(nameof(order));
            _orderId = order.Id;
            OrderItemsCollection.ItemsSource = OrderItems;
            LoadOrderDetails();
        }

        private async void LoadOrderDetails()
        {
            try
            {
                var order = _order ?? await _orderService.GetOrderByDatabaseIdAsync(_orderId);
                if (order == null)
                {
                    await AppAlertService.ShowAlertAsync("Order Not Found", "The selected order could not be loaded.");
                    return;
                }

                _order = order;

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

                var customerLines = new[] { order.CustomerName, order.CustomerEmail }
                    .Where(value => !string.IsNullOrWhiteSpace(value));
                CustomerNameLabel.Text = string.Join(" • ", customerLines);
                CustomerPhoneLabel.Text = order.CustomerPhone ?? string.Empty;
                CustomerAddressLabel.Text = order.CustomerAddress ?? string.Empty;
                CustomerInfoFrame.IsVisible = customerLines.Any()
                    || !string.IsNullOrWhiteSpace(order.CustomerPhone)
                    || !string.IsNullOrWhiteSpace(order.CustomerAddress);

                PaymentMethodLabel.Text = OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod);
                PaymentStatusLabel.Text = OnlineOrderPaymentHelper.GetStatusDisplay(order.PaymentMethod, order.PaymentStatusRaw);
                AmountPaidLabel.Text = order.AmountPaid.HasValue ? $"£{order.AmountPaid.Value:F2}" : "Not supplied";
                PaymentReferenceLabel.Text = OnlineOrderPaymentHelper.FormatReceiptReference(order.TransactionId) ?? "Not supplied";
                PaymentProviderLabel.Text = string.IsNullOrWhiteSpace(order.PaymentProvider) ? "Not supplied" : order.PaymentProvider;
                DeliveryFeeLabel.Text = $"£{order.DeliveryFee:F2}";
                ServiceChargeLabel.Text = $"£{order.ServiceChargeAmount:F2}";
                TipsLabel.Text = $"£{order.CashTipAmount + order.CardTipAmount:F2}";

                ScheduledLabel.Text = order.ScheduledTime.HasValue
                    ? order.ScheduledTime.Value.ToString("dd/MM/yyyy h:mm tt")
                    : "As soon as possible";
                InstructionsLabel.Text = string.IsNullOrWhiteSpace(order.SpecialInstructions)
                    ? "None"
                    : order.SpecialInstructions;
                PromoLabel.Text = string.IsNullOrWhiteSpace(order.PromoCode) ? "None" : order.PromoCode;
                GiftCardLabel.Text = order.GiftCardNumberMasked ?? "None";
                LoyaltyLabel.Text = order.LoyaltyPointsEarned == 0 && order.LoyaltyPointsRedeemed == 0
                    ? "None"
                    : $"Earned {order.LoyaltyPointsEarned} • Redeemed {order.LoyaltyPointsRedeemed}";

                OrderItems.Clear();
                foreach (var item in order.Items.Where(item =>
                             !(item.MenuItemId ?? string.Empty).StartsWith("tasting-course:", StringComparison.OrdinalIgnoreCase)))
                {
                    OrderItems.Add(new OrderItemDisplay
                    {
                        ItemName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.ItemName : item.DisplayName,
                        Quantity = item.Quantity,
                        UnitPrice = item.ItemPrice ?? 0m,
                        TotalPrice = item.TotalPrice,
                        Details = BuildItemDetails(item)
                    });
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load order details: {ex.Message}");
            }
        }

        private static string BuildItemDetails(OrderItem item)
        {
            var details = new List<string>();
            if (item.Addons.Count > 0)
            {
                details.Add(string.Join(", ", item.Addons.Select(addon => $"+ {addon.AddonName}")));
            }
            if (!string.IsNullOrWhiteSpace(item.SpecialInstructions))
            {
                details.Add($"Note: {item.SpecialInstructions}");
            }
            return string.Join(" • ", details);
        }

        private async void OnPrintClicked(object sender, EventArgs e)
        {
            var order = _order ?? await _orderService.GetOrderByDatabaseIdAsync(_orderId);
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
        public string Details { get; set; } = string.Empty;
        public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
        public string QuantityDisplay => $"{Quantity}x";
    }
}
