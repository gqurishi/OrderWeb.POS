using System.Globalization;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother-side order history for Client POS. Same query rules as Order History page;
/// Clients fetch on demand — no continuous history push.
/// </summary>
public sealed class ClientOrderHistoryService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    private readonly DatabaseService _databaseService;

    public ClientOrderHistoryService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<ClientOrderHistoryResult> SearchAsync(
        ClientOrderHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);
        var orderType = NormalizeOrderTypeFilter(request.OrderType);
        var search = (request.Search ?? string.Empty).Trim().TrimStart('#');
        var date = request.Date?.Date ?? DateTime.Today;

        var completed = new List<ClientOrderHistoryItemDto>(pageSize);
        var voided = new List<ClientOrderHistoryItemDto>(pageSize);

        await using var connection = await _databaseService.GetConnectionAsync();
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
                  {BuildSourceFilter(orderType, search)}
                  {BuildDateFilter(search)}
                  {BuildOrderTypeFilter(orderType)}
                  {BuildSearchFilter(search)}
                  {BuildLifecycleFilter(orderType)}
            ORDER BY o.created_at DESC, o.id DESC
            LIMIT @pageLimit OFFSET @offset";

        await using (var command = new MySqlCommand(query, connection))
        {
            ApplyQueryParameters(command, date, search);
            var offset = (page - 1) * pageSize;
            command.Parameters.AddWithValue("@offset", offset);
            command.Parameters.AddWithValue("@pageLimit", pageSize + 1);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = ReadHistoryItem(reader);
                (item.HistoryGroup == "voided" ? voided : completed).Add(item);
            }
        }

        var message = "Local order history";
        if (string.Equals(orderType, "WEB", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(search))
        {
            var cloud = await TryMergeCloudHistoryAsync(completed, voided, search, cancellationToken);
            message = string.IsNullOrWhiteSpace(cloud)
                ? "Recent cache + OrderWeb history"
                : cloud;
        }

        var isCloudSearch = string.Equals(orderType, "WEB", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(search);
        var hasNextPage = !isCloudSearch && completed.Count + voided.Count > pageSize;
        TrimToPageSize(completed, voided, pageSize);

        return ClientOrderHistoryResult.Ok(completed, voided, hasNextPage, message);
    }

    private static async Task<string> TryMergeCloudHistoryAsync(
        List<ClientOrderHistoryItemDto> completed,
        List<ClientOrderHistoryItemDto> voided,
        string search,
        CancellationToken cancellationToken)
    {
        var cloudService = ServiceHelper.GetService<CloudOrderService>();
        if (cloudService == null)
        {
            return "OrderWeb history service is unavailable.";
        }

        try
        {
            var cloudResult = await cloudService.SearchOrderHistoryAsync(search, cancellationToken);
            if (!cloudResult.Success)
            {
                return cloudResult.Message;
            }

            MergeCloudResults(completed, voided, cloudResult.Orders);
            return string.IsNullOrWhiteSpace(cloudResult.Message)
                ? $"OrderWeb: {cloudResult.Orders.Count} match(es)"
                : cloudResult.Message;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client order-history cloud merge failed: {ex.Message}");
            return "Could not reach OrderWeb for history search.";
        }
    }

    private static void MergeCloudResults(
        List<ClientOrderHistoryItemDto> completed,
        List<ClientOrderHistoryItemDto> voided,
        IReadOnlyList<Order> cloudOrders)
    {
        var existingKeys = new HashSet<string>(
            completed.Concat(voided).Select(row => row.OrderId),
            StringComparer.OrdinalIgnoreCase);

        foreach (var order in cloudOrders)
        {
            var key = string.IsNullOrWhiteSpace(order.CloudOrderId) ? order.OrderId : order.CloudOrderId;
            if (!existingKeys.Add(key))
            {
                continue;
            }

            var status = order.Status.ToString();
            var item = new ClientOrderHistoryItemDto(
                Id: 0,
                OrderId: key,
                OrderNumber: FormatOrderNumber(order.OrderNumber, order.OrderId),
                OrderType: order.OrderType ?? string.Empty,
                OrderTypeDisplay: FormatOrderType(order.OrderType),
                TotalAmount: order.TotalAmount,
                CreatedAtUtc: ToUtcIso(order.CreatedAt),
                OrderDateTime: $"{order.CreatedAt:h:mm tt} • {order.CreatedAt:dd/MM/yyyy}",
                Status: status,
                StatusDisplay: FormatStatus(status, order.PaymentStatusRaw),
                CustomerDisplay: FormatCustomer(order.CustomerName, order.CustomerPhone),
                PaymentDisplay: OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod),
                SourceChannel: "web",
                IsWebOrder: true,
                HistoryGroup: order.Status == OrderStatus.Cancelled ||
                              order.LocalLifecycleState == LocalLifecycleState.Voided
                    ? "voided"
                    : "completed");

            if (item.HistoryGroup == "voided")
            {
                voided.Add(item);
            }
            else
            {
                completed.Add(item);
            }
        }

        completed.Sort((left, right) => string.CompareOrdinal(right.CreatedAtUtc, left.CreatedAtUtc));
        voided.Sort((left, right) => string.CompareOrdinal(right.CreatedAtUtc, left.CreatedAtUtc));
    }

    private static void TrimToPageSize(
        List<ClientOrderHistoryItemDto> completed,
        List<ClientOrderHistoryItemDto> voided,
        int pageSize)
    {
        while (completed.Count + voided.Count > pageSize)
        {
            if (completed.Count == 0)
            {
                voided.RemoveAt(voided.Count - 1);
            }
            else if (voided.Count == 0)
            {
                completed.RemoveAt(completed.Count - 1);
            }
            else if (string.CompareOrdinal(completed[^1].CreatedAtUtc, voided[^1].CreatedAtUtc) <= 0)
            {
                completed.RemoveAt(completed.Count - 1);
            }
            else
            {
                voided.RemoveAt(voided.Count - 1);
            }
        }
    }

    private static ClientOrderHistoryItemDto ReadHistoryItem(MySqlDataReader reader)
    {
        var orderType = ReadString(reader, "order_type");
        var createdAt = reader.GetDateTime("created_at");
        var historyGroup = ReadString(reader, "history_group");
        var source = ReadString(reader, "source_channel");
        if (string.IsNullOrWhiteSpace(source))
        {
            source = "local";
        }

        return new ClientOrderHistoryItemDto(
            Id: reader.GetInt32("id"),
            OrderId: ReadString(reader, "order_id"),
            OrderNumber: FormatOrderNumber(ReadString(reader, "order_number"), ReadString(reader, "order_id")),
            OrderType: orderType,
            OrderTypeDisplay: FormatOrderType(orderType),
            TotalAmount: reader.GetDecimal("total_amount"),
            CreatedAtUtc: ToUtcIso(createdAt),
            OrderDateTime: $"{createdAt:h:mm tt} • {createdAt:dd/MM/yyyy}",
            Status: ReadString(reader, "status"),
            StatusDisplay: FormatStatus(ReadString(reader, "status"), ReadString(reader, "payment_status")),
            CustomerDisplay: FormatCustomer(ReadString(reader, "customer_name"), ReadString(reader, "customer_phone")),
            PaymentDisplay: OnlineOrderPaymentHelper.GetDisplayMethod(ReadString(reader, "payment_method")),
            SourceChannel: source.ToLowerInvariant(),
            IsWebOrder: source.Equals("web", StringComparison.OrdinalIgnoreCase),
            HistoryGroup: string.Equals(historyGroup, "voided", StringComparison.OrdinalIgnoreCase) ? "voided" : "completed");
    }

    private static string NormalizeOrderTypeFilter(string? orderType)
    {
        var value = (orderType ?? "ALL").Trim().ToUpperInvariant();
        return value switch
        {
            "ALL" or "" => "ALL",
            "COL" or "COLLECTION" or "PICKUP" => "COL",
            "DEL" or "DELIVERY" => "DEL",
            "TBL" or "TABLE" or "DINE_IN" or "DINE-IN" => "TBL",
            "WEB" or "ONLINE" => "WEB",
            _ => "ALL"
        };
    }

    private static string BuildSourceFilter(string orderType, string search) =>
        orderType switch
        {
            "WEB" when !string.IsNullOrWhiteSpace(search) =>
                "AND LOWER(COALESCE(o.source_channel, '')) = 'web' AND o.created_at >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)",
            "WEB" => "AND LOWER(COALESCE(o.source_channel, '')) = 'web'",
            "COL" or "DEL" or "TBL" =>
                "AND LOWER(COALESCE(o.source_channel, 'local')) = 'local'",
            _ => string.Empty
        };

    private static string BuildLifecycleFilter(string orderType) =>
        orderType == "WEB"
            ? string.Empty
            : @"AND (o.local_lifecycle_state IN ('paid', 'voided') OR o.status IN ('completed', 'closed', 'paid', 'void', 'cancelled'))
                AND EXISTS (
                    SELECT 1
                    FROM order_items valid_item
                    WHERE valid_item.order_id = o.id
                      AND valid_item.quantity > 0
                      AND COALESCE(NULLIF(TRIM(valid_item.item_name), ''), '') <> ''
                )";

    private static string BuildOrderTypeFilter(string orderType) =>
        orderType switch
        {
            "COL" => "AND LOWER(o.order_type) IN ('col', 'collection', 'pickup')",
            "DEL" => "AND LOWER(o.order_type) IN ('del', 'delivery')",
            "TBL" => "AND LOWER(o.order_type) IN ('tbl', 'table', 'dine_in', 'dine-in')",
            _ => string.Empty
        };

    private static string BuildDateFilter(string search) =>
        string.IsNullOrWhiteSpace(search)
            ? "AND o.created_at >= @dayStart AND o.created_at < @dayEnd"
            : string.Empty;

    private static string BuildSearchFilter(string search) =>
        string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : "AND (o.order_id = @searchExact OR o.order_number LIKE @searchContains OR o.customer_phone LIKE @searchContains)";

    private static void ApplyQueryParameters(MySqlCommand command, DateTime date, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            command.Parameters.AddWithValue("@dayStart", date.Date);
            command.Parameters.AddWithValue("@dayEnd", date.Date.AddDays(1));
            return;
        }

        command.Parameters.AddWithValue("@searchExact", search);
        command.Parameters.AddWithValue("@searchContains", $"%{search}%");
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

    private static string ReadString(MySqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? string.Empty : reader.GetString(column);

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

    private static string FormatOrderType(string? orderType)
    {
        var normalized = orderType?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "pickup" or "collection" or "col" => "Collection",
            "delivery" or "del" => "Delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "Table",
            _ => "Order"
        };
    }

    private static string FormatCustomer(string? name, string? phone)
    {
        var parts = new[] { name?.Trim(), phone?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value));
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

    private static string ToUtcIso(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
        return utc.ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Read-only detail for history View. Allows paid/voided/closed (unlike live GET /orders/{id}).
    /// </summary>
    public async Task<OrderWeb.Contracts.Dtos.ClientOrderHistoryDetailResponseDto> GetDetailAsync(
        string? orderKey,
        CancellationToken cancellationToken = default)
    {
        var key = (orderKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new OrderWeb.Contracts.Dtos.ClientOrderHistoryDetailResponseDto(
                Success: false,
                Message: "An order id is required.",
                Error: "An order id is required.",
                ErrorCode: OrderWeb.Contracts.Dtos.OrderHistoryErrorCodes.Validation);
        }

        try
        {
            var orderService = new OrderService();
            Order? order = await orderService.GetOrderByExternalIdAsync(key);
            if (order == null && int.TryParse(key, out var dbId) && dbId > 0)
            {
                order = await orderService.GetOrderByDatabaseIdAsync(dbId);
            }

            if (order == null)
            {
                return new OrderWeb.Contracts.Dtos.ClientOrderHistoryDetailResponseDto(
                    Success: false,
                    Message: "Mother POS could not find this order in local history.",
                    Error: "Mother POS could not find this order in local history.",
                    ErrorCode: OrderWeb.Contracts.Dtos.OrderHistoryErrorCodes.NotFound);
            }

            return new OrderWeb.Contracts.Dtos.ClientOrderHistoryDetailResponseDto(
                Success: true,
                Message: "Order history detail",
                Order: MapDetail(order));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client order-history detail failed: {ex.Message}");
            return new OrderWeb.Contracts.Dtos.ClientOrderHistoryDetailResponseDto(
                Success: false,
                Message: "Mother POS could not load this order.",
                Error: "Mother POS could not load this order.",
                ErrorCode: OrderWeb.Contracts.Dtos.OrderHistoryErrorCodes.Unknown);
        }
    }

    private static OrderWeb.Contracts.Dtos.ClientOrderHistoryDetailDto MapDetail(Order order)
    {
        var isVoided = order.LocalLifecycleState == LocalLifecycleState.Voided ||
                       order.Status is OrderStatus.Cancelled;
        var source = string.IsNullOrWhiteSpace(order.SourceChannel) ? "local" : order.SourceChannel.ToLowerInvariant();
        var lines = order.Items
            .Where(item => !(item.MenuItemId ?? string.Empty).StartsWith("tasting-course:", StringComparison.OrdinalIgnoreCase))
            .Select(item => new OrderWeb.Contracts.Dtos.ClientOrderHistoryDetailLineDto(
                Name: string.IsNullOrWhiteSpace(item.DisplayName) ? item.ItemName : item.DisplayName,
                Quantity: item.Quantity,
                UnitPrice: item.ItemPrice ?? 0m,
                TotalPrice: item.TotalPrice,
                Details: BuildLineDetails(item)))
            .ToList();

        return new OrderWeb.Contracts.Dtos.ClientOrderHistoryDetailDto(
            Id: order.Id,
            OrderId: string.IsNullOrWhiteSpace(order.OrderId) ? order.CloudOrderId : order.OrderId,
            OrderNumber: FormatOrderNumber(order.OrderNumber, order.OrderId),
            OrderType: order.OrderType,
            OrderTypeDisplay: FormatOrderType(order.OrderType),
            Status: order.Status.ToString(),
            StatusDisplay: isVoided
                ? "VOIDED"
                : FormatStatus(order.Status.ToString(), order.PaymentStatusRaw),
            HistoryGroup: isVoided ? "voided" : "completed",
            CreatedAtUtc: ToUtcIso(order.CreatedAt),
            OrderDateTime: $"{order.CreatedAt:dd/MM/yyyy h:mm tt}",
            CustomerName: order.CustomerName,
            CustomerPhone: order.CustomerPhone,
            CustomerEmail: order.CustomerEmail,
            CustomerAddress: order.CustomerAddress,
            PaymentMethod: order.PaymentMethod,
            PaymentDisplay: OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod),
            PaymentStatusDisplay: OnlineOrderPaymentHelper.GetStatusDisplay(order.PaymentMethod, order.PaymentStatusRaw),
            AmountPaid: order.AmountPaid,
            PaymentProvider: string.IsNullOrWhiteSpace(order.PaymentProvider) ? null : order.PaymentProvider,
            PaymentReference: OnlineOrderPaymentHelper.FormatReceiptReference(order.TransactionId),
            SubtotalAmount: order.SubtotalAmount,
            TaxAmount: order.TaxAmount,
            DiscountAmount: order.DiscountAmount,
            DeliveryFee: order.DeliveryFee,
            ServiceChargeAmount: order.ServiceChargeAmount,
            TipsAmount: order.CashTipAmount + order.CardTipAmount,
            TotalAmount: order.TotalAmount,
            ScheduledDisplay: order.ScheduledTime.HasValue
                ? order.ScheduledTime.Value.ToString("dd/MM/yyyy h:mm tt")
                : "As soon as possible",
            SpecialInstructions: string.IsNullOrWhiteSpace(order.SpecialInstructions) ? null : order.SpecialInstructions,
            PromoCode: string.IsNullOrWhiteSpace(order.PromoCode) ? null : order.PromoCode,
            GiftCardDisplay: order.GiftCardNumberMasked,
            LoyaltyDisplay: order.LoyaltyPointsEarned == 0 && order.LoyaltyPointsRedeemed == 0
                ? null
                : $"Earned {order.LoyaltyPointsEarned} • Redeemed {order.LoyaltyPointsRedeemed}",
            SourceChannel: source,
            IsWebOrder: source.Equals("web", StringComparison.OrdinalIgnoreCase),
            CanReprint: !string.IsNullOrWhiteSpace(order.OrderId) || order.Id > 0,
            Lines: lines);
    }

    private static string BuildLineDetails(OrderItem item)
    {
        var details = new List<string>();
        if (item.Addons.Count > 0)
        {
            details.Add(string.Join(", ", item.Addons.Select(addon => $"+ {addon.AddonName}")));
        }

        if (!string.IsNullOrWhiteSpace(item.SpecialInstructions))
        {
            details.Add($"Note: {item.SpecialInstructions}");
        }

        return string.Join(" • ", details);
    }
}

public sealed record ClientOrderHistoryRequest(
    DateTime? Date,
    string? OrderType,
    string? Search,
    int Page,
    int PageSize);

public sealed record ClientOrderHistoryItemDto(
    int Id,
    string OrderId,
    string OrderNumber,
    string OrderType,
    string OrderTypeDisplay,
    decimal TotalAmount,
    string CreatedAtUtc,
    string OrderDateTime,
    string Status,
    string StatusDisplay,
    string CustomerDisplay,
    string PaymentDisplay,
    string SourceChannel,
    bool IsWebOrder,
    string HistoryGroup);

public sealed record ClientOrderHistoryResult(
    bool Success,
    string Message,
    IReadOnlyList<ClientOrderHistoryItemDto> Completed,
    IReadOnlyList<ClientOrderHistoryItemDto> Voided,
    bool HasNextPage)
{
    public static ClientOrderHistoryResult Ok(
        IReadOnlyList<ClientOrderHistoryItemDto> completed,
        IReadOnlyList<ClientOrderHistoryItemDto> voided,
        bool hasNextPage,
        string message) =>
        new(true, message, completed, voided, hasNextPage);
}
