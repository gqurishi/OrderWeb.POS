using OrderWeb.Contracts.Dtos;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Services;

/// <summary>
/// Fire-and-forget-safe Phase 5 hooks — Mother till + Client paths. Never throws to the till.
/// </summary>
public static class BarStockSaleHooks
{
    public static async Task DeductForOrderSafeAsync(
        Order? order,
        BarStockMovementActorDto? actor = null)
    {
        if (order?.Items is not { Count: > 0 } || string.IsNullOrWhiteSpace(order.OrderId))
        {
            return;
        }

        try
        {
            var lines = BuildLines(order.Items);
            if (lines.Count == 0)
            {
                return;
            }

            var stock = ServiceHelper.GetService<BarStockService>() ?? new BarStockService();
            var result = await stock.DeductSalesForOrderAsync(
                order.OrderId,
                lines,
                actor ?? new BarStockMovementActorDto { Source = "mother" });

            if (result.AppliedCount > 0)
            {
                TryPublishUpdated();
            }

            if (result.OverdrawCount > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BarStockSale] Overdraw on order {order.OrderId}: {result.OverdrawCount} movement(s).");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockSale] Deduct hook failed (ignored): {ex.Message}");
        }
    }

    public static async Task ReverseForOrderSafeAsync(
        Order? order,
        BarStockMovementActorDto? actor = null)
    {
        if (order?.Items is not { Count: > 0 } || string.IsNullOrWhiteSpace(order.OrderId))
        {
            return;
        }

        try
        {
            var lines = BuildLines(order.Items);
            if (lines.Count == 0)
            {
                return;
            }

            var stock = ServiceHelper.GetService<BarStockService>() ?? new BarStockService();
            var result = await stock.ReverseSalesForOrderAsync(
                order.OrderId,
                lines,
                actor ?? new BarStockMovementActorDto { Source = "mother" });

            if (result.AppliedCount > 0)
            {
                TryPublishUpdated();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockSale] Reverse hook failed (ignored): {ex.Message}");
        }
    }

    public static List<BarSaleOrderLineDto> BuildLines(IEnumerable<OrderItem> items)
    {
        var list = new List<BarSaleOrderLineDto>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0 || string.IsNullOrWhiteSpace(item.MenuItemId))
            {
                continue;
            }

            string? lineKey = null;
            if (item.Id > 0)
            {
                lineKey = $"i{item.Id}";
            }
            else if (!string.IsNullOrWhiteSpace(item.ClientItemId))
            {
                lineKey = $"c{item.ClientItemId.Trim()}";
            }

            if (string.IsNullOrWhiteSpace(lineKey))
            {
                continue;
            }

            list.Add(new BarSaleOrderLineDto
            {
                LineKey = lineKey,
                MenuItemId = item.MenuItemId.Trim(),
                Quantity = item.Quantity
            });
        }

        return list;
    }

    private static void TryPublishUpdated()
    {
        try
        {
            var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
            if (broadcast is null)
            {
                return;
            }

            _ = broadcast.PublishDataChangedAsync(
                "barinventory.updated",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockSale] WS publish skipped: {ex.Message}");
        }
    }
}
