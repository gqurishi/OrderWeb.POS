using OrderWeb.Client.Models;
using OrderWeb.Client.Services.Customer;
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client.Services.Customer;

public sealed class ClientCustomerDirectoryService : ICustomerDirectoryService
{
    private readonly ClientCacheService _cache;
    private readonly ClientCustomerFieldPolicyService _policyService;
    private readonly ClientMotherSyncStatusService _syncStatus;
    private readonly MotherCustomerClient _motherCustomers;

    public ClientCustomerDirectoryService(
        ClientCacheService cache,
        ClientCustomerFieldPolicyService policyService,
        ClientMotherSyncStatusService syncStatus,
        MotherCustomerClient motherCustomers)
    {
        _cache = cache;
        _policyService = policyService;
        _syncStatus = syncStatus;
        _motherCustomers = motherCustomers;
    }

    public CustomerFieldAccessPolicy GetFieldAccessPolicy() => _policyService.GetPolicy();

    public async Task<OperationResult<CustomerSearchResultDto>> SearchCustomersAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var policy = await EnsurePolicyAsync(cancellationToken);
        if (!policy.AllowCustomerDirectory)
        {
            var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);
            return OperationResult<CustomerSearchResultDto>.Ok(new CustomerSearchResultDto(
                Array.Empty<CustomerSummaryDto>(),
                policy,
                sync,
                "Customer directory is disabled for this terminal.",
                "permission"));
        }

        var syncStatus = await _syncStatus.GetSyncStatusAsync(cancellationToken);
        var customers = new List<CustomerSummaryDto>();
        var legacyRequest = ClientCustomerMapping.ToLegacyRequest(request);

        if (syncStatus.IsOnline)
        {
            var remote = await _motherCustomers.SearchCustomersAsync(legacyRequest, policy, cancellationToken);
            customers.AddRange(remote);
        }

        var cached = await _cache.SearchCachedCustomersAsync(legacyRequest, policy, cancellationToken);
        customers.AddRange(cached.Select(row =>
            Project(ClientCustomerMapping.ToSummary(row), policy, CustomerFieldAccessScope.Search)));

        var merged = customers
            .GroupBy(customer => string.IsNullOrWhiteSpace(customer.MotherId) ? customer.Id : customer.MotherId!)
            .Select(group => group.First())
            .Take(10)
            .ToList();

        if (syncStatus.IsOnline && merged.Count > 0)
        {
            await _cache.CacheCustomersAsync(
                merged.Select(ClientCustomerMapping.ToCached).ToList(),
                policy,
                cancellationToken);
        }

        var banner = !syncStatus.IsOnline
            ? "Offline — showing cached customers only."
            : syncStatus.IsStale
                ? "Customer search may be incomplete."
                : null;

        return OperationResult<CustomerSearchResultDto>.Ok(new CustomerSearchResultDto(
            merged,
            policy,
            syncStatus,
            banner,
            !syncStatus.IsOnline ? "offline" : "warning"));
    }

    public async Task<OperationResult<CustomerDetailDto>> GetCustomerAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        var policy = await EnsurePolicyAsync(cancellationToken);
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);

        if (!policy.AllowCustomerDirectory)
        {
            return OperationResult<CustomerDetailDto>.Fail(OperationError.PermissionDenied(
                "Customer directory is disabled for this terminal."));
        }

        var cached = await _cache.GetCachedCustomerAsync(customerId, cancellationToken);
        if (cached is null)
        {
            return OperationResult<CustomerDetailDto>.Fail(OperationError.NotFound("Customer not found in local cache."));
        }

        var summary = Project(ClientCustomerMapping.ToSummary(cached), policy, CustomerFieldAccessScope.Detail);
        return OperationResult<CustomerDetailDto>.Ok(new CustomerDetailDto(
            summary,
            Notes: null,
            LastUsedAtUtc: null,
            LastCollectionAtUtc: null,
            LastDeliveryAtUtc: null,
            VisibleFields: policy.DetailFields,
            FieldPolicy: policy,
            SyncStatus: sync));
    }

    public async Task<OperationResult<CustomerSummaryDto>> UpsertCustomerAsync(
        CustomerSummaryDto customer,
        CancellationToken cancellationToken = default)
    {
        var policy = await EnsurePolicyAsync(cancellationToken);
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);

        var draft = new CustomerOrderDraft(
            "Both",
            ClientCustomerMapping.ToCached(customer),
            null,
            null,
            null,
            customer.Address,
            customer.Postcode,
            null,
            0m);

        var saved = sync.IsOnline
            ? await _motherCustomers.SaveCustomerAsync(draft, policy, cancellationToken)
            : ClientCustomerMapping.ToCached(customer);

        await _cache.CacheCustomersAsync(new[] { saved }, policy, cancellationToken);
        var projected = Project(ClientCustomerMapping.ToSummary(saved), policy, CustomerFieldAccessScope.Detail);
        return OperationResult<CustomerSummaryDto>.Ok(projected);
    }

    private async Task<CustomerFieldAccessPolicy> EnsurePolicyAsync(CancellationToken cancellationToken)
    {
        var loaded = await _policyService.LoadPolicyAsync(cancellationToken);
        if (await _syncStatus.IsMotherOnlineAsync(cancellationToken))
        {
            var remote = await _motherCustomers.FetchFieldAccessPolicyAsync(cancellationToken);
            if (remote is not null)
            {
                await _policyService.SavePolicyAsync(remote, cancellationToken);
                return remote;
            }
        }

        return loaded;
    }

    private static CustomerSummaryDto Project(
        CustomerSummaryDto source,
        CustomerFieldAccessPolicy policy,
        CustomerFieldAccessScope scope) =>
        CustomerFieldProjector.Project(source, policy, scope);
}

public sealed class ClientCollectionDetailsService : ICollectionDetailsService
{
    private readonly ClientCustomerDirectoryService _directory;
    private readonly ClientMotherSyncStatusService _syncStatus;
    private CollectionDetailsDto _draft = new(null, null, null, null, null, null, CustomerOrderUiHelpersSync.LiveSync());

    public ClientCollectionDetailsService(
        ClientCustomerDirectoryService directory,
        ClientMotherSyncStatusService syncStatus)
    {
        _directory = directory;
        _syncStatus = syncStatus;
    }

    public async Task<OperationResult<CollectionDetailsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);
        return OperationResult<CollectionDetailsDto>.Ok(_draft with { SyncStatus = sync });
    }

    public async Task<OperationResult<CollectionDetailsDto>> SearchAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var search = await _directory.SearchCustomersAsync(
            request with { OrderKind = CustomerOrderKind.Collection },
            cancellationToken);
        if (!search.IsSuccess || search.Value is null)
        {
            return OperationResult<CollectionDetailsDto>.Fail(search.Error ?? OperationError.Failure("Customer search failed."));
        }

        _draft = _draft with
        {
            SearchResults = search.Value,
            SyncStatus = search.Value.SyncStatus
        };

        return OperationResult<CollectionDetailsDto>.Ok(_draft);
    }
}

public sealed class ClientDeliveryDetailsService : IDeliveryDetailsService
{
    private readonly ClientCustomerDirectoryService _directory;
    private readonly ClientMotherSyncStatusService _syncStatus;
    private readonly MotherCustomerClient _motherCustomers;
    private DeliveryDetailsDto _draft = new(
        null, null, null, null, null, null, null, null, null, null,
        Array.Empty<AddressSuggestionDto>(),
        null,
        CustomerOrderUiHelpersSync.LiveSync());

    public ClientDeliveryDetailsService(
        ClientCustomerDirectoryService directory,
        ClientMotherSyncStatusService syncStatus,
        MotherCustomerClient motherCustomers)
    {
        _directory = directory;
        _syncStatus = syncStatus;
        _motherCustomers = motherCustomers;
    }

    public async Task<OperationResult<DeliveryDetailsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);
        return OperationResult<DeliveryDetailsDto>.Ok(_draft with { SyncStatus = sync });
    }

    public async Task<OperationResult<DeliveryDetailsDto>> SearchCustomersAsync(
        CustomerSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var search = await _directory.SearchCustomersAsync(
            request with { OrderKind = CustomerOrderKind.Delivery },
            cancellationToken);
        if (!search.IsSuccess || search.Value is null)
        {
            return OperationResult<DeliveryDetailsDto>.Fail(search.Error ?? OperationError.Failure("Customer search failed."));
        }

        _draft = _draft with
        {
            SearchResults = search.Value,
            SyncStatus = search.Value.SyncStatus
        };

        return OperationResult<DeliveryDetailsDto>.Ok(_draft);
    }

    public async Task<OperationResult<DeliveryDetailsDto>> LookupAddressAsync(
        string addressOrPostcode,
        CancellationToken cancellationToken = default)
    {
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);
        var suggestions = sync.IsOnline
            ? (await _motherCustomers.LookupAddressesAsync(addressOrPostcode, cancellationToken))
                .Select(address => new AddressSuggestionDto(
                    address.DisplayText,
                    address.AddressLine1,
                    address.AddressLine2,
                    address.AddressLine3,
                    address.City,
                    address.County,
                    address.Postcode,
                    address.Country))
                .ToList()
            : Array.Empty<AddressSuggestionDto>();

        decimal? fee = null;
        string? zoneName = null;
        if (sync.IsOnline && !string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            var quote = await _motherCustomers.QuoteDeliveryZoneAsync(addressOrPostcode, cancellationToken);
            if (quote.IsKnownZone)
            {
                fee = quote.DeliveryFee;
                zoneName = quote.ZoneName;
            }
        }

        _draft = _draft with
        {
            AddressSuggestions = suggestions,
            DeliveryFee = fee,
            ZoneName = zoneName,
            SyncStatus = sync,
            StatusBanner = !sync.IsOnline ? "Offline — address lookup unavailable." : null,
            StatusBannerTone = "offline"
        };

        return OperationResult<DeliveryDetailsDto>.Ok(_draft);
    }
}

internal static class CustomerOrderUiHelpersSync
{
    public static CustomerSyncStatusDto LiveSync() =>
        new(true, false, DateTimeOffset.UtcNow, "Live");
}
