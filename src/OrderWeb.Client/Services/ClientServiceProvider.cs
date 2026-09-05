using OrderWeb.Client.Services.Customer;
using OrderWeb.Client.Services.Orders;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client.Services;

public static class ClientServiceProvider
{
    private static ClientCacheService? _cache;
    private static ClientCustomerFieldPolicyService? _policy;
    private static ClientMotherSyncStatusService? _syncStatus;
    private static MotherCustomerClient? _motherCustomers;
    private static MotherOrderQueryClient? _motherOrders;
    private static ClientCustomerDirectoryService? _directory;
    private static ClientCollectionDetailsService? _collectionDetails;
    private static ClientDeliveryDetailsService? _deliveryDetails;
    private static ClientOpenOrderListService? _openOrders;
    private static ClientOrderSearchService? _orderSearch;
    private static ClientOrderHistoryService? _orderHistory;
    private static ClientCustomerPreviousOrdersService? _previousOrders;

    public static ClientCacheService Cache => _cache ??= new ClientCacheService();

    public static ClientCustomerFieldPolicyService FieldPolicy => _policy ??= new ClientCustomerFieldPolicyService(Cache);

    public static ClientMotherSyncStatusService SyncStatus => _syncStatus ??= new ClientMotherSyncStatusService(Cache);

    public static MotherCustomerClient MotherCustomers => _motherCustomers ??= new MotherCustomerClient(Cache);

    public static MotherOrderQueryClient MotherOrders => _motherOrders ??= new MotherOrderQueryClient(Cache);

    public static ICustomerDirectoryService CustomerDirectory =>
        _directory ??= new ClientCustomerDirectoryService(Cache, FieldPolicy, SyncStatus, MotherCustomers);

    public static ICollectionDetailsService CollectionDetails =>
        _collectionDetails ??= new ClientCollectionDetailsService(
            (ClientCustomerDirectoryService)CustomerDirectory,
            SyncStatus);

    public static IDeliveryDetailsService DeliveryDetails =>
        _deliveryDetails ??= new ClientDeliveryDetailsService(
            (ClientCustomerDirectoryService)CustomerDirectory,
            SyncStatus,
            MotherCustomers);

    public static IOpenOrderListService OpenOrders =>
        _openOrders ??= new ClientOpenOrderListService(Cache, SyncStatus);

    public static IOrderSearchService OrderSearch =>
        _orderSearch ??= new ClientOrderSearchService(Cache, SyncStatus, MotherOrders);

    public static IOrderHistoryService OrderHistory =>
        _orderHistory ??= new ClientOrderHistoryService(Cache, FieldPolicy, SyncStatus, MotherOrders);

    public static ICustomerPreviousOrdersService PreviousOrders =>
        _previousOrders ??= new ClientCustomerPreviousOrdersService(FieldPolicy, SyncStatus, MotherOrders);
}
