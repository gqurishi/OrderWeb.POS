using System.Text.Json;
using POS_in_NET.Models.Api;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public class OrderWebContractV2Tests
{
    [Fact]
    public void FullContractV2_DeserializesUuidItemAndMasksGiftCardSecret()
    {
        const string json = """
        {
          "contract_version": 2,
          "id": "8f94590e-6c85-4dde-9216-43ca6429ad91",
          "order_number": "BIS-10452",
          "subtotal": "25.00",
          "discount_amount": "2.00",
          "delivery_fee": "3.00",
          "service_charge_percentage": "0.00",
          "service_charge_basis": "0.00",
          "service_charge_amount": "0.00",
          "cash_tips": "0.00",
          "card_tips": "0.00",
          "tax": "0.00",
          "total": "26.00",
          "order_type": "delivery",
          "payment_method": "gift_card",
          "payment_status": "paid",
          "amount_paid": "26.00",
          "currency": "GBP",
          "gift_card": {
            "card_number": "SECRET-GIFT-CARD-1234",
            "amount_paid": "26.00",
            "remaining_balance": "10.00"
          },
          "loyalty": {
            "points_earned": 26,
            "points_redeemed": 0,
            "points_discount": "0.00",
            "balance_after": 120
          },
          "items": [{
            "id": "item-uuid-123",
            "menuItemId": "menu-1",
            "name": "Beef Burger",
            "quantity": 2,
            "price": 8.5,
            "selectedAddons": [{ "id": "addon-1", "name": "Cheese", "price": 1.0 }]
          }]
        }
        """;

        var order = JsonSerializer.Deserialize<CloudOrderResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(order);
        Assert.Equal(2, order!.ContractVersion);
        Assert.Equal("item-uuid-123", order.Items.Single().Id);
        Assert.Equal("****1234", order.GiftCard?.CardNumberMasked);
        Assert.Equal(26, order.Loyalty?.PointsEarned);

        var safeJson = JsonSerializer.Serialize(order);
        Assert.DoesNotContain("SECRET-GIFT-CARD-1234", safeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"card_number\"", safeJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContractV2_AcceptsLegacyNumericItemIdentifierWithoutLosingIt()
    {
        const string json = """{"id":501,"name":"Item","quantity":1,"price":1.0}""";
        var item = JsonSerializer.Deserialize<CloudOrderItem>(json);
        Assert.Equal("501", item?.Id);
    }
}
