namespace POS_in_NET.Models;

public class BusinessInfo
{
    public int Id { get; set; }
    public string RestaurantName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string County { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Postcode { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string VATNumber { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? LogoPath { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string UpdatedBy { get; set; } = string.Empty;
    
    // Label Printer Settings
    public string? LabelPrinterIp { get; set; }
    public int LabelPrinterPort { get; set; } = 9100;
    public bool LabelPrinterEnabled { get; set; }
}