using MySqlConnector;
using OrderWeb.Contracts.Dtos;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Advance Collection/Delivery orders: list upcoming, find due for T−3h kitchen print, print once.
/// Mother owns the clock. Phase 1 = print + flags; popup/WS come in later phases.
/// </summary>
public sealed class AdvanceOrderService
{
    public static readonly TimeSpan LeadTime = TimeSpan.FromHours(3);

    /// <summary>Raised on Mother after a successful once-print so the till can show the SharedUI popup.</summary>
    public static event EventHandler<AdvanceOrderReminderEventArgs>? ReminderRaised;

    private readonly DatabaseService _databaseService;
    private readonly OrderService _orderService;
    private readonly OrderRoutingPrintService _printService;
    private bool _schemaEnsured;

    public AdvanceOrderService(
        DatabaseService databaseService,
        OrderService orderService,
        OrderRoutingPrintService printService)
    {
        _databaseService = databaseService;
        _orderService = orderService;
        _printService = printService;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaEnsured || RuntimeSchemaPolicy.IsMigrationManaged)
        {
            _schemaEnsured = true;
            return;
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            ALTER TABLE orders
            ADD COLUMN IF NOT EXISTS advance_kitchen_printed_at DATETIME NULL,
            ADD COLUMN IF NOT EXISTS advance_reminded_at DATETIME NULL";

        try
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdvanceOrder] Schema ensure: {ex.Message}");
        }

        _schemaEnsured = true;
    }

    /// <summary>Upcoming advance orders in [from, to] for Manager list (Phase 3+).</summary>
    public async Task<IReadOnlyList<AdvanceOrderSummary>> ListUpcomingAsync(
        DateTime fromInclusive,
        DateTime toExclusive,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT o.id, o.order_id, o.order_number, o.order_type, o.customer_name, o.customer_phone,
                   o.total_amount, o.scheduled_time, o.advance_kitchen_printed_at, o.advance_reminded_at,
                   o.local_lifecycle_state, o.is_open, o.source_channel
            FROM orders o
            WHERE o.scheduled_time IS NOT NULL
              AND o.scheduled_time >= @from
              AND o.scheduled_time < @to
              AND LOWER(COALESCE(NULLIF(o.source_channel, ''), 'local')) IN ('local', 'web')
              AND LOWER(COALESCE(o.order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway', 'delivery', 'del')
              AND LOWER(COALESCE(o.local_lifecycle_state, '')) NOT IN ('voided', 'paid')
            ORDER BY o.scheduled_time ASC, o.id ASC";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", fromInclusive);
        command.Parameters.AddWithValue("@to", toExclusive);

        var rows = new List<AdvanceOrderSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapSummary(reader));
        }

        return rows;
    }

    /// <summary>
    /// Orders due for advance kitchen print: have ScheduledTime, not yet printed,
    /// not void/paid, Collection/Delivery, and ScheduledTime ≤ now + 3h (includes past).
    /// </summary>
    public async Task<IReadOnlyList<AdvanceOrderSummary>> GetDueForRemindAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var dueBy = now.Add(LeadTime);

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT o.id, o.order_id, o.order_number, o.order_type, o.customer_name, o.customer_phone,
                   o.total_amount, o.scheduled_time, o.advance_kitchen_printed_at, o.advance_reminded_at,
                   o.local_lifecycle_state, o.is_open, o.source_channel
            FROM orders o
            WHERE o.scheduled_time IS NOT NULL
              AND o.advance_kitchen_printed_at IS NULL
              AND o.scheduled_time <= @dueBy
              AND LOWER(COALESCE(NULLIF(o.source_channel, ''), 'local')) IN ('local', 'web')
              AND LOWER(COALESCE(o.order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway', 'delivery', 'del')
              AND LOWER(COALESCE(o.local_lifecycle_state, '')) NOT IN ('voided', 'paid')
            ORDER BY o.scheduled_time ASC, o.id ASC
            LIMIT 40";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@dueBy", dueBy);

        var rows = new List<AdvanceOrderSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapSummary(reader));
        }

        return rows;
    }

    /// <summary>Atomically stamp print (+ remind) time. Returns false if already printed.</summary>
    public async Task<bool> MarkPrintedAsync(
        string orderId,
        DateTime printedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return false;
        }

        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            UPDATE orders
            SET advance_kitchen_printed_at = @printedAt,
                advance_reminded_at = COALESCE(advance_reminded_at, @printedAt),
                updated_at = CURRENT_TIMESTAMP
            WHERE order_id = @orderId
              AND advance_kitchen_printed_at IS NULL";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@printedAt", printedAt);
        command.Parameters.AddWithValue("@orderId", orderId.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<(DateTime From, DateTime ToExclusive, string NormalizedRange)> ResolveRangeAsync(
        string? range,
        DateTime? nowLocal = null)
    {
        var now = nowLocal ?? DateTime.Now;
        var today = now.Date;
        var normalized = (range ?? AdvanceOrderRanges.Today).Trim().ToLowerInvariant();
        return normalized switch
        {
            AdvanceOrderRanges.Tomorrow or "tommorrow" or "tmr" =>
                (today.AddDays(1), today.AddDays(2), AdvanceOrderRanges.Tomorrow),
            AdvanceOrderRanges.Next7Days or "7" or "week" =>
                (today, today.AddDays(7), AdvanceOrderRanges.Next7Days),
            _ => (today, today.AddDays(1), AdvanceOrderRanges.Today)
        };
    }

    public async Task<IReadOnlyList<AdvanceOrderSummary>> ListByRangeAsync(
        string? range,
        CancellationToken cancellationToken = default)
    {
        var (from, to, _) = await ResolveRangeAsync(range);
        return await ListUpcomingAsync(from, to, cancellationToken);
    }

    /// <summary>
    /// Manager manual kitchen print. Allowed even if advance auto-print already ran
    /// (staff reprint); does not clear the once-flag.
    /// </summary>
    public async Task<(bool Success, string Message, bool AlreadyPrinted)> PrintKitchenManualAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return (false, "Order id is required.", false);
        }

        var order = await _orderService.GetOrderByExternalIdAsync(orderId.Trim());
        if (order == null)
        {
            return (false, "Advance order not found.", false);
        }

        if (!order.ScheduledTime.HasValue)
        {
            return (false, "This order is not an advance order.", false);
        }

        if (order.LocalLifecycleState is LocalLifecycleState.Voided or LocalLifecycleState.Paid)
        {
            return (false, "This order is closed.", false);
        }

        if (order.Items == null || order.Items.Count == 0)
        {
            return (false, "Order has no items to print.", false);
        }

        var alreadyPrinted = order.AdvanceKitchenPrintedAt.HasValue;
        var printOrder = ToKitchenPrintOrder(order);
        var printResult = await _printService.PrintTakeawayOrderAsync(
            printOrder,
            NormalizePrintOrderType(order.OrderType));

        if (!printResult.AnyPrinted)
        {
            var reason = printResult.FailedRoutes.Count > 0
                ? string.Join("; ", printResult.FailedRoutes)
                : "Kitchen printer did not accept the ticket.";
            return (false, reason, alreadyPrinted);
        }

        if (!alreadyPrinted)
        {
            await MarkPrintedAsync(order.OrderId, DateTime.Now, cancellationToken);
        }

        return (true, "Kitchen ticket sent.", alreadyPrinted);
    }

    public static string FormatScheduledDisplay(DateTime? scheduled) =>
        scheduled.HasValue ? scheduled.Value.ToString("dd MMM HH:mm") : string.Empty;

    public static string DisplayOrderType(string? orderType)
    {
        var normalized = (orderType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "delivery" or "del" ? "Delivery" : "Collection";
    }

    public static string StatusLabel(AdvanceOrderSummary row)
    {
        if (row.AdvanceKitchenPrintedAt.HasValue)
        {
            return "Printed";
        }

        return "Pending";
    }

    /// <summary>Background job: print kitchen for every due advance order (once).</summary>
    public async Task<BackgroundSyncRunResult> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        if (!TerminalConfigurationService.IsConfigured || !TerminalConfigurationService.IsMotherTerminal)
        {
            return BackgroundSyncRunResult.Skip("Advance order scheduler runs on Mother only.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await EnsureSchemaAsync(cancellationToken);

        var now = DateTime.Now;
        var due = await GetDueForRemindAsync(now, cancellationToken);
        if (due.Count == 0)
        {
            return BackgroundSyncRunResult.Skip("No advance orders due.");
        }

        var printed = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var row in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await TryPrintAdvanceKitchenAsync(row.OrderId, now, cancellationToken);
                if (result == AdvancePrintAttempt.Printed)
                {
                    printed++;
                }
                else if (result == AdvancePrintAttempt.Failed)
                {
                    failed++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                System.Diagnostics.Debug.WriteLine($"[AdvanceOrder] Print {row.OrderId}: {ex.Message}");
            }
        }

        if (printed == 0 && failed == 0)
        {
            return BackgroundSyncRunResult.Skip($"Advance due {due.Count}, skipped {skipped}.");
        }

        if (failed > 0 && printed == 0)
        {
            return BackgroundSyncRunResult.Failed($"Advance kitchen print failed for {failed} order(s).");
        }

        return BackgroundSyncRunResult.Completed(
            $"Advance kitchen printed {printed}, failed {failed}, skipped {skipped}.");
    }

    private async Task<AdvancePrintAttempt> TryPrintAdvanceKitchenAsync(
        string orderId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByExternalIdAsync(orderId);
        if (order == null)
        {
            return AdvancePrintAttempt.Skipped;
        }

        if (order.AdvanceKitchenPrintedAt.HasValue)
        {
            return AdvancePrintAttempt.Skipped;
        }

        if (order.LocalLifecycleState is LocalLifecycleState.Voided or LocalLifecycleState.Paid)
        {
            return AdvancePrintAttempt.Skipped;
        }

        if (!order.ScheduledTime.HasValue)
        {
            return AdvancePrintAttempt.Skipped;
        }

        if (order.Items == null || order.Items.Count == 0)
        {
            return AdvancePrintAttempt.Skipped;
        }

        // Still outside the window (clock skew / race) — wait.
        if (order.ScheduledTime.Value > now.Add(LeadTime).AddSeconds(30))
        {
            return AdvancePrintAttempt.Skipped;
        }

        var printOrder = ToKitchenPrintOrder(order);
        var orderType = NormalizePrintOrderType(order.OrderType);
        var printResult = await _printService.PrintTakeawayOrderAsync(printOrder, orderType);

        if (!printResult.AnyPrinted)
        {
            var reason = printResult.FailedRoutes.Count > 0
                ? string.Join("; ", printResult.FailedRoutes)
                : "no items printed";
            System.Diagnostics.Debug.WriteLine($"[AdvanceOrder] Kitchen print not sent for {orderId}: {reason}");
            return AdvancePrintAttempt.Failed;
        }

        var marked = await MarkPrintedAsync(orderId, now, cancellationToken);
        if (!marked)
        {
            // Another tick already stamped — treat as success (idempotent).
            return AdvancePrintAttempt.Skipped;
        }

        AppDiagnostics.Log(
            $"[AdvanceOrder] Kitchen printed once for {order.OrderNumber ?? orderId} " +
            $"scheduled {order.ScheduledTime:dd MMM HH:mm}");

        await NotifyAdvanceReminderAsync(order, kitchenPrinted: true);
        return AdvancePrintAttempt.Printed;
    }

    private static async Task NotifyAdvanceReminderAsync(Order order, bool kitchenPrinted)
    {
        var isFromWeb = string.Equals(order.SourceChannel, "web", StringComparison.OrdinalIgnoreCase);
        var presentation = new OrderWeb.SharedUI.Views.AdvanceOrderReminderPresentation(
            order.OrderId,
            order.OrderNumber,
            DisplayOrderType(order.OrderType),
            FormatScheduledDisplay(order.ScheduledTime),
            order.CustomerName ?? string.Empty,
            order.CustomerPhone,
            kitchenPrinted,
            isFromWeb);

        try
        {
            ReminderRaised?.Invoke(null, new AdvanceOrderReminderEventArgs(presentation));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdvanceOrder] Local remind failed: {ex.Message}");
        }

        try
        {
            var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
            if (broadcast == null)
            {
                return;
            }

            await broadcast.PublishAdvanceReminderAsync(
                order.OrderId,
                order.OrderNumber,
                DisplayOrderType(order.OrderType),
                order.ScheduledTime,
                order.CustomerName,
                order.CustomerPhone,
                order.TotalAmount,
                kitchenPrinted,
                isFromWeb: isFromWeb);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdvanceOrder] WS remind failed: {ex.Message}");
        }
    }

    private static TableOrder ToKitchenPrintOrder(Order order)
    {
        var printOrder = new TableOrder
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            Notes = order.SpecialInstructions,
            ScheduledTime = order.ScheduledTime,
            OrderMode = "takeaway",
            StartTime = order.CreatedAt == default ? DateTime.Now : order.CreatedAt,
            CreatedAt = order.CreatedAt == default ? DateTime.Now : order.CreatedAt,
            UpdatedAt = order.UpdatedAt == default ? DateTime.Now : order.UpdatedAt,
            DeliveryFee = order.DeliveryFee,
            Discount = order.DiscountAmount,
            Subtotal = order.SubtotalAmount,
            Total = order.TotalAmount,
            VAT = order.TaxAmount
        };

        foreach (var item in order.Items)
        {
            // Force NotSent so PrintTakeawayOrderAsync includes every line on the advance ticket.
            printOrder.Items.Add(new TableOrderItem
            {
                Id = string.IsNullOrWhiteSpace(item.ClientItemId) ? item.Id.ToString() : item.ClientItemId,
                OrderId = order.OrderId,
                MenuItemId = item.MenuItemId ?? string.Empty,
                VariantId = item.VariantId,
                VariantName = item.VariantName,
                DisplayName = item.DisplayName,
                PrintGroupId = item.PrintGroupId,
                PrintInRed = item.PrintInRed,
                Name = item.ItemName,
                UnitPrice = item.ItemPrice ?? 0m,
                Quantity = Math.Max(1, item.Quantity),
                Notes = item.SpecialInstructions,
                CourseType = item.CourseType,
                SendStatus = ItemSendStatus.NotSent
            });
        }

        return printOrder;
    }

    private static string NormalizePrintOrderType(string? orderType)
    {
        var normalized = (orderType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "delivery" or "del" => "delivery",
            _ => "collection"
        };
    }

    private static AdvanceOrderSummary MapSummary(MySqlDataReader reader) =>
        new(
            reader.GetInt32(reader.GetOrdinal("id")),
            reader["order_id"]?.ToString() ?? string.Empty,
            reader["order_number"]?.ToString(),
            reader["order_type"]?.ToString() ?? string.Empty,
            reader["customer_name"]?.ToString() ?? string.Empty,
            reader["customer_phone"]?.ToString(),
            reader["total_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(reader["total_amount"]),
            reader["scheduled_time"] as DateTime?,
            reader["advance_kitchen_printed_at"] as DateTime?,
            reader["advance_reminded_at"] as DateTime?,
            reader["local_lifecycle_state"]?.ToString(),
            reader["is_open"] != DBNull.Value && Convert.ToBoolean(reader["is_open"]),
            reader["source_channel"]?.ToString());

    private enum AdvancePrintAttempt
    {
        Printed,
        Failed,
        Skipped
    }
}

public sealed record AdvanceOrderSummary(
    int DatabaseId,
    string OrderId,
    string? OrderNumber,
    string OrderType,
    string CustomerName,
    string? CustomerPhone,
    decimal TotalAmount,
    DateTime? ScheduledTime,
    DateTime? AdvanceKitchenPrintedAt,
    DateTime? AdvanceRemindedAt,
    string? LocalLifecycleState,
    bool IsOpen,
    string? SourceChannel = null)
{
    public bool IsFromWeb =>
        string.Equals(SourceChannel, "web", StringComparison.OrdinalIgnoreCase);
}

public sealed class AdvanceOrderReminderEventArgs(OrderWeb.SharedUI.Views.AdvanceOrderReminderPresentation reminder) : EventArgs
{
    public OrderWeb.SharedUI.Views.AdvanceOrderReminderPresentation Reminder { get; } = reminder;
}
