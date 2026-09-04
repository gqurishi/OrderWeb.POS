namespace POS_in_NET.Models;

/// <summary>
/// Represents a UK address returned from OrderWeb address lookup.
/// </summary>
public class AddressResult
{
    public string FormattedAddress { get; set; } = string.Empty;

    public string AddressLine1 { get; set; } = string.Empty;

    public string AddressLine2 { get; set; } = string.Empty;

    public string AddressLine3 { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string County { get; set; } = string.Empty;

    public string Postcode { get; set; } = string.Empty;

    public string Country { get; set; } = "GB";

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Uprn { get; set; }

    public long? Udprn { get; set; }

    public string DisplayText => string.IsNullOrWhiteSpace(FormattedAddress)
        ? BuildDisplayText()
        : FormattedAddress;

    private string BuildDisplayText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(AddressLine1)) parts.Add(AddressLine1);
        if (!string.IsNullOrWhiteSpace(AddressLine2)) parts.Add(AddressLine2);
        if (!string.IsNullOrWhiteSpace(AddressLine3)) parts.Add(AddressLine3);
        if (!string.IsNullOrWhiteSpace(City)) parts.Add(City);
        if (!string.IsNullOrWhiteSpace(Postcode)) parts.Add(Postcode);
        return string.Join(", ", parts);
    }
}
