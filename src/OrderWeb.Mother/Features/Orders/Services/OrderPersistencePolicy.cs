using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Final safety boundary for permanent order storage. UI checks improve the
/// workflow, but this policy prevents any caller from creating an empty local
/// table order in the shared orders ledger.
/// </summary>
public static class OrderPersistencePolicy
{
    public const string EmptyTableOrderMessage =
        "An empty table basket cannot be stored as an order. Add an item and send or take payment first.";

    public static bool TryValidate(Order? order, out string message)
    {
        if (order == null)
        {
            message = "Order is required.";
            return false;
        }

        var invalidLine = order.Items.Any(item =>
            item.Quantity <= 0 || string.IsNullOrWhiteSpace(item.ItemName));
        if (invalidLine)
        {
            message = "Every order line requires an item name and a quantity greater than zero.";
            return false;
        }

        if (IsLocalTableOrder(order) && order.Items.Count == 0)
        {
            message = EmptyTableOrderMessage;
            return false;
        }

        message = string.Empty;
        return true;
    }

    public static bool IsLocalTableOrder(Order order)
    {
        var source = (order.SourceChannel ?? string.Empty).Trim().ToLowerInvariant();
        var type = (order.OrderType ?? string.Empty).Trim().ToLowerInvariant();
        return source is "" or "local"
            && type is "table" or "tbl" or "dine_in" or "dine-in";
    }
}
