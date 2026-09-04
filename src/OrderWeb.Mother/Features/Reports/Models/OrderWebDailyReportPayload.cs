namespace POS_in_NET.Models;

public sealed class OrderWebDailyReportPayload
{
    public int ContractVersion { get; set; } = 2;
    public string Tenant { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public decimal TotalSales { get; set; }
    public int TotalOrders { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal ItemSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal ServiceCharges { get; set; }
    public int RemovedServiceChargeCount { get; set; }
    public decimal RemovedServiceChargeValue { get; set; }
    public decimal CashTips { get; set; }
    public decimal CardTips { get; set; }
    public decimal DeliveryFees { get; set; }
    public decimal Refunds { get; set; }
    public decimal Vat { get; set; }
    public decimal FinalMoneyCollected { get; set; }
    public OrderWebLabourUploadPayload? Labour { get; set; }

    public string ReportDateValue => ReportDate.ToString("yyyy-MM-dd");
}

public sealed class OrderWebDailyReportSyncResult
{
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string Message { get; set; } = string.Empty;
    public OrderWebDailyReportPayload? Payload { get; set; }
}
