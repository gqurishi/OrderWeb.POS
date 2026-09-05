using MySqlConnector;
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class MotherOrderHistoryService : IOrderHistoryService
{
    private const int PageSize = 50;
    private readonly DatabaseService _databaseService;
    private readonly CloudOrderService? _cloudOrderService;
    private readonly CustomerDataService _customerData;

    private IReadOnlyDictionary<string, int> _databaseIdsByOrderId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, Order> _cloudOrdersByOrderId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);

    public bool TryResolveDatabaseId(string orderId, out int databaseId) =>
        _databaseIdsByOrderId.TryGetValue(orderId, out databaseId);

    public bool TryResolveCloudOrder(string orderId, out Order? order) =>
        _cloudOrdersByOrderId.TryGetValue(orderId, out order);

    public MotherOrderHistoryService(
        DatabaseService databaseService,
        CustomerDataService customerData,
        CloudOrderService? cloudOrderService = null)
    {
        _databaseService = databaseService;
        _customerData = customerData;
        _cloudOrderService = cloudOrderService;
    }

    public async Task<OperationResult<OrderHistoryPageDto>> GetHistoryAsync(
        DateOnly date,
        OpenOrderChannelKind channel = OpenOrderChannelKind.All,
        string? searchQuery = null,
        int pageIndex = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var completedRows = new List<OrderHistoryItemDto>(PageSize);
            var voidedRows = new List<OrderHistoryItemDto>(PageSize);
            var databaseIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var cloudOrders = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
            using var connection = await _databaseService.GetConnectionAsync();
            var hasPaymentStatus = await HasColumnAsync(connection, "orders", "payment_status", cancellationToken);
            var paymentStatusProjection = OrderHistoryQueryCompatibility.PaymentStatusProjection(hasPaymentStatus);

            var query = $@"
                SELECT o.id, o.order_id, o.order_number, o.order_type, o.total_amount,
                       o.created_at, o.status, o.customer_name, o.customer_phone,
                       o.payment_method, {paymentStatusProjection} AS payment_status, o.source_channel,
                       CASE WHEN o.local_lifecycle_state = 'voided' OR o.status IN ('void', 'cancelled')
                            THEN 'voided' ELSE 'completed' END AS history_group
                FROM orders o
                WHERE 1 = 1
                      {BuildSourceFilter(channel, searchQuery)}
                      {BuildDateFilter(searchQuery)}
                      {BuildOrderTypeFilter(channel)}
                      {BuildSearchFilter(searchQuery)}
                      {BuildLifecycleFilter(channel)}
                ORDER BY o.created_at DESC, o.id DESC
                LIMIT @pageLimit OFFSET @offset";

            await using var command = new MySqlCommand(query, connection);
            ApplyQueryParameters(command, date, searchQuery);
            var offset = pageIndex * PageSize;
            command.Parameters.AddWithValue("@offset", offset);
            command.Parameters.AddWithValue("@pageLimit", PageSize + 1);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = MapHistoryItem(reader);
                var dbId = reader.GetInt32("id");
                databaseIds[row.OrderId] = dbId;
                (ReadString(reader, "history_group") == "voided" ? voidedRows : completedRows).Add(row);
            }

            await reader.DisposeAsync();

            var cloudMessage = string.Empty;
            if (channel == OpenOrderChannelKind.All
                && !string.IsNullOrWhiteSpace(searchQuery)
                && _cloudOrderService != null)
            {
                var cloudResult = await _cloudOrderService.SearchOrderHistoryAsync(searchQuery, cancellationToken);
                cloudMessage = cloudResult.Message;
                if (cloudResult.Success)
                {
                    MergeCloudResults(completedRows, voidedRows, cloudResult.Orders, cloudOrders);
                }
            }

            _databaseIdsByOrderId = databaseIds;
            _cloudOrdersByOrderId = cloudOrders;

            var hasNextPage = completedRows.Count + voidedRows.Count > PageSize;
            TrimToPageSize(completedRows, voidedRows);

            var sync = await BuildSyncStatusAsync();
            var statusBanner = string.IsNullOrWhiteSpace(cloudMessage) ? null : cloudMessage;
            return OperationResult<OrderHistoryPageDto>.Ok(new OrderHistoryPageDto(
                SelectedDate: date,
                SelectedChannel: channel,
                SearchQuery: searchQuery,
                Items: completedRows,
                VoidedItems: voidedRows,
                PageIndex: pageIndex,
                PageCount: hasNextPage ? pageIndex + 2 : pageIndex + 1,
                CanAccessHistory: true,
                SyncStatus: sync,
                StatusBanner: statusBanner));
        }
        catch (Exception ex)
        {
            return OperationResult<OrderHistoryPageDto>.Fail(
                OperationError.Failure("Order history could not be loaded.", ex.Message));
        }
    }

    private static OrderHistoryItemDto MapHistoryItem(MySqlDataReader reader)
    {
        var orderType = ReadString(reader, "order_type");
        var createdAt = reader.GetDateTime("created_at");
        var status = ReadString(reader, "status");
        var isVoided = ReadString(reader, "history_group") == "voided";
        var isWeb = ReadString(reader, "source_channel").Equals("web", StringComparison.OrdinalIgnoreCase);

        return new OrderHistoryItemDto(
            OrderId: ReadString(reader, "order_id"),
            OrderNumber: FormatOrderNumber(ReadString(reader, "order_number"), ReadString(reader, "order_id")),
            ChannelLabel: FormatOrderType(orderType),
            CustomerDisplay: FormatCustomer(ReadString(reader, "customer_name"), ReadString(reader, "customer_phone")),
            TotalAmount: reader.GetDecimal("total_amount"),
            CreatedAtUtc: new DateTimeOffset(createdAt.ToUniversalTime()),
            StatusDisplay: FormatStatus(status, ReadString(reader, "payment_status")),
            PaymentDisplay: OnlineOrderPaymentHelper.GetDisplayMethod(ReadString(reader, "payment_method")),
            IsVoided: isVoided,
            IsWebOrder: isWeb);
    }

    private static string BuildSourceFilter(OpenOrderChannelKind channel, string? searchQuery) =>
        channel switch
        {
            OpenOrderChannelKind.All when !string.IsNullOrWhiteSpace(searchQuery) => string.Empty,
            _ => "AND LOWER(COALESCE(o.source_channel, 'local')) = 'local'"
        };

    private static string BuildLifecycleFilter(OpenOrderChannelKind channel) =>
        @"AND (o.local_lifecycle_state IN ('paid', 'voided') OR o.status IN ('completed', 'closed', 'paid', 'void', 'cancelled'))
          AND EXISTS (
              SELECT 1
              FROM order_items valid_item
              WHERE valid_item.order_id = o.id
                AND valid_item.quantity > 0
                AND COALESCE(NULLIF(TRIM(valid_item.item_name), ''), '') <> ''
          )";

    private static string BuildOrderTypeFilter(OpenOrderChannelKind channel) =>
        channel switch
        {
            OpenOrderChannelKind.Collection => "AND LOWER(o.order_type) IN ('col', 'collection', 'pickup')",
            OpenOrderChannelKind.Delivery => "AND LOWER(o.order_type) IN ('del', 'delivery')",
            OpenOrderChannelKind.Table => "AND LOWER(o.order_type) IN ('tbl', 'table', 'dine_in', 'dine-in')",
            _ => string.Empty
        };

    private static string BuildDateFilter(string? searchQuery) =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? "AND o.created_at >= @dayStart AND o.created_at < @dayEnd"
            : string.Empty;

    private static string BuildSearchFilter(string? searchQuery) =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? string.Empty
            : "AND (o.order_id = @searchExact OR o.order_number LIKE @searchContains OR o.customer_phone LIKE @searchContains)";

    private static void ApplyQueryParameters(MySqlCommand command, DateOnly date, string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            command.Parameters.AddWithValue("@dayStart", date.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@dayEnd", date.AddDays(1).ToDateTime(TimeOnly.MinValue));
            return;
        }

        var search = searchQuery.Trim().TrimStart('#');
        command.Parameters.AddWithValue("@searchExact", search);
        command.Parameters.AddWithValue("@searchContains", $"%{search}%");
    }

    private static void MergeCloudResults(
        List<OrderHistoryItemDto> completedRows,
        List<OrderHistoryItemDto> voidedRows,
        IReadOnlyList<Order> cloudOrders,
        Dictionary<string, Order> cloudOrderMap)
    {
        var existingKeys = new HashSet<string>(
            completedRows.Concat(voidedRows).Select(row => row.OrderId),
            StringComparer.OrdinalIgnoreCase);

        foreach (var order in cloudOrders)
        {
            var key = string.IsNullOrWhiteSpace(order.CloudOrderId) ? order.OrderId : order.CloudOrderId;
            if (!existingKeys.Add(key))
            {
                continue;
            }

            var row = new OrderHistoryItemDto(
                OrderId: key,
                OrderNumber: FormatOrderNumber(order.OrderNumber, order.OrderId),
                ChannelLabel: FormatOrderType(order.OrderType),
                CustomerDisplay: FormatCustomer(order.CustomerName, order.CustomerPhone),
                TotalAmount: order.TotalAmount,
                CreatedAtUtc: new DateTimeOffset(order.CreatedAt.ToUniversalTime()),
                StatusDisplay: FormatStatus(order.Status.ToString(), order.PaymentStatusRaw),
                PaymentDisplay: OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod),
                IsVoided: order.Status == OrderStatus.Cancelled || order.LocalLifecycleState == LocalLifecycleState.Voided,
                IsWebOrder: true);

            if (row.IsVoided)
            {
                voidedRows.Add(row);
            }
            else
            {
                completedRows.Add(row);
            }

            cloudOrderMap[key] = order;
        }

        completedRows.Sort((left, right) => right.CreatedAtUtc.CompareTo(left.CreatedAtUtc));
        voidedRows.Sort((left, right) => right.CreatedAtUtc.CompareTo(left.CreatedAtUtc));
    }

    private static void TrimToPageSize(
        List<OrderHistoryItemDto> completedRows,
        List<OrderHistoryItemDto> voidedRows)
    {
        while (completedRows.Count + voidedRows.Count > PageSize)
        {
            if (completedRows.Count == 0)
            {
                voidedRows.RemoveAt(voidedRows.Count - 1);
            }
            else if (voidedRows.Count == 0)
            {
                completedRows.RemoveAt(completedRows.Count - 1);
            }
            else if (completedRows[^1].CreatedAtUtc <= voidedRows[^1].CreatedAtUtc)
            {
                completedRows.RemoveAt(completedRows.Count - 1);
            }
            else
            {
                voidedRows.RemoveAt(voidedRows.Count - 1);
            }
        }
    }

    private static string ReadString(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static async Task<bool> HasColumnAsync(
        MySqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = @tableName
              AND column_name = @columnName
            """,
            connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private async Task<CustomerSyncStatusDto> BuildSyncStatusAsync()
    {
        try
        {
            var summary = await _customerData.GetSyncSummaryAsync();
            return MotherCustomerMapping.ToSyncStatus(summary);
        }
        catch
        {
            return new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live");
        }
    }

    private static string FormatOrderNumber(string? orderNumber, string? orderId)
    {
        var readableNumber = orderNumber?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(readableNumber))
        {
            return readableNumber.StartsWith('#') ? readableNumber : $"#{readableNumber}";
        }

        var legacyId = orderId?.Trim() ?? string.Empty;
        if (legacyId.Length > 10)
        {
            legacyId = legacyId[..10].ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(legacyId) ? "#—" : $"#{legacyId}";
    }

    private static string FormatOrderType(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "pickup" or "collection" or "col" => "Collection",
            "delivery" or "del" => "Delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "Table",
            _ => "Order"
        };

    private static string FormatCustomer(string? name, string? phone)
    {
        var parts = new[] { name?.Trim(), phone?.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value));
        var display = string.Join(" • ", parts);
        return string.IsNullOrWhiteSpace(display) ? "Customer not supplied" : display;
    }

    private static string FormatStatus(string? status, string? paymentStatus)
    {
        var orderStatus = string.IsNullOrWhiteSpace(status) ? "Order" : status.Replace('_', ' ');
        var payment = string.IsNullOrWhiteSpace(paymentStatus) ? string.Empty : paymentStatus.Replace('_', ' ');
        return string.IsNullOrWhiteSpace(payment)
            ? Humanize(orderStatus)
            : $"{Humanize(orderStatus)} • {Humanize(payment)}";
    }

    private static string Humanize(string value) => string.Join(" ", value
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
}
