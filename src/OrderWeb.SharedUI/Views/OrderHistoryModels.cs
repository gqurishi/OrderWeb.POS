namespace OrderWeb.SharedUI.Views;

/// <summary>Mother Order History type tabs (All / Collection / Delivery / Table / Web).</summary>
public enum OrderHistoryFilter
{
    All,
    Collection,
    Delivery,
    Table,
    Web
}

/// <summary>Host-neutral row for completed or voided history lists.</summary>
public sealed record OrderHistoryRowPresentation(
    string OrderNumber,
    string OrderDateTime,
    string CustomerDisplay,
    string OrderTypeDisplay,
    string PaymentDisplay,
    string StatusDisplay,
    string TotalDisplay,
    bool IsVoided = false,
    object? Tag = null);

public sealed class OrderHistoryFilterChangedEventArgs(OrderHistoryFilter filter) : EventArgs
{
    public OrderHistoryFilter Filter { get; } = filter;
}

public sealed class OrderHistoryRowTappedEventArgs(OrderHistoryRowPresentation row) : EventArgs
{
    public OrderHistoryRowPresentation Row { get; } = row;
}

public static class OrderHistoryFilterCodes
{
    public static string ToApiCode(OrderHistoryFilter filter) => filter switch
    {
        OrderHistoryFilter.Collection => "COL",
        OrderHistoryFilter.Delivery => "DEL",
        OrderHistoryFilter.Table => "TBL",
        OrderHistoryFilter.Web => "WEB",
        _ => "ALL"
    };

    public static OrderHistoryFilter FromApiCode(string? code) =>
        (code ?? "ALL").Trim().ToUpperInvariant() switch
        {
            "COL" or "COLLECTION" => OrderHistoryFilter.Collection,
            "DEL" or "DELIVERY" => OrderHistoryFilter.Delivery,
            "TBL" or "TABLE" => OrderHistoryFilter.Table,
            "WEB" => OrderHistoryFilter.Web,
            _ => OrderHistoryFilter.All
        };
}
