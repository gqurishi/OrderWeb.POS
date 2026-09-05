namespace OrderWeb.Contracts.Customers;

/// <summary>
/// Mother-controlled customer field access for Client terminals.
/// Search responses and Client SQLite may only retain approved fields.
/// </summary>
public enum CustomerFieldKind
{
    Name,
    Phone,
    Email,
    Address,
    City,
    County,
    Postcode,
    LoyaltyPoints,
    Notes
}

public enum CustomerOrderKind
{
    Collection,
    Delivery,
    Both
}

public sealed record CustomerFieldAccessPolicy(
    IReadOnlyList<CustomerFieldKind> SearchFields,
    IReadOnlyList<CustomerFieldKind> CacheFields,
    IReadOnlyList<CustomerFieldKind> DetailFields,
    bool AllowOrderHistory,
    bool AllowCustomerDirectory,
    bool AllowAssignCustomer)
{
    /// <summary>Full Mother POS access.</summary>
    public static CustomerFieldAccessPolicy MotherFull { get; } = new(
        SearchFields: Enum.GetValues<CustomerFieldKind>(),
        CacheFields: Enum.GetValues<CustomerFieldKind>(),
        DetailFields: Enum.GetValues<CustomerFieldKind>(),
        AllowOrderHistory: true,
        AllowCustomerDirectory: true,
        AllowAssignCustomer: true);

    /// <summary>
    /// Default Client policy: identity + phone for search/cache;
    /// address fields only when Mother expands the policy for delivery.
    /// </summary>
    public static CustomerFieldAccessPolicy ClientDefault { get; } = new(
        SearchFields: new[] { CustomerFieldKind.Name, CustomerFieldKind.Phone },
        CacheFields: new[] { CustomerFieldKind.Name, CustomerFieldKind.Phone },
        DetailFields: new[] { CustomerFieldKind.Name, CustomerFieldKind.Phone },
        AllowOrderHistory: false,
        AllowCustomerDirectory: true,
        AllowAssignCustomer: true);

    public static CustomerFieldAccessPolicy ClientWithDeliveryAddress { get; } = new(
        SearchFields: new[]
        {
            CustomerFieldKind.Name,
            CustomerFieldKind.Phone,
            CustomerFieldKind.Address,
            CustomerFieldKind.Postcode
        },
        CacheFields: new[]
        {
            CustomerFieldKind.Name,
            CustomerFieldKind.Phone,
            CustomerFieldKind.Address,
            CustomerFieldKind.Postcode
        },
        DetailFields: new[]
        {
            CustomerFieldKind.Name,
            CustomerFieldKind.Phone,
            CustomerFieldKind.Address,
            CustomerFieldKind.City,
            CustomerFieldKind.Postcode
        },
        AllowOrderHistory: false,
        AllowCustomerDirectory: true,
        AllowAssignCustomer: true);

    public bool Allows(CustomerFieldKind field, CustomerFieldAccessScope scope) =>
        scope switch
        {
            CustomerFieldAccessScope.Search => SearchFields.Contains(field),
            CustomerFieldAccessScope.Cache => CacheFields.Contains(field),
            _ => DetailFields.Contains(field)
        };
}

public enum CustomerFieldAccessScope
{
    Search,
    Cache,
    Detail
}

public sealed record CustomerSyncStatusDto(
    bool IsOnline,
    bool IsStale,
    DateTimeOffset? LastSyncedAtUtc,
    string DisplayText);

public sealed record CustomerSummaryDto(
    string Id,
    string? MotherId,
    string? Name,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    string? County,
    string? Postcode,
    int? LoyaltyPoints,
    CustomerOrderKind OrderKind,
    string? DisplayLine);

public sealed record CustomerDetailDto(
    CustomerSummaryDto Customer,
    string? Notes,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? LastCollectionAtUtc,
    DateTimeOffset? LastDeliveryAtUtc,
    IReadOnlyList<CustomerFieldKind> VisibleFields,
    CustomerFieldAccessPolicy FieldPolicy,
    CustomerSyncStatusDto SyncStatus,
    string? StatusBanner = null,
    string StatusBannerTone = "warning");

public sealed record CustomerSearchRequestDto(
    string? Name,
    string? Phone,
    string? AddressOrPostcode,
    CustomerOrderKind? OrderKind);

public sealed record CustomerSearchResultDto(
    IReadOnlyList<CustomerSummaryDto> Customers,
    CustomerFieldAccessPolicy FieldPolicy,
    CustomerSyncStatusDto SyncStatus,
    string? StatusBanner = null,
    string StatusBannerTone = "warning",
    bool IsLoading = false,
    string? LoadingMessage = null);

public sealed record CollectionDetailsDto(
    string? CustomerId,
    string? Name,
    string? Phone,
    string? PickupTime,
    string? Notes,
    CustomerSearchResultDto? SearchResults,
    CustomerSyncStatusDto SyncStatus,
    string? StatusBanner = null,
    string StatusBannerTone = "warning",
    bool IsLoading = false,
    string? LoadingMessage = null);

public sealed record DeliveryDetailsDto(
    string? CustomerId,
    string? Name,
    string? Phone,
    string? AddressLine,
    string? FlatOrHouse,
    string? Road,
    string? City,
    string? Postcode,
    decimal? DeliveryFee,
    string? ZoneName,
    IReadOnlyList<AddressSuggestionDto> AddressSuggestions,
    CustomerSearchResultDto? SearchResults,
    CustomerSyncStatusDto SyncStatus,
    string? StatusBanner = null,
    string StatusBannerTone = "warning",
    bool IsLoading = false,
    string? LoadingMessage = null);

public sealed record AddressSuggestionDto(
    string DisplayText,
    string AddressLine1,
    string AddressLine2,
    string AddressLine3,
    string City,
    string County,
    string Postcode,
    string Country);

public static class CustomerFieldProjector
{
    public static CustomerSummaryDto Project(
        CustomerSummaryDto source,
        CustomerFieldAccessPolicy policy,
        CustomerFieldAccessScope scope)
    {
        string? Keep(CustomerFieldKind kind, string? value) =>
            policy.Allows(kind, scope) ? value : null;

        int? KeepPoints(int? value) =>
            policy.Allows(CustomerFieldKind.LoyaltyPoints, scope) ? value : null;

        var name = Keep(CustomerFieldKind.Name, source.Name);
        var phone = Keep(CustomerFieldKind.Phone, source.Phone);
        var email = Keep(CustomerFieldKind.Email, source.Email);
        var address = Keep(CustomerFieldKind.Address, source.Address);
        var city = Keep(CustomerFieldKind.City, source.City);
        var county = Keep(CustomerFieldKind.County, source.County);
        var postcode = Keep(CustomerFieldKind.Postcode, source.Postcode);
        var points = KeepPoints(source.LoyaltyPoints);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name.Trim());
        if (!string.IsNullOrWhiteSpace(phone)) parts.Add(phone.Trim());
        if (!string.IsNullOrWhiteSpace(address)) parts.Add(address.Trim());
        if (!string.IsNullOrWhiteSpace(postcode)) parts.Add(postcode.Trim());

        return source with
        {
            Name = name,
            Phone = phone,
            Email = email,
            Address = address,
            City = city,
            County = county,
            Postcode = postcode,
            LoyaltyPoints = points,
            DisplayLine = parts.Count == 0 ? source.Id : string.Join(" · ", parts)
        };
    }
}
