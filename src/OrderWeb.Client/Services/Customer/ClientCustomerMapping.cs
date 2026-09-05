using OrderWeb.Client.Models;
using OrderWeb.Contracts.Customers;

namespace OrderWeb.Client.Services.Customer;

internal static class ClientCustomerMapping
{
    public static CustomerSummaryDto ToSummary(CachedCustomer customer, CustomerOrderKind orderKind = CustomerOrderKind.Both) =>
        new(
            Id: customer.Id.ToString(),
            MotherId: string.IsNullOrWhiteSpace(customer.MotherId) ? null : customer.MotherId,
            Name: customer.Name,
            Phone: customer.Phone,
            Email: customer.Email,
            Address: customer.Address,
            City: null,
            County: null,
            Postcode: customer.Postcode,
            LoyaltyPoints: customer.LoyaltyPoints,
            OrderKind: orderKind,
            DisplayLine: null);

    public static CachedCustomer ToCached(CustomerSummaryDto customer) =>
        new(
            int.TryParse(customer.Id, out var id) ? id : Math.Abs(Guid.NewGuid().GetHashCode()),
            customer.MotherId ?? customer.Id,
            customer.Name ?? string.Empty,
            customer.Phone ?? string.Empty,
            customer.Email,
            customer.Address ?? string.Empty,
            customer.Postcode,
            customer.LoyaltyPoints ?? 0);

    public static CustomerOrderKind ParseOrderKind(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "delivery" or "del" => CustomerOrderKind.Delivery,
            "collection" or "col" => CustomerOrderKind.Collection,
            _ => CustomerOrderKind.Both
        };

    public static CustomerSearchRequest ToLegacyRequest(CustomerSearchRequestDto request, CustomerOrderKind? fallbackKind = null) =>
        new(
            OrderType: request.OrderKind?.ToString() ?? fallbackKind?.ToString() ?? "Both",
            Name: request.Name,
            Phone: request.Phone,
            AddressOrPostcode: request.AddressOrPostcode);
}
