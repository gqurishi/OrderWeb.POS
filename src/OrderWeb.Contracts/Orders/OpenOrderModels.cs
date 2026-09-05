namespace OrderWeb.Contracts.Orders;

using OrderWeb.Contracts.Customers;

public enum OpenOrderChannelKind
{
    All,
    Collection,
    Delivery,
    Table
}

public enum OpenOrderHealthKind
{
    Healthy,
    StaleDraft,
    NeedsAttention
}

public sealed record OpenOrderCardDto(
    string OrderId,
    string? OrderNumber,
    OpenOrderChannelKind Channel,
    string ChannelLabel,
    string? CustomerName,
    string? TableLabel,
    decimal TotalAmount,
    DateTimeOffset CreatedAtUtc,
    OpenOrderHealthKind Health,
    string AccentColor,
    IReadOnlyList<string> Badges,
    string TimeDisplay);

public sealed record OpenOrderListDto(
    OpenOrderChannelKind SelectedChannel,
    IReadOnlyList<OpenOrderCardDto> Orders,
    CustomerSyncStatusDto SyncStatus,
    string? StatusBanner = null,
    string StatusBannerTone = "warning",
    bool IsLoading = false,
    string? LoadingMessage = null);

public sealed record OrderSearchRequestDto(
    string? OrderNumberOrPhone,
    DateOnly? OnDate);

public sealed record OrderSearchHitDto(
    string OrderId,
    string? OrderNumber,
    string ChannelLabel,
    string? CustomerDisplay,
    string? PhoneDisplay,
    decimal TotalAmount,
    DateTimeOffset CreatedAtUtc,
    string StatusDisplay,
    string PaymentDisplay);

public sealed record OrderSearchResultDto(
    string? Query,
    IReadOnlyList<OrderSearchHitDto> Hits,
    CustomerSyncStatusDto SyncStatus,
    string? StatusBanner = null,
    string StatusBannerTone = "warning",
    bool IsLoading = false,
    string? LoadingMessage = null);

public sealed record OrderHistoryItemDto(
    string OrderId,
    string? OrderNumber,
    string ChannelLabel,
    string? CustomerDisplay,
    decimal TotalAmount,
    DateTimeOffset CreatedAtUtc,
    string StatusDisplay,
    string PaymentDisplay,
    bool IsVoided,
    bool IsWebOrder);

public sealed record OrderHistoryPageDto(
    DateOnly SelectedDate,
    OpenOrderChannelKind SelectedChannel,
    string? SearchQuery,
    IReadOnlyList<OrderHistoryItemDto> Items,
    IReadOnlyList<OrderHistoryItemDto> VoidedItems,
    int PageIndex,
    int PageCount,
    bool CanAccessHistory,
    CustomerSyncStatusDto SyncStatus,
    string? StatusBanner = null,
    string StatusBannerTone = "warning",
    bool IsLoading = false,
    string? LoadingMessage = null);

public sealed record CustomerPreviousOrderDto(
    string OrderId,
    string OrderReferenceDisplay,
    string DateDisplay,
    string ChannelLabel,
    string TotalDisplay,
    string StatusDisplay,
    string? ItemsText,
    string? NotesDisplay);

public sealed record CustomerPreviousOrdersDto(
    string? CustomerId,
    string? CustomerDisplay,
    IReadOnlyList<CustomerPreviousOrderDto> Orders,
    bool CanAccessHistory,
    CustomerSyncStatusDto SyncStatus,
    string? StatusBanner = null,
    string StatusBannerTone = "warning");
