using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace POS_in_NET.Services;

public sealed class MotherCustomerDirectoryService : ICustomerDirectoryService
{
    private readonly CustomerDataService _customerData;

    public MotherCustomerDirectoryService(CustomerDataService customerData)
    {
        _customerData = customerData;
    }

    public CustomerFieldAccessPolicy GetFieldAccessPolicy() => CustomerFieldAccessPolicy.MotherFull;

    public async Task<OperationResult<CustomerSearchResultDto>> SearchCustomersAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sync = await BuildSyncStatusAsync();
            var policy = GetFieldAccessPolicy();
            var records = request.OrderKind switch
            {
                CustomerOrderKind.Collection => await _customerData.SearchForCollectionAsync(request.Name, request.Phone),
                CustomerOrderKind.Delivery => await _customerData.SearchForDeliveryAsync(
                    request.AddressOrPostcode,
                    request.Name,
                    request.Phone),
                _ => await _customerData.SearchAsync(
                    request.Name,
                    request.Phone,
                    request.AddressOrPostcode,
                    includeCollection: true,
                    includeDelivery: true)
            };

            var customers = records
                .Select(record => MotherCustomerMapping.ToSummary(record, policy))
                .ToList();

            return OperationResult<CustomerSearchResultDto>.Ok(
                MotherCustomerMapping.BuildSearchResult(customers, policy, sync));
        }
        catch (Exception ex)
        {
            return OperationResult<CustomerSearchResultDto>.Fail(
                OperationError.Failure("Customer search failed.", ex.Message));
        }
    }

    public async Task<OperationResult<CustomerDetailDto>> GetCustomerAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(customerId, out var id))
        {
            return OperationResult<CustomerDetailDto>.Fail(
                OperationError.NotFound("Customer not found."));
        }

        try
        {
            var record = await _customerData.GetByIdAsync(id);
            if (record == null)
            {
                return OperationResult<CustomerDetailDto>.Fail(
                    OperationError.NotFound("Customer not found."));
            }

            var policy = GetFieldAccessPolicy();
            var sync = await BuildSyncStatusAsync();
            var summary = MotherCustomerMapping.ToSummary(record, policy, CustomerFieldAccessScope.Detail);

            return OperationResult<CustomerDetailDto>.Ok(new CustomerDetailDto(
                Customer: summary,
                Notes: null,
                LastUsedAtUtc: record.LastUsedAt == default
                    ? null
                    : new DateTimeOffset(record.LastUsedAt.ToUniversalTime()),
                LastCollectionAtUtc: record.LastCollectionOrderDate.HasValue
                    ? new DateTimeOffset(record.LastCollectionOrderDate.Value.ToUniversalTime())
                    : null,
                LastDeliveryAtUtc: record.LastDeliveryOrderDate.HasValue
                    ? new DateTimeOffset(record.LastDeliveryOrderDate.Value.ToUniversalTime())
                    : null,
                VisibleFields: policy.DetailFields,
                FieldPolicy: policy,
                SyncStatus: sync));
        }
        catch (Exception ex)
        {
            return OperationResult<CustomerDetailDto>.Fail(
                OperationError.Failure("Could not load customer.", ex.Message));
        }
    }

    public async Task<OperationResult<CustomerSummaryDto>> UpsertCustomerAsync(
        CustomerSummaryDto customer,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(customer.Name) || string.IsNullOrWhiteSpace(customer.Phone))
            {
                return OperationResult<CustomerSummaryDto>.Fail(
                    OperationError.Validation("Customer name and phone are required."));
            }

            switch (customer.OrderKind)
            {
                case CustomerOrderKind.Delivery:
                    await _customerData.UpsertDeliveryCustomerAsync(
                        customer.Name,
                        customer.Phone,
                        customer.Address ?? string.Empty,
                        customer.City,
                        customer.County,
                        customer.Postcode);
                    break;
                default:
                    await _customerData.UpsertCollectionCustomerAsync(customer.Name, customer.Phone);
                    break;
            }

            var policy = GetFieldAccessPolicy();
            return OperationResult<CustomerSummaryDto>.Ok(
                MotherCustomerMapping.ToSummary(new CustomerDataRecord
                {
                    Id = int.TryParse(customer.Id, out var parsedId) ? parsedId : 0,
                    Name = customer.Name,
                    PhoneNumber = customer.Phone,
                    FullAddress = customer.Address ?? string.Empty,
                    City = customer.City ?? string.Empty,
                    County = customer.County ?? string.Empty,
                    Postcode = customer.Postcode ?? string.Empty,
                    OrderTypes = customer.OrderKind switch
                    {
                        CustomerOrderKind.Delivery => "delivery",
                        CustomerOrderKind.Both => "both",
                        _ => "collection"
                    },
                    PointsBalance = customer.LoyaltyPoints ?? 0
                }, policy, CustomerFieldAccessScope.Detail));
        }
        catch (Exception ex)
        {
            return OperationResult<CustomerSummaryDto>.Fail(
                OperationError.Failure("Could not save customer.", ex.Message));
        }
    }

    private async Task<CustomerSyncStatusDto> BuildSyncStatusAsync()
    {
        var summary = await _customerData.GetSyncSummaryAsync();
        return MotherCustomerMapping.ToSyncStatus(summary);
    }
}
