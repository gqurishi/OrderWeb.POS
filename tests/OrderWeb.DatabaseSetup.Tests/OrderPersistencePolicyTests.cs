using POS_in_NET.Models;
using POS_in_NET.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class OrderPersistencePolicyTests
{
    [Fact]
    public void Rejects_empty_local_table_order()
    {
        var order = new Order { OrderType = "table", SourceChannel = "local" };

        var valid = OrderPersistencePolicy.TryValidate(order, out var message);

        Assert.False(valid);
        Assert.Equal(OrderPersistencePolicy.EmptyTableOrderMessage, message);
    }

    [Fact]
    public void Accepts_local_table_order_after_first_valid_item()
    {
        var order = new Order
        {
            OrderType = "table",
            SourceChannel = "local",
            Items = [new OrderItem { ItemName = "Soup", Quantity = 1 }]
        };

        Assert.True(OrderPersistencePolicy.TryValidate(order, out var message));
        Assert.Empty(message);
    }

    [Fact]
    public void Rejects_zero_quantity_line()
    {
        var order = new Order
        {
            OrderType = "table",
            SourceChannel = "local",
            Items = [new OrderItem { ItemName = "Soup", Quantity = 0 }]
        };

        Assert.False(OrderPersistencePolicy.TryValidate(order, out var message));
        Assert.Contains("quantity", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Does_not_block_legacy_non_table_empty_record_at_policy_boundary()
    {
        var order = new Order { OrderType = "pickup", SourceChannel = "local" };

        Assert.True(OrderPersistencePolicy.TryValidate(order, out _));
    }
}
