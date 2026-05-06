namespace POS_in_NET.Models;

/// <summary>
/// Represents a UK address returned from lookup service
/// </summary>
public class AddressResult
{
    public string FullAddress { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string County { get; set; } = string.Empty;
    public string Postcode { get; set; } = string.Empty;
    public string Country { get; set; } = "United Kingdom";
    
    // Optional coordinates for mapping/routing
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    
    /// <summary>
    /// Format address for display in dropdown
    /// </summary>
    public string DisplayText => string.IsNullOrEmpty(AddressLine2) 
        ? $"{AddressLine1}, {City}, {Postcode}"
        : $"{AddressLine1}, {AddressLine2}, {City}, {Postcode}";
}
