namespace OrderWeb.Client.Models;

public sealed record CachedCustomer(
    int Id,
    string MotherId,
    string Name,
    string Phone,
    string? Email,
    string Address,
    string? Postcode,
    int LoyaltyPoints);

public sealed record CustomerSearchRequest(string OrderType, string? Name, string? Phone, string? AddressOrPostcode);

public sealed record DeliveryZoneQuote(string? ZoneName, string Postcode, decimal DeliveryFee, bool IsKnownZone);

public sealed record AddressSuggestion(
    string DisplayText,
    string AddressLine1,
    string AddressLine2,
    string AddressLine3,
    string City,
    string County,
    string Postcode,
    string Country);

public sealed record CustomerOrderDraft(
    string OrderType,
    CachedCustomer Customer,
    string? PickupTime,
    string? ScheduledTime,
    string? Notes,
    string? Address,
    string? Postcode,
    string? DeliveryZone,
    decimal DeliveryFee);
