using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace POS_in_NET.Services;

public sealed class MotherCollectionDetailsService : ICollectionDetailsService
{
    private readonly ICustomerDirectoryService _customers;

    public MotherCollectionDetailsService(ICustomerDirectoryService customers)
    {
        _customers = customers;
    }

    public Task<OperationResult<CollectionDetailsDto>> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<CollectionDetailsDto>.Ok(new CollectionDetailsDto(
            CustomerId: null,
            Name: null,
            Phone: null,
            PickupTime: null,
            Notes: null,
            SearchResults: null,
            SyncStatus: new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live"))));

    public async Task<OperationResult<CollectionDetailsDto>> SearchAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var scoped = request with { OrderKind = CustomerOrderKind.Collection };
        var search = await _customers.SearchCustomersAsync(scoped, cancellationToken);
        if (!search.IsSuccess || search.Value == null)
        {
            return OperationResult<CollectionDetailsDto>.Fail(
                search.Error ?? OperationError.Failure("Customer search failed."));
        }

        return OperationResult<CollectionDetailsDto>.Ok(new CollectionDetailsDto(
            CustomerId: null,
            Name: request.Name,
            Phone: request.Phone,
            PickupTime: null,
            Notes: null,
            SearchResults: search.Value,
            SyncStatus: search.Value.SyncStatus,
            IsLoading: false));
    }
}
