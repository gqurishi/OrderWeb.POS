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
            "Web" => LiveOrderFilter.Web,
            _ => LiveOrderFilter.All
        };

    public static string FromFilter(LiveOrderFilter filter) => filter switch
    {
        LiveOrderFilter.Collection => "Collection",
        LiveOrderFilter.Delivery => "Delivery",
        LiveOrderFilter.Table => "Table",
        LiveOrderFilter.Web => "Web",
        _ => "All"
    };

    public static string EmptyText(LiveOrderFilter filter) => LiveOrderSampleData.EmptyTextFor(filter);

    public static string NormalizeType(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "all" => "All",
            "delivery" or "del" => "Delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "Table",
            "web" or "weborder" or "web_order" => "Web",
            _ => "Collection"
        };

    /// <summary>
    /// Mother Live Order is a kitchen board: drafts stay off until Send to Kitchen.
    /// </summary>
    public static IReadOnlyList<MotherOrderState> OpenOrdersOnly(IReadOnlyList<MotherOrderState> orders) =>
        orders.Where(IsKitchenBoardOrder).ToList();

    public static bool IsKitchenBoardOrder(MotherOrderState order) =>
        IsKitchenBoardStatus(order.Status);

    public static bool IsKitchenBoardStatus(string? status)
    {
        var key = NormalizeStatus(status);
        return key is "senttokitchen" or "kitchen" or "preparing" or "ready"
            or "sent" or "sentpartial" or "sentfull" or "paymentpartial" or "foodserved";
    }

    public static bool IsTerminalOrderStatus(string? status)
    {
        var key = NormalizeStatus(status);
        return key is "closed" or "cancelled" or "canceled" or "void" or "voided" or "paid" or "completed";
    }

    /// <summary>
    /// Mother GET /api/client/orders is already kitchen-filtered. Stamp status so a later
    /// cache read still matches the board rule when the payload status is a customer name.
    /// </summary>
    public static MotherOrderState AsKitchenBoardOrder(MotherOrderState order)
    {
        if (IsTerminalOrderStatus(order.Status) || IsKitchenBoardStatus(order.Status))
        {
            return order;
        }

        return order with { Status = "sent_to_kitchen" };
    }

    private static string NormalizeStatus(string? status) =>
        new((status ?? string.Empty).Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static IReadOnlyList<LiveOrderCardPresentation> Map(
        IReadOnlyList<MotherOrderState> openOrders,
        LiveOrderFilter filter)
    {
        var selected = FromFilter(filter);
        return openOrders
            .Select(order => (order, type: NormalizeType(order.OrderType)))
            .Where(pair =>
            {
                if (filter == LiveOrderFilter.All)
                {
                    return true;
                }

                if (filter == LiveOrderFilter.Web)
                {
                    // Web cash-due list needs SourceChannel/PaymentMethod from Mother open-orders API.
                    return IsWebCashDue(pair.order);
                }

                return pair.type == selected;
            })
            .OrderByDescending(pair => pair.type == "Table")
            .ThenBy(pair => FormatTime(pair.order.UpdatedUtc))
            .Select(pair => MapOne(pair.order, pair.type))
            .ToList();
    }

    public static string Fingerprint(IReadOnlyList<MotherOrderState> openOrders, string selectedFilter)
    {
        var filter = ToFilter(selectedFilter);
        var selected = FromFilter(filter);
        var parts = OpenOrdersOnly(openOrders)
            .Select(order =>
            {
                var type = NormalizeType(order.OrderType);
                if (filter == LiveOrderFilter.Web)
                {
                    if (!IsWebCashDue(order))
                    {
                        return null;
                    }
                }
                else if (selected != "All" && type != selected)
                {
                    return null;
                }

                return $"{order.OrderId}:{order.Version}:{order.Status}:{order.Total:F2}:{order.UpdatedUtc}";
            })
            .Where(part => part is not null)
            .OrderBy(part => part, StringComparer.Ordinal);
        return selected + "#" + string.Join("|", parts);
    }

    /// <summary>
    /// Cash-due web orders when Mother sends channel/method on open-order state.
    /// Until those fields exist on Client cache, Web tab stays empty (Mother Live Order is the desk).
    /// </summary>
    public static bool IsWebCashDue(MotherOrderState order) =>
        string.Equals(order.SourceChannel, "web", StringComparison.OrdinalIgnoreCase)
        && IsDeferredCashMethod(order.PaymentMethod);

    private static bool IsDeferredCashMethod(string? paymentMethod)
    {
        var key = new string((paymentMethod ?? "cash")
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return key is "cash" or "cod" or "cashondelivery" or "cashoncollection" or "";
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
            Badges: BuildBadges(order, type));
    }

    private static IReadOnlyList<LiveOrderBadgePresentation>? BuildBadges(MotherOrderState order, string type)
    {
        if (!string.Equals(order.SourceChannel, "web", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var badges = new List<LiveOrderBadgePresentation>
        {
            new("WEB", "#DBEAFE", "#1D4ED8"),
            new(type.ToUpperInvariant(), "#E0F2FE", "#0369A1")
        };

        if (IsWebCashDue(order))
        {
            badges.Add(new LiveOrderBadgePresentation("CASH DUE", "#FEF3C7", "#B45309"));
        }

        return badges;
    }

    private static bool HasCustomerName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !string.Equals(name.Trim(), "Guest", StringComparison.OrdinalIgnoreCase);
}
