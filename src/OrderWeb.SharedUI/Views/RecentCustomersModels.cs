namespace OrderWeb.SharedUI.Views;

public enum RecentCustomerFilter
{
    All,
    Collection,
    Delivery
}

public sealed record RecentCustomerSyncSummaryPresentation(
    int SyncedCount,
    int PendingCount,
    int FailedCount,
    string LastCloudSyncDisplay,
    string Subtitle);

public sealed record RecentCustomerRowPresentation(
    string Name,
    string ContactDetail,
    bool ShowCollectionBadge,
    bool ShowDeliveryBadge,
    bool ShowSyncedBadge,
    bool ShowPendingBadge,
    bool ShowFailedBadge,
    object? Tag = null);

public sealed class RecentCustomerFilterChangedEventArgs(RecentCustomerFilter filter) : EventArgs
{
    public RecentCustomerFilter Filter { get; } = filter;
}

public sealed class RecentCustomerRowEventArgs(RecentCustomerRowPresentation row) : EventArgs
{
    public RecentCustomerRowPresentation Row { get; } = row;
}

public static class RecentCustomerFilterCodes
{
    public static string ToApiCode(RecentCustomerFilter filter) => filter switch
    {
        RecentCustomerFilter.Collection => "collection",
        RecentCustomerFilter.Delivery => "delivery",
        _ => "all"
    };

    public static RecentCustomerFilter FromApiCode(string? code) =>
        (code ?? "all").Trim().ToLowerInvariant() switch
        {
            "collection" or "col" => RecentCustomerFilter.Collection,
            "delivery" or "del" => RecentCustomerFilter.Delivery,
            _ => RecentCustomerFilter.All
        };
}
