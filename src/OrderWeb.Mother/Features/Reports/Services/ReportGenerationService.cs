using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class ReportGenerationService
{
    private readonly DatabaseService _databaseService;

    public ReportGenerationService(DatabaseService? databaseService = null)
    {
        _databaseService = databaseService ?? new DatabaseService();
    }

    /// <summary>
    /// Generate and store a daily report for the specified date
    /// </summary>
    public async Task<ReportSnapshot?> GenerateDailyReportAsync(DateTime date)
    {
        try
        {
            if (!TerminalRoleService.CanGenerateEndOfDayReports)
            {
                System.Diagnostics.Debug.WriteLine(" [ReportGen] Daily report skipped: end-of-day reports run on the mother terminal only.");
                return null;
            }

            System.Diagnostics.Debug.WriteLine($" [ReportGen] Generating daily report for {date:yyyy-MM-dd}");
            
            var startDate = date.Date;
            var endDate = date.Date.AddDays(1);
            
            return await GenerateReportAsync(ReportType.Daily, startDate, endDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportGen] Daily report error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generate and store a weekly report for the specified week (Monday-Sunday)
    /// </summary>
    public async Task<ReportSnapshot?> GenerateWeeklyReportAsync(DateTime weekStartDate)
    {
        try
        {
            if (!TerminalRoleService.CanGenerateEndOfDayReports)
            {
                System.Diagnostics.Debug.WriteLine(" [ReportGen] Weekly report skipped: end-of-day reports run on the mother terminal only.");
                return null;
            }

            // Normalize to Monday
            var monday = weekStartDate.Date;
            while (monday.DayOfWeek != DayOfWeek.Monday)
            {
                monday = monday.AddDays(-1);
            }
            
            var sunday = monday.AddDays(7);
            
            System.Diagnostics.Debug.WriteLine($" [ReportGen] Generating weekly report for {monday:yyyy-MM-dd} to {sunday:yyyy-MM-dd}");
            
            return await GenerateReportAsync(ReportType.Weekly, monday, sunday);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportGen] Weekly report error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generate and store a monthly report for the specified year/month
    /// </summary>
    public async Task<ReportSnapshot?> GenerateMonthlyReportAsync(int year, int month)
    {
        try
        {
            if (!TerminalRoleService.CanGenerateEndOfDayReports)
            {
                System.Diagnostics.Debug.WriteLine(" [ReportGen] Monthly report skipped: end-of-day reports run on the mother terminal only.");
                return null;
            }

            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1);
            
            System.Diagnostics.Debug.WriteLine($" [ReportGen] Generating monthly report for {startDate:yyyy-MM}");
            
            return await GenerateReportAsync(ReportType.Monthly, startDate, endDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportGen] Monthly report error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Core report generation logic
    /// </summary>
    private async Task<ReportSnapshot?> GenerateReportAsync(ReportType reportType, DateTime startDate, DateTime endDate)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();

        try
        {
            await EnsureOrderItemReportColumnsAsync(connection);

            // Check if report already exists
            var existing = await GetExistingReportAsync(connection, reportType, startDate, endDate);
            if (existing != null)
            {
                System.Diagnostics.Debug.WriteLine($" [ReportGen] Report already exists, ID: {existing.Id}");
                return existing;
            }

            var snapshot = new ReportSnapshot
            {
                Type = reportType,
                StartDate = startDate,
                EndDate = endDate,
                GeneratedAt = DateTime.Now
            };

            // Saved report snapshots are intentionally permanent until manually removed.
            // Rebuild each snapshot from the current production tables, not the old TableOrders table.
            var orders = await LoadOrdersForPeriodAsync(connection, startDate, endDate);
            var saleOrders = orders.Where(o => o.IsSale).ToList();

            PopulateSummaryMetrics(snapshot, saleOrders);
            PopulateOrderBreakdown(snapshot, saleOrders);
            await PopulatePaymentMethodsAsync(connection, snapshot, startDate, endDate, saleOrders);
            await PopulateDiscountAuditAsync(connection, snapshot, startDate, endDate);
            PopulateVoidAnalysis(snapshot, orders);
            await PopulateTopItemsAsync(connection, snapshot, startDate, endDate);
            PopulateStaffPerformance(snapshot, saleOrders);
            await PopulateVatBreakdownAsync(connection, snapshot, startDate, endDate);
            await PopulateOrderTypeAnalysisAsync(connection, snapshot, startDate, endDate);

            // Save to database
            var savedSnapshot = await SaveReportAsync(connection, snapshot);
            
            System.Diagnostics.Debug.WriteLine($" [ReportGen] Report saved, ID: {savedSnapshot?.Id}");
            
            return savedSnapshot;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportGen] Error generating report: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    private static async Task EnsureOrderItemReportColumnsAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
        await using var command = new MySqlCommand(@"
            ALTER TABLE order_items
            ADD COLUMN IF NOT EXISTS variant_id VARCHAR(100) NULL,
            ADD COLUMN IF NOT EXISTS variant_name VARCHAR(100) NULL,
            ADD COLUMN IF NOT EXISTS display_name VARCHAR(180) NULL", connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Check if report already exists
    /// </summary>
    private async Task<ReportSnapshot?> GetExistingReportAsync(MySqlConnection connection, ReportType type, DateTime startDate, DateTime endDate)
    {
        var query = @"
            SELECT Id, OrderCount, GrossSales, NetSales, VatTotal
            FROM ReportSnapshots
            WHERE ReportType = @Type 
            AND StartDate = @Start 
            AND EndDate = @End
            LIMIT 1";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Type", type.ToString());
        command.Parameters.AddWithValue("@Start", startDate);
        command.Parameters.AddWithValue("@End", endDate);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ReportSnapshot
            {
                Id = reader.GetInt32(0),
                OrderCount = reader.GetInt32(1),
                GrossSales = reader.GetDecimal(2),
                NetSales = reader.GetDecimal(3),
                VatTotal = reader.GetDecimal(4)
            };
        }

        return null;
    }

    /// <summary>
    /// Load all current-schema orders in the period.
    /// </summary>
    private async Task<List<SnapshotOrderRow>> LoadOrdersForPeriodAsync(MySqlConnection connection, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT
                id,
                order_id,
                order_number,
                created_at,
                status,
                COALESCE(local_lifecycle_state, '') AS local_lifecycle_state,
                COALESCE(source_channel, '') AS source_channel,
                COALESCE(order_type, '') AS order_type,
                COALESCE(payment_method, '') AS payment_method,
                COALESCE(subtotal_amount, 0.00) AS subtotal_amount,
                COALESCE(discount_amount, 0.00) AS discount_amount,
                COALESCE(delivery_fee, 0.00) AS delivery_fee,
                COALESCE(tax_amount, 0.00) AS tax_amount,
                COALESCE(total_amount, 0.00) AS total_amount,
                void_reason,
                voided_at,
                voided_by,
                paid_at,
                EXISTS (
                    SELECT 1
                    FROM order_payments op
                    WHERE op.order_id = orders.id
                      AND LOWER(COALESCE(op.status, '')) = 'approved'
                ) AS has_approved_payment
            FROM orders
            WHERE created_at >= @Start
              AND created_at < @End
            ORDER BY created_at";

        var orders = new List<SnapshotOrderRow>();

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Start", startDate);
        command.Parameters.AddWithValue("@End", endDate);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            orders.Add(new SnapshotOrderRow
            {
                Id = ReadInt32(reader, "id"),
                OrderId = ReadString(reader, "order_id"),
                OrderNumber = ReadString(reader, "order_number"),
                CreatedAt = ReadDateTime(reader, "created_at"),
                Status = ReadString(reader, "status"),
                LifecycleState = ReadString(reader, "local_lifecycle_state"),
                SourceChannel = ReadString(reader, "source_channel"),
                OrderType = ReadString(reader, "order_type"),
                PaymentMethod = ReadString(reader, "payment_method"),
                SubtotalAmount = ReadDecimal(reader, "subtotal_amount"),
                DiscountAmount = ReadDecimal(reader, "discount_amount"),
                DeliveryFee = ReadDecimal(reader, "delivery_fee"),
                TaxAmount = ReadDecimal(reader, "tax_amount"),
                TotalAmount = ReadDecimal(reader, "total_amount"),
                VoidReason = ReadString(reader, "void_reason"),
                VoidedAt = ReadNullableDateTime(reader, "voided_at"),
                VoidedBy = ReadString(reader, "voided_by"),
                PaidAt = ReadNullableDateTime(reader, "paid_at"),
                HasApprovedPayment = ReadInt32(reader, "has_approved_payment") > 0
            });
        }

        return orders;
    }

    private static void PopulateSummaryMetrics(ReportSnapshot snapshot, IReadOnlyCollection<SnapshotOrderRow> saleOrders)
    {
        snapshot.OrderCount = saleOrders.Count;
        snapshot.GrossSales = saleOrders.Sum(o => o.TotalAmount);
        snapshot.NetSales = saleOrders.Sum(o => Math.Max(0m, o.TotalAmount - o.TaxAmount));
        snapshot.VatTotal = saleOrders.Sum(o => o.TaxAmount);
        snapshot.AverageOrderValue = snapshot.OrderCount > 0 ? snapshot.GrossSales / snapshot.OrderCount : 0m;

        snapshot.EstimatedCogs = snapshot.NetSales * 0.35m;
        snapshot.EstimatedMargin = snapshot.NetSales - snapshot.EstimatedCogs;
        snapshot.MarginPercent = snapshot.NetSales > 0 ? (snapshot.EstimatedMargin / snapshot.NetSales) * 100 : 0;
    }

    private static void PopulateOrderBreakdown(ReportSnapshot snapshot, IReadOnlyCollection<SnapshotOrderRow> saleOrders)
    {
        snapshot.DineInOrders = saleOrders.Count(o => IsOrderType(o, "table") || IsOrderType(o, "dine_in"));
        snapshot.DeliveryOrders = saleOrders.Count(o => IsOrderType(o, "delivery"));
        snapshot.PickupOrders = saleOrders.Count(o => IsOrderType(o, "pickup") || IsOrderType(o, "collection"));
        snapshot.OnlineOrders = saleOrders.Count(o => string.Equals(o.SourceChannel, "web", StringComparison.OrdinalIgnoreCase));
    }

    private async Task PopulatePaymentMethodsAsync(
        MySqlConnection connection,
        ReportSnapshot snapshot,
        DateTime startDate,
        DateTime endDate,
        IReadOnlyCollection<SnapshotOrderRow> saleOrders)
    {
        const string paymentQuery = @"
            SELECT
                COALESCE(op.payment_method, 'unknown') AS payment_method,
                SUM(CASE WHEN op.status = 'approved' THEN 1 ELSE 0 END) AS approved_count,
                SUM(CASE WHEN op.status = 'approved' THEN COALESCE(op.amount, 0.00) ELSE 0.00 END) AS approved_amount,
                SUM(CASE WHEN op.status = 'failed' THEN 1 ELSE 0 END) AS failed_count
            FROM order_payments op
            INNER JOIN orders o ON o.id = op.order_id
            WHERE op.created_at >= @Start
              AND op.created_at < @End
              AND LOWER(COALESCE(o.status, '')) NOT IN ('cancelled', 'voided')
              AND COALESCE(LOWER(o.local_lifecycle_state), '') <> 'voided'
              AND (
                  LOWER(COALESCE(o.local_lifecycle_state, '')) = 'paid'
                  OR LOWER(COALESCE(o.status, '')) IN ('completed', 'paid', 'closed')
                  OR o.paid_at IS NOT NULL
                  OR EXISTS (
                      SELECT 1
                      FROM order_payments approved_op
                      WHERE approved_op.order_id = o.id
                        AND LOWER(COALESCE(approved_op.status, '')) = 'approved'
                  )
              )
            GROUP BY COALESCE(op.payment_method, 'unknown')
            ORDER BY approved_amount DESC";

        using var command = new MySqlCommand(paymentQuery, connection);
        command.Parameters.AddWithValue("@Start", startDate);
        command.Parameters.AddWithValue("@End", endDate);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var approvedCount = ReadInt32(reader, "approved_count");
                var failedCount = ReadInt32(reader, "failed_count");
                var totalAttempts = approvedCount + failedCount;
                var method = NormalizePaymentMethod(ReadString(reader, "payment_method"));
                var amount = ReadDecimal(reader, "approved_amount");

                if (approvedCount <= 0 && amount <= 0)
                {
                    continue;
                }

                snapshot.PaymentMethods.Add(new ReportPaymentMethod
                {
                    PaymentMethod = method,
                    TransactionCount = approvedCount,
                    TotalAmount = amount,
                    FailureCount = failedCount,
                    SuccessRate = totalAttempts > 0 ? approvedCount * 100m / totalAttempts : 100m
                });
            }
        }

        if (snapshot.PaymentMethods.Count == 0 && saleOrders.Count > 0)
        {
            foreach (var group in saleOrders.GroupBy(o => NormalizePaymentMethod(o.PaymentMethod)))
            {
                snapshot.PaymentMethods.Add(new ReportPaymentMethod
                {
                    PaymentMethod = group.Key,
                    TransactionCount = group.Count(),
                    TotalAmount = group.Sum(o => o.TotalAmount),
                    SuccessRate = 100m
                });
            }
        }

        snapshot.CashTotal = snapshot.PaymentMethods
            .Where(m => m.PaymentMethod.Contains("cash", StringComparison.OrdinalIgnoreCase))
            .Sum(m => m.TotalAmount);
        snapshot.CardTotal = snapshot.PaymentMethods
            .Where(m => m.PaymentMethod.Contains("card", StringComparison.OrdinalIgnoreCase))
            .Sum(m => m.TotalAmount);
        snapshot.MobilePayTotal = snapshot.PaymentMethods
            .Where(m => !m.PaymentMethod.Contains("cash", StringComparison.OrdinalIgnoreCase)
                     && !m.PaymentMethod.Contains("card", StringComparison.OrdinalIgnoreCase))
            .Sum(m => m.TotalAmount);
        snapshot.PaymentSuccessRate = snapshot.PaymentMethods.Count > 0
            ? snapshot.PaymentMethods.Average(m => m.SuccessRate)
            : 100m;
    }

    private async Task PopulateDiscountAuditAsync(MySqlConnection connection, ReportSnapshot snapshot, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT
                COALESCE(NULLIF(reason, ''), 'Unspecified') AS discount_reason,
                COUNT(*) AS discount_count,
                SUM(CASE WHEN event_action <> 'removed' THEN COALESCE(discount_amount, 0.00) ELSE 0.00 END) AS discount_total,
                AVG(CASE WHEN event_action <> 'removed' AND COALESCE(discount_percent, 0.00) > 0 THEN discount_percent ELSE NULL END) AS average_percent
            FROM discount_events
            WHERE event_at >= @Start
              AND event_at < @End
            GROUP BY COALESCE(NULLIF(reason, ''), 'Unspecified')
            ORDER BY discount_total DESC";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Start", startDate);
        command.Parameters.AddWithValue("@End", endDate);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var amount = ReadDecimal(reader, "discount_total");
            var count = ReadInt32(reader, "discount_count");
            if (count <= 0 || amount <= 0)
            {
                continue;
            }

            snapshot.DiscountAudits.Add(new ReportDiscountAudit
            {
                DiscountReason = ReadString(reader, "discount_reason"),
                DiscountCount = count,
                TotalDiscountAmount = amount,
                AverageDiscountPercent = ReadDecimal(reader, "average_percent")
            });
        }

        snapshot.DiscountTotal = snapshot.DiscountAudits.Sum(a => a.TotalDiscountAmount);
        snapshot.DiscountCount = snapshot.DiscountAudits.Sum(a => a.DiscountCount);
    }

    private static void PopulateVoidAnalysis(ReportSnapshot snapshot, IReadOnlyCollection<SnapshotOrderRow> orders)
    {
        var voidedOrders = orders.Where(o => o.IsVoided).ToList();
        var cancelledOrders = orders.Where(o => o.IsCancelled).ToList();

        snapshot.VoidCount = voidedOrders.Count;
        snapshot.VoidTotal = voidedOrders.Sum(o => o.TotalAmount);
        snapshot.CancelledOrderCount = cancelledOrders.Count;
    }

    private async Task PopulateVatBreakdownAsync(MySqlConnection connection, ReportSnapshot snapshot, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT
                CASE
                    WHEN SUM(line_net) > 0 THEN ROUND((SUM(line_vat) / SUM(line_net)) * 100, 2)
                    ELSE 0.00
                END AS vat_rate,
                SUM(line_net) AS taxable_amount,
                SUM(line_vat) AS vat_amount,
                SUM(quantity) AS item_count
            FROM (
                SELECT
                    oi.quantity,
                    CASE
                        WHEN COALESCE(o.total_amount, 0.00) > 0 AND COALESCE(o.tax_amount, 0.00) > 0
                            THEN ROUND(((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0)) / COALESCE(o.total_amount, 1.00) * COALESCE(o.tax_amount, 0.00), 4)
                        ELSE 0.0000
                    END AS line_vat,
                    CASE
                        WHEN COALESCE(o.total_amount, 0.00) > 0 AND COALESCE(o.tax_amount, 0.00) > 0
                            THEN ROUND(((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0)) - (((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0)) / COALESCE(o.total_amount, 1.00) * COALESCE(o.tax_amount, 0.00)), 4)
                        ELSE ROUND((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0), 4)
                    END AS line_net
                FROM orders o
                INNER JOIN order_items oi ON oi.order_id = o.id
                LEFT JOIN (
                    SELECT order_item_id, SUM(COALESCE(addon_price, 0.00) * COALESCE(quantity, 1)) AS addon_unit_total
                    FROM order_item_addons
                    GROUP BY order_item_id
                ) addons ON addons.order_item_id = oi.id
                WHERE o.created_at >= @Start
                  AND o.created_at < @End
                  AND LOWER(COALESCE(o.status, '')) NOT IN ('cancelled', 'voided')
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
                  )
            ) lines
            GROUP BY CASE
                WHEN line_net > 0 THEN ROUND((line_vat / line_net) * 100, 2)
                ELSE 0.00
            END
            ORDER BY vat_rate";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Start", startDate);
        command.Parameters.AddWithValue("@End", endDate);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var itemCount = ReadInt32(reader, "item_count");
            if (itemCount <= 0)
            {
                continue;
            }

            var rate = ReadDecimal(reader, "vat_rate");
            snapshot.VatBreakdowns.Add(new ReportVatBreakdown
            {
                VatRate = rate,
                TaxableAmount = ReadDecimal(reader, "taxable_amount"),
                VatAmount = ReadDecimal(reader, "vat_amount"),
                ItemCount = itemCount,
                VatCategoryName = GetVatCategoryName(rate)
            });
        }
    }

    private async Task PopulateTopItemsAsync(MySqlConnection connection, ReportSnapshot snapshot, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT
                oi.menu_item_id,
                oi.variant_id,
                COALESCE(NULLIF(oi.display_name, ''), NULLIF(CONCAT(oi.item_name, CASE WHEN COALESCE(oi.variant_name, '') <> '' THEN CONCAT(' (', oi.variant_name, ')') ELSE '' END), ''), oi.item_name) AS item_name,
                CASE
                    WHEN oi.menu_item_id LIKE 'tasting:%' THEN 'Tasting Menus'
                    ELSE COALESCE(fmc.Name, 'Uncategorized')
                END AS category_name,
                SUM(oi.quantity) AS total_quantity,
                SUM((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0)) AS gross_sales,
                SUM(CASE
                    WHEN COALESCE(o.total_amount, 0.00) > 0 AND COALESCE(o.tax_amount, 0.00) > 0
                        THEN ROUND(((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0)) / COALESCE(o.total_amount, 1.00) * COALESCE(o.tax_amount, 0.00), 4)
                    ELSE 0.0000
                END) AS vat_amount
            FROM orders o
            INNER JOIN order_items oi ON oi.order_id = o.id
            LEFT JOIN (
                SELECT order_item_id, SUM(COALESCE(addon_price, 0.00) * COALESCE(quantity, 1)) AS addon_unit_total
                FROM order_item_addons
                GROUP BY order_item_id
            ) addons ON addons.order_item_id = oi.id
            LEFT JOIN FoodMenuItems fmi ON CONVERT(fmi.Id USING utf8mb4) COLLATE utf8mb4_unicode_ci = CONVERT(oi.menu_item_id USING utf8mb4) COLLATE utf8mb4_unicode_ci
            LEFT JOIN FoodMenuCategories fmc ON CONVERT(fmc.Id USING utf8mb4) COLLATE utf8mb4_unicode_ci = CONVERT(fmi.CategoryId USING utf8mb4) COLLATE utf8mb4_unicode_ci
            WHERE o.created_at >= @Start
              AND o.created_at < @End
              AND LOWER(COALESCE(o.status, '')) NOT IN ('cancelled', 'voided')
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
              )
            GROUP BY oi.menu_item_id, oi.variant_id, item_name, category_name
            HAVING total_quantity > 0
            ORDER BY total_quantity DESC, gross_sales DESC
            LIMIT 20";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Start", startDate);
        command.Parameters.AddWithValue("@End", endDate);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var gross = ReadDecimal(reader, "gross_sales");
            var vat = ReadDecimal(reader, "vat_amount");
            var net = Math.Max(0m, gross - vat);
            var cogs = net * 0.35m;
            var margin = net - cogs;

            snapshot.TopItems.Add(new ReportTopItem
            {
                ItemName = ReadString(reader, "item_name"),
                CategoryName = ReadString(reader, "category_name"),
                TotalQuantity = ReadInt32(reader, "total_quantity"),
                GrossSales = gross,
                NetSales = net,
                VatAmount = vat,
                VatRate = net > 0 ? vat / net * 100 : 0m,
                EstimatedCogs = cogs,
                Margin = margin,
                MarginPercent = net > 0 ? margin / net * 100 : 0m
            });
        }
    }

    private static void PopulateStaffPerformance(ReportSnapshot snapshot, IReadOnlyCollection<SnapshotOrderRow> saleOrders)
    {
        if (saleOrders.Count == 0 && snapshot.VoidCount == 0 && snapshot.DiscountCount == 0)
        {
            return;
        }

        snapshot.StaffMetrics.Add(new ReportStaffPerformance
        {
            StaffName = "POS",
            OrdersProcessed = saleOrders.Count,
            TotalSales = saleOrders.Sum(o => o.TotalAmount),
            DiscountsApplied = snapshot.DiscountTotal,
            VoidsInitiated = snapshot.VoidCount,
            AverageOrderValue = saleOrders.Count > 0 ? saleOrders.Average(o => o.TotalAmount) : 0m,
            ErrorRate = saleOrders.Count + snapshot.VoidCount > 0
                ? snapshot.VoidCount * 100m / (saleOrders.Count + snapshot.VoidCount)
                : 0m
        });
    }

    private async Task PopulateOrderTypeAnalysisAsync(MySqlConnection connection, ReportSnapshot snapshot, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT
                COALESCE(order_type, 'pickup') AS order_type,
                SUM(CASE
                    WHEN LOWER(COALESCE(status, '')) NOT IN ('cancelled', 'voided')
                     AND COALESCE(LOWER(local_lifecycle_state), '') <> 'voided'
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
                    THEN 1 ELSE 0
                END) AS sale_count,
                SUM(CASE
                    WHEN LOWER(COALESCE(status, '')) NOT IN ('cancelled', 'voided')
                     AND COALESCE(LOWER(local_lifecycle_state), '') <> 'voided'
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
                    THEN COALESCE(total_amount, 0.00) ELSE 0.00
                END) AS sale_total,
                COUNT(*) AS total_count,
                SUM(CASE WHEN LOWER(COALESCE(status, '')) = 'cancelled' THEN 1 ELSE 0 END) AS cancelled_count
            FROM orders
            WHERE created_at >= @Start
              AND created_at < @End
            GROUP BY COALESCE(order_type, 'pickup')
            ORDER BY sale_total DESC";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Start", startDate);
        command.Parameters.AddWithValue("@End", endDate);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var saleCount = ReadInt32(reader, "sale_count");
            var totalCount = ReadInt32(reader, "total_count");
            var cancelledCount = ReadInt32(reader, "cancelled_count");
            var saleTotal = ReadDecimal(reader, "sale_total");

            snapshot.OrderTypeAnalysis.Add(new ReportOrderTypeAnalysis
            {
                OrderType = NormalizeOrderType(ReadString(reader, "order_type")),
                OrderCount = saleCount,
                TotalSales = saleTotal,
                AverageOrderValue = saleCount > 0 ? saleTotal / saleCount : 0m,
                AveragePrepTime = 0,
                CancellationRate = totalCount > 0 ? cancelledCount * 100m / totalCount : 0m
            });
        }
    }

    private static bool IsOrderType(SnapshotOrderRow order, string type)
    {
        return string.Equals(order.OrderType, type, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeOrderType(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "table" => "Table",
            "dine_in" => "Table",
            "delivery" => "Delivery",
            "collection" => "Collection",
            "pickup" => "Collection",
            "" => "Collection",
            _ => value
        };
    }

    private static string NormalizePaymentMethod(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "cash" => "Cash",
            "card" => "Card",
            "gift_card" => "Gift Card",
            "mobile" => "Mobile Pay",
            "apple_pay" => "Mobile Pay",
            "google_pay" => "Mobile Pay",
            "" => "Unknown",
            _ => value
        };
    }

    private static string GetVatCategoryName(decimal rate)
    {
        return rate switch
        {
            0m => "Zero-rated",
            5m => "Reduced (5%)",
            20m => "Standard (20%)",
            _ => $"Custom ({rate:0.##}%)"
        };
    }

    private static int ReadInt32(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal ReadDecimal(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static string ReadString(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
    }

    private static DateTime ReadDateTime(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(ordinal));
    }

    private static DateTime? ReadNullableDateTime(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
    }

    private sealed class SnapshotOrderRow
    {
        public int Id { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string LifecycleState { get; set; } = string.Empty;
        public string SourceChannel { get; set; } = string.Empty;
        public string OrderType { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal SubtotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DeliveryFee { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string VoidReason { get; set; } = string.Empty;
        public DateTime? VoidedAt { get; set; }
        public string VoidedBy { get; set; } = string.Empty;
        public DateTime? PaidAt { get; set; }
        public bool HasApprovedPayment { get; set; }

        public bool IsCancelled =>
            string.Equals(Status, "cancelled", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(LifecycleState, "voided", StringComparison.OrdinalIgnoreCase);
        public bool IsVoided =>
            string.Equals(LifecycleState, "voided", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Status, "voided", StringComparison.OrdinalIgnoreCase);
        public bool IsPaid => string.Equals(LifecycleState, "paid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "paid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "closed", StringComparison.OrdinalIgnoreCase)
            || PaidAt.HasValue
            || HasApprovedPayment;
        public bool IsSale => IsPaid && !IsCancelled && !IsVoided;
    }

    /// <summary>
    /// Save report to database
    /// </summary>
    private async Task<ReportSnapshot?> SaveReportAsync(MySqlConnection connection, ReportSnapshot snapshot)
    {
        var query = @"
            INSERT INTO ReportSnapshots 
            (ReportType, StartDate, EndDate, GeneratedAt,
             OrderCount, GrossSales, NetSales, VatTotal,
             DineInOrders, DeliveryOrders, PickupOrders, OnlineOrders,
             CashTotal, CardTotal, MobilePayTotal,
             DiscountTotal, DiscountCount,
             VoidTotal, VoidCount, CancelledOrderCount,
             EstimatedCogs, EstimatedMargin, MarginPercent,
             AverageOrderValue, AveragePrepTime, PaymentSuccessRate)
            VALUES 
            (@Type, @Start, @End, @Generated,
             @OrderCount, @Gross, @Net, @Vat,
             @DineIn, @Delivery, @Pickup, @Online,
             @Cash, @Card, @Mobile,
             @Discount, @DiscountCount,
             @VoidTotal, @VoidCount, @Cancelled,
             @Cogs, @Margin, @MarginPercent,
             @Avg, @PrepTime, @PaymentRate);
            SELECT LAST_INSERT_ID();";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Type", snapshot.Type.ToString());
        command.Parameters.AddWithValue("@Start", snapshot.StartDate);
        command.Parameters.AddWithValue("@End", snapshot.EndDate);
        command.Parameters.AddWithValue("@Generated", snapshot.GeneratedAt);
        command.Parameters.AddWithValue("@OrderCount", snapshot.OrderCount);
        command.Parameters.AddWithValue("@Gross", snapshot.GrossSales);
        command.Parameters.AddWithValue("@Net", snapshot.NetSales);
        command.Parameters.AddWithValue("@Vat", snapshot.VatTotal);
        command.Parameters.AddWithValue("@DineIn", snapshot.DineInOrders);
        command.Parameters.AddWithValue("@Delivery", snapshot.DeliveryOrders);
        command.Parameters.AddWithValue("@Pickup", snapshot.PickupOrders);
        command.Parameters.AddWithValue("@Online", snapshot.OnlineOrders);
        command.Parameters.AddWithValue("@Cash", snapshot.CashTotal);
        command.Parameters.AddWithValue("@Card", snapshot.CardTotal);
        command.Parameters.AddWithValue("@Mobile", snapshot.MobilePayTotal);
        command.Parameters.AddWithValue("@Discount", snapshot.DiscountTotal);
        command.Parameters.AddWithValue("@DiscountCount", snapshot.DiscountCount);
        command.Parameters.AddWithValue("@VoidTotal", snapshot.VoidTotal);
        command.Parameters.AddWithValue("@VoidCount", snapshot.VoidCount);
        command.Parameters.AddWithValue("@Cancelled", snapshot.CancelledOrderCount);
        command.Parameters.AddWithValue("@Cogs", snapshot.EstimatedCogs);
        command.Parameters.AddWithValue("@Margin", snapshot.EstimatedMargin);
        command.Parameters.AddWithValue("@MarginPercent", snapshot.MarginPercent);
        command.Parameters.AddWithValue("@Avg", snapshot.AverageOrderValue);
        command.Parameters.AddWithValue("@PrepTime", snapshot.AveragePrepTime);
        command.Parameters.AddWithValue("@PaymentRate", snapshot.PaymentSuccessRate);

        var snapshotId = Convert.ToInt32(await command.ExecuteScalarAsync());
        snapshot.Id = snapshotId;

        // Save related tables
        await SaveVatBreakdownAsync(connection, snapshotId, snapshot.VatBreakdowns);
        await SaveTopItemsAsync(connection, snapshotId, snapshot.TopItems);
        await SaveStaffPerformanceAsync(connection, snapshotId, snapshot.StaffMetrics);
        await SaveDiscountAuditsAsync(connection, snapshotId, snapshot.DiscountAudits);
        await SavePaymentMethodsAsync(connection, snapshotId, snapshot.PaymentMethods);
        await SaveOrderTypeAnalysisAsync(connection, snapshotId, snapshot.OrderTypeAnalysis);

        return snapshot;
    }

    private async Task SaveVatBreakdownAsync(MySqlConnection connection, int snapshotId, List<ReportVatBreakdown> breakdowns)
    {
        if (breakdowns == null || breakdowns.Count == 0) return;
        
        foreach (var breakdown in breakdowns)
        {
            var query = @"
                INSERT INTO ReportVatBreakdown 
                (SnapshotId, VatRate, TaxableAmount, VatAmount, ItemCount, VatCategoryName)
                VALUES (@Id, @Rate, @Taxable, @Vat, @Items, @Category)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", snapshotId);
            command.Parameters.AddWithValue("@Rate", breakdown.VatRate);
            command.Parameters.AddWithValue("@Taxable", breakdown.TaxableAmount);
            command.Parameters.AddWithValue("@Vat", breakdown.VatAmount);
            command.Parameters.AddWithValue("@Items", breakdown.ItemCount);
            command.Parameters.AddWithValue("@Category", breakdown.VatCategoryName);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task SaveTopItemsAsync(MySqlConnection connection, int snapshotId, List<ReportTopItem> items)
    {
        if (items == null || items.Count == 0) return;
        
        foreach (var item in items)
        {
            var query = @"
                INSERT INTO ReportTopItems 
                (SnapshotId, ItemId, CategoryId, ItemName, CategoryName, TotalQuantity, GrossSales, NetSales, VatAmount, VatRate, EstimatedCogs, Margin, MarginPercent)
                VALUES (@Id, @ItemId, @CatId, @Name, @CatName, @Qty, @Gross, @Net, @Vat, @Rate, @Cogs, @Margin, @MarginPct)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", snapshotId);
            command.Parameters.AddWithValue("@ItemId", item.ItemId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@CatId", item.CategoryId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Name", item.ItemName);
            command.Parameters.AddWithValue("@CatName", item.CategoryName);
            command.Parameters.AddWithValue("@Qty", item.TotalQuantity);
            command.Parameters.AddWithValue("@Gross", item.GrossSales);
            command.Parameters.AddWithValue("@Net", item.NetSales);
            command.Parameters.AddWithValue("@Vat", item.VatAmount);
            command.Parameters.AddWithValue("@Rate", item.VatRate);
            command.Parameters.AddWithValue("@Cogs", item.EstimatedCogs);
            command.Parameters.AddWithValue("@Margin", item.Margin);
            command.Parameters.AddWithValue("@MarginPct", item.MarginPercent);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task SaveStaffPerformanceAsync(MySqlConnection connection, int snapshotId, List<ReportStaffPerformance> staff)
    {
        if (staff == null || staff.Count == 0) return;
        
        foreach (var s in staff)
        {
            var query = @"
                INSERT INTO ReportStaffPerformance 
                (SnapshotId, StaffId, StaffName, OrdersProcessed, TotalSales, DiscountsApplied, VoidsInitiated, AverageOrderValue, ErrorRate)
                VALUES (@Id, @StaffId, @Name, @Orders, @Sales, @Discount, @Voids, @Avg, @Error)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", snapshotId);
            command.Parameters.AddWithValue("@StaffId", s.StaffId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Name", s.StaffName);
            command.Parameters.AddWithValue("@Orders", s.OrdersProcessed);
            command.Parameters.AddWithValue("@Sales", s.TotalSales);
            command.Parameters.AddWithValue("@Discount", s.DiscountsApplied);
            command.Parameters.AddWithValue("@Voids", s.VoidsInitiated);
            command.Parameters.AddWithValue("@Avg", s.AverageOrderValue);
            command.Parameters.AddWithValue("@Error", s.ErrorRate);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task SaveDiscountAuditsAsync(MySqlConnection connection, int snapshotId, List<ReportDiscountAudit> audits)
    {
        if (audits == null || audits.Count == 0) return;
        
        foreach (var audit in audits)
        {
            var query = @"
                INSERT INTO ReportDiscountAudit 
                (SnapshotId, DiscountReason, DiscountCount, TotalDiscountAmount, AverageDiscountPercent)
                VALUES (@Id, @Reason, @Count, @Total, @Avg)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", snapshotId);
            command.Parameters.AddWithValue("@Reason", audit.DiscountReason);
            command.Parameters.AddWithValue("@Count", audit.DiscountCount);
            command.Parameters.AddWithValue("@Total", audit.TotalDiscountAmount);
            command.Parameters.AddWithValue("@Avg", audit.AverageDiscountPercent);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task SavePaymentMethodsAsync(MySqlConnection connection, int snapshotId, List<ReportPaymentMethod> methods)
    {
        if (methods == null || methods.Count == 0) return;
        
        foreach (var method in methods)
        {
            var query = @"
                INSERT INTO ReportPaymentMethods 
                (SnapshotId, PaymentMethod, TransactionCount, TotalAmount, SuccessRate, FailureCount, ProcessingFee)
                VALUES (@Id, @Method, @Count, @Total, @Success, @Failure, @Fee)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", snapshotId);
            command.Parameters.AddWithValue("@Method", method.PaymentMethod);
            command.Parameters.AddWithValue("@Count", method.TransactionCount);
            command.Parameters.AddWithValue("@Total", method.TotalAmount);
            command.Parameters.AddWithValue("@Success", method.SuccessRate);
            command.Parameters.AddWithValue("@Failure", method.FailureCount);
            command.Parameters.AddWithValue("@Fee", method.ProcessingFee);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task SaveOrderTypeAnalysisAsync(MySqlConnection connection, int snapshotId, List<ReportOrderTypeAnalysis> analyses)
    {
        if (analyses == null || analyses.Count == 0) return;
        
        foreach (var analysis in analyses)
        {
            var query = @"
                INSERT INTO ReportOrderTypeAnalysis 
                (SnapshotId, OrderType, OrderCount, TotalSales, AverageOrderValue, AveragePrepTime, CancellationRate)
                VALUES (@Id, @Type, @Count, @Sales, @Avg, @Prep, @Cancel)";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", snapshotId);
            command.Parameters.AddWithValue("@Type", analysis.OrderType);
            command.Parameters.AddWithValue("@Count", analysis.OrderCount);
            command.Parameters.AddWithValue("@Sales", analysis.TotalSales);
            command.Parameters.AddWithValue("@Avg", analysis.AverageOrderValue);
            command.Parameters.AddWithValue("@Prep", analysis.AveragePrepTime);
            command.Parameters.AddWithValue("@Cancel", analysis.CancellationRate);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Retrieve a stored report by ID
    /// </summary>
    public async Task<ReportSnapshot?> GetReportAsync(int snapshotId)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();

        var query = @"
            SELECT Id, ReportType, StartDate, EndDate, GeneratedAt,
                   OrderCount, GrossSales, NetSales, VatTotal,
                   DineInOrders, DeliveryOrders, PickupOrders, OnlineOrders,
                   CashTotal, CardTotal, MobilePayTotal,
                   DiscountTotal, DiscountCount,
                   VoidTotal, VoidCount, CancelledOrderCount,
                   EstimatedCogs, EstimatedMargin, MarginPercent,
                   AverageOrderValue, AveragePrepTime, PaymentSuccessRate,
                   CreatedByStaffId, IsArchived
            FROM ReportSnapshots
            WHERE Id = @Id";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Id", snapshotId);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var snapshot = new ReportSnapshot
            {
                Id = reader.GetInt32(0),
                Type = (ReportType)Enum.Parse(typeof(ReportType), reader.GetString(1)),
                StartDate = reader.GetDateTime(2),
                EndDate = reader.GetDateTime(3),
                GeneratedAt = reader.GetDateTime(4),
                OrderCount = reader.GetInt32(5),
                GrossSales = reader.GetDecimal(6),
                NetSales = reader.GetDecimal(7),
                VatTotal = reader.GetDecimal(8),
                DineInOrders = reader.GetInt32(9),
                DeliveryOrders = reader.GetInt32(10),
                PickupOrders = reader.GetInt32(11),
                OnlineOrders = reader.GetInt32(12),
                CashTotal = reader.GetDecimal(13),
                CardTotal = reader.GetDecimal(14),
                MobilePayTotal = reader.GetDecimal(15),
                DiscountTotal = reader.GetDecimal(16),
                DiscountCount = reader.GetInt32(17),
                VoidTotal = reader.GetDecimal(18),
                VoidCount = reader.GetInt32(19),
                CancelledOrderCount = reader.GetInt32(20),
                EstimatedCogs = reader.GetDecimal(21),
                EstimatedMargin = reader.GetDecimal(22),
                MarginPercent = reader.GetDecimal(23),
                AverageOrderValue = reader.GetDecimal(24),
                AveragePrepTime = reader.GetInt32(25),
                PaymentSuccessRate = reader.GetDecimal(26),
                CreatedByStaffId = reader.IsDBNull(27) ? null : reader.GetInt32(27),
                IsArchived = reader.GetBoolean(28)
            };

            // Load related tables
            await LoadVatBreakdownAsync(connection, snapshotId, snapshot);
            await LoadPaymentMethodsAsync(connection, snapshotId, snapshot);
            await LoadOrderTypeAnalysisAsync(connection, snapshotId, snapshot);
            await LoadStaffPerformanceAsync(connection, snapshotId, snapshot);
            await LoadDiscountAuditsAsync(connection, snapshotId, snapshot);

            return snapshot;
        }

        return null;
    }

    private async Task LoadVatBreakdownAsync(MySqlConnection connection, int snapshotId, ReportSnapshot snapshot)
    {
        var query = "SELECT VatRate, TaxableAmount, VatAmount, ItemCount, VatCategoryName FROM ReportVatBreakdown WHERE SnapshotId = @Id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Id", snapshotId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            snapshot.VatBreakdowns.Add(new ReportVatBreakdown
            {
                VatRate = reader.GetDecimal(0),
                TaxableAmount = reader.GetDecimal(1),
                VatAmount = reader.GetDecimal(2),
                ItemCount = reader.GetInt32(3),
                VatCategoryName = reader.GetString(4)
            });
        }
    }

    private async Task LoadPaymentMethodsAsync(MySqlConnection connection, int snapshotId, ReportSnapshot snapshot)
    {
        var query = "SELECT PaymentMethod, TransactionCount, TotalAmount, SuccessRate, FailureCount FROM ReportPaymentMethods WHERE SnapshotId = @Id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Id", snapshotId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            snapshot.PaymentMethods.Add(new ReportPaymentMethod
            {
                PaymentMethod = reader.GetString(0),
                TransactionCount = reader.GetInt32(1),
                TotalAmount = reader.GetDecimal(2),
                SuccessRate = reader.GetDecimal(3),
                FailureCount = reader.GetInt32(4)
            });
        }
    }

    private async Task LoadOrderTypeAnalysisAsync(MySqlConnection connection, int snapshotId, ReportSnapshot snapshot)
    {
        var query = "SELECT OrderType, OrderCount, TotalSales, AverageOrderValue, AveragePrepTime, CancellationRate FROM ReportOrderTypeAnalysis WHERE SnapshotId = @Id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Id", snapshotId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            snapshot.OrderTypeAnalysis.Add(new ReportOrderTypeAnalysis
            {
                OrderType = reader.GetString(0),
                OrderCount = reader.GetInt32(1),
                TotalSales = reader.GetDecimal(2),
                AverageOrderValue = reader.GetDecimal(3),
                AveragePrepTime = reader.GetInt32(4),
                CancellationRate = reader.GetDecimal(5)
            });
        }
    }

    private async Task LoadStaffPerformanceAsync(MySqlConnection connection, int snapshotId, ReportSnapshot snapshot)
    {
        var query = "SELECT StaffId, StaffName, OrdersProcessed, TotalSales, DiscountsApplied, VoidsInitiated, AverageOrderValue, ErrorRate FROM ReportStaffPerformance WHERE SnapshotId = @Id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Id", snapshotId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            snapshot.StaffMetrics.Add(new ReportStaffPerformance
            {
                StaffId = reader.IsDBNull(0) ? null : reader.GetInt32(0),
                StaffName = reader.GetString(1),
                OrdersProcessed = reader.GetInt32(2),
                TotalSales = reader.GetDecimal(3),
                DiscountsApplied = reader.GetDecimal(4),
                VoidsInitiated = reader.GetInt32(5),
                AverageOrderValue = reader.GetDecimal(6),
                ErrorRate = reader.GetDecimal(7)
            });
        }
    }

    private async Task LoadDiscountAuditsAsync(MySqlConnection connection, int snapshotId, ReportSnapshot snapshot)
    {
        var query = "SELECT DiscountReason, DiscountCount, TotalDiscountAmount, AverageDiscountPercent FROM ReportDiscountAudit WHERE SnapshotId = @Id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Id", snapshotId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            snapshot.DiscountAudits.Add(new ReportDiscountAudit
            {
                DiscountReason = reader.GetString(0),
                DiscountCount = reader.GetInt32(1),
                TotalDiscountAmount = reader.GetDecimal(2),
                AverageDiscountPercent = reader.GetDecimal(3)
            });
        }
    }
}
