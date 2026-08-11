namespace POS_in_NET.Models;

public sealed class OrderServiceAvailabilitySettings
{
    public bool TableEnabled { get; set; } = true;
    public bool CollectionEnabled { get; set; } = true;
    public bool DeliveryEnabled { get; set; } = true;
    public int? UpdatedByUserId { get; set; }
    public string? UpdatedByName { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool HasAnyEnabled => TableEnabled || CollectionEnabled || DeliveryEnabled;

    public OrderServiceAvailabilitySettings Copy() => new()
    {
        TableEnabled = TableEnabled,
        CollectionEnabled = CollectionEnabled,
        DeliveryEnabled = DeliveryEnabled,
        UpdatedByUserId = UpdatedByUserId,
        UpdatedByName = UpdatedByName,
        UpdatedAt = UpdatedAt
    };
}

public enum PosOrderService
{
    Table,
    Collection,
    Delivery
}
