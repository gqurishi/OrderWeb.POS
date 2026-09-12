namespace OrderWeb.Contracts.Dtos;

/// <summary>Client ↔ Mother Recent Customers (7-day cache) contracts.</summary>
public static class RecentCustomersErrorCodes
{
    public const string OfflineMother = "recent_customers.offline_mother";
    public const string AccessDenied = "recent_customers.access_denied";
    public const string Validation = "recent_customers.validation";
    public const string NotFound = "recent_customers.not_found";
    public const string Unknown = "recent_customers.unknown";
}

public sealed record ClientRecentCustomerItemDto(
    int Id = 0,
    string? Name = null,
    string? PhoneNumber = null,
    string? ContactDetail = null,
    string? OrderTypes = null,
    string? SyncStatus = null,
    bool ShowCollectionBadge = false,
    bool ShowDeliveryBadge = false,
    bool ShowSyncedBadge = false,
    bool ShowPendingBadge = false,
    bool ShowFailedBadge = false);

public sealed record ClientRecentCustomersSummaryDto(
    int TotalRecent = 0,
    int SyncedCount = 0,
    int PendingCount = 0,
    int FailedCount = 0,
    int QueueCount = 0,
    string? LastCloudSyncDisplay = null);

public sealed record ClientRecentCustomersResponseDto(
    bool Success,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    IReadOnlyList<ClientRecentCustomerItemDto>? Customers = null,
    ClientRecentCustomersSummaryDto? Summary = null);

public sealed record ClientRecentCustomerMutateResponseDto(
    bool Success,
    string? Message = null,
    string? Error = null,
    string? ErrorCode = null,
    int Synced = 0,
    int Failed = 0);

public sealed record ClientRecentCustomerMutateRequestDto(string? SessionToken = null);

public sealed record ClientRecentCustomerDeleteCacheRequestDto(int Id = 0, string? SessionToken = null);
