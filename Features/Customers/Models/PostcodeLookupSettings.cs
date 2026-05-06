namespace POS_in_NET.Models;

/// <summary>
/// Settings for postcode lookup service
/// Stored in cloud database for centralized configuration
/// </summary>
public class PostcodeLookupSettings
{
    public int Id { get; set; }
    
    /// <summary>
    /// Active provider: "Mapbox" or "Custom"
    /// </summary>
    public string Provider { get; set; } = "Mapbox";
    
    // Mapbox Settings
    public string MapboxApiToken { get; set; } = string.Empty;
    public bool MapboxEnabled { get; set; } = true;
    
    // Custom PAF Settings (for future use)
    public string CustomApiUrl { get; set; } = string.Empty;
    public string CustomAuthToken { get; set; } = string.Empty;
    public bool CustomEnabled { get; set; } = false;
    
    // Statistics
    public int TotalLookups { get; set; } = 0;
    public DateTime? LastUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
