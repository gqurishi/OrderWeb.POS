using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MySqlConnector;

namespace POS_in_NET.Pages
{
    public partial class OrderDetailsModal : ContentPage
    {
        private readonly DatabaseService _databaseService;
        private readonly int _orderId;
        private ObservableCollection<OrderItemDisplay> OrderItems { get; set; } = new();
        
        private string _orderNumber = string.Empty;
        private decimal _totalAmount = 0;

        public OrderDetailsModal(int orderId)
        {
            InitializeComponent();
            
            _databaseService = new DatabaseService();
            _orderId = orderId;
            
            OrderItemsCollection.ItemsSource = OrderItems;
            
            LoadOrderDetails();
        }

        private async void LoadOrderDetails()
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                // Load order header
                var orderQuery = @"
                    SELECT o.order_id, o.order_type, o.status, o.local_lifecycle_state, o.created_at,
                           o.subtotal, o.vat_amount, o.discount_amount, o.total_amount,
                           o.customer_name, o.customer_phone, o.delivery_address
                    FROM orders o
                    WHERE o.id = @orderId
                    AND COALESCE(o.source_channel, 'local') = 'local'";
                
                using var orderCommand = new MySqlCommand(orderQuery, connection);
                orderCommand.Parameters.AddWithValue("@orderId", _orderId);
                
                using var orderReader = await orderCommand.ExecuteReaderAsync();
                
                if (await orderReader.ReadAsync())
                {
                    _orderNumber = orderReader.GetString("order_id");
                    OrderNumberLabel.Text = _orderNumber;
                    
                    var createdAt = orderReader.GetDateTime("created_at");
                    OrderDateLabel.Text = $"{createdAt:dd/MM/yyyy h:mm tt}";
                    
                    var orderType = orderReader.GetString("order_type");
                    var normalizedOrderType = orderType.Trim().ToLowerInvariant();
                    OrderTypeLabel.Text = normalizedOrderType switch
                    {
                        "pickup" => " Collection",
                        "collection" => " Collection",
                        "col" => " Collection",
                        "delivery" => " Delivery",
                        "del" => " Delivery",
                        "table" => " Table",
                        "tbl" => " Table",
                        "dine_in" => " Table",
                        "dine-in" => " Table",
                        _ => orderType.ToUpperInvariant()
                    };
                    
                    var status = orderReader.GetString("status");
                    OrderStatusLabel.Text = status.ToUpper();
                    
                    // Hide refund button for voided orders
                    var lifecycleState = orderReader.IsDBNull(orderReader.GetOrdinal("local_lifecycle_state")) ? "" : orderReader.GetString("local_lifecycle_state");
                    if (status.ToLower() == "void" || status.ToLower() == "cancelled" || lifecycleState == "voided")
                    {
                        RefundButton.IsVisible = false;
                    }
                    
                    // Totals
                    var subtotal = orderReader.IsDBNull(orderReader.GetOrdinal("subtotal")) ? 0 : orderReader.GetDecimal("subtotal");
                    var vat = orderReader.IsDBNull(orderReader.GetOrdinal("vat_amount")) ? 0 : orderReader.GetDecimal("vat_amount");
                    var discount = orderReader.IsDBNull(orderReader.GetOrdinal("discount_amount")) ? 0 : orderReader.GetDecimal("discount_amount");
                    _totalAmount = orderReader.GetDecimal("total_amount");
                    
                    SubtotalLabel.Text = $"£{subtotal:F2}";
                    VatLabel.Text = $"£{vat:F2}";
                    DiscountLabel.Text = discount > 0 ? $"-£{discount:F2}" : "£0.00";
                    TotalLabel.Text = $"£{_totalAmount:F2}";
                    
                    // Customer info
                    var customerName = orderReader.IsDBNull(orderReader.GetOrdinal("customer_name")) ? "" : orderReader.GetString("customer_name");
                    var customerPhone = orderReader.IsDBNull(orderReader.GetOrdinal("customer_phone")) ? "" : orderReader.GetString("customer_phone");
                    var deliveryAddress = orderReader.IsDBNull(orderReader.GetOrdinal("delivery_address")) ? "" : orderReader.GetString("delivery_address");
                    
                    // Show customer info if available
                    if (!string.IsNullOrEmpty(customerName) || !string.IsNullOrEmpty(customerPhone))
                    {
                        // Customer info display - handled in XAML bindings
                    }
                }
                
                await orderReader.CloseAsync();
                
                // Load order items
                var itemsQuery = @"
                    SELECT oi.item_name, oi.quantity, oi.unit_price, oi.total_price
                    FROM order_items oi
                    WHERE oi.order_id = @orderId
                    ORDER BY oi.id";
                
                using var itemsCommand = new MySqlCommand(itemsQuery, connection);
                itemsCommand.Parameters.AddWithValue("@orderId", _orderId);
                
                using var itemsReader = await itemsCommand.ExecuteReaderAsync();
                
                while (await itemsReader.ReadAsync())
                {
                    OrderItems.Add(new OrderItemDisplay
                    {
                        ItemName = itemsReader.GetString("item_name"),
                        Quantity = itemsReader.GetInt32("quantity"),
                        UnitPrice = itemsReader.GetDecimal("unit_price"),
                        TotalPrice = itemsReader.GetDecimal("total_price")
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
            // TODO: Implement print functionality
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Print", "Printing receipt...");
        }

        private async void OnRefundClicked(object sender, EventArgs e)
        {
            var confirm = await DisplayAlert(
                "Process Refund",
                $"Process refund for {_orderNumber}?\nAmount: £{_totalAmount:F2}",
                "Yes",
                "Cancel"
            );

            if (confirm)
            {
                var refundModal = new RefundModal(_orderId, _orderNumber, _totalAmount);
                refundModal.RefundCompleted += async () =>
                {
                    await Navigation.PopModalAsync();
                };
                await Navigation.PushModalAsync(refundModal);
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
