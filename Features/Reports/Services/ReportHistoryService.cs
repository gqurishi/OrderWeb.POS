using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Service for retrieving and comparing historical reports
/// Enables browsing past reports and calculating period-over-period metrics
/// </summary>
public sealed class ReportHistoryService
{
    private readonly DatabaseService _databaseService;

    public ReportHistoryService(DatabaseService? databaseService = null)
    {
        _databaseService = databaseService ?? new DatabaseService();
    }

    /// <summary>
    /// Get all available report snapshots within the last N months
    /// </summary>
    public async Task<List<ReportHistorySummary>> GetAvailableReportsAsync(int monthsBack = 12)
    {
        var result = new List<ReportHistorySummary>();
        
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            var startDate = DateTime.Now.AddMonths(-monthsBack);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ReportType, StartDate, EndDate, GeneratedAt, GrossSales 
                FROM ReportSnapshots 
                WHERE GeneratedAt >= @startDate 
                ORDER BY GeneratedAt DESC 
                LIMIT 500";
            cmd.Parameters.AddWithValue("@startDate", startDate);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ReportHistorySummary
                {
                    Id = reader.GetInt32(0),
                    Type = ParseReportType(reader.GetValue(1)),
                    StartDate = reader.GetDateTime(2),
                    EndDate = reader.GetDateTime(3),
                    GeneratedAt = reader.GetDateTime(4),
                    TotalRevenue = reader.GetDecimal(5)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportHistory] Error fetching available reports: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Get all daily reports for a specific month
    /// </summary>
    public async Task<List<ReportHistorySummary>> GetDailyReportsForMonthAsync(int year, int month)
    {
        var result = new List<ReportHistorySummary>();

        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ReportType, StartDate, EndDate, GeneratedAt, GrossSales 
                FROM ReportSnapshots 
                WHERE ReportType = @type 
                AND StartDate >= @startDate 
                AND StartDate < @endDate 
                ORDER BY StartDate ASC";
            cmd.Parameters.AddWithValue("@type", ReportType.Daily.ToString());
            cmd.Parameters.AddWithValue("@startDate", startDate);
            cmd.Parameters.AddWithValue("@endDate", endDate);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ReportHistorySummary
                {
                    Id = reader.GetInt32(0),
                    Type = ParseReportType(reader.GetValue(1)),
                    StartDate = reader.GetDateTime(2),
                    EndDate = reader.GetDateTime(3),
                    GeneratedAt = reader.GetDateTime(4),
                    TotalRevenue = reader.GetDecimal(5)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportHistory] Error fetching daily reports: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Get all weekly reports for a specific year
    /// </summary>
    public async Task<List<ReportHistorySummary>> GetWeeklyReportsForYearAsync(int year)
    {
        var result = new List<ReportHistorySummary>();

        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            var startDate = new DateTime(year, 1, 1);
            var endDate = new DateTime(year + 1, 1, 1);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ReportType, StartDate, EndDate, GeneratedAt, GrossSales 
                FROM ReportSnapshots 
                WHERE ReportType = @type 
                AND StartDate >= @startDate 
                AND StartDate < @endDate 
                ORDER BY StartDate ASC";
            cmd.Parameters.AddWithValue("@type", ReportType.Weekly.ToString());
            cmd.Parameters.AddWithValue("@startDate", startDate);
            cmd.Parameters.AddWithValue("@endDate", endDate);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ReportHistorySummary
                {
                    Id = reader.GetInt32(0),
                    Type = ParseReportType(reader.GetValue(1)),
                    StartDate = reader.GetDateTime(2),
                    EndDate = reader.GetDateTime(3),
                    GeneratedAt = reader.GetDateTime(4),
                    TotalRevenue = reader.GetDecimal(5)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportHistory] Error fetching weekly reports: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Get all monthly reports within a date range
    /// </summary>
    public async Task<List<ReportHistorySummary>> GetMonthlyReportsAsync(int yearFrom, int yearTo)
    {
        var result = new List<ReportHistorySummary>();

        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            var startDate = new DateTime(yearFrom, 1, 1);
            var endDate = new DateTime(yearTo + 1, 1, 1);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ReportType, StartDate, EndDate, GeneratedAt, GrossSales 
                FROM ReportSnapshots 
                WHERE ReportType = @type 
                AND StartDate >= @startDate 
                AND StartDate < @endDate 
                ORDER BY StartDate ASC";
            cmd.Parameters.AddWithValue("@type", ReportType.Monthly.ToString());
            cmd.Parameters.AddWithValue("@startDate", startDate);
            cmd.Parameters.AddWithValue("@endDate", endDate);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ReportHistorySummary
                {
                    Id = reader.GetInt32(0),
                    Type = ParseReportType(reader.GetValue(1)),
                    StartDate = reader.GetDateTime(2),
                    EndDate = reader.GetDateTime(3),
                    GeneratedAt = reader.GetDateTime(4),
                    TotalRevenue = reader.GetDecimal(5)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportHistory] Error fetching monthly reports: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Get a specific report by ID
    /// </summary>
    public async Task<ReportSnapshot?> GetReportByIdAsync(int snapshotId)
    {
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    Id, ReportType, StartDate, EndDate, GeneratedAt,
                    OrderCount, GrossSales, NetSales, VatTotal,
                    CashTotal, CardTotal, DiscountTotal, VoidTotal,
                    AverageOrderValue, UpdatedAt
                FROM ReportSnapshots 
                WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", snapshotId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapReportSnapshot(reader);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportHistory] Error fetching report: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Get the report from N months ago
    /// </summary>
    public async Task<ReportSnapshot?> GetReportFromMonthsAgoAsync(int monthsBack, ReportType reportType = ReportType.Monthly)
    {
        try
        {
            var targetDate = DateTime.Now.AddMonths(-monthsBack);

            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    Id, ReportType, StartDate, EndDate, GeneratedAt,
                    OrderCount, GrossSales, NetSales, VatTotal,
                    CashTotal, CardTotal, DiscountTotal, VoidTotal,
                    AverageOrderValue, UpdatedAt
                FROM ReportSnapshots 
                WHERE ReportType = @type 
                AND StartDate <= @targetDate 
                ORDER BY StartDate DESC 
                LIMIT 1";
            cmd.Parameters.AddWithValue("@type", reportType.ToString());
            cmd.Parameters.AddWithValue("@targetDate", targetDate);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapReportSnapshot(reader);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportHistory] Error fetching report from months ago: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Get a specific daily report for a date
    /// </summary>
    public async Task<ReportSnapshot?> GetDailyReportForDateAsync(DateTime date)
    {
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            var startDate = date.Date;
            var endDate = startDate.AddDays(1);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    Id, ReportType, StartDate, EndDate, GeneratedAt,
                    OrderCount, GrossSales, NetSales, VatTotal,
                    CashTotal, CardTotal, DiscountTotal, VoidTotal,
                    AverageOrderValue, UpdatedAt
                FROM ReportSnapshots 
                WHERE ReportType = @type 
                AND StartDate >= @startDate 
                AND StartDate < @endDate 
                LIMIT 1";
            cmd.Parameters.AddWithValue("@type", ReportType.Daily.ToString());
            cmd.Parameters.AddWithValue("@startDate", startDate);
            cmd.Parameters.AddWithValue("@endDate", endDate);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapReportSnapshot(reader);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportHistory] Error fetching daily report for date: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Enrich a report snapshot with comparison data against the prior period and the same period last year
    /// </summary>
    public async Task<ReportSnapshot> EnrichComparisonsAsync(ReportSnapshot report)
    {
        report.ComparisonVsPrior = await GetComparisonForPeriodAsync(report, GetPriorPeriodDate(report), GetComparisonType(report.Type));
        report.ComparisonVsYearAgo = await GetComparisonForPeriodAsync(report, GetYearAgoDate(report), ComparisonType.YearOverYear);
        return report;
    }

    /// <summary>
    /// Get the best matching historical report on or before a target date for a specific report type
    /// </summary>
    private async Task<ReportSnapshot?> GetReportOnOrBeforeAsync(ReportType reportType, DateTime targetDate)
    {
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    Id, ReportType, StartDate, EndDate, GeneratedAt,
                    OrderCount, GrossSales, NetSales, VatTotal,
                    CashTotal, CardTotal, DiscountTotal, VoidTotal,
                    AverageOrderValue, UpdatedAt
                FROM ReportSnapshots
                WHERE ReportType = @type
                AND StartDate <= @targetDate
                ORDER BY StartDate DESC
                LIMIT 1";
            cmd.Parameters.AddWithValue("@type", reportType.ToString());
            cmd.Parameters.AddWithValue("@targetDate", targetDate);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapReportSnapshot(reader);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" [ReportHistory] Error fetching previous report: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Build a comparison against a prior period if one exists
    /// </summary>
    private async Task<ReportComparison?> GetComparisonForPeriodAsync(ReportSnapshot current, DateTime priorTargetDate, ComparisonType comparisonType)
    {
        var previous = await GetReportOnOrBeforeAsync(current.Type, priorTargetDate);
        return previous == null ? null : CreateComparison(current, previous, comparisonType);
    }

    private static DateTime GetPriorPeriodDate(ReportSnapshot report)
    {
        return report.Type switch
        {
            ReportType.Daily => report.StartDate.AddDays(-1),
            ReportType.Weekly => report.StartDate.AddDays(-7),
            ReportType.Monthly => report.StartDate.AddMonths(-1),
            _ => report.StartDate.AddDays(-1)
        };
    }

    private static DateTime GetYearAgoDate(ReportSnapshot report)
    {
        return report.Type switch
        {
            ReportType.Daily => report.StartDate.AddYears(-1),
            ReportType.Weekly => report.StartDate.AddYears(-1),
            ReportType.Monthly => report.StartDate.AddYears(-1),
            _ => report.StartDate.AddYears(-1)
        };
    }

    private static ComparisonType GetComparisonType(ReportType reportType)
    {
        return reportType switch
        {
            ReportType.Daily => ComparisonType.DayOverDay,
            ReportType.Weekly => ComparisonType.WeekOverWeek,
            ReportType.Monthly => ComparisonType.MonthOverMonth,
            _ => ComparisonType.DayOverDay
        };
    }

    /// <summary>
    /// Calculate simple period-over-period comparison using existing model structure
    /// </summary>
    public ReportComparison CreateComparison(ReportSnapshot current, ReportSnapshot? previous, ComparisonType comparisonType)
    {
        var comparison = new ReportComparison
        {
            SnapshotId = current.Id,
            ComparedToSnapshotId = previous?.Id,
            Type = comparisonType,
            CreatedAt = DateTime.Now
        };

        if (previous != null)
        {
            // Calculate deltas
            comparison.SalesChange = current.GrossSales > 0 && previous.GrossSales > 0
                ? ((current.GrossSales - previous.GrossSales) / previous.GrossSales) * 100
                : 0;

            comparison.OrderCountChange = current.OrderCount > 0 && previous.OrderCount > 0
                ? ((current.OrderCount - previous.OrderCount) / (decimal)previous.OrderCount) * 100
                : 0;

            // Calculate margin change
            var currentMargin = current.EstimatedMargin > 0 ? current.MarginPercent : 0;
            var previousMargin = previous.EstimatedMargin > 0 ? previous.MarginPercent : 0;
            comparison.MarginChange = currentMargin - previousMargin;

            // Calculate VAT change
            comparison.VatChange = current.VatTotal > 0 && previous.VatTotal > 0
                ? ((current.VatTotal - previous.VatTotal) / previous.VatTotal) * 100
                : 0;

            // Build insight
            var insights = new List<string>();
            if (Math.Abs(comparison.SalesChange) > 10)
                insights.Add($"Sales {(comparison.SalesChange > 0 ? "up" : "down")} {Math.Abs(comparison.SalesChange):F1}%");
            if (Math.Abs(comparison.OrderCountChange) > 10)
                insights.Add($"Orders {(comparison.OrderCountChange > 0 ? "up" : "down")} {Math.Abs(comparison.OrderCountChange):F1}%");
            if (comparison.MarginChange != 0)
                insights.Add($"Margin changed by {comparison.MarginChange:F1}pp");

            comparison.InsightText = string.Join(", ", insights.Take(2));

            // Detect anomalies
            if (comparison.SalesChange < -15)
                comparison.Anomalies.Add("Significant sales drop detected");
            if (comparison.SalesChange > 25)
                comparison.Anomalies.Add("Unusual sales spike");
            if (comparison.MarginChange < -5)
                comparison.Anomalies.Add("Margin compression detected");
        }

        return comparison;
    }

    /// <summary>
    /// Map database row to ReportSnapshot object
    /// </summary>
    private ReportSnapshot MapReportSnapshot(MySqlDataReader reader)
    {
        return new ReportSnapshot
        {
            Id = reader.GetInt32(0),
            Type = ParseReportType(reader.GetValue(1)),
            StartDate = reader.GetDateTime(2),
            EndDate = reader.GetDateTime(3),
            GeneratedAt = reader.GetDateTime(4),
            OrderCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
            GrossSales = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
            NetSales = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
            VatTotal = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
            CashTotal = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9),
            CardTotal = reader.IsDBNull(10) ? 0 : reader.GetDecimal(10),
            DiscountTotal = reader.IsDBNull(11) ? 0 : reader.GetDecimal(11),
            VoidTotal = reader.IsDBNull(12) ? 0 : reader.GetDecimal(12),
            AverageOrderValue = reader.IsDBNull(13) ? 0 : reader.GetDecimal(13),
            UpdatedAt = reader.IsDBNull(14) ? DateTime.Now : reader.GetDateTime(14)
        };
    }

    private static ReportType ParseReportType(object value)
    {
        if (value is string text && Enum.TryParse<ReportType>(text, true, out var parsedFromString))
        {
            return parsedFromString;
        }

        try
        {
            var asInt = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (Enum.IsDefined(typeof(ReportType), asInt))
            {
                return (ReportType)asInt;
            }
        }
        catch
        {
            // Ignore and use fallback below.
        }

        return ReportType.Daily;
    }
}

/// <summary>
/// Lightweight summary for displaying report lists
/// </summary>
public class ReportHistorySummary
{
    public int Id { get; set; }
    public ReportType Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime GeneratedAt { get; set; }
    public decimal TotalRevenue { get; set; }

    public string DisplayName => $"{Type} Report - {StartDate:yyyy-MM-dd}";
}

