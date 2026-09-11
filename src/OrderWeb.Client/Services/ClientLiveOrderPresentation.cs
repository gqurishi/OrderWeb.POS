using OrderWeb.Client.Models;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Services;

/// <summary>
/// Shared Client → LiveOrderBoardView mapping (MainPage + LiveOrderPage).
/// Table tab filters table-type orders (not Mother sessions).
/// </summary>
public static class ClientLiveOrderPresentation
{
    public static LiveOrderFilter ToFilter(string? value) =>
        NormalizeType(value) switch
        {
            "Collection" => LiveOrderFilter.Collection,
            "Delivery" => LiveOrderFilter.Delivery,
            "Table" => LiveOrderFilter.Table,
            _ => LiveOrderFilter.All
        };

    public static string FromFilter(LiveOrderFilter filter) => filter switch
    {
        LiveOrderFilter.Collection => "Collection",
        LiveOrderFilter.Delivery => "Delivery",
        LiveOrderFilter.Table => "Table",
        _ => "All"
    };

    public static string EmptyText(LiveOrderFilter filter) => LiveOrderSampleData.EmptyTextFor(filter);

    public static string NormalizeType(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "all" => "All",
            "delivery" or "del" => "Delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "Table",
            _ => "Collection"
        };

    public static IReadOnlyList<MotherOrderState> OpenOrdersOnly(IReadOnlyList<MotherOrderState> orders) =>
        orders
            .Where(order => !string.Equals(order.Status, "Closed", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static IReadOnlyList<LiveOrderCardPresentation> Map(
        IReadOnlyList<MotherOrderState> openOrders,
        LiveOrderFilter filter)
    {
        var selected = FromFilter(filter);
        return openOrders
            .Select(order => (order, type: NormalizeType(order.OrderType)))
            .Where(pair => filter == LiveOrderFilter.All || pair.type == selected)
            .OrderByDescending(pair => pair.type == "Table")
            .ThenBy(pair => FormatTime(pair.order.UpdatedUtc))
            .Select(pair => MapOne(pair.order, pair.type))
            .ToList();
    }

    public static string Fingerprint(IReadOnlyList<MotherOrderState> openOrders, string selectedFilter)
    {
        var selected = NormalizeType(selectedFilter);
        var parts = OpenOrdersOnly(openOrders)
            .Select(order =>
            {
                var type = NormalizeType(order.OrderType);
                if (selected != "All" && type != selected)
                {
                    return null;
                }

                return $"{order.OrderId}:{order.Version}:{order.Status}:{order.Total:F2}:{order.UpdatedUtc}";
            })
            .Where(part => part is not null)
            .OrderBy(part => part, StringComparer.Ordinal);
        return selected + "#" + string.Join("|", parts);
    }

    public static string FormatNumber(string? orderNumber, string? fallbackId)
    {
        var value = !string.IsNullOrWhiteSpace(orderNumber) ? orderNumber.Trim() : fallbackId?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Order";
        }

        return value.StartsWith("#", StringComparison.Ordinal) ? value : $"#{value}";
    }

    public static string FormatTime(string? value)
    {
        if (DateTimeOffset.TryParse(value, out var timestamp))
        {
            return timestamp.ToLocalTime().ToString("HH:mm · dd/MM");
        }

        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static LiveOrderCardPresentation MapOne(MotherOrderState order, string type)
    {
        string? subtitle = null;
        if (type == "Table")
        {
            subtitle = !string.IsNullOrWhiteSpace(order.TableNumber)
                ? $"Table {order.TableNumber}"
                : "Table";
        }
        else if (HasCustomerName(order.CustomerName))
        {
            subtitle = order.CustomerName!.Trim();
        }

        return new LiveOrderCardPresentation(
            Key: order.OrderId,
            Kind: LiveOrderCardKind.Order,
            Title: type,
            OrderNumber: FormatNumber(order.OrderNumber, order.OrderId),
            Subtitle: subtitle,
            TotalText: $"£{order.Total:F2}",
            TimeText: FormatTime(order.UpdatedUtc),
            AccentColorHex: "#10B981",
            Badges: null);
    }

    private static bool HasCustomerName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !string.Equals(name.Trim(), "Guest", StringComparison.OrdinalIgnoreCase);
}
