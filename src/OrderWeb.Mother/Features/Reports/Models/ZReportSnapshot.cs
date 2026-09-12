using System.Globalization;

namespace POS_in_NET.Models;

public sealed class ZReportSnapshot
{
    public DateTime ReportDate { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public string BusinessName { get; set; } = "Restaurant POS";
    public string TerminalName { get; set; } = string.Empty;
    public string PrintedByName { get; set; } = string.Empty;
    public bool IsReprint { get; set; }

    public int OrderCount { get; set; }
    public decimal GrossSales { get; set; }
    public decimal NetSales { get; set; }
    public decimal VatAmount { get; set; }
    public decimal AverageOrderValue { get; set; }

    public decimal CashTotal { get; set; }
    public int CashTransactionCount { get; set; }
    public decimal CardTotal { get; set; }
    public int CardTransactionCount { get; set; }
    public decimal GiftCardTotal { get; set; }
    public int GiftCardTransactionCount { get; set; }
    public decimal TipsTotal { get; set; }
    public decimal CashTips { get; set; }
    public decimal CardTips { get; set; }
    public decimal RefundTotal { get; set; }
    public int RefundCount { get; set; }
    public decimal ItemSales { get; set; }
    public decimal TableServiceCharges { get; set; }
    public decimal RemovedServiceChargeValue { get; set; }
    public int RemovedServiceChargeCount { get; set; }
    public decimal DeliveryFees { get; set; }
    public decimal FinalMoneyCollected { get; set; }

    public int PosOrderCount { get; set; }
    public decimal PosGrossSales { get; set; }
    public int OnlineOrderCount { get; set; }
    public decimal OnlineGrossSales { get; set; }

    public int TableOrderCount { get; set; }
    public decimal TableGrossSales { get; set; }
    public int DeliveryOrderCount { get; set; }
    public decimal DeliveryGrossSales { get; set; }
    public int PickupOrderCount { get; set; }
    public decimal PickupGrossSales { get; set; }

    public decimal TillNetOut { get; set; }
    public decimal TillShoppingNet { get; set; }
    public decimal TillDeliveryTotal { get; set; }
    public decimal TillOtherTotal { get; set; }
    public decimal TillCashReturned { get; set; }
    public int TillPendingShoppingCount { get; set; }
    public decimal TillPendingShoppingTotal { get; set; }
    public decimal? LastCashCountAmount { get; set; }
    public DateTime? LastCashCountAt { get; set; }

    public decimal DiscountTotal { get; set; }
    public int DiscountEventCount { get; set; }
    public int VoidCount { get; set; }

    public decimal OpeningTillFloat { get; set; }
    public decimal ExpectedCashInDrawer { get; set; }
    public decimal? CashCountVariance { get; set; }

    public decimal? YesterdayGrossSales { get; set; }
    public decimal? SalesVsYesterdayPercent { get; set; }

    public List<ZReportTopItemRow> TopItems { get; set; } = new();
    public List<ZReportServiceChargeRemovalRow> ServiceChargeRemovals { get; set; } = new();
    public string ReportReference { get; set; } = string.Empty;

    public string DateDisplay => ReportDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public string GrossDisplay => FormatMoney(GrossSales);
    public string NetDisplay => FormatMoney(NetSales);
    public string VatDisplay => FormatMoney(VatAmount);
    public string AvgDisplay => FormatMoney(AverageOrderValue);
    public string CashDisplay => FormatMoney(CashTotal);
    public string CardDisplay => FormatMoney(CardTotal);
    public string GiftCardDisplay => FormatMoney(GiftCardTotal);
    public string TipsDisplay => FormatMoney(TipsTotal);
    public string CashTipsDisplay => FormatMoney(CashTips);
    public string CardTipsDisplay => FormatMoney(CardTips);
    public string ServiceChargeDisplay => FormatMoney(TableServiceCharges);
    public string RemovedServiceChargeDisplay => FormatMoney(RemovedServiceChargeValue);
    public string DeliveryFeesDisplay => FormatMoney(DeliveryFees);
    public string FinalMoneyDisplay => FormatMoney(FinalMoneyCollected);
    public string RefundDisplay => FormatMoney(RefundTotal);
    public string PosDisplay => FormatMoney(PosGrossSales);
    public string OnlineDisplay => FormatMoney(OnlineGrossSales);
    public string TillNetOutDisplay => FormatMoney(TillNetOut);
    public string ExpectedCashDisplay => FormatMoney(ExpectedCashInDrawer);
    public string DiscountDisplay => FormatMoney(DiscountTotal);
    public string LastUpdatedDisplay => $"Updated {GeneratedAt:HH:mm:ss}";

    public string SalesVsYesterdayDisplay
    {
        get
        {
            if (!SalesVsYesterdayPercent.HasValue || YesterdayGrossSales.GetValueOrDefault() <= 0)
            {
                return "No sales yesterday";
            }

            var sign = SalesVsYesterdayPercent.Value >= 0 ? "+" : string.Empty;
            return $"{sign}{SalesVsYesterdayPercent.Value:F1}% · yesterday {FormatMoney(YesterdayGrossSales.GetValueOrDefault())}";
        }
    }

    private static string FormatMoney(decimal value) => $"£{value:F2}";
}

public sealed class ZReportTopItemRow
{
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal GrossSales { get; set; }

    public string DisplayLine => $"{Quantity}x {ItemName}";
    public string AmountDisplay => $"£{GrossSales:F2}";
}

public sealed class ZReportServiceChargeRemovalRow
{
    public DateTime RemovedAt { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
}

public sealed class ZReportPrintResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PrinterName { get; set; }
    public int SlipsPrinted { get; set; }
}
