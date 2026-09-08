namespace OrderWeb.Contracts.Access;

/// <summary>
/// Frozen Mother-hub rules for Collection, Delivery, and Table multi-terminal edit.
/// Mother is the only authoritative store; Client is a terminal cache.
/// Docs: COLLECTION / DELIVERY / TABLE_MULTI_TERMINAL_RULES.md
/// </summary>
public static class CustomerOrderHubRules
{
    public const string CollectionOrderTypeDisplay = "Collection";
    public const string CollectionOrderTypeStored = "pickup";
    public const string DeliveryOrderTypeDisplay = "Delivery";
    public const string DeliveryOrderTypeStored = "delivery";
    public const string TableOrderTypeDisplay = "Table";
    public const string TableOrderTypeStored = "table";

    /// <summary>MariaDB on Mother is the only real Collection/Delivery/Table order.</summary>
    public const bool MotherIsAuthoritativeStore = true;

    /// <summary>Client SQLite must never be treated as the master order copy.</summary>
    public const bool ClientCacheIsAuthoritative = false;

    /// <summary>Clients sync only through Mother — never Client-to-Client.</summary>
    public const bool ClientPeerSyncAllowed = false;

    /// <summary>
    /// Offline may show a cached open-order list / floor. Opening for edit, save, pay,
    /// void, kitchen, and print confirmation require Mother online.
    /// </summary>
    public const bool OfflineViewCachedListAllowed = true;
    public const bool OfflineEditOrSaveAllowed = false;
    public const bool OfflinePayAllowed = false;
    public const bool OfflineVoidAllowed = false;
    public const bool OfflinePrintAsSuccessAllowed = false;
    public const bool OfflineSendToKitchenAllowed = false;

    /// <summary>All terminals must use Mother's order id for the same order.</summary>
    public const bool RequireMotherOrderId = true;

    /// <summary>Table busy/in-use status is owned by Mother table sessions.</summary>
    public const bool MotherOwnsTableBusyStatus = true;

    public static bool IsCollectionOrderType(string? orderType)
    {
        var type = orderType?.Trim() ?? string.Empty;
        return type.Equals(CollectionOrderTypeDisplay, StringComparison.OrdinalIgnoreCase)
               || type.Equals(CollectionOrderTypeStored, StringComparison.OrdinalIgnoreCase)
               || type.Equals("col", StringComparison.OrdinalIgnoreCase)
               || type.Equals("takeaway", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDeliveryOrderType(string? orderType)
    {
        var type = orderType?.Trim() ?? string.Empty;
        return type.Equals(DeliveryOrderTypeDisplay, StringComparison.OrdinalIgnoreCase)
               || type.Equals(DeliveryOrderTypeStored, StringComparison.OrdinalIgnoreCase)
               || type.Equals("del", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTableOrderType(string? orderType)
    {
        var type = orderType?.Trim() ?? string.Empty;
        return type.Equals(TableOrderTypeDisplay, StringComparison.OrdinalIgnoreCase)
               || type.Equals(TableOrderTypeStored, StringComparison.OrdinalIgnoreCase)
               || type.Equals("tbl", StringComparison.OrdinalIgnoreCase)
               || type.Equals("dine_in", StringComparison.OrdinalIgnoreCase)
               || type.Equals("dine-in", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Collection or Delivery customer orders.</summary>
    public static bool IsCustomerOrderType(string? orderType) =>
        IsCollectionOrderType(orderType) || IsDeliveryOrderType(orderType);

    /// <summary>Any Mother-hub order: Collection, Delivery, or Table.</summary>
    public static bool IsCustomerHubOrderType(string? orderType) =>
        IsCustomerOrderType(orderType) || IsTableOrderType(orderType);

    /// <summary>
    /// Optimistic concurrency token shared by Mother and Client order payloads.
    /// Changes whenever Mother's order UpdatedAt changes.
    /// </summary>
    public static int ComputeOrderVersion(DateTime updatedAt, DateTime createdAt = default)
    {
        var stamp = updatedAt == default ? createdAt : updatedAt;
        if (stamp == default)
        {
            return 1;
        }

        return Math.Max(1, (int)(stamp.Ticks % int.MaxValue));
    }
}

/// <summary>
/// Backward-compatible Collection aliases. Prefer <see cref="CustomerOrderHubRules"/>.
/// </summary>
public static class CollectionOrderHubRules
{
    public const string OrderTypeDisplay = CustomerOrderHubRules.CollectionOrderTypeDisplay;
    public const string OrderTypeStored = CustomerOrderHubRules.CollectionOrderTypeStored;
    public const bool MotherIsAuthoritativeStore = CustomerOrderHubRules.MotherIsAuthoritativeStore;
    public const bool ClientCacheIsAuthoritative = CustomerOrderHubRules.ClientCacheIsAuthoritative;
    public const bool ClientPeerSyncAllowed = CustomerOrderHubRules.ClientPeerSyncAllowed;
    public const bool OfflineViewCachedListAllowed = CustomerOrderHubRules.OfflineViewCachedListAllowed;
    public const bool OfflineEditOrSaveAllowed = CustomerOrderHubRules.OfflineEditOrSaveAllowed;
    public const bool OfflinePayAllowed = CustomerOrderHubRules.OfflinePayAllowed;
    public const bool OfflineVoidAllowed = CustomerOrderHubRules.OfflineVoidAllowed;
    public const bool OfflinePrintAsSuccessAllowed = CustomerOrderHubRules.OfflinePrintAsSuccessAllowed;
    public const bool OfflineSendToKitchenAllowed = CustomerOrderHubRules.OfflineSendToKitchenAllowed;
    public const bool RequireMotherOrderId = CustomerOrderHubRules.RequireMotherOrderId;

    public static int ComputeOrderVersion(DateTime updatedAt, DateTime createdAt = default) =>
        CustomerOrderHubRules.ComputeOrderVersion(updatedAt, createdAt);
}
