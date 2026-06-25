namespace POS_in_NET.Models;

/// <summary>
/// Settings for OrderWeb UK address lookup (shared platform owp_ key).
/// </summary>
public class PostcodeLookupSettings
{
    public int Id { get; set; }

    public string Provider { get; set; } = "OrderWeb";

    public string OrderWebAddressApiKey { get; set; } = string.Empty;

    public string OrderWebBaseUrl { get; set; } = "https://orderweb.net";

    public bool OrderWebAddressEnabled { get; set; } = true;

    public int TotalLookups { get; set; }

    public DateTime? LastUsed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
