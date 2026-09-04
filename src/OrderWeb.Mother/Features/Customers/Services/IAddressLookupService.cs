using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// UK address lookup via OrderWeb platform API.
/// </summary>
public interface IAddressLookupService
{
    Task<List<AddressResult>> LookupPostcodeAsync(string postcode);

    Task<bool> TestConnectionAsync();

    string ProviderName { get; }
}
