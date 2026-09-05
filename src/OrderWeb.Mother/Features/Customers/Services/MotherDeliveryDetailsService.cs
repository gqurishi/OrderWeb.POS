using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class MotherDeliveryDetailsService : IDeliveryDetailsService
{
    private readonly ICustomerDirectoryService _customers;
    private readonly PostcodeLookupService _postcodeLookup;
    private readonly DeliveryZoneService _deliveryZones;

    public MotherDeliveryDetailsService(
        ICustomerDirectoryService customers,
        PostcodeLookupService postcodeLookup,
        DeliveryZoneService deliveryZones)
    {
        _customers = customers;
        _postcodeLookup = postcodeLookup;
        _deliveryZones = deliveryZones;
    }

    public Task<OperationResult<DeliveryDetailsDto>> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<DeliveryDetailsDto>.Ok(EmptyState()));

    public async Task<OperationResult<DeliveryDetailsDto>> SearchCustomersAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var scoped = request with { OrderKind = CustomerOrderKind.Delivery };
        var search = await _customers.SearchCustomersAsync(scoped, cancellationToken);
        if (!search.IsSuccess || search.Value == null)
        {
            return OperationResult<DeliveryDetailsDto>.Fail(
                search.Error ?? OperationError.Failure("Customer search failed."));
        }

        return OperationResult<DeliveryDetailsDto>.Ok(new DeliveryDetailsDto(
            CustomerId: null,
            Name: request.Name,
            Phone: request.Phone,
            AddressLine: request.AddressOrPostcode,
            FlatOrHouse: null,
            Road: null,
            City: null,
            Postcode: request.AddressOrPostcode,
            DeliveryFee: null,
            ZoneName: null,
            AddressSuggestions: Array.Empty<AddressSuggestionDto>(),
            SearchResults: search.Value,
            SyncStatus: search.Value.SyncStatus));
    }

    public async Task<OperationResult<DeliveryDetailsDto>> LookupAddressAsync(
        string addressOrPostcode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            return OperationResult<DeliveryDetailsDto>.Fail(
                OperationError.Validation("Enter a postcode or address to look up."));
        }

        try
        {
            var addresses = await _postcodeLookup.LookupPostcodeAsync(addressOrPostcode);
            var suggestions = addresses
                .Select(MapAddressSuggestion)
                .ToList();

            var normalizedPostcode = DeliveryZoneService.NormalizePostcode(addressOrPostcode);
            decimal? fee = null;
            string? zoneName = null;
            if (!string.IsNullOrWhiteSpace(normalizedPostcode))
            {
                var zone = await _deliveryZones.FindZoneForPostcodeAsync(normalizedPostcode);
                if (zone != null)
                {
                    fee = zone.DeliveryFee;
                    zoneName = zone.ZoneName;
                }
            }

            return OperationResult<DeliveryDetailsDto>.Ok(new DeliveryDetailsDto(
                CustomerId: null,
                Name: null,
                Phone: null,
                AddressLine: null,
                FlatOrHouse: null,
                Road: null,
                City: null,
                Postcode: addressOrPostcode.Trim(),
                DeliveryFee: fee,
                ZoneName: zoneName,
                AddressSuggestions: suggestions,
                SearchResults: null,
                SyncStatus: new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live")));
        }
        catch (AddressLookupException ex)
        {
            return OperationResult<DeliveryDetailsDto>.Fail(
                OperationError.Validation(ex.Message));
        }
        catch (Exception ex)
        {
            return OperationResult<DeliveryDetailsDto>.Fail(
                OperationError.Failure("Address lookup failed.", ex.Message));
        }
    }

    private static DeliveryDetailsDto EmptyState() =>
        new(
            CustomerId: null,
            Name: null,
            Phone: null,
            AddressLine: null,
            FlatOrHouse: null,
            Road: null,
            City: null,
            Postcode: null,
            DeliveryFee: null,
            ZoneName: null,
            AddressSuggestions: Array.Empty<AddressSuggestionDto>(),
            SearchResults: null,
            SyncStatus: new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live"));

    private static AddressSuggestionDto MapAddressSuggestion(AddressResult address) =>
        new(
            DisplayText: string.Join(", ", new[]
            {
                address.AddressLine1,
                address.AddressLine2,
                address.City,
                address.Postcode
            }.Where(part => !string.IsNullOrWhiteSpace(part))),
            AddressLine1: address.AddressLine1 ?? string.Empty,
            AddressLine2: address.AddressLine2 ?? string.Empty,
            AddressLine3: address.AddressLine3 ?? string.Empty,
            City: address.City ?? string.Empty,
            County: address.County ?? string.Empty,
            Postcode: address.Postcode ?? string.Empty,
            Country: address.Country ?? "United Kingdom");
}
