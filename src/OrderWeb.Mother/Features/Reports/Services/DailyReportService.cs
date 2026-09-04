using MySqlConnector;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using System.Globalization;
using System.Text;
using POS_in_NET.Models;
using PdfColor = Syncfusion.Drawing.Color;
using PdfPointF = Syncfusion.Drawing.PointF;

namespace POS_in_NET.Services;

public enum ReportDatePreset
{
	Today,
	Last7Days,
	Last30Days,
	Custom
}

public enum ReportSourceFilter
{
	All,
	Local,
	Web
}

public enum ReportOrderTypeFilter
{
	All,
	Pickup,
	Delivery,
	Table
}

public enum TopSellSection
{
	Food,
	Drink
}

public sealed class ReportSummary
{
	public int OrderCount { get; set; }
	public decimal GrossSales { get; set; }
	public decimal NetSales { get; set; }
	public decimal VatAmount { get; set; }
	public decimal DeliveryChargeTotal { get; set; }
	public decimal ItemSales { get; set; }
	public decimal DiscountTotal { get; set; }
	public decimal ServiceChargeTotal { get; set; }
	public decimal RemovedServiceChargeValue { get; set; }
	public int RemovedServiceChargeCount { get; set; }
	public decimal CashTips { get; set; }
	public decimal CardTips { get; set; }
	public decimal TotalTips => CashTips + CardTips;
	public decimal RefundTotal { get; set; }
	public decimal FinalMoneyCollected { get; set; }
	public decimal AverageOrderValue { get; set; }
}

public sealed class ReportOrderRow
{
	public int OrderDbId { get; set; }
	public string OrderId { get; set; } = string.Empty;
	public string OrderNumber { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
	public string SourceChannel { get; set; } = string.Empty;
	public string OrderType { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string CustomerName { get; set; } = string.Empty;
	public string CustomerPhone { get; set; } = string.Empty;
	public int ItemCount { get; set; }
	public decimal GrossSales { get; set; }
	public decimal NetSales { get; set; }
	public decimal VatAmount { get; set; }

	public string CreatedAtDisplay => CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
	public string AmountDisplay => $"£{GrossSales:F2}";
	public string OrderMeta => $"{SourceChannel} · {OrderType} · {Status}";
}

public sealed class ReportOrderAddonRow
{
	public string AddonName { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal AddonPrice { get; set; }

	public decimal TotalPrice => AddonPrice * Quantity;
	public string DisplayText => Quantity > 1
		? $"{Quantity}x {AddonName} (+£{TotalPrice:F2})"
		: $"{AddonName} (+£{TotalPrice:F2})";
}

public sealed class ReportOrderLineDetail
{
	public int OrderItemId { get; set; }
	public string ItemName { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal AddonUnitTotal { get; set; }
	public decimal LineGross { get; set; }
	public decimal LineNet { get; set; }
	public decimal LineVat { get; set; }
	public string SpecialInstructions { get; set; } = string.Empty;
	public string AddonSummary { get; set; } = string.Empty;
	public List<ReportOrderAddonRow> Addons { get; set; } = new();

	public string QuantityDisplay => $"{Quantity}x";
	public string UnitPriceDisplay => $"£{UnitPrice:F2}";
	public string LineTotalDisplay => $"£{LineGross:F2}";
	public string LineVatDisplay => $"£{LineVat:F2}";
	public bool HasAddons => Addons.Count > 0 || !string.IsNullOrWhiteSpace(AddonSummary);
	public bool HasInstructions => !string.IsNullOrWhiteSpace(SpecialInstructions);
}

public sealed class ReportOrderDetail
{
	public int OrderDbId { get; set; }
	public string OrderId { get; set; } = string.Empty;
	public string OrderNumber { get; set; } = string.Empty;
	public string CloudOrderId { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
	public string SourceChannel { get; set; } = string.Empty;
	public string OrderType { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string CustomerName { get; set; } = string.Empty;
	public string CustomerPhone { get; set; } = string.Empty;
	public string CustomerEmail { get; set; } = string.Empty;
	public string CustomerAddress { get; set; } = string.Empty;
	public string PaymentMethod { get; set; } = string.Empty;
	public string SpecialInstructions { get; set; } = string.Empty;
	public decimal SubtotalAmount { get; set; }
	public decimal DeliveryFee { get; set; }
	public decimal TaxAmount { get; set; }
	public decimal TotalAmount { get; set; }
	public List<ReportOrderLineDetail> Lines { get; set; } = new();

	public string CreatedAtDisplay => CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
	public string TotalDisplay => $"£{TotalAmount:F2}";
	public string SubtotalDisplay => $"£{SubtotalAmount:F2}";
	public string DeliveryFeeDisplay => $"£{DeliveryFee:F2}";
	public string TaxDisplay => $"£{TaxAmount:F2}";
	public bool HasCustomerDetails => !string.IsNullOrWhiteSpace(CustomerName) || !string.IsNullOrWhiteSpace(CustomerPhone) || !string.IsNullOrWhiteSpace(CustomerEmail) || !string.IsNullOrWhiteSpace(CustomerAddress);
}

public sealed class ReportTopItemRow
{
	public string CategoryName { get; set; } = string.Empty;
	public string ItemName { get; set; } = string.Empty;
	public int TotalQuantity { get; set; }
	public decimal GrossSales { get; set; }
	public decimal NetSales { get; set; }
	public decimal VatAmount { get; set; }

	public string GrossDisplay => $"£{GrossSales:F2}";
	public string NetDisplay => $"£{NetSales:F2}";
	public string VatDisplay => $"£{VatAmount:F2}";
}

public sealed class ReportDailyTrendRow
{
	public DateTime BusinessDate { get; set; }
	public int OrderCount { get; set; }
	public decimal GrossSales { get; set; }
	public decimal NetSales { get; set; }
	public decimal VatAmount { get; set; }
}

public sealed class ReportVoidCancelledSummary
{
	public int VoidedCount { get; set; }
	public int CancelledCount { get; set; }
	public decimal VoidedAmount { get; set; }
	public decimal CancelledAmount { get; set; }
	public int TotalCount => VoidedCount + CancelledCount;
	public decimal TotalAmount => VoidedAmount + CancelledAmount;
}

public sealed class ReportVoidCancelledRow
{
	public int OrderDbId { get; set; }
	public string OrderId { get; set; } = string.Empty;
	public string OrderNumber { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
	public DateTime AuditAt { get; set; }
	public string SourceChannel { get; set; } = string.Empty;
	public string OrderType { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string CustomerName { get; set; } = string.Empty;
	public string CustomerPhone { get; set; } = string.Empty;
	public decimal OriginalAmount { get; set; }
	public string Reason { get; set; } = string.Empty;
	public string ActorName { get; set; } = string.Empty;

	public string CreatedAtDisplay => CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
	public string AuditAtDisplay => AuditAt == DateTime.MinValue ? CreatedAtDisplay : AuditAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
	public string AmountDisplay => $"£{OriginalAmount:F2}";
	public string CustomerDisplay => string.IsNullOrWhiteSpace(CustomerName) ? CustomerPhone : CustomerName;
}

public sealed class ReportVoidCancelledSnapshot
{
	public DateTime StartDate { get; set; }
	public DateTime EndDate { get; set; }
	public string SearchText { get; set; } = string.Empty;
	public ReportVoidCancelledSummary Summary { get; set; } = new();
	public List<ReportVoidCancelledRow> Orders { get; set; } = new();
}

public sealed class DailyReportSnapshot
{
	public DateTime StartDate { get; set; }
	public DateTime EndDate { get; set; }
	public string SearchText { get; set; } = string.Empty;
	public ReportSourceFilter SourceFilter { get; set; } = ReportSourceFilter.All;
	public ReportOrderTypeFilter OrderTypeFilter { get; set; } = ReportOrderTypeFilter.All;
	public ReportSummary Summary { get; set; } = new();
	public List<ReportOrderRow> Orders { get; set; } = new();
	public List<ReportTopItemRow> TopItems { get; set; } = new();
	public List<ServiceChargeRemovalAuditRow> ServiceChargeRemovalAudits { get; set; } = new();
}

public sealed class ServiceChargeRemovalAuditRow
{
	public string OrderNumber { get; set; } = string.Empty;
	public decimal Percentage { get; set; }
	public decimal Amount { get; set; }
	public string Reason { get; set; } = string.Empty;
	public string PerformedBy { get; set; } = string.Empty;
	public string ApprovedBy { get; set; } = string.Empty;
	public DateTime EventAt { get; set; }
}

public sealed class DailyReportService
{
	private readonly DatabaseService _databaseService;
	private readonly OrderService _orderService;
	private readonly SemaphoreSlim _viewSchemaLock = new(1, 1);
	private bool _viewSchemaReady;

	public DailyReportService(DatabaseService databaseService)
	{
		_databaseService = databaseService;
		_orderService = new OrderService();
	}

	public async Task<bool> HardDeleteOrderAsync(
		int orderDbId,
		int deletedByUserId,
		string deletedByName,
		string deletionReason)
	{
		if (orderDbId <= 0 || deletedByUserId <= 0 || string.IsNullOrWhiteSpace(deletionReason))
		{
			return false;
		}

		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		await using var transaction = await connection.BeginTransactionAsync();

		try
		{
			var orderInfo = await LoadOrderIdentityForDeleteAsync(connection, transaction, orderDbId);
			if (orderInfo == null)
			{
				await transaction.RollbackAsync();
				return false;
			}

			if (!string.Equals(orderInfo.SourceChannel, "local", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					"OrderWeb orders are cloud records and cannot be permanently deleted from the POS report. " +
					"The seven-day cache cleanup will remove finalized local copies automatically.");
			}

			if (await WasBusinessDateUploadedAsync(connection, transaction, TradingDayHelper.GetBusinessDate(orderInfo.CreatedAt)))
			{
				throw new InvalidOperationException(
					$"The report for {orderInfo.CreatedAt:dd MMM yyyy} has already been uploaded to OrderWeb. " +
					"To keep the original cloud financial record immutable, this local order can no longer be deleted.");
			}

			await InsertLocalDeletionAuditAsync(
				connection,
				transaction,
				orderInfo,
				deletedByUserId,
				deletedByName,
				deletionReason.Trim());

			var identityValues = new[]
			{
				orderInfo.OrderId,
				orderInfo.OrderNumber,
				orderInfo.CloudOrderId
			}
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

			await DeletePrintAndSyncRowsAsync(connection, transaction, identityValues, orderDbId);
			await DeleteOptionalStringLinkedRowsAsync(connection, transaction, identityValues);
			await DeleteOrderCoreRowsAsync(connection, transaction, orderDbId);
			await CleanupTableSessionAfterOrderDeleteAsync(connection, transaction, orderInfo.TableSessionId, orderInfo.OrderId);

			var deletedOrders = await ExecuteNonQueryAsync(
				connection,
				transaction,
				"DELETE FROM orders WHERE id = @orderDbId",
				command => command.Parameters.AddWithValue("@orderDbId", orderDbId));

			await transaction.CommitAsync();
			return deletedOrders > 0;
		}
		catch
		{
			await transaction.RollbackAsync();
			throw;
		}
	}

	public async Task<OperationalAnalyticsSnapshot> GetOperationalAnalyticsAsync(DateTime startDate, DateTime endDate)
	{
		var normalizedStart = TradingDayHelper.GetBusinessDayStart(startDate);
		var normalizedEndExclusive = TradingDayHelper.GetBusinessDayEnd(endDate);
		return await _orderService.GetOperationalAnalyticsAsync(normalizedStart, normalizedEndExclusive);
	}

	private sealed class OrderDeleteIdentity
	{
		public int OrderDbId { get; set; }
		public string OrderId { get; set; } = string.Empty;
		public string OrderNumber { get; set; } = string.Empty;
		public string CloudOrderId { get; set; } = string.Empty;
		public int? TableSessionId { get; set; }
		public string SourceChannel { get; set; } = string.Empty;
		public string Status { get; set; } = string.Empty;
		public string LifecycleState { get; set; } = string.Empty;
		public string PaymentMethod { get; set; } = string.Empty;
		public decimal TotalAmount { get; set; }
		public DateTime CreatedAt { get; set; }
	}

	private static async Task<OrderDeleteIdentity?> LoadOrderIdentityForDeleteAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		int orderDbId)
	{
		await using var command = new MySqlCommand(@"
			SELECT id, order_id, order_number, cloud_order_id, table_session_id,
			       source_channel, status, local_lifecycle_state, payment_method,
			       total_amount, created_at
			FROM orders
			WHERE id = @orderDbId
			LIMIT 1", connection, transaction);
		command.Parameters.AddWithValue("@orderDbId", orderDbId);

		await using var reader = await command.ExecuteReaderAsync();
		if (!await reader.ReadAsync())
		{
			return null;
		}

		return new OrderDeleteIdentity
		{
			OrderDbId = GetInt32(reader, "id"),
			OrderId = GetString(reader, "order_id"),
			OrderNumber = GetString(reader, "order_number"),
			CloudOrderId = GetString(reader, "cloud_order_id"),
			TableSessionId = reader["table_session_id"] == DBNull.Value ? null : Convert.ToInt32(reader["table_session_id"]),
			SourceChannel = GetString(reader, "source_channel"),
			Status = GetString(reader, "status"),
			LifecycleState = GetString(reader, "local_lifecycle_state"),
			PaymentMethod = GetString(reader, "payment_method"),
			TotalAmount = reader["total_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(reader["total_amount"]),
			CreatedAt = Convert.ToDateTime(reader["created_at"])
		};
	}

	private static async Task<bool> WasBusinessDateUploadedAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		DateTime businessDate)
	{
		await using var command = new MySqlCommand(@"
			SELECT EXISTS(
				SELECT 1
				FROM orderweb_daily_report_sync_log
				WHERE report_date = @businessDate
				  AND success = 1
			)", connection, transaction);
		command.Parameters.AddWithValue("@businessDate", businessDate.Date);
		return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
	}

	private static async Task InsertLocalDeletionAuditAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		OrderDeleteIdentity order,
		int deletedByUserId,
		string deletedByName,
		string deletionReason)
	{
		await using var command = new MySqlCommand(@"
			INSERT INTO local_order_deletion_audit
				(action_type, original_order_db_id, order_id, order_number, cloud_order_id,
				 source_channel, order_status, lifecycle_state, total_amount, payment_method,
				 business_date, deletion_reason, deleted_by_user_id, deleted_by_name, terminal_name)
			VALUES
				('admin_test_delete', @orderDbId, @orderId, @orderNumber, @cloudOrderId,
				 @sourceChannel, @status, @lifecycleState, @totalAmount, @paymentMethod,
				 @businessDate, @reason, @deletedByUserId, @deletedByName, @terminalName)", connection, transaction);
		command.Parameters.AddWithValue("@orderDbId", order.OrderDbId);
		command.Parameters.AddWithValue("@orderId", string.IsNullOrWhiteSpace(order.OrderId) ? DBNull.Value : order.OrderId);
		command.Parameters.AddWithValue("@orderNumber", string.IsNullOrWhiteSpace(order.OrderNumber) ? DBNull.Value : order.OrderNumber);
		command.Parameters.AddWithValue("@cloudOrderId", string.IsNullOrWhiteSpace(order.CloudOrderId) ? DBNull.Value : order.CloudOrderId);
		command.Parameters.AddWithValue("@sourceChannel", order.SourceChannel);
		command.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(order.Status) ? DBNull.Value : order.Status);
		command.Parameters.AddWithValue("@lifecycleState", string.IsNullOrWhiteSpace(order.LifecycleState) ? DBNull.Value : order.LifecycleState);
		command.Parameters.AddWithValue("@totalAmount", order.TotalAmount);
		command.Parameters.AddWithValue("@paymentMethod", string.IsNullOrWhiteSpace(order.PaymentMethod) ? DBNull.Value : order.PaymentMethod);
		command.Parameters.AddWithValue("@businessDate", TradingDayHelper.GetBusinessDate(order.CreatedAt));
		command.Parameters.AddWithValue("@reason", deletionReason);
		command.Parameters.AddWithValue("@deletedByUserId", deletedByUserId);
		command.Parameters.AddWithValue("@deletedByName", string.IsNullOrWhiteSpace(deletedByName) ? $"Admin #{deletedByUserId}" : deletedByName.Trim());
		var terminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
		command.Parameters.AddWithValue("@terminalName", string.IsNullOrWhiteSpace(terminalName) ? Environment.MachineName : terminalName);
		await command.ExecuteNonQueryAsync();
	}

	private static async Task DeleteOrderCoreRowsAsync(MySqlConnection connection, MySqlTransaction transaction, int orderDbId)
	{
		await ExecuteIfTableExistsAsync(connection, transaction, "order_item_addons", @"
			DELETE oia FROM order_item_addons oia
			INNER JOIN order_items oi ON oia.order_item_id = oi.id
			WHERE oi.order_id = @orderDbId",
			command => command.Parameters.AddWithValue("@orderDbId", orderDbId));

		await ExecuteDeleteByIntOrderIdAsync(connection, transaction, "order_item_send_tracking", orderDbId);
		await ExecuteDeleteByIntOrderIdAsync(connection, transaction, "order_payments", orderDbId);
		await ExecuteDeleteByIntOrderIdAsync(connection, transaction, "order_events", orderDbId);
		await ExecuteDeleteByIntOrderIdAsync(connection, transaction, "order_refunds", orderDbId);
		await ExecuteDeleteByIntOrderIdAsync(connection, transaction, "order_items", orderDbId);
	}

	private static async Task DeletePrintAndSyncRowsAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		IReadOnlyCollection<string> identityValues,
		int orderDbId)
	{
		await ExecuteDeleteByStringOrderIdAsync(connection, transaction, "network_print_queue", identityValues);
		await ExecuteDeleteByStringOrderIdAsync(connection, transaction, "pending_acks", identityValues);
		await ExecuteDeleteByStringOrderIdAsync(connection, transaction, "order_received_log", identityValues);

		if (identityValues.Count == 0)
		{
			return;
		}

		await ExecuteIfTableColumnExistsAsync(connection, transaction, "terminal_events", "entity_id", @"
			DELETE FROM terminal_events
			WHERE (entity_type = 'order' OR entity_type IS NULL)
			  AND (entity_id IN (" + BuildIdentityPlaceholderList(identityValues.Count) + @") OR entity_id = @orderDbIdText)",
			command =>
			{
				AddIdentityParameters(command, identityValues);
				command.Parameters.AddWithValue("@orderDbIdText", orderDbId.ToString(CultureInfo.InvariantCulture));
			});
	}

	private static async Task DeleteOptionalStringLinkedRowsAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		IReadOnlyCollection<string> identityValues)
	{
		await ExecuteDeleteByStringOrderIdAsync(connection, transaction, "discount_events", identityValues);
		await ExecuteDeleteByStringOrderIdAsync(connection, transaction, "cash_drawer_events", identityValues);
		await ExecuteDeleteByStringOrderIdAsync(connection, transaction, "till_expenses", identityValues);
	}

	private static async Task CleanupTableSessionAfterOrderDeleteAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		int? tableSessionId,
		string orderId)
	{
		if (tableSessionId is null or <= 0)
		{
			return;
		}

		var otherOrders = Convert.ToInt32(await ExecuteScalarAsync(
			connection,
			transaction,
			"SELECT COUNT(*) FROM orders WHERE table_session_id = @sessionId AND order_id <> @orderId",
			command =>
			{
				command.Parameters.AddWithValue("@sessionId", tableSessionId.Value);
				command.Parameters.AddWithValue("@orderId", orderId);
			}) ?? 0);

		if (otherOrders > 0)
		{
			await ExecuteIfTableColumnExistsAsync(connection, transaction, "TableSessions", "CurrentOrderId", @"
				UPDATE TableSessions
				SET CurrentOrderId = NULL
				WHERE Id = @sessionId AND CurrentOrderId = @orderId",
				command =>
				{
					command.Parameters.AddWithValue("@sessionId", tableSessionId.Value);
					command.Parameters.AddWithValue("@orderId", orderId);
				});
			return;
		}

		await ExecuteIfTableColumnExistsAsync(connection, transaction, "TableSessionEvents", "SessionId", @"
			DELETE FROM TableSessionEvents
			WHERE SessionId = @sessionId",
			command => command.Parameters.AddWithValue("@sessionId", tableSessionId.Value));

		await ExecuteIfTableColumnExistsAsync(connection, transaction, "TableSessions", "Id", @"
			DELETE FROM TableSessions
			WHERE Id = @sessionId",
			command => command.Parameters.AddWithValue("@sessionId", tableSessionId.Value));
	}

	private static async Task ExecuteDeleteByIntOrderIdAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		string tableName,
		int orderDbId)
	{
		await ExecuteIfTableColumnExistsAsync(connection, transaction, tableName, "order_id",
			$"DELETE FROM {tableName} WHERE order_id = @orderDbId",
			command => command.Parameters.AddWithValue("@orderDbId", orderDbId));
	}

	private static async Task ExecuteDeleteByStringOrderIdAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		string tableName,
		IReadOnlyCollection<string> identityValues)
	{
		if (identityValues.Count == 0)
		{
			return;
		}

		await ExecuteIfTableColumnExistsAsync(connection, transaction, tableName, "order_id",
			$"DELETE FROM {tableName} WHERE order_id IN ({BuildIdentityPlaceholderList(identityValues.Count)})",
			command => AddIdentityParameters(command, identityValues));
	}

	private static async Task<int> ExecuteIfTableExistsAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		string tableName,
		string sql,
		Action<MySqlCommand>? configure = null)
	{
		if (!await TableExistsAsync(connection, transaction, tableName))
		{
			return 0;
		}

		return await ExecuteNonQueryAsync(connection, transaction, sql, configure);
	}

	private static async Task<int> ExecuteIfTableColumnExistsAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		string tableName,
		string columnName,
		string sql,
		Action<MySqlCommand>? configure = null)
	{
		if (!await ColumnExistsAsync(connection, transaction, tableName, columnName))
		{
			return 0;
		}

		return await ExecuteNonQueryAsync(connection, transaction, sql, configure);
	}

	private static async Task<int> ExecuteNonQueryAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		string sql,
		Action<MySqlCommand>? configure = null)
	{
		await using var command = new MySqlCommand(sql, connection, transaction);
		configure?.Invoke(command);
		return await command.ExecuteNonQueryAsync();
	}

	private static async Task<object?> ExecuteScalarAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		string sql,
		Action<MySqlCommand>? configure = null)
	{
		await using var command = new MySqlCommand(sql, connection, transaction);
		configure?.Invoke(command);
		return await command.ExecuteScalarAsync();
	}

	private static async Task<bool> TableExistsAsync(MySqlConnection connection, MySqlTransaction transaction, string tableName)
	{
		var result = await ExecuteScalarAsync(connection, transaction, @"
			SELECT COUNT(*)
			FROM information_schema.tables
			WHERE table_schema = DATABASE() AND table_name = @tableName",
			command => command.Parameters.AddWithValue("@tableName", tableName));

		return Convert.ToInt32(result ?? 0) > 0;
	}

	private static async Task<bool> ColumnExistsAsync(
		MySqlConnection connection,
		MySqlTransaction transaction,
		string tableName,
		string columnName)
	{
		var result = await ExecuteScalarAsync(connection, transaction, @"
			SELECT COUNT(*)
			FROM information_schema.columns
			WHERE table_schema = DATABASE()
			  AND table_name = @tableName
			  AND column_name = @columnName",
			command =>
			{
				command.Parameters.AddWithValue("@tableName", tableName);
				command.Parameters.AddWithValue("@columnName", columnName);
			});

		return Convert.ToInt32(result ?? 0) > 0;
	}

	private static void AddIdentityParameters(MySqlCommand command, IReadOnlyCollection<string> identityValues)
	{
		var index = 0;
		foreach (var identity in identityValues)
		{
			command.Parameters.AddWithValue($"@identity{index}", identity);
			index++;
		}
	}

	private static string BuildIdentityPlaceholderList(int count)
	{
		return string.Join(",", Enumerable.Range(0, count).Select(index => $"@identity{index}"));
	}

	public async Task<List<ReportDailyTrendRow>> GetDailyTrendAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter = ReportSourceFilter.All,
		ReportOrderTypeFilter orderTypeFilter = ReportOrderTypeFilter.All)
	{
		if (startDate.Date == endDate.Date)
		{
			return await GetHourlyTrendAsync(startDate, endDate, sourceFilter, orderTypeFilter);
		}

		return await GetDailyTrendInternalAsync(startDate, endDate, sourceFilter, orderTypeFilter, groupByHour: false);
	}

	private async Task<List<ReportDailyTrendRow>> GetHourlyTrendAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		return await GetDailyTrendInternalAsync(startDate, endDate, sourceFilter, orderTypeFilter, groupByHour: true);
	}

	private async Task<List<ReportDailyTrendRow>> GetDailyTrendInternalAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter,
		bool groupByHour)
	{
		await EnsureLiveReportViewsAsync();

		var normalizedStart = TradingDayHelper.GetBusinessDayStart(startDate);
		var normalizedEndExclusive = TradingDayHelper.GetBusinessDayEnd(endDate);

		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		var bucketExpression = groupByHour
			? "DATE_FORMAT(created_at, '%Y-%m-%d %H:00:00')"
			: "DATE(DATE_SUB(created_at, INTERVAL 1 HOUR))";

		var query = new StringBuilder($@"
			SELECT
				{bucketExpression} AS bucket,
				COUNT(DISTINCT order_db_id) AS order_count,
				COALESCE(SUM(line_gross), 0.00) AS gross_sales,
				COALESCE(SUM(line_net), 0.00) AS net_sales,
				COALESCE(SUM(line_vat), 0.00) AS vat_amount
			FROM vw_report_order_lines_live
			WHERE created_at >= @startDate
			  AND created_at < @endDate");

		if (sourceFilter != ReportSourceFilter.All)
		{
			query.Append(" AND source_channel = @sourceChannel");
		}

		if (orderTypeFilter != ReportOrderTypeFilter.All)
		{
			query.Append(" AND order_type = @orderType");
		}

		query.Append($@"
			GROUP BY bucket
			ORDER BY bucket");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", normalizedStart);
		command.Parameters.AddWithValue("@endDate", normalizedEndExclusive);

		if (sourceFilter != ReportSourceFilter.All)
		{
			command.Parameters.AddWithValue("@sourceChannel", sourceFilter.ToString().ToLowerInvariant());
		}

		if (orderTypeFilter != ReportOrderTypeFilter.All)
		{
			command.Parameters.AddWithValue("@orderType", orderTypeFilter.ToString().ToLowerInvariant());
		}

		var rows = new List<ReportDailyTrendRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			var bucket = GetString(reader, "bucket");
			rows.Add(new ReportDailyTrendRow
			{
				BusinessDate = DateTime.TryParse(bucket, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
					? parsed
					: normalizedStart,
				OrderCount = GetInt32(reader, "order_count"),
				GrossSales = GetDecimal(reader, "gross_sales"),
				NetSales = GetDecimal(reader, "net_sales"),
				VatAmount = GetDecimal(reader, "vat_amount")
			});
		}

		return rows;
	}

	public async Task<DailyReportSnapshot> GetReportAsync(
		DateTime startDate,
		DateTime endDate,
		string? searchText = null,
		ReportSourceFilter sourceFilter = ReportSourceFilter.All,
		ReportOrderTypeFilter orderTypeFilter = ReportOrderTypeFilter.All)
	{
		await EnsureLiveReportViewsAsync();

		var snapshot = new DailyReportSnapshot
		{
			StartDate = startDate.Date,
			EndDate = endDate.Date,
			SearchText = searchText?.Trim() ?? string.Empty,
			SourceFilter = sourceFilter,
			OrderTypeFilter = orderTypeFilter
		};

		var queryStartDate = TradingDayHelper.GetBusinessDayStart(snapshot.StartDate);
		var queryEndDate = TradingDayHelper.GetBusinessDayEnd(snapshot.EndDate);
		var summaryTask = LoadSummaryWithConnectionAsync(queryStartDate, queryEndDate, sourceFilter, orderTypeFilter);
		var deliveryChargeTask = LoadDeliveryChargeTotalAsync(queryStartDate, queryEndDate, sourceFilter, orderTypeFilter);
		var financialTotalsTask = LoadFinancialTotalsAsync(queryStartDate, queryEndDate, sourceFilter, orderTypeFilter);
		var ordersTask = LoadOrdersWithConnectionAsync(queryStartDate, queryEndDate, snapshot.SearchText, sourceFilter, orderTypeFilter);
		var topItemsTask = LoadTopItemsWithConnectionAsync(queryStartDate, queryEndDate, sourceFilter, orderTypeFilter);
		var removalAuditTask = LoadServiceChargeRemovalAuditsAsync(queryStartDate, queryEndDate, sourceFilter, orderTypeFilter);

		await Task.WhenAll(summaryTask, deliveryChargeTask, financialTotalsTask, ordersTask, topItemsTask, removalAuditTask);

		snapshot.Summary = await summaryTask;
		snapshot.Summary.DeliveryChargeTotal = await deliveryChargeTask;
		var financialTotals = await financialTotalsTask;
		snapshot.Summary.ItemSales = financialTotals.ItemSales;
		snapshot.Summary.DiscountTotal = financialTotals.Discounts;
		snapshot.Summary.ServiceChargeTotal = financialTotals.ServiceCharges;
		snapshot.Summary.RemovedServiceChargeValue = financialTotals.RemovedServiceChargeValue;
		snapshot.Summary.RemovedServiceChargeCount = financialTotals.RemovedServiceChargeCount;
		snapshot.Summary.CashTips = financialTotals.CashTips;
		snapshot.Summary.CardTips = financialTotals.CardTips;
		snapshot.Summary.RefundTotal = financialTotals.Refunds;
		snapshot.Summary.FinalMoneyCollected = financialTotals.FinalMoneyCollected;
		snapshot.Orders = await ordersTask;
		snapshot.TopItems = await topItemsTask;
		snapshot.ServiceChargeRemovalAudits = await removalAuditTask;

		return snapshot;
	}

	private async Task<List<ServiceChargeRemovalAuditRow>> LoadServiceChargeRemovalAuditsAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();
		var query = new StringBuilder("""
			SELECT COALESCE(orders.order_number, orders.order_id) AS order_number,
			       sc.service_charge_percentage, sc.service_charge_amount,
			       COALESCE(sc.reason, '') AS reason,
			       sc.performed_by_name, COALESCE(sc.approved_by_name, '') AS approved_by_name,
			       sc.event_at
			FROM order_service_charge_events sc
			INNER JOIN orders ON orders.id = sc.order_id
			WHERE sc.event_type = 'removed'
			  AND sc.event_at >= @startDate AND sc.event_at < @endDate
			""");
		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);
		query.Append(" ORDER BY sc.event_at DESC");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);
		var rows = new List<ServiceChargeRemovalAuditRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			rows.Add(new ServiceChargeRemovalAuditRow
			{
				OrderNumber = GetString(reader, "order_number"),
				Percentage = GetDecimal(reader, "service_charge_percentage"),
				Amount = GetDecimal(reader, "service_charge_amount"),
				Reason = GetString(reader, "reason"),
				PerformedBy = GetString(reader, "performed_by_name"),
				ApprovedBy = GetString(reader, "approved_by_name"),
				EventAt = reader.GetDateTime("event_at")
			});
		}
		return rows;
	}

	private async Task<ReportSummary> LoadSummaryWithConnectionAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await EnsureLiveReportViewsAsync();

		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();
		return await LoadSummaryAsync(connection, startDate, endDate, sourceFilter, orderTypeFilter);
	}

	private async Task<decimal> LoadDeliveryChargeTotalAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

			var query = new StringBuilder(@"
				SELECT COALESCE(SUM(delivery_fee), 0.00)
				FROM orders
				WHERE created_at >= @startDate
				  AND created_at < @endDate
				  AND COALESCE(status, '') NOT IN ('cancelled', 'voided')
				  AND COALESCE(local_lifecycle_state, '') <> 'voided'
				  AND (
					LOWER(COALESCE(local_lifecycle_state, '')) = 'paid'
					OR LOWER(COALESCE(status, '')) IN ('completed', 'paid', 'closed')
					OR paid_at IS NOT NULL
					OR EXISTS (
						SELECT 1
						FROM order_payments op
						WHERE op.order_id = orders.id
						  AND LOWER(COALESCE(op.status, '')) = 'approved'
					)
				  )");

		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);

		return Convert.ToDecimal(await command.ExecuteScalarAsync() ?? 0m, CultureInfo.InvariantCulture);
	}

	private async Task<ReportFinancialTotals> LoadFinancialTotalsAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		var result = new ReportFinancialTotals();
		var orderQuery = new StringBuilder("""
			SELECT COALESCE(SUM(subtotal_amount), 0) AS item_sales,
			       COALESCE(SUM(discount_amount), 0) AS discounts,
			       COALESCE(SUM(CASE WHEN service_charge_status = 'applied' THEN service_charge_amount ELSE 0 END), 0) AS service_charges
			FROM orders
			WHERE created_at >= @startDate AND created_at < @endDate
			  AND local_lifecycle_state = 'paid'
			""");
		AppendOptionalFilters(orderQuery, sourceFilter, orderTypeFilter);
		await using (var command = new MySqlCommand(orderQuery.ToString(), connection))
		{
			command.Parameters.AddWithValue("@startDate", startDate);
			command.Parameters.AddWithValue("@endDate", endDate);
			AddOptionalParameters(command, sourceFilter, orderTypeFilter);
			await using var reader = await command.ExecuteReaderAsync();
			if (await reader.ReadAsync())
			{
				result.ItemSales = GetDecimal(reader, "item_sales");
				result.Discounts = GetDecimal(reader, "discounts");
				result.ServiceCharges = GetDecimal(reader, "service_charges");
			}
		}

		// Service charge is collected when the order is paid, which can be a
		// different business day from when a table was first opened.
		var serviceChargeQuery = new StringBuilder("""
			SELECT COALESCE(SUM(service_charge_amount), 0) AS service_charges
			FROM orders
			WHERE local_lifecycle_state = 'paid'
			  AND service_charge_status = 'applied'
			  AND COALESCE(paid_at, updated_at, created_at) >= @startDate
			  AND COALESCE(paid_at, updated_at, created_at) < @endDate
			""");
		AppendOptionalFilters(serviceChargeQuery, sourceFilter, orderTypeFilter);
		await using (var command = new MySqlCommand(serviceChargeQuery.ToString(), connection))
		{
			command.Parameters.AddWithValue("@startDate", startDate);
			command.Parameters.AddWithValue("@endDate", endDate);
			AddOptionalParameters(command, sourceFilter, orderTypeFilter);
			result.ServiceCharges = Convert.ToDecimal(
				await command.ExecuteScalarAsync() ?? 0m,
				CultureInfo.InvariantCulture);
		}

		var paymentQuery = new StringBuilder("""
			SELECT
			  COALESCE(SUM(CASE WHEN op.payment_method = 'cash' THEN op.tip_amount ELSE 0 END), 0)
			    - COALESCE(SUM(CASE WHEN op.payment_method = 'refund' THEN CAST(COALESCE(JSON_UNQUOTE(JSON_EXTRACT(op.metadata_json, '$.cashTipReversed')), '0') AS DECIMAL(10,2)) ELSE 0 END), 0) AS cash_tips,
			  COALESCE(SUM(CASE WHEN op.payment_method = 'card' THEN op.tip_amount ELSE 0 END), 0)
			    - COALESCE(SUM(CASE WHEN op.payment_method = 'refund' THEN CAST(COALESCE(JSON_UNQUOTE(JSON_EXTRACT(op.metadata_json, '$.cardTipReversed')), '0') AS DECIMAL(10,2)) ELSE 0 END), 0) AS card_tips,
			  COALESCE(SUM(CASE WHEN op.payment_method = 'refund' THEN op.amount ELSE 0 END), 0) AS refunds,
			  COALESCE(SUM(op.amount), 0) AS money_collected
			FROM order_payments op
			INNER JOIN orders ON orders.id = op.order_id
			WHERE op.status = 'approved'
			  AND op.created_at >= @startDate AND op.created_at < @endDate
			""");
		AppendOptionalFilters(paymentQuery, sourceFilter, orderTypeFilter);
		await using (var command = new MySqlCommand(paymentQuery.ToString(), connection))
		{
			command.Parameters.AddWithValue("@startDate", startDate);
			command.Parameters.AddWithValue("@endDate", endDate);
			AddOptionalParameters(command, sourceFilter, orderTypeFilter);
			await using var reader = await command.ExecuteReaderAsync();
			if (await reader.ReadAsync())
			{
				result.CashTips = GetDecimal(reader, "cash_tips");
				result.CardTips = GetDecimal(reader, "card_tips");
				result.Refunds = GetDecimal(reader, "refunds");
				result.FinalMoneyCollected = GetDecimal(reader, "money_collected");
			}
		}

		var removedQuery = new StringBuilder("""
			SELECT COUNT(*) AS removed_count,
			       COALESCE(SUM(sc.service_charge_amount), 0) AS removed_value
			FROM order_service_charge_events sc
			INNER JOIN orders ON orders.id = sc.order_id
			WHERE sc.event_type = 'removed' AND sc.event_at >= @startDate AND sc.event_at < @endDate
			""");
		AppendOptionalFilters(removedQuery, sourceFilter, orderTypeFilter);
		await using (var command = new MySqlCommand(removedQuery.ToString(), connection))
		{
			command.Parameters.AddWithValue("@startDate", startDate);
			command.Parameters.AddWithValue("@endDate", endDate);
			AddOptionalParameters(command, sourceFilter, orderTypeFilter);
			await using var reader = await command.ExecuteReaderAsync();
			if (await reader.ReadAsync())
			{
				result.RemovedServiceChargeCount = reader.GetInt32("removed_count");
				result.RemovedServiceChargeValue = GetDecimal(reader, "removed_value");
			}
		}

		return result;
	}

	private sealed class ReportFinancialTotals
	{
		public decimal ItemSales { get; set; }
		public decimal Discounts { get; set; }
		public decimal ServiceCharges { get; set; }
		public decimal RemovedServiceChargeValue { get; set; }
		public int RemovedServiceChargeCount { get; set; }
		public decimal CashTips { get; set; }
		public decimal CardTips { get; set; }
		public decimal Refunds { get; set; }
		public decimal FinalMoneyCollected { get; set; }
	}

	private async Task<List<ReportOrderRow>> LoadOrdersWithConnectionAsync(
		DateTime startDate,
		DateTime endDate,
		string searchText,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();
		return await LoadOrdersAsync(connection, startDate, endDate, searchText, sourceFilter, orderTypeFilter);
	}

	private async Task<List<ReportTopItemRow>> LoadTopItemsWithConnectionAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await EnsureLiveReportViewsAsync();

		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();
		return await LoadTopItemsAsync(connection, startDate, endDate, sourceFilter, orderTypeFilter);
	}

	public async Task<DailyReportSnapshot> GetTopSellReportAsync(
		DateTime startDate,
		DateTime endDate,
		TopSellSection section,
		string? searchText = null)
	{
		await EnsureLiveReportViewsAsync();

		var snapshot = new DailyReportSnapshot
		{
			StartDate = startDate.Date,
			EndDate = endDate.Date,
			SearchText = searchText?.Trim() ?? string.Empty
		};

		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		snapshot.TopItems = await LoadTopSellItemsAsync(
			connection,
			TradingDayHelper.GetBusinessDayStart(snapshot.StartDate),
			TradingDayHelper.GetBusinessDayEnd(snapshot.EndDate),
			section,
			snapshot.SearchText);
		snapshot.Summary = new ReportSummary
		{
			OrderCount = 0,
			GrossSales = snapshot.TopItems.Sum(item => item.GrossSales),
			NetSales = snapshot.TopItems.Sum(item => item.NetSales),
			VatAmount = snapshot.TopItems.Sum(item => item.VatAmount),
			AverageOrderValue = 0m
		};

		return snapshot;
	}

	public async Task<ReportVoidCancelledSnapshot> GetVoidCancelledReportAsync(
		DateTime startDate,
		DateTime endDate,
		string? searchText = null)
	{
		var snapshot = new ReportVoidCancelledSnapshot
		{
			StartDate = startDate.Date,
			EndDate = endDate.Date,
			SearchText = searchText?.Trim() ?? string.Empty
		};

		var queryStartDate = TradingDayHelper.GetBusinessDayStart(snapshot.StartDate);
		var queryEndDate = TradingDayHelper.GetBusinessDayEnd(snapshot.EndDate);
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		var query = new StringBuilder(@"
			SELECT
				o.id AS order_db_id,
				o.order_id,
				COALESCE(NULLIF(o.order_number, ''), o.order_id) AS order_number,
				o.created_at,
				COALESCE(o.voided_at, cancel_event.event_at, void_event.event_at, o.updated_at, o.created_at) AS audit_at,
				COALESCE(o.source_channel, '') AS source_channel,
				COALESCE(o.order_type, '') AS order_type,
				COALESCE(o.status, '') AS status,
				COALESCE(o.local_lifecycle_state, '') AS lifecycle_state,
				COALESCE(o.customer_name, '') AS customer_name,
				COALESCE(o.customer_phone, '') AS customer_phone,
				COALESCE(o.total_amount, 0.00) AS original_amount,
				COALESCE(
					NULLIF(JSON_UNQUOTE(JSON_EXTRACT(void_event.payload_json, '$.reason')), ''),
					NULLIF(JSON_UNQUOTE(JSON_EXTRACT(cancel_event.payload_json, '$.reason')), ''),
					NULLIF(o.void_reason, ''),
					'Unspecified'
				) AS audit_reason,
				COALESCE(
					NULLIF(void_event.actor_name, ''),
					NULLIF(cancel_event.actor_name, ''),
					NULLIF(o.voided_by, ''),
					'Unknown'
				) AS actor_name
			FROM orders o
			LEFT JOIN order_events void_event ON void_event.id = (
				SELECT oe.id
				FROM order_events oe
				WHERE oe.order_id = o.id
				  AND oe.event_type = 'voided'
				ORDER BY oe.event_at DESC, oe.id DESC
				LIMIT 1
			)
			LEFT JOIN order_events cancel_event ON cancel_event.id = (
				SELECT oe.id
				FROM order_events oe
				WHERE oe.order_id = o.id
				  AND oe.event_type = 'state_changed'
				  AND JSON_UNQUOTE(JSON_EXTRACT(oe.payload_json, '$.status')) IN ('cancelled', 'voided')
				ORDER BY oe.event_at DESC, oe.id DESC
				LIMIT 1
			)
			WHERE o.created_at >= @startDate
			  AND o.created_at < @endDate
			  AND (
				  LOWER(COALESCE(o.status, '')) IN ('cancelled', 'voided')
				  OR COALESCE(LOWER(o.local_lifecycle_state), '') = 'voided'
			  )");

		if (!string.IsNullOrWhiteSpace(snapshot.SearchText))
		{
			query.Append(@"
				AND (
					o.order_number LIKE @searchText
					OR o.customer_name LIKE @searchText
					OR o.customer_phone LIKE @searchText
					OR o.order_id LIKE @searchText
				)");
		}

		query.Append(" ORDER BY audit_at DESC, o.created_at DESC LIMIT 500");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", queryStartDate);
		command.Parameters.AddWithValue("@endDate", queryEndDate);
		if (!string.IsNullOrWhiteSpace(snapshot.SearchText))
		{
			command.Parameters.AddWithValue("@searchText", $"%{snapshot.SearchText}%");
		}

		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			var status = GetString(reader, "status");
			var lifecycle = GetString(reader, "lifecycle_state");
			var isCancelled = string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);
			var isVoided = !isCancelled && (string.Equals(lifecycle, "voided", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(status, "voided", StringComparison.OrdinalIgnoreCase));
			var amount = GetDecimal(reader, "original_amount");

			if (isVoided)
			{
				snapshot.Summary.VoidedCount++;
				snapshot.Summary.VoidedAmount += amount;
			}
			else
			{
				snapshot.Summary.CancelledCount++;
				snapshot.Summary.CancelledAmount += amount;
			}

			snapshot.Orders.Add(new ReportVoidCancelledRow
			{
				OrderDbId = GetInt32(reader, "order_db_id"),
				OrderId = GetString(reader, "order_id"),
				OrderNumber = GetString(reader, "order_number"),
				CreatedAt = GetDateTime(reader, "created_at"),
				AuditAt = GetDateTime(reader, "audit_at"),
				SourceChannel = GetString(reader, "source_channel"),
				OrderType = GetString(reader, "order_type"),
				Status = isVoided ? "Voided" : "Cancelled",
				CustomerName = GetString(reader, "customer_name"),
				CustomerPhone = GetString(reader, "customer_phone"),
				OriginalAmount = amount,
				Reason = GetString(reader, "audit_reason"),
				ActorName = GetString(reader, "actor_name")
			});
		}

		return snapshot;
	}

	public async Task<string> ExportCsvAsync(DailyReportSnapshot report)
	{
		var folderPath = GetReportOutputFolder();
		Directory.CreateDirectory(folderPath);

		var fileName = $"Report_{report.StartDate:yyyyMMdd}_{report.EndDate:yyyyMMdd}_{DateTime.Now:HHmmss}.csv";
		var filePath = Path.Combine(folderPath, fileName);

		var builder = new StringBuilder();
		builder.AppendLine($"Report Range,{report.StartDate:yyyy-MM-dd},{report.EndDate:yyyy-MM-dd}");
		builder.AppendLine($"Source Filter,{report.SourceFilter}");
		builder.AppendLine($"Order Type Filter,{report.OrderTypeFilter}");
		builder.AppendLine($"Search,{EscapeCsv(report.SearchText)}");
		builder.AppendLine();
		builder.AppendLine("Summary");
		builder.AppendLine("Order Count,Item Sales,Gross Sales,Net Sales,Discounts,Service Charges,Removed Service Charge Count,Potential Removed Value,Cash Tips,Card Tips,Total Tips,Delivery Fees,Refunds,VAT,Final Money Collected,Average Order Value");
		builder.AppendLine($"{report.Summary.OrderCount},{report.Summary.ItemSales:F2},{report.Summary.GrossSales:F2},{report.Summary.NetSales:F2},{report.Summary.DiscountTotal:F2},{report.Summary.ServiceChargeTotal:F2},{report.Summary.RemovedServiceChargeCount},{report.Summary.RemovedServiceChargeValue:F2},{report.Summary.CashTips:F2},{report.Summary.CardTips:F2},{report.Summary.TotalTips:F2},{report.Summary.DeliveryChargeTotal:F2},{report.Summary.RefundTotal:F2},{report.Summary.VatAmount:F2},{report.Summary.FinalMoneyCollected:F2},{report.Summary.AverageOrderValue:F2}");
		builder.AppendLine();
		builder.AppendLine("Orders");
		builder.AppendLine("Created At,Order Number,Customer,Phone,Source,Type,Status,Items,Gross,Net,VAT");

		foreach (var order in report.Orders)
		{
			builder.AppendLine(string.Join(',', new[]
			{
				EscapeCsv(order.CreatedAtDisplay),
				EscapeCsv(order.OrderNumber),
				EscapeCsv(order.CustomerName),
				EscapeCsv(order.CustomerPhone),
				EscapeCsv(order.SourceChannel),
				EscapeCsv(order.OrderType),
				EscapeCsv(order.Status),
				order.ItemCount.ToString(CultureInfo.InvariantCulture),
				order.GrossSales.ToString("F2", CultureInfo.InvariantCulture),
				order.NetSales.ToString("F2", CultureInfo.InvariantCulture),
				order.VatAmount.ToString("F2", CultureInfo.InvariantCulture)
			}));
		}

		builder.AppendLine();
		builder.AppendLine("Service Charge Removal Audit");
		builder.AppendLine("Time,Order,Percentage,Potential Removed Value,Reason,Performed By,Approved By");
		foreach (var audit in report.ServiceChargeRemovalAudits)
		{
			builder.AppendLine(string.Join(',', new[]
			{
				EscapeCsv(audit.EventAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
				EscapeCsv(audit.OrderNumber),
				audit.Percentage.ToString("F2", CultureInfo.InvariantCulture),
				audit.Amount.ToString("F2", CultureInfo.InvariantCulture),
				EscapeCsv(audit.Reason),
				EscapeCsv(audit.PerformedBy),
				EscapeCsv(audit.ApprovedBy)
			}));
		}

		builder.AppendLine();
		builder.AppendLine("Top Items");
		builder.AppendLine("Category,Item,Quantity,Gross,Net,VAT");

		foreach (var item in report.TopItems)
		{
			builder.AppendLine(string.Join(',', new[]
			{
				EscapeCsv(item.CategoryName),
				EscapeCsv(item.ItemName),
				item.TotalQuantity.ToString(CultureInfo.InvariantCulture),
				item.GrossSales.ToString("F2", CultureInfo.InvariantCulture),
				item.NetSales.ToString("F2", CultureInfo.InvariantCulture),
				item.VatAmount.ToString("F2", CultureInfo.InvariantCulture)
			}));
		}

		await File.WriteAllTextAsync(filePath, builder.ToString(), Encoding.UTF8);
		return filePath;
	}

	public async Task<string> ExportPdfAsync(DailyReportSnapshot report, BusinessInfo? businessInfo = null, string businessName = "POS-in-NET")
	{
		var folderPath = GetReportOutputFolder();
		Directory.CreateDirectory(folderPath);

		var fileName = $"Report_{report.StartDate:yyyyMMdd}_{report.EndDate:yyyyMMdd}_{DateTime.Now:HHmmss}.pdf";
		var filePath = Path.Combine(folderPath, fileName);

		using var document = new PdfDocument();
		document.PageSettings.Size = PdfPageSize.A4;
		document.PageSettings.Orientation = PdfPageOrientation.Landscape;

		var page = document.Pages.Add();
		var graphics = page.Graphics;

		var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 20, PdfFontStyle.Bold);
		var headingFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
		var normalFont = new PdfStandardFont(PdfFontFamily.Helvetica, 10);
		var smallFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
		var metaFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9);

		var resolvedBusinessName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
			? businessName
			: businessInfo!.RestaurantName.Trim();

		var pageWidth = page.GetClientSize().Width;
		var contentWidth = pageWidth - 40;

		var y = 20f;
		var headerRect = new RectangleF(20, y, contentWidth, 92);
		graphics.DrawRectangle(new PdfSolidBrush(PdfColor.FromArgb(255, 239, 246, 255)), headerRect);
		graphics.DrawRectangle(new PdfPen(PdfColor.FromArgb(255, 191, 219, 254), 1), headerRect);

		var textStartX = 34f;
		if (!string.IsNullOrWhiteSpace(businessInfo?.LogoPath) && File.Exists(businessInfo.LogoPath))
		{
			try
			{
				await using var logoStream = File.OpenRead(businessInfo.LogoPath);
				var logo = new PdfBitmap(logoStream);
				graphics.DrawImage(logo, new RectangleF(34, y + 16, 56, 56));
				textStartX = 100f;
			}
			catch
			{
				// Keep rendering even if logo cannot be loaded.
			}
		}

		graphics.DrawString(resolvedBusinessName, titleFont, PdfBrushes.Black, new PdfPointF(textStartX, y + 16));

		var rightMetaX = pageWidth - 250;
		graphics.DrawString("Sales Report", headingFont, PdfBrushes.Black, new PdfPointF(rightMetaX, y + 16));
		graphics.DrawString($"Period: {report.StartDate:dd MMM yyyy} - {report.EndDate:dd MMM yyyy}", metaFont, PdfBrushes.DarkSlateGray, new PdfPointF(rightMetaX, y + 36));
		graphics.DrawString($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}", metaFont, PdfBrushes.DarkSlateGray, new PdfPointF(rightMetaX, y + 52));

		y += 104;

		var businessLines = BuildBusinessInfoLines(businessInfo);
		if (businessLines.Count > 0)
		{
			var businessInfoHeight = 14 + (businessLines.Count * 12);
			var infoRect = new RectangleF(20, y, contentWidth, businessInfoHeight);
			graphics.DrawRectangle(new PdfSolidBrush(PdfColor.FromArgb(255, 248, 250, 252)), infoRect);
			graphics.DrawRectangle(new PdfPen(PdfColor.FromArgb(255, 226, 232, 240), 1), infoRect);

			graphics.DrawString("Business Information", headingFont, PdfBrushes.Black, new PdfPointF(28, y + 6));
			var businessLineY = y + 20;
			foreach (var line in businessLines)
			{
				graphics.DrawString(line, metaFont, PdfBrushes.DarkSlateGray, new PdfPointF(28, businessLineY));
				businessLineY += 12;
			}

			y += businessInfoHeight + 10;
		}

		var filterRect = new RectangleF(20, y, contentWidth, 24);
		graphics.DrawRectangle(new PdfSolidBrush(PdfColor.FromArgb(255, 255, 255, 255)), filterRect);
		graphics.DrawRectangle(new PdfPen(PdfColor.FromArgb(255, 226, 232, 240), 1), filterRect);
		graphics.DrawString($"Source: {report.SourceFilter}  |  Order Type: {report.OrderTypeFilter}  |  Search: {(string.IsNullOrWhiteSpace(report.SearchText) ? "All" : report.SearchText)}", metaFont, PdfBrushes.DarkSlateGray, new PdfPointF(28, y + 7));

		y += 34;

		var metricWidth = (contentWidth - 40) / 6;
		DrawMetricBlock(graphics, "Orders", report.Summary.OrderCount.ToString(CultureInfo.InvariantCulture), 20, y, metricWidth);
		DrawMetricBlock(graphics, "Gross", $"£{report.Summary.GrossSales:F2}", 20 + (metricWidth + 8), y, metricWidth);
		DrawMetricBlock(graphics, "Net", $"£{report.Summary.NetSales:F2}", 20 + ((metricWidth + 8) * 2), y, metricWidth);
		DrawMetricBlock(graphics, "VAT", $"£{report.Summary.VatAmount:F2}", 20 + ((metricWidth + 8) * 3), y, metricWidth);
		DrawMetricBlock(graphics, "Delivery", $"£{report.Summary.DeliveryChargeTotal:F2}", 20 + ((metricWidth + 8) * 4), y, metricWidth);
		DrawMetricBlock(graphics, "AOV", $"£{report.Summary.AverageOrderValue:F2}", 20 + ((metricWidth + 8) * 5), y, metricWidth);
		y += 72;
		graphics.DrawString(
			$"Service charges £{report.Summary.ServiceChargeTotal:F2}  |  Removed £{report.Summary.RemovedServiceChargeValue:F2} ({report.Summary.RemovedServiceChargeCount})  |  Cash tips £{report.Summary.CashTips:F2}  |  Card tips £{report.Summary.CardTips:F2}",
			smallFont,
			PdfBrushes.Black,
			new PdfPointF(20, y));
		y += 16;
		graphics.DrawString(
			$"Delivery fees £{report.Summary.DeliveryChargeTotal:F2}  |  Refunds £{Math.Abs(report.Summary.RefundTotal):F2}  |  Final money collected £{report.Summary.FinalMoneyCollected:F2}",
			smallFont,
			PdfBrushes.Black,
			new PdfPointF(20, y));
		y += 24;

		graphics.DrawString("Top Items", headingFont, PdfBrushes.Black, new PdfPointF(20, y));
		y += 16;
		foreach (var item in report.TopItems.Take(8))
		{
			graphics.DrawString($"{item.CategoryName} / {item.ItemName}  •  Qty {item.TotalQuantity}  •  Gross £{item.GrossSales:F2}  •  VAT £{item.VatAmount:F2}", normalFont, PdfBrushes.Black, new PdfPointF(24, y));
			y += 14;
		}

		y += 10;
		graphics.DrawString("Recent Orders", headingFont, PdfBrushes.Black, new PdfPointF(20, y));
		y += 16;
		foreach (var order in report.Orders.Take(10))
		{
			var line = $"{order.CreatedAtDisplay}  |  {order.OrderNumber}  |  {order.CustomerName}  |  {order.OrderMeta}  |  £{order.GrossSales:F2}";
			graphics.DrawString(line, smallFont, PdfBrushes.Black, new PdfPointF(24, y));
			y += 12;
		}

		if (report.ServiceChargeRemovalAudits.Count > 0)
		{
			y += 10;
			graphics.DrawString("Service Charge Removal Audit", headingFont, PdfBrushes.Black, new PdfPointF(20, y));
			y += 16;
			foreach (var audit in report.ServiceChargeRemovalAudits.Take(8))
			{
				var line = $"{audit.EventAt:dd MMM HH:mm} | {audit.OrderNumber} | £{audit.Amount:F2} | {audit.Reason} | Approved: {audit.ApprovedBy}";
				graphics.DrawString(line, smallFont, PdfBrushes.Black, new PdfPointF(24, y));
				y += 12;
			}
		}

		await using var stream = File.Create(filePath);
		document.Save(stream);

		return filePath;
	}

	private static List<string> BuildBusinessInfoLines(BusinessInfo? businessInfo)
	{
		var lines = new List<string>();
		if (businessInfo == null)
		{
			return lines;
		}

		var addressParts = new[]
		{
			businessInfo.Address,
			businessInfo.City,
			businessInfo.County,
			businessInfo.Postcode,
			businessInfo.Country
		}
		.Where(part => !string.IsNullOrWhiteSpace(part))
		.Select(part => part.Trim())
		.ToArray();

		if (addressParts.Length > 0)
		{
			lines.Add($"Address: {string.Join(", ", addressParts)}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.PhoneNumber))
		{
			lines.Add($"Phone: {businessInfo.PhoneNumber.Trim()}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.Email))
		{
			lines.Add($"Email: {businessInfo.Email.Trim()}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.Website))
		{
			lines.Add($"Website: {businessInfo.Website.Trim()}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.VATNumber))
		{
			lines.Add($"VAT Number: {businessInfo.VATNumber.Trim()}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.TaxCode))
		{
			lines.Add($"Tax Code: {businessInfo.TaxCode.Trim()}");
		}

		return lines;
	}

	private static string GetReportOutputFolder()
	{
		return Path.Combine(FileSystem.Current.AppDataDirectory, "POS_Reports");
	}

	private async Task<ReportSummary> LoadSummaryAsync(
		MySqlConnection connection,
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		var query = new StringBuilder(@"
			SELECT
				COUNT(DISTINCT order_db_id) AS order_count,
				COALESCE(SUM(line_gross), 0) AS gross_sales,
				COALESCE(SUM(line_net), 0) AS net_sales,
				COALESCE(SUM(line_vat), 0) AS vat_amount
			FROM vw_report_order_lines_live
			WHERE created_at >= @startDate AND created_at < @endDate");

		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);

		await using var reader = await command.ExecuteReaderAsync();
		if (await reader.ReadAsync())
		{
			var orderCount = GetInt32(reader, "order_count");
			var grossSales = GetDecimal(reader, "gross_sales");
			var netSales = GetDecimal(reader, "net_sales");
			var vatAmount = GetDecimal(reader, "vat_amount");

			return new ReportSummary
			{
				OrderCount = orderCount,
				GrossSales = grossSales,
				NetSales = netSales,
				VatAmount = vatAmount,
				AverageOrderValue = orderCount > 0 ? grossSales / orderCount : 0m
			};
		}

		return new ReportSummary();
	}

	private async Task<List<ReportOrderRow>> LoadOrdersAsync(
		MySqlConnection connection,
		DateTime startDate,
		DateTime endDate,
		string searchText,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		var query = new StringBuilder(@"
			SELECT
				o.order_db_id,
				o.order_id,
				o.order_number,
				o.created_at,
				o.source_channel,
				o.order_type,
				o.status,
				o.customer_name,
				o.customer_phone,
				COALESCE(order_lines.item_count, 0) AS item_count,
				COALESCE(order_lines.gross_sales, 0) AS gross_sales,
				COALESCE(order_lines.net_sales, 0) AS net_sales,
				COALESCE(order_lines.vat_amount, 0) AS vat_amount
			FROM vw_report_orders_live o
			LEFT JOIN (
				SELECT
					order_db_id,
					COUNT(*) AS item_count,
					ROUND(SUM(line_gross), 2) AS gross_sales,
					ROUND(SUM(line_net), 2) AS net_sales,
					ROUND(SUM(line_vat), 2) AS vat_amount
				FROM vw_report_order_lines_live
				GROUP BY order_db_id
			) order_lines ON order_lines.order_db_id = o.order_db_id
			WHERE o.created_at >= @startDate AND o.created_at < @endDate");

		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);

		if (!string.IsNullOrWhiteSpace(searchText))
		{
			query.Append(@"
				AND (
					o.order_number LIKE @searchText
					OR o.customer_name LIKE @searchText
					OR o.customer_phone LIKE @searchText
					OR o.order_id LIKE @searchText
				)");
		}

		query.Append(" ORDER BY o.created_at DESC LIMIT 500");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			command.Parameters.AddWithValue("@searchText", $"%{searchText.Trim()}%");
		}

		var orders = new List<ReportOrderRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			orders.Add(new ReportOrderRow
			{
				OrderDbId = GetInt32(reader, "order_db_id"),
				OrderId = GetString(reader, "order_id"),
				OrderNumber = GetString(reader, "order_number"),
				CreatedAt = GetDateTime(reader, "created_at"),
				SourceChannel = GetString(reader, "source_channel"),
				OrderType = GetString(reader, "order_type"),
				Status = GetString(reader, "status"),
				CustomerName = GetString(reader, "customer_name"),
				CustomerPhone = GetString(reader, "customer_phone"),
				ItemCount = GetInt32(reader, "item_count"),
				GrossSales = GetDecimal(reader, "gross_sales"),
				NetSales = GetDecimal(reader, "net_sales"),
				VatAmount = GetDecimal(reader, "vat_amount")
			});
		}

		return orders;
	}

	public async Task<ReportOrderDetail?> GetOrderDetailAsync(int orderDbId)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		var detail = await LoadOrderHeaderAsync(connection, orderDbId);
		if (detail == null)
		{
			return null;
		}

		detail.Lines = await LoadOrderLinesAsync(connection, orderDbId);
		return detail;
	}

	private async Task<List<ReportTopItemRow>> LoadTopItemsAsync(
		MySqlConnection connection,
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		var query = new StringBuilder(@"
			SELECT
				display_name AS item_name,
				SUM(quantity) AS total_quantity,
				ROUND(SUM(line_gross), 2) AS gross_sales,
				ROUND(SUM(line_net), 2) AS net_sales,
				ROUND(SUM(line_vat), 2) AS vat_amount
			FROM vw_report_order_lines_live
			WHERE created_at >= @startDate AND created_at < @endDate");

		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);
		query.Append(@"
			GROUP BY display_name
			ORDER BY total_quantity DESC, gross_sales DESC
			LIMIT 20");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);

		var items = new List<ReportTopItemRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			items.Add(new ReportTopItemRow
			{
				ItemName = GetString(reader, "item_name"),
				TotalQuantity = GetInt32(reader, "total_quantity"),
				GrossSales = GetDecimal(reader, "gross_sales"),
				NetSales = GetDecimal(reader, "net_sales"),
				VatAmount = GetDecimal(reader, "vat_amount")
			});
		}

		return items;
	}

	private async Task<List<ReportTopItemRow>> LoadTopSellItemsAsync(
		MySqlConnection connection,
		DateTime startDate,
		DateTime endDate,
		TopSellSection section,
		string searchText)
	{
		var drinkKeywords = new[]
		{
			"drink", "beverage", "juice", "coffee", "tea", "water", "soda", "cola",
			"milkshake", "shake", "smoothie", "latte", "espresso", "cappuccino",
			"hot chocolate", "beer", "wine", "cider", "mocktail", "cocktail",
			"soft drink", "bottle", "bottled"
		};

		const string categoryExpression = "CASE WHEN l.menu_item_id LIKE 'tasting:%' THEN _utf8mb4'Tasting Menus' COLLATE utf8mb4_unicode_ci ELSE COALESCE(fmc.Name, _utf8mb4'Uncategorized' COLLATE utf8mb4_unicode_ci) END";
		const string normalizedText = "LOWER(CONCAT(COALESCE(fmc.Name, ''), ' ', COALESCE(l.display_name, l.item_name, ''))) COLLATE utf8mb4_unicode_ci";
		var drinkFilter = string.Join(" OR ", drinkKeywords.Select(word => $"{normalizedText} LIKE '%{word}%'"));
		const string itemTypeText = "LOWER(COALESCE(fmi.ItemType, ''))";

		var query = new StringBuilder(@"
			SELECT
				" + categoryExpression + @" AS category_name,
				l.display_name AS item_name,
				SUM(l.quantity) AS total_quantity,
				ROUND(SUM(l.line_gross), 2) AS gross_sales,
				ROUND(SUM(l.line_net), 2) AS net_sales,
				ROUND(SUM(l.line_vat), 2) AS vat_amount
			FROM vw_report_order_lines_live l
			LEFT JOIN FoodMenuItems fmi ON CONVERT(fmi.Id USING utf8mb4) COLLATE utf8mb4_unicode_ci = CONVERT(l.menu_item_id USING utf8mb4) COLLATE utf8mb4_unicode_ci
			LEFT JOIN FoodMenuCategories fmc ON CONVERT(fmc.Id USING utf8mb4) COLLATE utf8mb4_unicode_ci = CONVERT(fmi.CategoryId USING utf8mb4) COLLATE utf8mb4_unicode_ci
			WHERE l.created_at >= @startDate AND l.created_at < @endDate");

		query.Append(section == TopSellSection.Drink
			? $@"
				AND l.menu_item_id NOT LIKE 'tasting:%'
				AND ({itemTypeText} = 'drink' OR ({itemTypeText} = '' AND ({drinkFilter})))"
			: $@"
				AND (l.menu_item_id LIKE 'tasting:%' OR {itemTypeText} = 'food' OR ({itemTypeText} = '' AND NOT ({drinkFilter})))");

		if (!string.IsNullOrWhiteSpace(searchText))
		{
			query.Append(@"
				AND (
					l.item_name COLLATE utf8mb4_unicode_ci LIKE @searchText
					OR l.display_name COLLATE utf8mb4_unicode_ci LIKE @searchText
					OR fmc.Name COLLATE utf8mb4_unicode_ci LIKE @searchText
				)");
		}

		query.Append(@"
			GROUP BY category_name, l.menu_item_id, l.variant_id, l.display_name
			ORDER BY total_quantity DESC, gross_sales DESC
			LIMIT 30");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			command.Parameters.AddWithValue("@searchText", $"%{searchText.Trim()}%");
		}

		var items = new List<ReportTopItemRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			items.Add(new ReportTopItemRow
			{
				CategoryName = GetString(reader, "category_name"),
				ItemName = GetString(reader, "item_name"),
				TotalQuantity = GetInt32(reader, "total_quantity"),
				GrossSales = GetDecimal(reader, "gross_sales"),
				NetSales = GetDecimal(reader, "net_sales"),
				VatAmount = GetDecimal(reader, "vat_amount")
			});
		}

		return items;
	}

	private async Task<ReportOrderDetail?> LoadOrderHeaderAsync(MySqlConnection connection, int orderDbId)
	{
		var query = @"
			SELECT
				id,
				order_id,
				order_number,
				cloud_order_id,
				created_at,
				source_channel,
				order_type,
				status,
				customer_name,
				customer_phone,
				customer_email,
				customer_address,
				payment_method,
				special_instructions,
				subtotal_amount,
				delivery_fee,
				tax_amount,
				total_amount
			FROM orders
			WHERE id = @orderDbId";

		await using var command = new MySqlCommand(query, connection);
		command.Parameters.AddWithValue("@orderDbId", orderDbId);

		await using var reader = await command.ExecuteReaderAsync();
		if (!await reader.ReadAsync())
		{
			return null;
		}

		return new ReportOrderDetail
		{
			OrderDbId = GetInt32(reader, "id"),
			OrderId = GetString(reader, "order_id"),
			OrderNumber = GetString(reader, "order_number"),
			CloudOrderId = GetString(reader, "cloud_order_id"),
			CreatedAt = GetDateTime(reader, "created_at"),
			SourceChannel = GetString(reader, "source_channel"),
			OrderType = GetString(reader, "order_type"),
			Status = GetString(reader, "status"),
			CustomerName = GetString(reader, "customer_name"),
			CustomerPhone = GetString(reader, "customer_phone"),
			CustomerEmail = GetString(reader, "customer_email"),
			CustomerAddress = GetString(reader, "customer_address"),
			PaymentMethod = GetString(reader, "payment_method"),
			SpecialInstructions = GetString(reader, "special_instructions"),
			SubtotalAmount = GetDecimal(reader, "subtotal_amount"),
			DeliveryFee = GetDecimal(reader, "delivery_fee"),
			TaxAmount = GetDecimal(reader, "tax_amount"),
			TotalAmount = GetDecimal(reader, "total_amount")
		};
	}

	private async Task<List<ReportOrderLineDetail>> LoadOrderLinesAsync(MySqlConnection connection, int orderDbId)
	{
		var query = @"
			SELECT
				oi.id AS order_item_id,
				COALESCE(order_lines.display_name, oi.display_name, oi.item_name) AS item_name,
				oi.quantity,
				COALESCE(oi.item_price, 0.00) AS unit_price,
				COALESCE(oi.special_instructions, '') AS special_instructions,
				COALESCE(addons.addon_unit_total, 0.00) AS addon_unit_total,
				COALESCE(addons.addon_summary, '') AS addon_summary,
				COALESCE(order_lines.line_gross, 0.00) AS line_gross,
				COALESCE(order_lines.line_net, 0.00) AS line_net,
				COALESCE(order_lines.line_vat, 0.00) AS line_vat
			FROM order_items oi
			LEFT JOIN (
				SELECT
					order_item_id,
					SUM(COALESCE(addon_price, 0.00) * COALESCE(quantity, 1)) AS addon_unit_total,
					GROUP_CONCAT(CONCAT(COALESCE(addon_name, ''), ' x', COALESCE(quantity, 1)) ORDER BY id SEPARATOR ', ') AS addon_summary
				FROM order_item_addons
				GROUP BY order_item_id
			) addons ON addons.order_item_id = oi.id
			LEFT JOIN vw_report_order_lines_live order_lines ON order_lines.order_item_id = oi.id
			WHERE oi.order_id = @orderDbId
			ORDER BY oi.id";

		await using var command = new MySqlCommand(query, connection);
		command.Parameters.AddWithValue("@orderDbId", orderDbId);

		var items = new List<ReportOrderLineDetail>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			items.Add(new ReportOrderLineDetail
			{
				OrderItemId = GetInt32(reader, "order_item_id"),
				ItemName = GetString(reader, "item_name"),
				Quantity = GetInt32(reader, "quantity"),
				UnitPrice = GetDecimal(reader, "unit_price"),
				AddonUnitTotal = GetDecimal(reader, "addon_unit_total"),
				AddonSummary = GetString(reader, "addon_summary"),
				LineGross = GetDecimal(reader, "line_gross"),
				LineNet = GetDecimal(reader, "line_net"),
				LineVat = GetDecimal(reader, "line_vat"),
				SpecialInstructions = GetString(reader, "special_instructions")
			});
		}

		return items;
	}

	private static void AppendOptionalFilters(StringBuilder query, ReportSourceFilter sourceFilter, ReportOrderTypeFilter orderTypeFilter)
	{
		if (sourceFilter != ReportSourceFilter.All)
		{
			query.Append(" AND source_channel = @sourceChannel");
		}

		if (orderTypeFilter != ReportOrderTypeFilter.All)
		{
			query.Append(" AND order_type = @orderType");
		}
	}

	private static void AddOptionalParameters(MySqlCommand command, ReportSourceFilter sourceFilter, ReportOrderTypeFilter orderTypeFilter)
	{
		if (sourceFilter != ReportSourceFilter.All)
		{
			command.Parameters.AddWithValue("@sourceChannel", sourceFilter.ToString().ToLowerInvariant());
		}

		if (orderTypeFilter != ReportOrderTypeFilter.All)
		{
			command.Parameters.AddWithValue("@orderType", orderTypeFilter.ToString().ToLowerInvariant());
		}
	}

	private async Task EnsureLiveReportViewsAsync()
	{
		if (RuntimeSchemaPolicy.IsMigrationManaged)
		{
			_viewSchemaReady = true;
			return;
		}
		if (_viewSchemaReady)
		{
			return;
		}

		await _viewSchemaLock.WaitAsync();
		try
		{
			if (_viewSchemaReady)
			{
				return;
			}

				await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
				await connection.OpenAsync();

				await using (var ensureOrderItemColumnsCommand = new MySqlCommand(@"
					ALTER TABLE order_items
					ADD COLUMN IF NOT EXISTS variant_id VARCHAR(100) NULL,
					ADD COLUMN IF NOT EXISTS variant_name VARCHAR(100) NULL,
					ADD COLUMN IF NOT EXISTS display_name VARCHAR(180) NULL", connection))
				{
					await ensureOrderItemColumnsCommand.ExecuteNonQueryAsync();
				}

				await using (var command = new MySqlCommand(@"
					CREATE OR REPLACE VIEW vw_report_order_lines_live AS
				SELECT
					o.id AS order_db_id,
					o.order_id,
					o.order_number,
					o.cloud_order_id,
					o.created_at,
					DATE(DATE_SUB(o.created_at, INTERVAL 1 HOUR)) AS business_date,
					o.source_channel,
					o.order_type,
					o.status,
					o.customer_name,
						o.customer_phone,
						oi.id AS order_item_id,
						oi.menu_item_id,
						oi.variant_id,
						oi.variant_name,
						COALESCE(
							NULLIF(oi.display_name, ''),
							NULLIF(CONCAT(oi.item_name, CASE WHEN COALESCE(oi.variant_name, '') <> '' THEN CONCAT(' (', oi.variant_name, ')') ELSE '' END), ''),
							oi.item_name
						) AS display_name,
						oi.item_name,
						oi.quantity,
					COALESCE(oi.item_price, 0.00) AS unit_price,
					COALESCE(addons.addon_unit_total, 0.00) AS addon_unit_total,
					(COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) AS line_unit_gross,
					(COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0) AS line_gross,
					COALESCE(o.total_amount, 0.00) AS order_gross_total,
					COALESCE(o.tax_amount, 0.00) AS order_tax_total,
					CASE
						WHEN COALESCE(o.total_amount, 0.00) > 0 AND COALESCE(o.tax_amount, 0.00) > 0
							THEN ROUND(((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0))
								 / COALESCE(o.total_amount, 1.00) * COALESCE(o.tax_amount, 0.00), 4)
						ELSE 0.0000
					END AS line_vat,
					CASE
						WHEN COALESCE(o.total_amount, 0.00) > 0 AND COALESCE(o.tax_amount, 0.00) > 0
							THEN ROUND(((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0))
								 - (((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0))
								 / COALESCE(o.total_amount, 1.00) * COALESCE(o.tax_amount, 0.00)), 4)
						ELSE ROUND((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0), 4)
					END AS line_net
				FROM orders o
				INNER JOIN order_items oi ON oi.order_id = o.id
				LEFT JOIN (
					SELECT
						order_item_id,
						SUM(COALESCE(addon_price, 0.00) * COALESCE(quantity, 1)) AS addon_unit_total
					FROM order_item_addons
					GROUP BY order_item_id
				) addons ON addons.order_item_id = oi.id
					WHERE LOWER(COALESCE(o.status, '')) NOT IN ('cancelled', 'voided')
					  AND COALESCE(LOWER(o.local_lifecycle_state), '') <> 'voided'
					  AND (
						LOWER(COALESCE(o.local_lifecycle_state, '')) = 'paid'
						OR LOWER(COALESCE(o.status, '')) IN ('completed', 'paid', 'closed')
						OR o.paid_at IS NOT NULL
						OR EXISTS (
							SELECT 1
							FROM order_payments op
							WHERE op.order_id = o.id
							  AND LOWER(COALESCE(op.status, '')) = 'approved'
						)
					  )", connection))
			{
				await command.ExecuteNonQueryAsync();
			}

			await using (var command = new MySqlCommand(@"
				CREATE OR REPLACE VIEW vw_report_orders_live AS
				SELECT
					o.id AS order_db_id,
					o.order_id,
					o.order_number,
					o.cloud_order_id,
					o.created_at,
					DATE(DATE_SUB(o.created_at, INTERVAL 1 HOUR)) AS business_date,
					o.source_channel,
					o.order_type,
					o.status,
					o.customer_name,
					o.customer_phone,
					COALESCE(o.subtotal_amount, 0.00) AS subtotal_amount,
					COALESCE(o.tax_amount, 0.00) AS tax_amount,
					COALESCE(o.delivery_fee, 0.00) AS delivery_fee,
					COALESCE(o.total_amount, 0.00) AS total_amount,
					COALESCE(o.payment_method, '') AS payment_method
					FROM orders o
					WHERE LOWER(COALESCE(o.status, '')) NOT IN ('cancelled', 'voided')
					  AND COALESCE(LOWER(o.local_lifecycle_state), '') <> 'voided'
					  AND (
						LOWER(COALESCE(o.local_lifecycle_state, '')) = 'paid'
						OR LOWER(COALESCE(o.status, '')) IN ('completed', 'paid', 'closed')
						OR o.paid_at IS NOT NULL
						OR EXISTS (
							SELECT 1
							FROM order_payments op
							WHERE op.order_id = o.id
							  AND LOWER(COALESCE(op.status, '')) = 'approved'
						)
					  )", connection))
			{
				await command.ExecuteNonQueryAsync();
			}

			_viewSchemaReady = true;
		}
		finally
		{
			_viewSchemaLock.Release();
		}
	}

	private static string EscapeCsv(string? value)
	{
		var text = value ?? string.Empty;
		if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
		{
			return $"\"{text.Replace("\"", "\"\"")}\"";
		}

		return text;
	}

	private static string GetString(MySqlDataReader reader, string columnName)
	{
		var ordinal = reader.GetOrdinal(columnName);
		return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
	}

	private static int GetInt32(MySqlDataReader reader, string columnName)
	{
		var ordinal = reader.GetOrdinal(columnName);
		return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	}

	private static decimal GetDecimal(MySqlDataReader reader, string columnName)
	{
		var ordinal = reader.GetOrdinal(columnName);
		return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	}

	private static DateTime GetDateTime(MySqlDataReader reader, string columnName)
	{
		var ordinal = reader.GetOrdinal(columnName);
		return reader.IsDBNull(ordinal) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	}

	private static void DrawMetricBlock(PdfGraphics graphics, string label, string value, float x, float y, float width = 130)
	{
		var rectangle = new RectangleF(x, y, width, 56);
		graphics.DrawRectangle(new PdfSolidBrush(PdfColor.FromArgb(255, 255, 255, 255)), rectangle);
		graphics.DrawRectangle(new PdfPen(PdfColor.FromArgb(255, 226, 232, 240), 1), rectangle);

		var labelFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
		var valueFont = new PdfStandardFont(PdfFontFamily.Helvetica, 16, PdfFontStyle.Bold);
		graphics.DrawString(label, labelFont, PdfBrushes.DarkSlateGray, new PdfPointF(x + 10, y + 10));
		graphics.DrawString(value, valueFont, PdfBrushes.Black, new PdfPointF(x + 10, y + 28));
	}
}
