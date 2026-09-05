using OrderWeb.Client.Services.Customer;
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client.Services.Orders;

public sealed class ClientOrderSearchService : IOrderSearchService
{
    private readonly ClientCacheService _cache;
    private readonly ClientMotherSyncStatusService _syncStatus;
    private readonly MotherOrderQueryClient _motherOrders;

    public ClientOrderSearchService(
        ClientCacheService cache,
        ClientMotherSyncStatusService syncStatus,
        MotherOrderQueryClient motherOrders)
    {
        _cache = cache;
        _syncStatus = syncStatus;
        _motherOrders = motherOrders;
    }

    public async Task<OperationResult<OrderSearchResultDto>> SearchOrdersAsync(
        OrderSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);
        var query = request.OrderNumberOrPhone?.Trim();
        var hits = new List<OrderSearchHitDto>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            hits.AddRange(await _cache.SearchCachedOrdersAsync(query, request.OnDate, cancellationToken));

            if (sync.IsOnline)
            {
                var remoteHits = await _motherOrders.SearchOrdersAsync(request, cancellationToken);
                hits = hits
                    .Concat(remoteHits)
                    .GroupBy(hit => hit.OrderId)
                    .Select(group => group.First())
                    .OrderByDescending(hit => hit.CreatedAtUtc)
                    .Take(20)
                    .ToList();
            }
        }

        var banner = !sync.IsOnline
            ? "Offline — search results may be incomplete."
            : sync.IsStale
                ? "Data may be stale — reconnect to Mother POS for full search."
                : null;

        return OperationResult<OrderSearchResultDto>.Ok(new OrderSearchResultDto(
            query,
            hits,
            sync,
            banner,
            !sync.IsOnline ? "offline" : "warning"));
    }
}

public sealed class ClientOrderHistoryService : IOrderHistoryService
{
    private readonly ClientCacheService _cache;
    private readonly ClientCustomerFieldPolicyService _policyService;
    private readonly ClientMotherSyncStatusService _syncStatus;
    private readonly MotherOrderQueryClient _motherOrders;

    public ClientOrderHistoryService(
        ClientCacheService cache,
        ClientCustomerFieldPolicyService policyService,
        ClientMotherSyncStatusService syncStatus,
        MotherOrderQueryClient motherOrders)
    {
        _cache = cache;
        _policyService = policyService;
        _syncStatus = syncStatus;
        _motherOrders = motherOrders;
    }

    public async Task<OperationResult<OrderHistoryPageDto>> GetHistoryAsync(
        DateOnly date,
        OpenOrderChannelKind channel = OpenOrderChannelKind.All,
        string? searchQuery = null,
        int pageIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyService.LoadPolicyAsync(cancellationToken);
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);

        if (!policy.AllowOrderHistory)
        {
            return OperationResult<OrderHistoryPageDto>.Ok(new OrderHistoryPageDto(
                date,
                channel,
                searchQuery,
                Array.Empty<OrderHistoryItemDto>(),
                Array.Empty<OrderHistoryItemDto>(),
                pageIndex,
                1,
                CanAccessHistory: false,
                sync,
                "Order history is not available for this terminal.",
                "permission"));
        }

        var items = await _cache.GetCachedHistoryOrdersAsync(date, channel, searchQuery, cancellationToken);
        if (sync.IsOnline)
        {
            var remoteItems = await _motherOrders.GetHistoryAsync(date, channel, searchQuery, pageIndex, cancellationToken);
            if (remoteItems.Count > 0)
            {
                items = remoteItems;
            }
        }

        var completed = items.Where(item => !item.IsVoided).ToList();
        var voided = items.Where(item => item.IsVoided).ToList();
        var banner = !sync.IsOnline
            ? "Offline — showing cached history only."
            : sync.IsStale
                ? "History may be incomplete until Mother POS syncs."
                : null;

        return OperationResult<OrderHistoryPageDto>.Ok(new OrderHistoryPageDto(
            date,
            channel,
            searchQuery,
            completed,
            voided,
            pageIndex,
            1,
            CanAccessHistory: true,
            sync,
            banner,
            !sync.IsOnline ? "offline" : "warning"));
    }
}

public sealed class ClientCustomerPreviousOrdersService : ICustomerPreviousOrdersService
{
    private readonly ClientCustomerFieldPolicyService _policyService;
    private readonly ClientMotherSyncStatusService _syncStatus;
    private readonly MotherOrderQueryClient _motherOrders;

    public ClientCustomerPreviousOrdersService(
        ClientCustomerFieldPolicyService policyService,
        ClientMotherSyncStatusService syncStatus,
        MotherOrderQueryClient motherOrders)
    {
        _policyService = policyService;
        _syncStatus = syncStatus;
        _motherOrders = motherOrders;
    }

    public async Task<OperationResult<CustomerPreviousOrdersDto>> GetPreviousOrdersAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyService.LoadPolicyAsync(cancellationToken);
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);

        if (!policy.AllowOrderHistory)
        {
            return OperationResult<CustomerPreviousOrdersDto>.Ok(new CustomerPreviousOrdersDto(
                customerId,
                null,
                Array.Empty<CustomerPreviousOrderDto>(),
                CanAccessHistory: false,
                sync,
                "Previous orders are not available for this terminal.",
                "permission"));
        }

        var orders = sync.IsOnline
            ? await _motherOrders.GetPreviousOrdersAsync(customerId, cancellationToken)
            : Array.Empty<CustomerPreviousOrderDto>();

        return OperationResult<CustomerPreviousOrdersDto>.Ok(new CustomerPreviousOrdersDto(
            customerId,
            null,
            orders,
            CanAccessHistory: true,
            sync,
            !sync.IsOnline ? "Offline — previous orders unavailable." : null,
            !sync.IsOnline ? "offline" : "warning"));
    }
}
