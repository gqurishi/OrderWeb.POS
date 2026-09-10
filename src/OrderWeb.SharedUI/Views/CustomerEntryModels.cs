namespace OrderWeb.SharedUI.Views;

/// <summary>Host-neutral result returned when a Collection (or generic) customer entry form is confirmed.</summary>
public sealed record CustomerEntryResult(string Name, string Phone, string? MotherId = null);

/// <summary>
/// Host-neutral search-result row shown in the customer picker. <see cref="Tag"/> is opaque to SharedUI;
/// hosts may leave it null (Collection) or populate it with a <see cref="DeliveryCustomerAddressInfo"/> or
/// <see cref="DeliveryAddressFields"/> so <see cref="DeliveryCustomerEntryView.ApplyCustomer"/> can also fill
/// the address fields.
/// </summary>
public sealed record CustomerEntrySearchItem(string Name, string Phone, string? MotherId = null, object? Tag = null);

/// <summary>Host-neutral result returned when a Delivery customer entry form is confirmed.</summary>
public sealed record DeliveryCustomerEntryResult(
    string Name,
    string Phone,
    string House,
    string Road,
    string City,
    string Postcode,
    string FormattedAddress);

/// <summary>
/// Host-neutral address lookup suggestion row. <see cref="Tag"/> should be a <see cref="DeliveryAddressFields"/>
/// so selecting the row can populate the structured House/Road/City/Postcode fields without SharedUI needing to
/// know the host's raw address lookup model shape.
/// </summary>
public sealed record DeliveryAddressSuggestionItem(string DisplayText, object? Tag = null);

/// <summary>Structured address fields used to populate the Delivery entry form from an address lookup result.</summary>
public sealed record DeliveryAddressFields(string House, string Road, string City, string Postcode);

/// <summary>
/// Raw (host-owned) customer address used to populate the Delivery entry form when an existing customer is
/// selected from search results. <see cref="RawAddress"/> is the multi-line "house road\ncity\npostcode" string
/// already used by Mother/Client customer records.
/// </summary>
public sealed record DeliveryCustomerAddressInfo(string? RawAddress, string? Postcode);
