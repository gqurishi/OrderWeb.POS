using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;

namespace OrderWeb.Contracts.Services;

public interface IOpenOrderListService
{
    Task<OperationResult<OpenOrderListDto>> GetOpenOrdersAsync(
        OpenOrderChannelKind channel = OpenOrderChannelKind.All,
        CancellationToken cancellationToken = default);
}

public interface IOrderSearchService
{
    Task<OperationResult<OrderSearchResultDto>> SearchOrdersAsync(
        OrderSearchRequestDto request,
        CancellationToken cancellationToken = default);
}

public interface IOrderHistoryService
{
    Task<OperationResult<OrderHistoryPageDto>> GetHistoryAsync(
        DateOnly date,
        OpenOrderChannelKind channel = OpenOrderChannelKind.All,
        string? searchQuery = null,
        int pageIndex = 0,
        CancellationToken cancellationToken = default);
}

public interface ICustomerDirectoryService
{
    CustomerFieldAccessPolicy GetFieldAccessPolicy();

    Task<OperationResult<CustomerSearchResultDto>> SearchCustomersAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<CustomerDetailDto>> GetCustomerAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<CustomerSummaryDto>> UpsertCustomerAsync(
        CustomerSummaryDto customer,
        CancellationToken cancellationToken = default);
}

public interface ICollectionDetailsService
{
    Task<OperationResult<CollectionDetailsDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<CollectionDetailsDto>> SearchAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default);
}

public interface IDeliveryDetailsService
{
    Task<OperationResult<DeliveryDetailsDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<DeliveryDetailsDto>> SearchCustomersAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default);

    Task<OperationResult<DeliveryDetailsDto>> LookupAddressAsync(
        string addressOrPostcode,
        CancellationToken cancellationToken = default);
}

public interface ICustomerPreviousOrdersService
{
    Task<OperationResult<CustomerPreviousOrdersDto>> GetPreviousOrdersAsync(
        string customerId,
        CancellationToken cancellationToken = default);
}
