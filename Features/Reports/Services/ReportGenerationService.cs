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
    private readonly OrderService _orderService;

    public ReportGenerationService(DatabaseService? databaseService = null)
    {
        _databaseService = databaseService ?? new DatabaseService();
        _orderService = new OrderService();
    }

    /// <summary>
    /// Generate and store a daily report for the specified date
    /// </summary>
    public async Task<ReportSnapshot?> GenerateDailyReportAsync(DateTime date)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"📊 [ReportGen] Generating daily report for {date:yyyy-MM-dd}");
            
            var startDate = date.Date;
            var endDate = date.Date.AddDays(1);
            
            return await GenerateReportAsync(ReportType.Daily, startDate, endDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ [ReportGen] Daily report error: {ex.Message}");
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
            // Normalize to Monday
            var monday = weekStartDate.Date;
            while (monday.DayOfWeek != DayOfWeek.Monday)
            {
                monday = monday.AddDays(-1);
            }
            
            var sunday = monday.AddDays(7);
            
            System.Diagnostics.Debug.WriteLine($"📊 [ReportGen] Generating weekly report for {monday:yyyy-MM-dd} to {sunday:yyyy-MM-dd}");
            
            return await GenerateReportAsync(ReportType.Weekly, monday, sunday);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ [ReportGen] Weekly report error: {ex.Message}");
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
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1);
            
            System.Diagnostics.Debug.WriteLine($"📊 [ReportGen] Generating monthly report for {startDate:yyyy-MM}");
            
            return await GenerateReportAsync(ReportType.Monthly, startDate, endDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ [ReportGen] Monthly report error: {ex.Message}");
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
            // Check if report already exists
            var existing = await GetExistingReportAsync(connection, reportType, startDate, endDate);
            if (existing != null)
            {
                System.Diagnostics.Debug.WriteLine($"📌 [ReportGen] Report already exists, ID: {existing.Id}");
                return existing;
            }

            var snapshot = new ReportSnapshot
            {
                Type = reportType,
                StartDate = startDate,
                EndDate = endDate,
                GeneratedAt = DateTime.Now
            };

            // Load all orders in period
            var orders = await LoadOrdersForPeriodAsync(connection, startDate, endDate);
            
            if (orders.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ [ReportGen] No orders found for period {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
                // Still create empty report
                return await SaveReportAsync(connection, snapshot);
            }

            // Calculate all metrics
            await PopulateSummaryMetricsAsync(snapshot, orders);
            await PopulateOrderBreakdownAsync(snapshot, orders);
            await PopulatePaymentMethodsAsync(snapshot, orders);
            await PopulateDiscountAuditAsync(snapshot, orders);
            await PopulateVoidAnalysisAsync(snapshot, orders);
            await PopulateTopItemsAsync(snapshot, orders);
            await PopulateStaffPerformanceAsync(snapshot, orders);
            await PopulateVatBreakdownAsync(snapshot, orders);
            await PopulateOrderTypeAnalysisAsync(snapshot, orders);

            // Save to database
            var savedSnapshot = await SaveReportAsync(connection, snapshot);
            
            System.Diagnostics.Debug.WriteLine($"✅ [ReportGen] Report saved, ID: {savedSnapshot?.Id}");
            
            return savedSnapshot;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ [ReportGen] Error generating report: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
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
    /// Load all orders in the period
    /// </summary>
    private async Task<List<TableOrder>> LoadOrdersForPeriodAsync(MySqlConnection connection, DateTime startDate, DateTime endDate)
    {
        var query = @"
            SELECT 
                o.Id, o.OrderNumber, o.Status, o.CreatedAt,
                o.Subtotal, o.Discount, o.ServiceCharge, o.VAT, o.Total,
                o.StaffId
            FROM TableOrders o
            WHERE o.CreatedAt >= @Start 
            AND o.CreatedAt < @End
            AND IsArchived = 0
            ORDER BY o.CreatedAt";

        var orders = new List<TableOrder>();

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@Start", startDate);
        command.Parameters.AddWithValue("@End", endDate);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            orders.Add(new TableOrder
            {
                Id = reader.GetString(0),
                OrderNumber = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Status = reader.IsDBNull(2) ? TableOrderStatus.Active : (TableOrderStatus)Enum.Parse(typeof(TableOrderStatus), reader.GetString(2)),
                CreatedAt = reader.GetDateTime(3),
                Subtotal = reader.GetDecimal(4),
                Discount = reader.GetDecimal(5),
                ServiceCharge = reader.GetDecimal(6),
                VAT = reader.GetDecimal(7),
                Total = reader.GetDecimal(8),
                StaffId = reader.GetInt32(9)
            });
        }

        return orders;
    }

    /// <summary>
    /// Populate summary metrics
    /// </summary>
    private async Task PopulateSummaryMetricsAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        snapshot.OrderCount = orders.Count;
        snapshot.GrossSales = orders.Sum(o => o.Total);
        snapshot.NetSales = orders.Sum(o => o.Total - o.VAT);
        snapshot.VatTotal = orders.Sum(o => o.VAT);
        
        if (snapshot.OrderCount > 0)
        {
            snapshot.AverageOrderValue = snapshot.GrossSales / snapshot.OrderCount;
        }

        // Estimate margin (assuming COGS is ~30-40% of net sales)
        snapshot.EstimatedCogs = snapshot.NetSales * 0.35m;
        snapshot.EstimatedMargin = snapshot.NetSales - snapshot.EstimatedCogs;
        snapshot.MarginPercent = snapshot.NetSales > 0 ? (snapshot.EstimatedMargin / snapshot.NetSales) * 100 : 0;

        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate order type breakdown
    /// </summary>
    private async Task PopulateOrderBreakdownAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        snapshot.DineInOrders = orders.Count(o => o.OrderMode == "dine_in");
        snapshot.DeliveryOrders = orders.Count(o => o.OrderMode == "delivery");
        snapshot.PickupOrders = orders.Count(o => o.OrderMode == "pickup");
        snapshot.OnlineOrders = orders.Count(o => o.OrderMode == "online");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate payment method breakdown
    /// </summary>
    private async Task PopulatePaymentMethodsAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        snapshot.CashTotal = 0;
        snapshot.CardTotal = 0;
        snapshot.MobilePayTotal = 0;

        // Create payment method detail records
        var paymentMethods = new Dictionary<string, (int count, decimal amount)>();
        
        foreach (var order in orders)
        {
            var method = "Unknown";
            if (!paymentMethods.ContainsKey(method))
            {
                paymentMethods[method] = (0, 0);
            }
            var (count, amount) = paymentMethods[method];
            paymentMethods[method] = (count + 1, amount + order.Total);
        }

        foreach (var (method, (count, amount)) in paymentMethods)
        {
            snapshot.PaymentMethods.Add(new ReportPaymentMethod
            {
                PaymentMethod = method,
                TransactionCount = count,
                TotalAmount = amount,
                SuccessRate = 100 // TODO: Track payment failures
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate discount audit
    /// </summary>
    private async Task PopulateDiscountAuditAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        var discountedOrders = orders.Where(o => o.Discount > 0).ToList();
        
        snapshot.DiscountTotal = discountedOrders.Sum(o => o.Discount);
        snapshot.DiscountCount = discountedOrders.Count;

        if (discountedOrders.Count > 0)
        {
            snapshot.DiscountAudits.Add(new ReportDiscountAudit
            {
                DiscountReason = "Manual Discount",
                DiscountCount = snapshot.DiscountCount,
                TotalDiscountAmount = snapshot.DiscountTotal,
                AverageDiscountPercent = discountedOrders.Average(o => (o.Discount / o.Total) * 100)
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate void analysis
    /// </summary>
    private async Task PopulateVoidAnalysisAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        var voidedOrders = orders.Where(o => o.Status == TableOrderStatus.Voided).ToList();
        var cancelledOrders = new List<TableOrder>();

        snapshot.VoidCount = voidedOrders.Count;
        snapshot.VoidTotal = voidedOrders.Sum(o => o.Total);
        snapshot.CancelledOrderCount = cancelledOrders.Count;

        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate VAT breakdown by rate
    /// </summary>
    private async Task PopulateVatBreakdownAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        // Group by VAT rate (assuming 0%, 5%, 20%)
        var vatRates = new Dictionary<decimal, (decimal taxable, decimal vat, int items)>
        {
            { 0m, (0, 0, 0) },
            { 5m, (0, 0, 0) },
            { 20m, (0, 0, 0) }
        };

        foreach (var order in orders)
        {
            // Simplified: assume all items at 20% unless zero-rated
            var rate = 20m;
            var taxable = order.Total - order.VAT;
            var (tax, vat, items) = vatRates[rate];
            vatRates[rate] = (tax + taxable, vat + order.VAT, items + 1);
        }

        foreach (var (rate, (taxable, vat, itemCount)) in vatRates)
        {
            if (itemCount > 0)
            {
                snapshot.VatBreakdowns.Add(new ReportVatBreakdown
                {
                    VatRate = rate,
                    TaxableAmount = taxable,
                    VatAmount = vat,
                    ItemCount = itemCount,
                    VatCategoryName = rate switch
                    {
                        0m => "Zero-rated",
                        5m => "Reduced (5%)",
                        20m => "Standard (20%)",
                        _ => $"Custom ({rate}%)"
                    }
                });
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate top items (placeholder - needs item detail query)
    /// </summary>
    private async Task PopulateTopItemsAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        // TODO: Query OrderItems table and aggregate quantities/sales
        // For now, this is a placeholder
        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate staff performance
    /// </summary>
    private async Task PopulateStaffPerformanceAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        var staffOrders = orders.GroupBy(o => o.StaffId).ToList();

        foreach (var staffGroup in staffOrders)
        {
            var voids = staffGroup.Count(o => o.Status == TableOrderStatus.Voided);
            var totalOrders = staffGroup.Count();
            
            snapshot.StaffMetrics.Add(new ReportStaffPerformance
            {
                StaffId = staffGroup.Key,
                StaffName = $"Staff {staffGroup.Key}", // TODO: Load actual name
                OrdersProcessed = totalOrders,
                TotalSales = staffGroup.Sum(o => o.Total),
                DiscountsApplied = staffGroup.Sum(o => o.Discount),
                VoidsInitiated = voids,
                AverageOrderValue = staffGroup.Average(o => o.Total),
                ErrorRate = totalOrders > 0 ? (voids * 100m) / totalOrders : 0
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Populate order type analysis
    /// </summary>
    private async Task PopulateOrderTypeAnalysisAsync(ReportSnapshot snapshot, List<TableOrder> orders)
    {
        var orderTypes = orders.GroupBy(o => o.OrderMode ?? "dine_in").ToList();

        foreach (var typeGroup in orderTypes)
        {
            var orderType = typeGroup.Key ?? "dine_in";
            var totalOrders = typeGroup.Count();

            snapshot.OrderTypeAnalysis.Add(new ReportOrderTypeAnalysis
            {
                OrderType = orderType,
                OrderCount = totalOrders,
                TotalSales = typeGroup.Sum(o => o.Total),
                AverageOrderValue = typeGroup.Average(o => o.Total),
                AveragePrepTime = 900, // TODO: Track actual prep times
                CancellationRate = 0 // TODO: Track cancellations
            });
        }

        await Task.CompletedTask;
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
