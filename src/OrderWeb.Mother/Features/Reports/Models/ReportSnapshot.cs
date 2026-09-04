using System;
using System.Collections.Generic;

namespace POS_in_NET.Models;

public enum ReportType
{
    Daily,
    Weekly,
    Monthly
}

public enum ComparisonType
{
    DayOverDay,
    WeekOverWeek,
    MonthOverMonth,
    YearOverYear
}

/// <summary>
/// Complete snapshot of a report period (daily/weekly/monthly)
/// </summary>
public sealed class ReportSnapshot
{
    public int Id { get; set; }
    public ReportType Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime GeneratedAt { get; set; }
    
    // Summary Metrics
    public int OrderCount { get; set; }
    public decimal GrossSales { get; set; }
    public decimal NetSales { get; set; }
    public decimal VatTotal { get; set; }
    public decimal VatRate { get; set; } = 20.00m;
    
    // Order Breakdown
    public int DineInOrders { get; set; }
    public int DeliveryOrders { get; set; }
    public int PickupOrders { get; set; }
    public int OnlineOrders { get; set; }
    
    // Payment Methods
    public decimal CashTotal { get; set; }
    public decimal CardTotal { get; set; }
    public decimal MobilePayTotal { get; set; }
    
    // Discounts & Adjustments
    public decimal DiscountTotal { get; set; }
    public int DiscountCount { get; set; }
    
    // Voids & Cancellations
    public decimal VoidTotal { get; set; }
    public int VoidCount { get; set; }
    public int CancelledOrderCount { get; set; }
    
    // Profitability
    public decimal EstimatedCogs { get; set; }
    public decimal EstimatedMargin { get; set; }
    public decimal MarginPercent { get; set; }
    
    // Operational
    public decimal AverageOrderValue { get; set; }
    public int AveragePrepTime { get; set; } // seconds
    public decimal PaymentSuccessRate { get; set; } = 100m;
    
    // Audit
    public int? CreatedByStaffId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
    
    // Related Data
    public List<ReportVatBreakdown> VatBreakdowns { get; set; } = new();
    public List<ReportTopItem> TopItems { get; set; } = new();
    public List<ReportPaymentMethod> PaymentMethods { get; set; } = new();
    public List<ReportOrderTypeAnalysis> OrderTypeAnalysis { get; set; } = new();
    public List<ReportStaffPerformance> StaffMetrics { get; set; } = new();
    public List<ReportDiscountAudit> DiscountAudits { get; set; } = new();
    
    // Comparisons
    public ReportComparison? ComparisonVsPrior { get; set; }
    public ReportComparison? ComparisonVsYearAgo { get; set; }
    
    // Display properties
    public string PeriodDisplay => Type switch
    {
        ReportType.Daily => $"{StartDate:dd MMM yyyy}",
        ReportType.Weekly => $"Week of {StartDate:dd MMM} - {EndDate:dd MMM yyyy}",
        ReportType.Monthly => $"{StartDate:MMMM yyyy}",
        _ => "Unknown"
    };
    
    public string GrossDisplay => $"£{GrossSales:F2}";
    public string NetDisplay => $"£{NetSales:F2}";
    public string VatDisplay => $"£{VatTotal:F2}";
    public string MarginDisplay => $"{MarginPercent:F1}%";
}

/// <summary>
/// VAT breakdown by rate
/// </summary>
public sealed class ReportVatBreakdown
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public decimal VatRate { get; set; } // 0, 5, 20
    public decimal TaxableAmount { get; set; }
    public decimal VatAmount { get; set; }
    public int ItemCount { get; set; }
    public string VatCategoryName { get; set; } = string.Empty; // "Food (0%)", "Standard (20%)"
    
    public string VatDisplay => $"£{VatAmount:F2}";
    public string TaxableDisplay => $"£{TaxableAmount:F2}";
    public string RateDisplay => $"{VatRate:F0}%";
}

/// <summary>
/// Top selling item in period
/// </summary>
public sealed class ReportTopItem
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public int? ItemId { get; set; }
    public int? CategoryId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public decimal GrossSales { get; set; }
    public decimal NetSales { get; set; }
    public decimal VatAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal EstimatedCogs { get; set; }
    public decimal Margin { get; set; }
    public decimal MarginPercent { get; set; }
    
    public string GrossDisplay => $"£{GrossSales:F2}";
    public string MarginDisplay => $"{MarginPercent:F1}%";
    public string QuantityDisplay => $"{TotalQuantity}x";
}

/// <summary>
/// Staff performance in period
/// </summary>
public sealed class ReportStaffPerformance
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public int? StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public int OrdersProcessed { get; set; }
    public decimal TotalSales { get; set; }
    public decimal DiscountsApplied { get; set; }
    public int VoidsInitiated { get; set; }
    public decimal AverageOrderValue { get; set; }
    public decimal ErrorRate { get; set; } // %
    
    public string SalesDisplay => $"£{TotalSales:F2}";
    public string AverageDisplay => $"£{AverageOrderValue:F2}";
    public decimal VoidRate => OrdersProcessed > 0 ? (VoidsInitiated * 100m) / OrdersProcessed : 0;
}

/// <summary>
/// Discount audit entries
/// </summary>
public sealed class ReportDiscountAudit
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public string DiscountReason { get; set; } = string.Empty;
    public int DiscountCount { get; set; }
    public decimal TotalDiscountAmount { get; set; }
    public decimal AverageDiscountPercent { get; set; }
    
    public string AmountDisplay => $"£{TotalDiscountAmount:F2}";
    public string PercentDisplay => $"{AverageDiscountPercent:F1}%";
}

/// <summary>
/// Payment method breakdown
/// </summary>
public sealed class ReportPaymentMethod
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal SuccessRate { get; set; } = 100m; // %
    public int FailureCount { get; set; }
    public decimal ProcessingFee { get; set; }
    
    public string AmountDisplay => $"£{TotalAmount:F2}";
    public string SuccessDisplay => $"{SuccessRate:F1}%";
    public string FailureDisplay => FailureCount > 0 ? $"{FailureCount} failed" : "All successful";
}

/// <summary>
/// Order type analysis
/// </summary>
public sealed class ReportOrderTypeAnalysis
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public string OrderType { get; set; } = string.Empty; // DineIn, Delivery, Pickup, Online
    public int OrderCount { get; set; }
    public decimal TotalSales { get; set; }
    public decimal AverageOrderValue { get; set; }
    public int AveragePrepTime { get; set; } // seconds
    public decimal CancellationRate { get; set; } // %
    
    public string SalesDisplay => $"£{TotalSales:F2}";
    public string AverageDisplay => $"£{AverageOrderValue:F2}";
    public string PrepTimeDisplay => $"{AveragePrepTime}s";
}

/// <summary>
/// Comparison between two periods
/// </summary>
public sealed class ReportComparison
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public int? ComparedToSnapshotId { get; set; }
    public ComparisonType Type { get; set; }
    public decimal SalesChange { get; set; } // +5, -2, etc
    public decimal OrderCountChange { get; set; }
    public decimal MarginChange { get; set; }
    public decimal VatChange { get; set; }
    public string InsightText { get; set; } = string.Empty;
    public List<string> Anomalies { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    
    public string SalesChangeDisplay => SalesChange >= 0 
        ? $"+{SalesChange:F1}% ↑" 
        : $"{SalesChange:F1}% ↓";
    
    public string OrderCountChangeDisplay => OrderCountChange >= 0 
        ? $"+{OrderCountChange:F1}% ↑" 
        : $"{OrderCountChange:F1}% ↓";
    
    public string ComparisonLabel => Type switch
    {
        ComparisonType.DayOverDay => "vs Yesterday",
        ComparisonType.WeekOverWeek => "vs Last Week",
        ComparisonType.MonthOverMonth => "vs Last Month",
        ComparisonType.YearOverYear => "vs Last Year",
        _ => "Comparison"
    };
    
    public bool IsPositive => SalesChange >= 0 && MarginChange >= 0;
}
