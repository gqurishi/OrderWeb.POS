using MySqlConnector;
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class MotherOpenOrderListService : IOpenOrderListService
{
    private readonly DatabaseService _databaseService;
    private readonly OrderLifecycleRolloutService _rolloutService;
    private readonly TableSessionService _tableSessionService;
    private readonly CustomerDataService _customerData;

    private bool _lifecycleColumnsEnsured;
    private bool _tableBackfillCompleted;
    private bool _webOrderTotalsRepairCompleted;

    private IReadOnlyDictionary<string, Order> _ordersByCardKey = new Dictionary<string, Order>();
    private IReadOnlyDictionary<string, TableSession> _tableSessionsByCardKey = new Dictionary<string, TableSession>();

    public MotherOpenOrderListService(
        DatabaseService databaseService,
        OrderLifecycleRolloutService rolloutService,
        TableSessionService tableSessionService,
        CustomerDataService customerData)
    {
        _databaseService = databaseService;
        _rolloutService = rolloutService;
        _tableSessionService = tableSessionService;
        _customerData = customerData;
    }

    public bool TryResolveOrder(OpenOrderCardDto card, out Order? order) =>
        _ordersByCardKey.TryGetValue(card.OrderId, out order);

    public bool TryResolveTableSession(OpenOrderCardDto card, out TableSession? session) =>
        _tableSessionsByCardKey.TryGetValue(card.OrderId, out session);

    public async Task<OperationResult<OpenOrderListDto>> GetOpenOrdersAsync(
        OpenOrderChannelKind channel = OpenOrderChannelKind.All,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _rolloutService.GetConfigAsync();
            await RepairOpenWebOrderTotalsAsync(cancellationToken);

            var allOrders = await LoadOpenOrdersAsync(null, cancellationToken);
            var tableSessions = await LoadTableSessionsAsync(cancellationToken);

            var cards = new List<OpenOrderCardDto>();
            var orderMap = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
            var sessionMap = new Dictionary<string, TableSession>(StringComparer.OrdinalIgnoreCase);

            foreach (var order in allOrders)
            {
                if (IsTableOrderType(order.OrderType))
                {
                    continue;
                }

                var card = MapOrderCard(order);
                cards.Add(card);
                orderMap[card.OrderId] = order;
            }

            foreach (var session in tableSessions)
            {
                var card = MapTableCard(session);
                cards.Add(card);
                sessionMap[card.OrderId] = session;
            }

            _ordersByCardKey = orderMap;
            _tableSessionsByCardKey = sessionMap;

            var sync = await BuildSyncStatusAsync();
            return OperationResult<OpenOrderListDto>.Ok(new OpenOrderListDto(
                SelectedChannel: channel,
                Orders: cards,
                SyncStatus: sync));
        }
        catch (Exception ex)
        {
            return OperationResult<OpenOrderListDto>.Fail(
                OperationError.Failure("Failed to load open orders.", ex.Message));
        }
    }

    private static OpenOrderCardDto MapOrderCard(Order order)
    {
        var channel = ResolveChannel(order.OrderType);
        var isStale = order.IsStaleDraft;
        var accent = isStale ? "#DC2626" : "#10B981";
        var badges = BuildOrderBadges(order);

        return new OpenOrderCardDto(
            OrderId: order.OrderId,
            OrderNumber: FormatOrderNumber(order.OrderNumber, order.OrderId),
            Channel: channel,
            ChannelLabel: FormatOrderTypeLabel(order.OrderType),
            CustomerName: HasCustomerName(order.CustomerName) ? order.CustomerName.Trim() : null,
            TableLabel: null,
            TotalAmount: order.TotalAmount,
            CreatedAtUtc: new DateTimeOffset(order.CreatedAt.ToUniversalTime()),
            Health: isStale ? OpenOrderHealthKind.StaleDraft : OpenOrderHealthKind.Healthy,
            AccentColor: accent,
            Badges: badges,
            TimeDisplay: order.CreatedAt.ToString("HH:mm · dd/MM"));
    }

    private static OpenOrderCardDto MapTableCard(TableSession session)
    {
        var isStale = session.LinkedOrderIsStaleDraft || !session.HasLinkedOpenOrder;
        var accent = isStale ? "#F59E0B" : "#10B981";
        var orderKey = !string.IsNullOrWhiteSpace(session.LinkedOrderId)
            ? session.LinkedOrderId!
            : $"table-session-{session.Id}";

        var tableLabel = string.IsNullOrWhiteSpace(session.TableDisplay)
            ? null
            : session.TableDisplay.StartsWith("Table ", StringComparison.OrdinalIgnoreCase)
                ? session.TableDisplay
                : $"Table {session.TableDisplay}";

        return new OpenOrderCardDto(
            OrderId: orderKey,
            OrderNumber: session.HasLinkedOpenOrder
                ? FormatOrderNumber(session.LinkedOrderNumber, session.LinkedOrderId)
                : null,
            Channel: OpenOrderChannelKind.Table,
            ChannelLabel: "Table",
            CustomerName: null,
            TableLabel: tableLabel,
            TotalAmount: session.LinkedOrderTotalAmount ?? 0m,
            CreatedAtUtc: new DateTimeOffset(session.StartTime.ToUniversalTime()),
            Health: isStale ? OpenOrderHealthKind.NeedsAttention : OpenOrderHealthKind.Healthy,
            AccentColor: accent,
            Badges: Array.Empty<string>(),
            TimeDisplay: $"{session.PartySize} guest{(session.PartySize == 1 ? string.Empty : "s")} · {session.TimeDisplay}");
    }

    private async Task<List<Order>> LoadOpenOrdersAsync(string? typeFilter, CancellationToken cancellationToken)
    {
        var orders = new List<Order>();
        using var connection = await _databaseService.GetConnectionAsync();
        await EnsureOrderLifecycleSchemaAsync(connection);

        var typeClause = typeFilter switch
        {
            "collection" => "AND LOWER(o.order_type) IN ('pickup', 'collection', 'col', 'takeaway')",
            "delivery" => "AND LOWER(o.order_type) IN ('delivery', 'del')",
            _ => string.Empty
        };

        var query = $@"
            SELECT o.id, o.order_id AS OrderId, o.order_number AS OrderNumber, o.customer_name AS CustomerName,
                   o.order_type AS OrderType, o.table_session_id AS TableSessionId,
                   o.source_channel AS SourceChannel, o.payment_method AS PaymentMethod,
                   o.total_amount AS TotalAmount, o.created_at AS CreatedAt,
                   o.updated_at AS UpdatedAt, o.local_lifecycle_state AS LocalLifecycleState,
                   COALESCE(o.is_open, 1) AS IsOpen, COALESCE(o.draft_abandoned_flag, 0) AS DraftAbandonedFlag,
                   COALESCE(o.send_attempt_count, 0) AS SendAttemptCount,
                   COALESCE(o.payment_attempt_count, 0) AS PaymentAttemptCount
            FROM orders o
            WHERE {MotherOpenOrderSql.LiveOrderSourceFilter}
              {MotherOpenOrderSql.ActiveLifecycleFilter}
              {typeClause}
            ORDER BY FIELD(COALESCE(LOWER(o.local_lifecycle_state), 'active'), 'draft', 'active', 'sent_partial', 'sent_full', 'payment_partial'),
                     o.updated_at DESC, o.created_at DESC";

        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            orders.Add(ReadOrderFromReader(reader));
        }

        return orders;
    }

    private async Task<List<TableSession>> LoadTableSessionsAsync(CancellationToken cancellationToken)
    {
        var sessions = new List<TableSession>();

        if (!_tableBackfillCompleted)
        {
            await _tableSessionService.BackfillOpenTableSessionsAsync();
            _tableBackfillCompleted = true;
        }

        using var connection = await _databaseService.GetConnectionAsync();
        await EnsureOrderLifecycleSchemaAsync(connection);

        var query = $@"
            SELECT ts.Id, ts.TableId, ts.SessionNumber, ts.PartySize,
                   ts.StartTime, ts.Status, ts.CurrentOrderId, ts.ParentSessionId, ts.MergedIntoSessionId, rt.TableNumber,
                   o.id AS LinkedOrderDbId, o.order_id AS LinkedOrderId, o.order_number AS LinkedOrderNumber,
                   o.total_amount AS LinkedOrderTotalAmount,
                   o.local_lifecycle_state AS LinkedOrderLifecycleState, o.updated_at AS LinkedOrderUpdatedAt,
                   COALESCE(o.is_open, 1) AS LinkedOrderIsOpen, COALESCE(o.draft_abandoned_flag, 0) AS LinkedOrderDraftAbandonedFlag
            FROM TableSessions ts
            LEFT JOIN RestaurantTables rt ON ts.TableId = rt.Id
            LEFT JOIN orders o ON o.id = (
                SELECT o2.id
                FROM orders o2
                WHERE o2.table_session_id = ts.Id
                  AND {MotherOpenOrderSql.LocalLinkedOrderSourceFilter}
                  {MotherOpenOrderSql.ActiveLinkedOrderLifecycleFilter}
                ORDER BY o2.updated_at DESC, o2.id DESC
                LIMIT 1
            )
            WHERE ts.Status != 'Closed' AND ts.IsActive = 1
            ORDER BY ts.StartTime DESC";

        await using (var command = new MySqlCommand(query, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                sessions.Add(ReadTableSessionFromReader(reader));
            }
        }

        await AddOpenTableOrdersWithoutActiveSessionAsync(connection, sessions, cancellationToken);
        return sessions;
    }

    private static async Task AddOpenTableOrdersWithoutActiveSessionAsync(
        MySqlConnection connection,
        List<TableSession> sessions,
        CancellationToken cancellationToken)
    {
        var knownOrderIds = sessions
            .Where(session => !string.IsNullOrWhiteSpace(session.LinkedOrderId))
            .Select(session => session.LinkedOrderId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var query = $@"
            SELECT o.id AS LinkedOrderDbId, o.order_id AS LinkedOrderId, o.order_number AS LinkedOrderNumber,
                   o.customer_name AS CustomerName, o.total_amount AS LinkedOrderTotalAmount,
                   o.local_lifecycle_state AS LinkedOrderLifecycleState, o.updated_at AS LinkedOrderUpdatedAt,
                   o.created_at AS CreatedAt, COALESCE(o.is_open, 1) AS LinkedOrderIsOpen,
                   COALESCE(o.draft_abandoned_flag, 0) AS LinkedOrderDraftAbandonedFlag,
                   o.table_session_id AS TableSessionId
            FROM orders o
            WHERE LOWER(COALESCE(o.order_type, '')) IN ('table', 'tbl', 'dine_in', 'dine-in')
              AND {MotherOpenOrderSql.LocalSourceFilter}
              {MotherOpenOrderSql.ActiveLifecycleFilter}
              AND (
                    o.table_session_id IS NULL
                    OR NOT EXISTS (
                        SELECT 1
                        FROM TableSessions ts
                        WHERE ts.Id = o.table_session_id
                          AND ts.Status <> 'Closed'
                          AND ts.IsActive = 1
                    )
              )
            ORDER BY o.updated_at DESC, o.created_at DESC";

        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var linkedOrderId = reader.IsDBNull(reader.GetOrdinal("LinkedOrderId"))
                ? string.Empty
                : reader.GetString("LinkedOrderId");
            if (string.IsNullOrWhiteSpace(linkedOrderId) || knownOrderIds.Contains(linkedOrderId))
            {
                continue;
            }

            var tableNumber = ExtractTableNumber(reader.IsDBNull(reader.GetOrdinal("CustomerName"))
                ? null
                : reader.GetString("CustomerName"));
            var orderDbId = reader.GetInt32("LinkedOrderDbId");

            sessions.Add(new TableSession
            {
                Id = -orderDbId,
                TableId = 0,
                SessionNumber = $"ORDER-{orderDbId}",
                PartySize = 1,
                StartTime = reader.IsDBNull(reader.GetOrdinal("CreatedAt")) ? DateTime.Now : reader.GetDateTime("CreatedAt"),
                Status = TableSessionStatus.Ordering,
                CurrentOrderId = linkedOrderId,
                LinkedOrderDbId = orderDbId,
                LinkedOrderId = linkedOrderId,
                LinkedOrderNumber = reader.IsDBNull(reader.GetOrdinal("LinkedOrderNumber")) ? null : reader.GetString("LinkedOrderNumber"),
                LinkedOrderTotalAmount = reader.IsDBNull(reader.GetOrdinal("LinkedOrderTotalAmount")) ? null : reader.GetDecimal("LinkedOrderTotalAmount"),
                LinkedOrderLifecycleState = reader.IsDBNull(reader.GetOrdinal("LinkedOrderLifecycleState")) ? null : reader.GetString("LinkedOrderLifecycleState"),
                LinkedOrderUpdatedAt = reader.IsDBNull(reader.GetOrdinal("LinkedOrderUpdatedAt")) ? null : reader.GetDateTime("LinkedOrderUpdatedAt"),
                LinkedOrderIsOpen = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderIsOpen")) && reader.GetBoolean("LinkedOrderIsOpen"),
                LinkedOrderDraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderDraftAbandonedFlag")) && reader.GetBoolean("LinkedOrderDraftAbandonedFlag"),
                Table = new RestaurantTable
                {
                    TableNumber = string.IsNullOrWhiteSpace(tableNumber) ? "Unlinked" : tableNumber
                }
            });

            knownOrderIds.Add(linkedOrderId);
        }
    }

    private async Task RepairOpenWebOrderTotalsAsync(CancellationToken cancellationToken)
    {
        if (_webOrderTotalsRepairCompleted)
        {
            return;
        }

        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            await EnsureOrderLifecycleSchemaAsync(connection);

            const string repairSql = @"
                UPDATE orders o
                INNER JOIN (
                    SELECT oi.order_id AS order_db_id,
                           SUM((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0)) AS item_total
                    FROM order_items oi
                    LEFT JOIN (
                        SELECT order_item_id,
                               SUM(COALESCE(addon_price, 0.00) * COALESCE(quantity, 1)) AS addon_unit_total
                        FROM order_item_addons
                        GROUP BY order_item_id
                    ) addons ON addons.order_item_id = oi.id
                    GROUP BY oi.order_id
                ) totals ON totals.order_db_id = o.id
                SET o.subtotal_amount = CASE
                        WHEN COALESCE(o.subtotal_amount, 0.00) = 0.00 THEN totals.item_total
                        ELSE o.subtotal_amount
                    END,
                    o.total_amount = CASE
                        WHEN COALESCE(o.total_amount, 0.00) = 0.00 THEN totals.item_total + COALESCE(o.delivery_fee, 0.00)
                        ELSE o.total_amount
                    END,
                    o.updated_at = CURRENT_TIMESTAMP
                WHERE LOWER(COALESCE(o.source_channel, '')) = 'web'
                  AND COALESCE(o.is_open, 1) = 1
                  AND LOWER(COALESCE(NULLIF(o.local_lifecycle_state, ''), 'active')) NOT IN ('paid', 'voided')
                  AND COALESCE(o.total_amount, 0.00) = 0.00
                  AND totals.item_total > 0.00";

            await using var command = new MySqlCommand(repairSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _webOrderTotalsRepairCompleted = true;
        }
        catch
        {
            // Non-fatal repair.
        }
    }

    private async Task EnsureOrderLifecycleSchemaAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged || _lifecycleColumnsEnsured)
        {
            return;
        }

        const string alterSql = @"
            ALTER TABLE orders
            ADD COLUMN IF NOT EXISTS local_lifecycle_state ENUM('draft', 'active', 'sent_partial', 'sent_full', 'payment_partial', 'paid', 'voided') DEFAULT 'draft',
            ADD COLUMN IF NOT EXISTS is_open BOOLEAN DEFAULT TRUE,
            ADD COLUMN IF NOT EXISTS void_reason VARCHAR(255) NULL,
            ADD COLUMN IF NOT EXISTS voided_at DATETIME NULL,
            ADD COLUMN IF NOT EXISTS voided_by VARCHAR(100) NULL,
            ADD COLUMN IF NOT EXISTS paid_at DATETIME NULL";

        try
        {
            await using var alter = new MySqlCommand(alterSql, connection);
            await alter.ExecuteNonQueryAsync();
        }
        catch
        {
            // Schema may be managed elsewhere.
        }

        _lifecycleColumnsEnsured = true;
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

    private static Order ReadOrderFromReader(MySqlDataReader reader)
    {
        var orderNumber = reader.IsDBNull(reader.GetOrdinal("OrderNumber"))
            ? reader.GetString(reader.GetOrdinal("OrderId"))
            : reader.GetString(reader.GetOrdinal("OrderNumber"));

        return new Order
        {
            Id = reader.GetInt32("id"),
            OrderId = reader.IsDBNull(reader.GetOrdinal("OrderId")) ? string.Empty : reader.GetString("OrderId"),
            OrderNumber = orderNumber,
            OrderType = reader.IsDBNull(reader.GetOrdinal("OrderType")) ? "pickup" : reader.GetString("OrderType"),
            SourceChannel = reader.IsDBNull(reader.GetOrdinal("SourceChannel")) ? "local" : reader.GetString("SourceChannel"),
            PaymentMethod = reader.IsDBNull(reader.GetOrdinal("PaymentMethod")) ? null : reader.GetString("PaymentMethod"),
            TableSessionId = reader.IsDBNull(reader.GetOrdinal("TableSessionId")) ? null : reader.GetInt32("TableSessionId"),
            CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Guest" : reader.GetString("CustomerName"),
            TotalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal("TotalAmount"),
            CreatedAt = reader.GetDateTime("CreatedAt"),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? reader.GetDateTime("CreatedAt") : reader.GetDateTime("UpdatedAt"),
            LocalLifecycleState = reader.IsDBNull(reader.GetOrdinal("LocalLifecycleState"))
                ? LocalLifecycleState.Active
                : ParseLifecycleState(reader.GetString("LocalLifecycleState")),
            IsOpen = !reader.IsDBNull(reader.GetOrdinal("IsOpen")) && reader.GetBoolean("IsOpen"),
            DraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("DraftAbandonedFlag")) && reader.GetBoolean("DraftAbandonedFlag"),
            SendAttemptCount = reader.IsDBNull(reader.GetOrdinal("SendAttemptCount")) ? 0 : reader.GetInt32("SendAttemptCount"),
            PaymentAttemptCount = reader.IsDBNull(reader.GetOrdinal("PaymentAttemptCount")) ? 0 : reader.GetInt32("PaymentAttemptCount")
        };
    }

    private static TableSession ReadTableSessionFromReader(MySqlDataReader reader)
    {
        var session = new TableSession
        {
            Id = reader.GetInt32("Id"),
            TableId = reader.GetInt32("TableId"),
            SessionNumber = reader.GetString("SessionNumber"),
            PartySize = reader.GetInt32("PartySize"),
            StartTime = reader.GetDateTime("StartTime"),
            Status = Enum.TryParse<TableSessionStatus>(reader.GetString("Status"), true, out var status)
                ? status
                : TableSessionStatus.Ordering,
            CurrentOrderId = reader.IsDBNull(reader.GetOrdinal("CurrentOrderId")) ? null : reader.GetString("CurrentOrderId"),
            ParentSessionId = reader.IsDBNull(reader.GetOrdinal("ParentSessionId")) ? null : reader.GetInt32("ParentSessionId"),
            MergedIntoSessionId = reader.IsDBNull(reader.GetOrdinal("MergedIntoSessionId")) ? null : reader.GetInt32("MergedIntoSessionId"),
            LinkedOrderDbId = reader.IsDBNull(reader.GetOrdinal("LinkedOrderDbId")) ? null : reader.GetInt32("LinkedOrderDbId"),
            LinkedOrderId = reader.IsDBNull(reader.GetOrdinal("LinkedOrderId")) ? null : reader.GetString("LinkedOrderId"),
            LinkedOrderNumber = reader.IsDBNull(reader.GetOrdinal("LinkedOrderNumber")) ? null : reader.GetString("LinkedOrderNumber"),
            LinkedOrderTotalAmount = reader.IsDBNull(reader.GetOrdinal("LinkedOrderTotalAmount")) ? null : reader.GetDecimal("LinkedOrderTotalAmount"),
            LinkedOrderLifecycleState = reader.IsDBNull(reader.GetOrdinal("LinkedOrderLifecycleState")) ? null : reader.GetString("LinkedOrderLifecycleState"),
            LinkedOrderUpdatedAt = reader.IsDBNull(reader.GetOrdinal("LinkedOrderUpdatedAt")) ? null : reader.GetDateTime("LinkedOrderUpdatedAt"),
            LinkedOrderIsOpen = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderIsOpen")) && reader.GetBoolean("LinkedOrderIsOpen"),
            LinkedOrderDraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderDraftAbandonedFlag")) && reader.GetBoolean("LinkedOrderDraftAbandonedFlag")
        };

        if (!reader.IsDBNull(reader.GetOrdinal("TableNumber")))
        {
            session.Table = new RestaurantTable
            {
                TableNumber = reader.GetString("TableNumber")
            };
        }

        return session;
    }

    private static OpenOrderChannelKind ResolveChannel(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "delivery" or "del" => OpenOrderChannelKind.Delivery,
            "table" or "tbl" or "dine_in" or "dine-in" => OpenOrderChannelKind.Table,
            _ => OpenOrderChannelKind.Collection
        };

    private static bool IsTableOrderType(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() is "table" or "tbl" or "dine_in" or "dine-in";

    private static IReadOnlyList<string> BuildOrderBadges(Order order)
    {
        if (!string.Equals(order.SourceChannel, "web", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var badges = new List<string> { "WEB", FormatOrderTypeLabel(order.OrderType).ToUpperInvariant() };
        if (OnlineOrderPaymentHelper.IsDeferredPaymentMethod(order.PaymentMethod)
            && order.LocalLifecycleState != LocalLifecycleState.Paid
            && order.LocalLifecycleState != LocalLifecycleState.Voided)
        {
            badges.Add("CASH DUE");
        }

        return badges;
    }

    private static string FormatOrderNumber(string? orderNumber, string? orderId)
    {
        var value = !string.IsNullOrWhiteSpace(orderNumber) ? orderNumber.Trim() : orderId?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? "Order"
            : value.StartsWith("#", StringComparison.Ordinal) ? value : $"#{value}";
    }

    private static bool HasCustomerName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !string.Equals(name.Trim(), "Guest", StringComparison.OrdinalIgnoreCase);

    private static string FormatOrderTypeLabel(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "delivery" or "del" => "Delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "Table",
            _ => "Collection"
        };

    private static LocalLifecycleState ParseLifecycleState(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "draft" => LocalLifecycleState.Draft,
            "active" => LocalLifecycleState.Active,
            "sent_partial" => LocalLifecycleState.SentPartial,
            "sentfull" => LocalLifecycleState.SentFull,
            "sent_full" => LocalLifecycleState.SentFull,
            "payment_partial" => LocalLifecycleState.PaymentPartial,
            "paid" => LocalLifecycleState.Paid,
            "voided" => LocalLifecycleState.Voided,
            _ => Enum.TryParse<LocalLifecycleState>(value, true, out var parsed) ? parsed : LocalLifecycleState.Active
        };

    private static string? ExtractTableNumber(string? customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return null;
        }

        var value = customerName.Trim();
        const string tablePrefix = "Table ";
        return value.StartsWith(tablePrefix, StringComparison.OrdinalIgnoreCase)
            ? value[tablePrefix.Length..].Trim()
            : value;
    }
}

internal static class MotherOpenOrderSql
{
    internal const string LocalSourceFilter = @"
                      (
                        LOWER(COALESCE(NULLIF(o.source_channel, ''), 'local')) = 'local'
                        OR (
                            LOWER(COALESCE(o.source_channel, '')) = 'web'
                            AND NULLIF(TRIM(COALESCE(o.order_id, '')), '') IS NOT NULL
                            AND NULLIF(TRIM(COALESCE(o.cloud_order_id, '')), '') IS NOT NULL
                            AND TRIM(o.order_id) <> TRIM(o.cloud_order_id)
                        )
                      )";

    internal const string LocalLinkedOrderSourceFilter = @"
                          (
                            LOWER(COALESCE(NULLIF(o2.source_channel, ''), 'local')) = 'local'
                            OR (
                                LOWER(COALESCE(o2.source_channel, '')) = 'web'
                                AND NULLIF(TRIM(COALESCE(o2.order_id, '')), '') IS NOT NULL
                                AND NULLIF(TRIM(COALESCE(o2.cloud_order_id, '')), '') IS NOT NULL
                                AND TRIM(o2.order_id) <> TRIM(o2.cloud_order_id)
                            )
                          )";

    internal const string WebCashDueSourceFilter = @"
                      (
                        LOWER(COALESCE(o.source_channel, '')) = 'web'
                        AND LOWER(COALESCE(o.order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway', 'delivery', 'del')
                        AND REPLACE(REPLACE(REPLACE(LOWER(COALESCE(o.payment_method, 'cash')), ' ', ''), '_', ''), '-', '')
                            IN ('cash', 'cod', 'cashondelivery', 'cashoncollection')
                      )";

    internal const string LiveOrderSourceFilter = @"
                      (
                        " + LocalSourceFilter + @"
                        OR
                        " + WebCashDueSourceFilter + @"
                      )";

    internal const string ActiveLifecycleFilter = @"
                      AND COALESCE(o.is_open, 1) = 1
                      AND LOWER(COALESCE(NULLIF(o.local_lifecycle_state, ''), 'active')) NOT IN ('paid', 'voided')";

    internal const string ActiveLinkedOrderLifecycleFilter = @"
                          AND COALESCE(o2.is_open, 1) = 1
                          AND LOWER(COALESCE(NULLIF(o2.local_lifecycle_state, ''), 'active')) NOT IN ('paid', 'voided')";
}
