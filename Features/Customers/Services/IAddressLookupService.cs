using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Interface for address lookup services (Mapbox, Custom PAF, etc.)
/// Allows easy provider switching without changing POS app code
/// </summary>
public interface IAddressLookupService
{
    /// <summary>
    /// Look up addresses for a UK postcode
    /// </summary>
    /// <param name="postcode">UK postcode (e.g., "SW1A 1AA")</param>
    /// <returns>List of addresses matching the postcode</returns>
    Task<List<AddressResult>> LookupPostcodeAsync(string postcode);

    /// <summary>
    /// Test connection to the address lookup provider
    /// </summary>
    /// <returns>True if connection successful, false otherwise</returns>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// Get provider name (e.g., "Mapbox", "Custom PAF")
    /// </summary>
    string ProviderName { get; }
}
