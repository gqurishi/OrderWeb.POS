namespace OrderWeb.Contracts.Features;

public static class PosFeatureKeys
{
    public const string DineIn = "pos.dine_in";
    public const string Collection = "pos.collection";
    public const string Delivery = "pos.delivery";
    public const string LiveOrders = "pos.live_orders";
    public const string Reservations = "pos.reservations";
    public const string Customers = "pos.customers";
    public const string Payments = "pos.payments";
    public const string GiftCards = "pos.gift_cards";
    /// <summary>
    /// Loyalty / customer points. Terminal Access UI label: <c>Loyalty</c>.
    /// Client POS loyalty APIs require this grant (<c>pos.customer_points</c>).
    /// </summary>
    public const string CustomerPoints = "pos.customer_points";
    /// <summary>Website food orders. Mother only. Never grant to Client POS.</summary>
    public const string WebOrders = "pos.web_orders";
    public const string SplitBill = "pos.split_bill";
    public const string TableTransfer = "pos.table_transfer";
    public const string KitchenStatus = "pos.kitchen_status";
    public const string Refunds = "pos.refunds";
}

public sealed record PosFeature(string Key, bool IsEnabled, string? Version = null);
