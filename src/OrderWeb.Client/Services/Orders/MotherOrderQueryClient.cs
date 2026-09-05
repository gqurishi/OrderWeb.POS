using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Orders;

namespace OrderWeb.Client.Services.Orders;

public sealed class MotherOrderQueryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherOrderQueryClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public async Task<IReadOnlyList<OrderSearchHitDto>> SearchOrdersAsync(
        OrderSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync(cancellationToken);
        if (auth is null)
        {
            return Array.Empty<OrderSearchHitDto>();
        }

        var endpoint = BuildUrl(auth.Settings.ApiBaseUrl, "/api/client/orders/search", new Dictionary<string, string?>
        {
            ["query"] = request.OrderNumberOrPhone,
            ["date"] = request.OnDate?.ToString("yyyy-MM-dd")
        });

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<OrderSearchHitDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<OrderSearchEnvelope>(json, JsonOptions);
            return (envelope?.Hits ?? envelope?.Payload?.Hits ?? Array.Empty<OrderSearchHitState>())
                .Select(ToHit)
                .ToList();
        }
        catch
        {
            return Array.Empty<OrderSearchHitDto>();
        }
    }

    public async Task<IReadOnlyList<OrderHistoryItemDto>> GetHistoryAsync(
        DateOnly date,
        OpenOrderChannelKind channel,
        string? searchQuery,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync(cancellationToken);
        if (auth is null)
        {
            return Array.Empty<OrderHistoryItemDto>();
        }

        var endpoint = BuildUrl(auth.Settings.ApiBaseUrl, "/api/client/orders/history", new Dictionary<string, string?>
        {
            ["date"] = date.ToString("yyyy-MM-dd"),
            ["channel"] = channel.ToString(),
            ["query"] = searchQuery,
            ["page"] = pageIndex.ToString()
        });

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<OrderHistoryItemDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<OrderHistoryEnvelope>(json, JsonOptions);
            return (envelope?.Items ?? envelope?.Payload?.Items ?? Array.Empty<OrderHistoryItemState>())
                .Select(ToHistoryItem)
                .ToList();
        }
        catch
        {
            return Array.Empty<OrderHistoryItemDto>();
        }
    }

    public async Task<IReadOnlyList<CustomerPreviousOrderDto>> GetPreviousOrdersAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync(cancellationToken);
        if (auth is null)
        {
            return Array.Empty<CustomerPreviousOrderDto>();
        }

        var endpoint = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/customers/{Uri.EscapeDataString(customerId)}/previous-orders";

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<CustomerPreviousOrderDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<PreviousOrdersEnvelope>(json, JsonOptions);
            return (envelope?.Orders ?? envelope?.Payload?.Orders ?? Array.Empty<PreviousOrderState>())
                .Select(ToPreviousOrder)
                .ToList();
        }
        catch
        {
            return Array.Empty<CustomerPreviousOrderDto>();
        }
    }

    private async Task<MotherClientAuth?> GetAuthAsync(CancellationToken cancellationToken)
    {
        await _cache.InitializeAsync();
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken))
        {
            return null;
        }

        return new MotherClientAuth(settings, await _cache.GetCurrentLoginSessionAsync());
    }

    private static HttpClient CreateClient(MotherClientAuth auth)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", auth.Settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", auth.Settings.TerminalToken);
        if (!string.IsNullOrWhiteSpace(auth.Session?.SessionToken))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", auth.Session.SessionToken);
        }

        return client;
    }

    private static string BuildUrl(string apiBaseUrl, string path, IReadOnlyDictionary<string, string?> query)
    {
        var parts = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        var queryString = string.Join("&", parts);
        return $"{apiBaseUrl.TrimEnd('/')}{path}{(queryString.Length == 0 ? string.Empty : $"?{queryString}")}";
    }

    private static OrderSearchHitDto ToHit(OrderSearchHitState hit) =>
        new(
            hit.OrderId ?? string.Empty,
            hit.OrderNumber,
            hit.ChannelLabel ?? "Order",
            hit.CustomerDisplay,
            hit.PhoneDisplay,
            hit.TotalAmount,
            hit.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            hit.StatusDisplay ?? "Unknown",
            hit.PaymentDisplay ?? string.Empty);

    private static OrderHistoryItemDto ToHistoryItem(OrderHistoryItemState item) =>
        new(
            item.OrderId ?? string.Empty,
            item.OrderNumber,
            item.ChannelLabel ?? "Order",
            item.CustomerDisplay,
            item.TotalAmount,
            item.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            item.StatusDisplay ?? "Unknown",
            item.PaymentDisplay ?? string.Empty,
            item.IsVoided,
            item.IsWebOrder);

    private static CustomerPreviousOrderDto ToPreviousOrder(PreviousOrderState order) =>
        new(
            order.OrderId ?? string.Empty,
            order.OrderReferenceDisplay ?? order.OrderId ?? "Order",
            order.DateDisplay ?? string.Empty,
            order.ChannelLabel ?? "Order",
            order.TotalDisplay ?? string.Empty,
            order.StatusDisplay ?? string.Empty,
            order.ItemsText,
            order.NotesDisplay);

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession? Session);

    private sealed record OrderSearchEnvelope(
        bool Success,
        IReadOnlyList<OrderSearchHitState>? Hits,
        OrderSearchPayload? Payload);

    private sealed record OrderSearchPayload(IReadOnlyList<OrderSearchHitState>? Hits);

    private sealed record OrderSearchHitState(
        string? OrderId,
        string? OrderNumber,
        string? ChannelLabel,
        string? CustomerDisplay,
        string? PhoneDisplay,
        decimal TotalAmount,
        DateTimeOffset? CreatedAtUtc,
        string? StatusDisplay,
        string? PaymentDisplay);

    private sealed record OrderHistoryEnvelope(
        bool Success,
        IReadOnlyList<OrderHistoryItemState>? Items,
        OrderHistoryPayload? Payload);

    private sealed record OrderHistoryPayload(IReadOnlyList<OrderHistoryItemState>? Items);

    private sealed record OrderHistoryItemState(
        string? OrderId,
        string? OrderNumber,
        string? ChannelLabel,
        string? CustomerDisplay,
        decimal TotalAmount,
        DateTimeOffset? CreatedAtUtc,
        string? StatusDisplay,
        string? PaymentDisplay,
        bool IsVoided,
        bool IsWebOrder);

    private sealed record PreviousOrdersEnvelope(
        bool Success,
        IReadOnlyList<PreviousOrderState>? Orders,
        PreviousOrdersPayload? Payload);

    private sealed record PreviousOrdersPayload(IReadOnlyList<PreviousOrderState>? Orders);

    private sealed record PreviousOrderState(
        string? OrderId,
        string? OrderReferenceDisplay,
        string? DateDisplay,
        string? ChannelLabel,
        string? TotalDisplay,
        string? StatusDisplay,
        string? ItemsText,
        string? NotesDisplay);
}
