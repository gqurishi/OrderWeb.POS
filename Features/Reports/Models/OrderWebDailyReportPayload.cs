namespace POS_in_NET.Models;

public sealed class OrderWebDailyReportPayload
{
    public string Tenant { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public decimal TotalSales { get; set; }
    public int TotalOrders { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }

    public string ReportDateValue => ReportDate.ToString("yyyy-MM-dd");
}

public sealed class OrderWebDailyReportSyncResult
{
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string Message { get; set; } = string.Empty;
    public OrderWebDailyReportPayload? Payload { get; set; }
}
