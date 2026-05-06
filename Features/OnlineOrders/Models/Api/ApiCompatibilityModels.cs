using System.Text.Json.Serialization;
using POS_in_NET.Models;

namespace POS_in_NET.Models.Api;

public class ApiConfiguration
{
    public string BaseUrl { get; set; } = "https://orderweb.net/api";
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? RestaurantId { get; set; }
    public string? LocationId { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int PollingIntervalSeconds { get; set; } = 30;
    public bool IsEnabled { get; set; } = true;
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
}

public class OrdersResponse
{
    public List<ApiOrder> Orders { get; set; } = new();
}

public class ApiOrder
{
    public string Id { get; set; } = string.Empty;
    public string? OrderNumber { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string? Address { get; set; }
    public string? Total { get; set; }
    public string? Subtotal { get; set; }
    public string? DeliveryFee { get; set; }
    public string? Tax { get; set; }
    public string? OrderType { get; set; }
    public string? PaymentMethod { get; set; }
    public string? SpecialInstructions { get; set; }
    public DateTime? ScheduledTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<CloudOrderItem> Items { get; set; } = new();

    public Order ToOrder()
    {
        decimal.TryParse(Total, out var total);
        decimal.TryParse(Subtotal, out var subtotal);
        decimal.TryParse(DeliveryFee, out var deliveryFee);
        decimal.TryParse(Tax, out var tax);

        var order = new Order
        {
            OrderId = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString() : Id,
            OrderNumber = OrderNumber,
            CloudOrderId = Id,
            CustomerName = string.IsNullOrWhiteSpace(CustomerName) ? "Online Customer" : CustomerName,
            CustomerPhone = CustomerPhone,
            CustomerEmail = CustomerEmail,
            CustomerAddress = Address,
            TotalAmount = total,
            SubtotalAmount = subtotal,
            DeliveryFee = deliveryFee,
            TaxAmount = tax,
            OrderType = OrderType,
            PaymentMethod = PaymentMethod,
            SpecialInstructions = SpecialInstructions,
            ScheduledTime = ScheduledTime,
            Status = OrderStatus.New,
            SyncStatus = SyncStatus.Synced,
            CreatedAt = CreatedAt,
            UpdatedAt = DateTime.Now,
            Items = new List<OrderItem>()
        };

        foreach (var item in Items)
        {
            var orderItem = new OrderItem
            {
                OrderId = order.OrderId,
                CloudItemId = item.Id,
                MenuItemId = item.MenuItemId,
                ItemName = item.Name ?? "Unknown Item",
                Quantity = item.Quantity,
                ItemPrice = item.Price,
                SpecialInstructions = item.SpecialInstructions,
                Addons = item.SelectedAddons.Select(a => new OrderItemAddon
                {
                    AddonId = a.Id,
                    AddonName = a.Name ?? "Addon",
                    AddonPrice = a.Price,
                    Quantity = 1
                }).ToList()
            };

            order.Items.Add(orderItem);
        }

        return order;
    }
}

public class OrderStatusUpdate
{
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdateTime { get; set; } = DateTime.Now;
    public string? Notes { get; set; }
    public string? UpdatedBy { get; set; }
}

public class OrderWebApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public List<CloudOrderResponse> Orders { get; set; } = new();
    public List<CloudOrderResponse> PendingOrders { get; set; } = new();
}

public class CloudOrderResponse
{
    public string Id { get; set; } = string.Empty;
    public string? OrderId
    {
        get => Id;
        set => Id = value ?? string.Empty;
    }

    public string OrderNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string? Address { get; set; }
    public string Total { get; set; } = "0";
    public string Subtotal { get; set; } = "0";
    public string DeliveryFee { get; set; } = "0";
    public string Tax { get; set; } = "0";
    public string? OrderType { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentStatus { get; set; }
    public string? VoucherCode { get; set; }
    public string? SpecialInstructions { get; set; }
    public DateTime? ScheduledTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<CloudOrderItem> Items { get; set; } = new();

    public decimal TotalAmount => decimal.TryParse(Total, out var value) ? value : 0m;
}

public class CloudOrderItem
{
    public int Id { get; set; }
    public string? MenuItemId { get; set; }
    public string? Name { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal? Price { get; set; }
    public string? SpecialInstructions { get; set; }
    public List<CloudOrderAddon> SelectedAddons { get; set; } = new();

    public decimal GetTotalPrice()
    {
        var basePrice = (Price ?? 0m) * Quantity;
        var addonTotal = SelectedAddons.Sum(a => (a.Price ?? 0m) * Quantity);
        return basePrice + addonTotal;
    }
}

public class CloudOrderAddon
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public decimal? Price { get; set; }
}

public class PendingAck
{
    public int Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime? PrintedAt { get; set; }
    public string? DeviceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int RetryCount { get; set; }
}

public class PullOrderDto
{
    public long OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public string? OrderType { get; set; }
    public string? SpecialInstructions { get; set; }
    public string? ScheduledFor { get; set; }
    public string? PrintStatus { get; set; }
    public PullOrderCustomer? Customer { get; set; }
    public PullOrderPayment? Payment { get; set; }
    public List<PullOrderItem> Items { get; set; } = new();
}

public class PullOrderCustomer
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
}

public class PullOrderPayment
{
    public double? Total { get; set; }
    public double? Subtotal { get; set; }
    public double? Tax { get; set; }
    public string? Method { get; set; }
    public string? Status { get; set; }
}

public class PullOrderItem
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int Quantity { get; set; }
    public double Price { get; set; }
    public string? SpecialInstructions { get; set; }
    public List<PullOrderModifier> Modifiers { get; set; } = new();
}

public class PullOrderModifier
{
    public string? Name { get; set; }
    public double Price { get; set; }
}
