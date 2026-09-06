namespace OrderWeb.Contracts.Features;

public static class PosFeatureKeys
{
    public const string DineIn = "pos.dine_in";
    public const string Collection = "pos.collection";
    public const string Delivery = "pos.delivery";
    public const string Reservations = "pos.reservations";
    public const string GiftCards = "pos.gift_cards";
    public const string CustomerPoints = "pos.customer_points";
    public const string SplitBill = "pos.split_bill";
    public const string TableTransfer = "pos.table_transfer";
    public const string KitchenStatus = "pos.kitchen_status";
    public const string Refunds = "pos.refunds";
}

public sealed record PosFeature(string Key, bool IsEnabled, string? Version = null);
