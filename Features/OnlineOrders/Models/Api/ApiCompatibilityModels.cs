using System.Text.Json;
using System.Text.Json.Serialization;
using POS_in_NET.Models;
using POS_in_NET.Services;

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
            PaymentMethod = OnlineOrderPaymentHelper.GetStorageMethod(PaymentMethod),
            SpecialInstructions = SpecialInstructions,
            ScheduledTime = ScheduledTime,
            Status = OrderStatus.New,
            LocalLifecycleState = LocalLifecycleState.Active,
            IsOpen = true,
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
                CloudItemId = int.TryParse(item.Id, out var numericCloudItemId) ? numericCloudItemId : null,
                CloudItemExternalId = item.Id,
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
    [JsonPropertyName("contract_version")]
    public int? ContractVersion { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public List<CloudOrderResponse> Orders { get; set; } = new();
    public List<CloudOrderResponse> PendingOrders { get; set; } = new();
}

public class CloudOrderResponse
{
    [JsonPropertyName("contract_version")]
    public int ContractVersion { get; set; } = 1;
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string? OrderId
    {
        get => string.IsNullOrWhiteSpace(Id) ? null : Id;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Id = value.Trim();
            }
        }
    }

    [JsonPropertyName("order_number")]
    public string OrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("customer_phone")]
    public string? CustomerPhone { get; set; }

    [JsonPropertyName("customer_email")]
    public string? CustomerEmail { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("total")]
    public string Total { get; set; } = "0";

    [JsonPropertyName("subtotal")]
    public string Subtotal { get; set; } = "0";

    [JsonPropertyName("delivery_fee")]
    public string DeliveryFee { get; set; } = "0";

    [JsonPropertyName("discount_amount")]
    public string DiscountAmount { get; set; } = "0";

    [JsonPropertyName("service_charge_percentage")] public string ServiceChargePercentage { get; set; } = "0";
    [JsonPropertyName("service_charge_basis")] public string ServiceChargeBasis { get; set; } = "0";
    [JsonPropertyName("service_charge_amount")] public string ServiceChargeAmount { get; set; } = "0";
    [JsonPropertyName("service_charge_status")] public string ServiceChargeStatus { get; set; } = "not_configured";
    [JsonPropertyName("service_charge_classification")] public string? ServiceChargeClassification { get; set; }
    [JsonPropertyName("service_charge_removal_reason")] public string? ServiceChargeRemovalReason { get; set; }
    [JsonPropertyName("service_charge_removed_by_user_id")] public int? ServiceChargeRemovedByUserId { get; set; }
    [JsonPropertyName("service_charge_removed_by")] public string? ServiceChargeRemovedByName { get; set; }
    [JsonPropertyName("service_charge_approved_by_user_id")] public int? ServiceChargeApprovedByUserId { get; set; }
    [JsonPropertyName("service_charge_approved_by")] public string? ServiceChargeApprovedByName { get; set; }
    [JsonPropertyName("service_charge_removed_at")] public DateTime? ServiceChargeRemovedAt { get; set; }
    [JsonPropertyName("cash_tips")] public string CashTips { get; set; } = "0";
    [JsonPropertyName("card_tips")] public string CardTips { get; set; } = "0";

    [JsonPropertyName("tax")]
    public string Tax { get; set; } = "0";

    [JsonPropertyName("order_type")]
    public string? OrderType { get; set; }

    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; set; }

    [JsonPropertyName("payment_status")]
    public string? PaymentStatus { get; set; }

    [JsonPropertyName("amount_paid")]
    public string? AmountPaid { get; set; }

    [JsonPropertyName("payment_provider")]
    public string? PaymentProvider { get; set; }

    [JsonPropertyName("payment_reference")]
    public string? PaymentReference { get; set; }

    [JsonPropertyName("transaction_id")]
    public string? TransactionId
    {
        get => PaymentReference;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                PaymentReference = value.Trim();
            }
        }
    }

    [JsonPropertyName("currency")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("voucher_code")]
    public string? VoucherCode { get; set; }

    [JsonPropertyName("promo_code")]
    public string? PromoCode { get; set; }

    [JsonPropertyName("gift_card")]
    public CloudGiftCardSummary? GiftCard { get; set; }

    [JsonPropertyName("loyalty")]
    public CloudLoyaltySummary? Loyalty { get; set; }

    [JsonPropertyName("special_instructions")]
    public string? SpecialInstructions { get; set; }

    [JsonPropertyName("scheduled_for")]
    public DateTime? ScheduledTime { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("items")]
    public List<CloudOrderItem> Items { get; set; } = new();

    public decimal TotalAmount => decimal.TryParse(Total, out var value) ? value : 0m;
}

public class CloudOrderItem
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string? Id { get; set; }
    [JsonPropertyName("menuItemId")]
    public string? MenuItemId { get; set; }
    [JsonPropertyName("variantId")]
    public string? VariantId { get; set; }
    [JsonPropertyName("variantName")]
    public string? VariantName { get; set; }
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
    [JsonPropertyName("variant_id")]
    public string? VariantIdSnake { get => VariantId; set => VariantId = value; }
    [JsonPropertyName("variant_name")]
    public string? VariantNameSnake { get => VariantName; set => VariantName = value; }
    [JsonPropertyName("display_name")]
    public string? DisplayNameSnake { get => DisplayName; set => DisplayName = value; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;
    [JsonPropertyName("price")]
    public decimal? Price { get; set; }
    [JsonPropertyName("specialInstructions")]
    public string? SpecialInstructions { get; set; }
    [JsonPropertyName("selectedAddons")]
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
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("price")]
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
    public double? DeliveryFee { get; set; }
    public double? DiscountAmount { get; set; }
    public double? ServiceChargePercentage { get; set; }
    public double? ServiceChargeBasis { get; set; }
    public double? ServiceChargeAmount { get; set; }
    public string? ServiceChargeStatus { get; set; }
    public double? CashTips { get; set; }
    public double? CardTips { get; set; }
    public double? AmountPaid { get; set; }
    public string? Method { get; set; }
    public string? Status { get; set; }
    public string? Provider { get; set; }
    public string? TransactionId { get; set; }
    public string? Reference { get; set; }
    public string? Currency { get; set; }
    public string? VoucherCode { get; set; }
}

public class CloudGiftCardSummary
{
    // Deliberately accept only an already-masked display value. Full card
    // numbers from the cloud are ignored by the typed contract.
    [JsonPropertyName("card_number_masked")]
    public string? CardNumberMasked { get; set; }

    [JsonPropertyName("card_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CardNumber
    {
        get => null;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.Trim();
            CardNumberMasked = trimmed.Length <= 4 ? trimmed : $"****{trimmed[^4..]}";
        }
    }

    [JsonPropertyName("amount_paid")]
    public string? AmountPaid { get; set; }

    [JsonPropertyName("remaining_balance")]
    public string? RemainingBalance { get; set; }
}

public class CloudLoyaltySummary
{
    [JsonPropertyName("points_earned")]
    public int PointsEarned { get; set; }

    [JsonPropertyName("points_redeemed")]
    public int PointsRedeemed { get; set; }

    [JsonPropertyName("points_discount")]
    public string PointsDiscount { get; set; } = "0";

    [JsonPropertyName("balance_after")]
    public int? BalanceAfter { get; set; }
}

public sealed class FlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var integer) => integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException("Expected a string or number identifier.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
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
