namespace POS_in_NET.Models;

public sealed class DeliveryZone
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DeliveryFee { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<DeliveryZonePostcode> Postcodes { get; set; } = new();

    public string DeliveryFeeDisplay => $"£{DeliveryFee:F2}";
    public string PostcodeSummary => Postcodes.Count == 0
        ? "No postcodes"
        : string.Join(", ", Postcodes.Take(4).Select(p => p.Postcode)) + (Postcodes.Count > 4 ? $" +{Postcodes.Count - 4} more" : string.Empty);
}

public sealed class DeliveryZonePostcode
{
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public string Postcode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class UnassignedDeliveryPostcode
{
    public int Id { get; set; }
    public string Postcode { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public sealed class DeliveryZoneMatch
{
    public int ZoneId { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public string Postcode { get; set; } = string.Empty;
    public decimal DeliveryFee { get; set; }
}
