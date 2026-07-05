using MySqlConnector;
using POS_in_NET.Models;
using System.Globalization;

namespace POS_in_NET.Services;

public sealed class ZReportService
{
    private readonly DatabaseService _databaseService;
    private readonly DailyReportService _dailyReportService;
    private readonly TillExpenseService _tillExpenseService;
    private readonly DiscountAuditService _discountAuditService;
    private readonly BusinessSettingsService _businessSettingsService;
    private static bool _auditSchemaEnsured;

    public ZReportService(
        DatabaseService databaseService,
        DailyReportService dailyReportService,
        TillExpenseService tillExpenseService,
        DiscountAuditService discountAuditService,
        BusinessSettingsService businessSettingsService)
    {
        _databaseService = databaseService;
        _dailyReportService = dailyReportService;
        _tillExpenseService = tillExpenseService;
        _discountAuditService = discountAuditService;
        _businessSettingsService = businessSettingsService;
    }

    public async Task EnsureAuditSchemaAsync()
    {
        if (_auditSchemaEnsured)
        {
            return;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS z_report_print_log (
                id INT PRIMARY KEY AUTO_INCREMENT,
                report_date DATE NOT NULL,
                printed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                printed_by_user_id INT NULL,
                printed_by_name VARCHAR(150) NULL,
                printer_name VARCHAR(120) NULL,
                terminal_name VARCHAR(120) NULL,
                is_reprint TINYINT(1) NOT NULL DEFAULT 0,
                include_detail TINYINT(1) NOT NULL DEFAULT 0,
                order_count INT NOT NULL DEFAULT 0,
                gross_sales DECIMAL(10,2) NOT NULL DEFAULT 0,
                success TINYINT(1) NOT NULL DEFAULT 1,
                error_message VARCHAR(500) NULL,
                report_reference VARCHAR(40) NULL,
                INDEX idx_z_report_print_date (report_date, printed_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
        await command.ExecuteNonQueryAsync();
        _auditSchemaEnsured = true;
    }

    public async Task<ZReportSnapshot> GetSummaryAsync(DateTime reportDate, string? printedByName = null, bool includeTopItems = true)
    {
        var start = reportDate.Date;
        var end = start.AddDays(1);

        var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
        var dailyReport = await _dailyReportService.GetReportAsync(start, start);
        var yesterdayReport = await _dailyReportService.GetReportAsync(start.AddDays(-1), start.AddDays(-1));
        var tillSummary = await _tillExpenseService.GetSummaryAsync(start, end);
        var discountEntries = await _discountAuditService.GetAuditEntriesAsync(start, end);
        var analytics = await _dailyReportService.GetOperationalAnalyticsAsync(start, start);
        var payments = await LoadPaymentBreakdownAsync(start, end);
        var channels = await LoadChannelBreakdownAsync(start, end);
        var orderTypes = await LoadOrderTypeBreakdownAsync(start, end);
        var lastCashCount = await LoadLatestCashCountAsync(start, end);
        var openingFloat = await LoadOpeningTillFloatAsync();

        var snapshot = new ZReportSnapshot
        {
            ReportDate = start,
            GeneratedAt = DateTime.Now,
            BusinessName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
                ? "Restaurant POS"
                : businessInfo.RestaurantName.Trim(),
            TerminalName = TerminalConfigurationService.IsConfigured
                ? TerminalConfigurationService.GetConfiguration().TerminalName
                : "Main Terminal",
            PrintedByName = printedByName ?? string.Empty,
            OrderCount = dailyReport.Summary.OrderCount,
            GrossSales = dailyReport.Summary.GrossSales,
            NetSales = dailyReport.Summary.NetSales,
            VatAmount = dailyReport.Summary.VatAmount,
            AverageOrderValue = dailyReport.Summary.AverageOrderValue,
            CashTotal = payments.CashTotal,
            CashTransactionCount = payments.CashCount,
            CardTotal = payments.CardTotal,
            CardTransactionCount = payments.CardCount,
            GiftCardTotal = payments.GiftCardTotal,
            GiftCardTransactionCount = payments.GiftCardCount,
            TipsTotal = payments.TipsTotal,
            RefundTotal = payments.RefundTotal,
            RefundCount = payments.RefundCount,
            PosOrderCount = channels.LocalCount,
            PosGrossSales = channels.LocalGross,
            OnlineOrderCount = channels.WebCount,
            OnlineGrossSales = channels.WebGross,
            TableOrderCount = orderTypes.TableCount,
            TableGrossSales = orderTypes.TableGross,
            DeliveryOrderCount = orderTypes.DeliveryCount,
            DeliveryGrossSales = orderTypes.DeliveryGross,
            PickupOrderCount = orderTypes.PickupCount,
            PickupGrossSales = orderTypes.PickupGross,
            TillNetOut = tillSummary.TotalNetOut,
            TillShoppingNet = tillSummary.ShoppingNet,
            TillDeliveryTotal = tillSummary.DeliveryTotal,
            TillOtherTotal = tillSummary.OtherTotal,
            TillCashReturned = tillSummary.CashReturnTotal,
            TillPendingShoppingCount = tillSummary.PendingShoppingCount,
            TillPendingShoppingTotal = tillSummary.PendingShoppingTotal,
            LastCashCountAmount = lastCashCount?.Amount,
            LastCashCountAt = lastCashCount?.CountedAt,
            DiscountTotal = discountEntries
                .Where(e => string.Equals(e.Action, "applied", StringComparison.OrdinalIgnoreCase))
                .Sum(e => e.DiscountAmount),
            DiscountEventCount = discountEntries.Count,
            VoidCount = analytics.VoidAudits.Count,
            OpeningTillFloat = openingFloat,
            YesterdayGrossSales = yesterdayReport.Summary.GrossSales,
            ReportReference = $"ZR-{start:yyyyMMdd}-{DateTime.Now:HHmmss}"
        };

        if (yesterdayReport.Summary.GrossSales > 0)
        {
            snapshot.SalesVsYesterdayPercent =
                ((snapshot.GrossSales - yesterdayReport.Summary.GrossSales) / yesterdayReport.Summary.GrossSales) * 100m;
        }

        snapshot.ExpectedCashInDrawer = openingFloat
            + snapshot.CashTotal
            + snapshot.TipsTotal
            - snapshot.TillNetOut
            - snapshot.RefundTotal;

        if (snapshot.LastCashCountAmount.HasValue)
        {
            snapshot.CashCountVariance = snapshot.LastCashCountAmount.Value - snapshot.ExpectedCashInDrawer;
        }

        if (includeTopItems)
        {
            snapshot.TopItems = dailyReport.TopItems
                .Take(5)
                .Select(item => new ZReportTopItemRow
                {
                    ItemName = item.ItemName,
                    Quantity = item.TotalQuantity,
                    GrossSales = item.GrossSales
                })
                .ToList();
        }

        return snapshot;
    }

    public async Task<OrderWebDailyReportPayload> BuildInRestaurantDailyUploadAsync(DateTime reportDate)
    {
        var start = reportDate.Date;
        var end = start.AddDays(1);

        var dailyReport = await _dailyReportService.GetReportAsync(
            start,
            start,
            sourceFilter: ReportSourceFilter.Local);

        var payments = await LoadPaymentBreakdownForLocalAsync(start, end);

        return new OrderWebDailyReportPayload
        {
            ReportDate = start,
            TotalSales = dailyReport.Summary.GrossSales,
            TotalOrders = dailyReport.Summary.OrderCount,
            CashSales = payments.CashTotal,
            CardSales = payments.CardTotal
        };
    }

    public async Task LogPrintAsync(
        ZReportSnapshot snapshot,
        string? printerName,
        bool includeDetail,
        bool success,
        string? errorMessage,
        int? userId)
    {
        await EnsureAuditSchemaAsync();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO z_report_print_log
                (report_date, printed_by_user_id, printed_by_name, printer_name, terminal_name,
                 is_reprint, include_detail, order_count, gross_sales, success, error_message, report_reference)
            VALUES
                (@reportDate, @userId, @printedByName, @printerName, @terminalName,
                 @isReprint, @includeDetail, @orderCount, @grossSales, @success, @errorMessage, @reportReference)";

        command.Parameters.AddWithValue("@reportDate", snapshot.ReportDate.Date);
        command.Parameters.AddWithValue("@userId", userId.HasValue ? userId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@printedByName", string.IsNullOrWhiteSpace(snapshot.PrintedByName) ? DBNull.Value : snapshot.PrintedByName);
        command.Parameters.AddWithValue("@printerName", string.IsNullOrWhiteSpace(printerName) ? DBNull.Value : printerName);
        command.Parameters.AddWithValue("@terminalName", string.IsNullOrWhiteSpace(snapshot.TerminalName) ? DBNull.Value : snapshot.TerminalName);
        command.Parameters.AddWithValue("@isReprint", snapshot.IsReprint);
        command.Parameters.AddWithValue("@includeDetail", includeDetail);
        command.Parameters.AddWithValue("@orderCount", snapshot.OrderCount);
        command.Parameters.AddWithValue("@grossSales", snapshot.GrossSales);
        command.Parameters.AddWithValue("@success", success);
        command.Parameters.AddWithValue("@errorMessage", string.IsNullOrWhiteSpace(errorMessage) ? DBNull.Value : errorMessage);
        command.Parameters.AddWithValue("@reportReference", snapshot.ReportReference);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<PaymentBreakdown> LoadPaymentBreakdownAsync(DateTime start, DateTime end)
    {
        var breakdown = new PaymentBreakdown();

        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    op.payment_method,
                    COUNT(*) AS txn_count,
                    COALESCE(SUM(op.amount), 0) AS amount_total,
                    COALESCE(SUM(op.tip_amount), 0) AS tip_total
                FROM order_payments op
                INNER JOIN orders o ON o.id = op.order_id
                WHERE op.status = 'approved'
                  AND o.created_at >= @startDate
                  AND o.created_at < @endDate
                GROUP BY op.payment_method";

            command.Parameters.AddWithValue("@startDate", start);
            command.Parameters.AddWithValue("@endDate", end);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ApplyPaymentRow(breakdown, reader);
            }
        }
        catch
        {
            // Fall back to order-level payment methods if payment lines are unavailable.
        }

        if (breakdown.CashTotal == 0 && breakdown.CardTotal == 0 && breakdown.GiftCardTotal == 0)
        {
            breakdown = await LoadPaymentBreakdownFromOrdersAsync(start, end);
        }

        return breakdown;
    }

    private async Task<PaymentBreakdown> LoadPaymentBreakdownForLocalAsync(DateTime start, DateTime end)
    {
        var breakdown = new PaymentBreakdown();

        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    op.payment_method,
                    COUNT(*) AS txn_count,
                    COALESCE(SUM(op.amount), 0) AS amount_total,
                    COALESCE(SUM(op.tip_amount), 0) AS tip_total
                FROM order_payments op
                INNER JOIN orders o ON o.id = op.order_id
                WHERE op.status = 'approved'
                  AND o.created_at >= @startDate
                  AND o.created_at < @endDate
                  AND LOWER(COALESCE(NULLIF(o.source_channel, ''), 'local')) NOT IN ('web', 'online')
                GROUP BY op.payment_method";

            command.Parameters.AddWithValue("@startDate", start);
            command.Parameters.AddWithValue("@endDate", end);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ApplyPaymentRow(breakdown, reader);
            }
        }
        catch
        {
            // Fall back below.
        }

        if (breakdown.CashTotal == 0 && breakdown.CardTotal == 0)
        {
            breakdown = await LoadPaymentBreakdownFromLocalOrdersAsync(start, end);
        }

        return breakdown;
    }

    private static void ApplyPaymentRow(PaymentBreakdown breakdown, MySqlDataReader reader)
    {
        var method = reader.GetString("payment_method").ToLowerInvariant();
        var count = reader.GetInt32("txn_count");
        var amount = reader.GetDecimal("amount_total");
        var tips = reader.GetDecimal("tip_total");

        switch (method)
        {
            case "cash":
                breakdown.CashTotal += amount;
                breakdown.CashCount += count;
                breakdown.TipsTotal += tips;
                break;
            case "card":
                breakdown.CardTotal += amount;
                breakdown.CardCount += count;
                breakdown.TipsTotal += tips;
                break;
            case "gift_card":
                breakdown.GiftCardTotal += amount;
                breakdown.GiftCardCount += count;
                break;
            case "refund":
                breakdown.RefundTotal += amount;
                breakdown.RefundCount += count;
                break;
            case "tip_adjust":
                breakdown.TipsTotal += amount;
                break;
        }
    }

    private async Task<PaymentBreakdown> LoadPaymentBreakdownFromLocalOrdersAsync(DateTime start, DateTime end)
    {
        var breakdown = new PaymentBreakdown();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                LOWER(COALESCE(NULLIF(payment_method, ''), 'cash')) AS payment_method,
                COUNT(*) AS txn_count,
                COALESCE(SUM(total_amount), 0) AS amount_total
            FROM orders
            WHERE created_at >= @startDate
              AND created_at < @endDate
              AND LOWER(COALESCE(NULLIF(source_channel, ''), 'local')) NOT IN ('web', 'online')
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
              )
            GROUP BY LOWER(COALESCE(NULLIF(payment_method, ''), 'cash'))";

        command.Parameters.AddWithValue("@startDate", start);
        command.Parameters.AddWithValue("@endDate", end);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var method = reader.GetString("payment_method");
            var count = reader.GetInt32("txn_count");
            var amount = reader.GetDecimal("amount_total");

            switch (method)
            {
                case "cash":
                case "cod":
                    breakdown.CashTotal += amount;
                    breakdown.CashCount += count;
                    break;
                case "card":
                case "credit_card":
                case "debit_card":
                    breakdown.CardTotal += amount;
                    breakdown.CardCount += count;
                    break;
            }
        }

        return breakdown;
    }

    private async Task<PaymentBreakdown> LoadPaymentBreakdownFromOrdersAsync(DateTime start, DateTime end)
    {
        var breakdown = new PaymentBreakdown();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                LOWER(COALESCE(NULLIF(payment_method, ''), 'cash')) AS payment_method,
                COUNT(*) AS txn_count,
                COALESCE(SUM(total_amount), 0) AS amount_total
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
              )
            GROUP BY LOWER(COALESCE(NULLIF(payment_method, ''), 'cash'))";

        command.Parameters.AddWithValue("@startDate", start);
        command.Parameters.AddWithValue("@endDate", end);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var method = reader.GetString("payment_method");
            var count = reader.GetInt32("txn_count");
            var amount = reader.GetDecimal("amount_total");

            switch (method)
            {
                case "cash":
                case "cod":
                    breakdown.CashTotal += amount;
                    breakdown.CashCount += count;
                    break;
                case "card":
                case "credit_card":
                case "debit_card":
                case "online":
                case "online_payment":
                    breakdown.CardTotal += amount;
                    breakdown.CardCount += count;
                    break;
                case "gift_card":
                case "voucher":
                case "giftcard":
                    breakdown.GiftCardTotal += amount;
                    breakdown.GiftCardCount += count;
                    break;
            }
        }

        return breakdown;
    }

    private async Task<ChannelBreakdown> LoadChannelBreakdownAsync(DateTime start, DateTime end)
    {
        var breakdown = new ChannelBreakdown();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                LOWER(COALESCE(NULLIF(source_channel, ''), 'local')) AS source_channel,
                COUNT(*) AS order_count,
                COALESCE(SUM(total_amount), 0) AS gross_sales
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
              )
            GROUP BY LOWER(COALESCE(NULLIF(source_channel, ''), 'local'))";

        command.Parameters.AddWithValue("@startDate", start);
        command.Parameters.AddWithValue("@endDate", end);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var channel = reader.GetString("source_channel");
            var count = reader.GetInt32("order_count");
            var gross = reader.GetDecimal("gross_sales");

            if (channel is "web" or "online")
            {
                breakdown.WebCount += count;
                breakdown.WebGross += gross;
            }
            else
            {
                breakdown.LocalCount += count;
                breakdown.LocalGross += gross;
            }
        }

        return breakdown;
    }

    private async Task<OrderTypeBreakdown> LoadOrderTypeBreakdownAsync(DateTime start, DateTime end)
    {
        var breakdown = new OrderTypeBreakdown();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                LOWER(COALESCE(NULLIF(order_type, ''), 'table')) AS order_type,
                COUNT(*) AS order_count,
                COALESCE(SUM(total_amount), 0) AS gross_sales
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
              )
            GROUP BY LOWER(COALESCE(NULLIF(order_type, ''), 'table'))";

        command.Parameters.AddWithValue("@startDate", start);
        command.Parameters.AddWithValue("@endDate", end);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var orderType = reader.GetString("order_type");
            var count = reader.GetInt32("order_count");
            var gross = reader.GetDecimal("gross_sales");

            switch (orderType)
            {
                case "delivery":
                    breakdown.DeliveryCount = count;
                    breakdown.DeliveryGross = gross;
                    break;
                case "pickup":
                case "collection":
                    breakdown.PickupCount = count;
                    breakdown.PickupGross = gross;
                    break;
                default:
                    breakdown.TableCount += count;
                    breakdown.TableGross += gross;
                    break;
            }
        }

        return breakdown;
    }

    private async Task<CashCountSnapshot?> LoadLatestCashCountAsync(DateTime start, DateTime end)
    {
        await _tillExpenseService.EnsureSchemaAsync();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT counted_cash, COALESCE(settled_at, created_at) AS counted_at
            FROM till_expenses
            WHERE category = 'cash_count'
              AND voided = 0
              AND COALESCE(settled_at, created_at) >= @startDate
              AND COALESCE(settled_at, created_at) < @endDate
            ORDER BY COALESCE(settled_at, created_at) DESC, id DESC
            LIMIT 1";

        command.Parameters.AddWithValue("@startDate", start);
        command.Parameters.AddWithValue("@endDate", end);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new CashCountSnapshot
        {
            Amount = reader.IsDBNull(reader.GetOrdinal("counted_cash"))
                ? null
                : reader.GetDecimal("counted_cash"),
            CountedAt = reader.GetDateTime("counted_at")
        };
    }

    private async Task<decimal> LoadOpeningTillFloatAsync()
    {
        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var ensure = connection.CreateCommand();
            ensure.CommandText = "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS opening_till_float DECIMAL(10,2) NOT NULL DEFAULT 0";
            await ensure.ExecuteNonQueryAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(opening_till_float, 0) FROM business_info ORDER BY id ASC LIMIT 1";
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0m;
        }
    }

    private sealed class PaymentBreakdown
    {
        public decimal CashTotal { get; set; }
        public int CashCount { get; set; }
        public decimal CardTotal { get; set; }
        public int CardCount { get; set; }
        public decimal GiftCardTotal { get; set; }
        public int GiftCardCount { get; set; }
        public decimal TipsTotal { get; set; }
        public decimal RefundTotal { get; set; }
        public int RefundCount { get; set; }
    }

    private sealed class ChannelBreakdown
    {
        public int LocalCount { get; set; }
        public decimal LocalGross { get; set; }
        public int WebCount { get; set; }
        public decimal WebGross { get; set; }
    }

    private sealed class OrderTypeBreakdown
    {
        public int TableCount { get; set; }
        public decimal TableGross { get; set; }
        public int DeliveryCount { get; set; }
        public decimal DeliveryGross { get; set; }
        public int PickupCount { get; set; }
        public decimal PickupGross { get; set; }
    }

    private sealed class CashCountSnapshot
    {
        public decimal? Amount { get; set; }
        public DateTime CountedAt { get; set; }
    }
}
