using MySqlConnector;
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace POS_in_NET.Services;

public sealed class MotherOrderSearchService : IOrderSearchService
{
    private const int MaxHits = 50;
    private readonly DatabaseService _databaseService;
    private readonly CustomerDataService _customerData;

    public MotherOrderSearchService(DatabaseService databaseService, CustomerDataService customerData)
    {
        _databaseService = databaseService;
        _customerData = customerData;
    }

    public async Task<OperationResult<OrderSearchResultDto>> SearchOrdersAsync(
        OrderSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var query = request.OrderNumberOrPhone?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return OperationResult<OrderSearchResultDto>.Fail(
                OperationError.Validation("Enter an order number or phone number."));
        }

        try
        {
            var hits = new List<OrderSearchHitDto>();
            using var connection = await _databaseService.GetConnectionAsync();
            var hasPaymentStatus = await HasColumnAsync(connection, "orders", "payment_status", cancellationToken);
            var paymentStatusProjection = OrderHistoryQueryCompatibility.PaymentStatusProjection(hasPaymentStatus);
            var search = query.TrimStart('#');

            var sql = $@"
                SELECT o.order_id, o.order_number, o.order_type, o.customer_name, o.customer_phone,
                       o.total_amount, o.created_at, o.status, o.payment_method, {paymentStatusProjection} AS payment_status,
                       o.source_channel
                FROM orders o
                WHERE (o.order_id = @searchExact OR o.order_number LIKE @searchContains OR o.customer_phone LIKE @searchContains)
                  {BuildDateFilter(request.OnDate)}
                ORDER BY o.created_at DESC, o.id DESC
                LIMIT @limit";

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@searchExact", search);
            command.Parameters.AddWithValue("@searchContains", $"%{search}%");
            command.Parameters.AddWithValue("@limit", MaxHits);
            ApplyDateParameters(command, request.OnDate);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add(MapHit(reader));
            }

            var sync = await BuildSyncStatusAsync();
            return OperationResult<OrderSearchResultDto>.Ok(new OrderSearchResultDto(
                Query: query,
                Hits: hits,
                SyncStatus: sync));
        }
        catch (Exception ex)
        {
            return OperationResult<OrderSearchResultDto>.Fail(
                OperationError.Failure("Order search failed.", ex.Message));
        }
    }

    private static OrderSearchHitDto MapHit(MySqlDataReader reader)
    {
        var orderType = ReadString(reader, "order_type");
        var createdAt = reader.GetDateTime("created_at");
        var status = ReadString(reader, "status");
        var paymentStatus = ReadString(reader, "payment_status");

        return new OrderSearchHitDto(
            OrderId: ReadString(reader, "order_id"),
            OrderNumber: FormatOrderNumber(ReadString(reader, "order_number"), ReadString(reader, "order_id")),
            ChannelLabel: FormatOrderType(orderType),
            CustomerDisplay: FormatCustomer(ReadString(reader, "customer_name"), ReadString(reader, "customer_phone")),
            PhoneDisplay: ReadString(reader, "customer_phone"),
            TotalAmount: reader.GetDecimal("total_amount"),
            CreatedAtUtc: new DateTimeOffset(createdAt.ToUniversalTime()),
            StatusDisplay: FormatStatus(status, paymentStatus),
            PaymentDisplay: OnlineOrderPaymentHelper.GetDisplayMethod(ReadString(reader, "payment_method")));
    }

    private static string BuildDateFilter(DateOnly? date) =>
        date.HasValue
            ? "AND o.created_at >= @dayStart AND o.created_at < @dayEnd"
            : string.Empty;

    private static void ApplyDateParameters(MySqlCommand command, DateOnly? date)
    {
        if (!date.HasValue)
        {
            return;
        }

        command.Parameters.AddWithValue("@dayStart", date.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@dayEnd", date.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
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
