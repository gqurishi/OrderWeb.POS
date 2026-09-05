using System.Globalization;
using OrderWeb.Client.Services.Customer;
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client.Services.Orders;

public sealed class ClientOpenOrderListService : IOpenOrderListService
{
    private readonly ClientCacheService _cache;
    private readonly ClientMotherSyncStatusService _syncStatus;

    public ClientOpenOrderListService(ClientCacheService cache, ClientMotherSyncStatusService syncStatus)
    {
        _cache = cache;
        _syncStatus = syncStatus;
    }

    public async Task<OperationResult<OpenOrderListDto>> GetOpenOrdersAsync(
        OpenOrderChannelKind channel = OpenOrderChannelKind.All,
        CancellationToken cancellationToken = default)
    {
        var sync = await _syncStatus.GetSyncStatusAsync(cancellationToken);
        var cards = await BuildCardsAsync(cancellationToken);
        var banner = sync.IsStale || !sync.IsOnline ? sync.DisplayText : null;
        var tone = !sync.IsOnline ? "offline" : sync.IsStale ? "stale" : "warning";

        return OperationResult<OpenOrderListDto>.Ok(new OpenOrderListDto(
            channel,
            cards,
            sync,
            banner,
            tone));
    }

    private async Task<IReadOnlyList<OpenOrderCardDto>> BuildCardsAsync(CancellationToken cancellationToken)
    {
        var openOrders = await _cache.GetOpenOrderSummariesAsync(cancellationToken);
        var onlineOrders = await _cache.GetOnlineOrdersAsync();
        var cards = new List<OpenOrderCardDto>();

        foreach (var order in openOrders.Where(order => !IsClosed(order.Status)))
        {
            cards.Add(MapOpenOrder(order));
        }

        foreach (var order in onlineOrders.Where(order => !string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase)))
        {
            cards.Add(MapOnlineOrder(order));
        }

        return cards
            .OrderByDescending(card => card.Channel == OpenOrderChannelKind.Table)
            .ThenByDescending(card => card.CreatedAtUtc)
            .ToList();
    }

    private static OpenOrderCardDto MapOpenOrder(CachedOpenOrderSummary order)
    {
        var channel = MapChannel(order.OrderType);
        var isStale = string.Equals(order.Status, "draft", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(order.Status, "stale", StringComparison.OrdinalIgnoreCase);
        var accent = channel == OpenOrderChannelKind.Table
            ? CustomerOrderUiHelpersStale.StaleRed
            : isStale
                ? CustomerOrderUiHelpersStale.StaleRed
                : CustomerOrderUiHelpersStale.SuccessGreen;

        return new OpenOrderCardDto(
            order.OrderId,
            FormatOrderNumber(order.OrderNumber, order.OrderId),
            channel,
            ChannelLabel(channel),
            order.CustomerName,
            order.TableLabel,
            order.Total,
            ParseTimestamp(order.OpenedUtc),
            isStale ? OpenOrderHealthKind.StaleDraft : OpenOrderHealthKind.Healthy,
            accent,
            BuildBadges(order.Status, channel),
            FormatTime(order.OpenedUtc));
    }

    private static OpenOrderCardDto MapOnlineOrder(CachedOnlineOrder order)
    {
        var channel = MapChannel(order.OrderType);
        var accent = channel == OpenOrderChannelKind.Delivery ? "#2563EB" : CustomerOrderUiHelpersStale.SuccessGreen;

        return new OpenOrderCardDto(
            order.Id,
            FormatOrderNumber(order.OrderNumber, order.MotherId),
            channel,
            ChannelLabel(channel),
            order.CustomerName,
            null,
            order.Total,
            ParseTimestamp(order.DueTime),
            OpenOrderHealthKind.Healthy,
            accent,
            Array.Empty<string>(),
            FormatTime(order.DueTime));
    }

    private static OpenOrderChannelKind MapChannel(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "delivery" or "del" => OpenOrderChannelKind.Delivery,
            "table" or "tbl" or "dine_in" or "dine-in" => OpenOrderChannelKind.Table,
            _ => OpenOrderChannelKind.Collection
        };

    private static string ChannelLabel(OpenOrderChannelKind channel) =>
        channel switch
        {
            OpenOrderChannelKind.Delivery => "Delivery",
            OpenOrderChannelKind.Table => "Table",
            _ => "Collection"
        };

    private static IReadOnlyList<string> BuildBadges(string status, OpenOrderChannelKind channel)
    {
        var badges = new List<string>();
        if (string.Equals(status, "sent_to_kitchen", StringComparison.OrdinalIgnoreCase))
        {
            badges.Add("Kitchen");
        }

        if (channel == OpenOrderChannelKind.Table)
        {
            badges.Add("Dine-in");
        }

        return badges;
    }

    private static bool IsClosed(string status) =>
        string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase);

    private static string FormatOrderNumber(string? orderNumber, string? fallbackId)
    {
        var value = !string.IsNullOrWhiteSpace(orderNumber) ? orderNumber.Trim() : fallbackId?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Order";
        }

        return value.StartsWith('#') ? value : $"#{value}";
    }

    private static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private static string FormatTime(string? value)
    {
        if (DateTimeOffset.TryParse(value, out var timestamp))
        {
            return timestamp.ToLocalTime().ToString("HH:mm - dd/MM", CultureInfo.InvariantCulture);
        }

        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static class CustomerOrderUiHelpersStale
    {
        public const string SuccessGreen = "#10B981";
        public const string StaleRed = "#DC2626";
    }
}

public sealed record CachedOpenOrderSummary(
    string OrderId,
    string OrderNumber,
    string OrderType,
    string Status,
    decimal Total,
    string OpenedUtc,
    string? CustomerName,
    string? TableLabel);
