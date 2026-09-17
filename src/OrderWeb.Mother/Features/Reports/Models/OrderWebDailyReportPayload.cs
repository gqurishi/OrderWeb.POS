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
    public decimal GiftCardSales { get; set; }
    public decimal ItemSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal ServiceCharges { get; set; }
    public int RemovedServiceChargeCount { get; set; }
    public decimal RemovedServiceChargeValue { get; set; }
    public decimal CashTips { get; set; }
    public decimal CardTips { get; set; }
    public decimal DeliveryFees { get; set; }
    public decimal Refunds { get; set; }
    /// <summary>Legacy single VAT field (kept for older cloud parsers).</summary>
    public decimal Vat { get; set; }
    /// <summary>Filing: sales ex VAT (Box 6).</summary>
    public decimal NetSales { get; set; }
    /// <summary>Filing: VAT on sales (Box 1). Same value as <see cref="Vat"/>.</summary>
    public decimal VatAmount { get; set; }
    /// <summary>Filing: gross = net + VAT check.</summary>
    public decimal GrossSales { get; set; }
    public List<OrderWebDailyVatByRateRow> VatByRate { get; set; } = new();
    public decimal FinalMoneyCollected { get; set; }
    public OrderWebLabourUploadPayload? Labour { get; set; }

    /// <summary>operations = Reports; vat = VAT ledger final.</summary>
    public string Purpose { get; set; } = OrderWebDailyReportUploadRules.PurposeOperations;

    /// <summary>cashier | automatic_3am (and aliases cloud accepts).</summary>
    public string Trigger { get; set; } = OrderWebDailyReportUploadRules.TriggerCashier;

    public string ReportDateValue => ReportDate.ToString("yyyy-MM-dd");
}

public sealed class OrderWebDailyVatByRateRow
{
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
}

public sealed class OrderWebDailyReportSyncResult
{
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string Message { get; set; } = string.Empty;
    public OrderWebDailyReportPayload? Payload { get; set; }
}

/// <summary>Read-only status for Report banner — auto VAT final (no manual VAT button).</summary>
public sealed class OrderWebDailyUploadStatus
{
    public int PendingDayCount { get; set; }
    public DateTime? OldestPendingDate { get; set; }
    public DateTime? NewestPendingDate { get; set; }
    public DateTime? LastSuccessfulUploadDate { get; set; }
    public DateTime? LastSuccessfulUploadedAt { get; set; }

    public bool HasPending => PendingDayCount > 0;

    /// <summary>Phase 5 status copy for Report page (status only — no Cashier / VAT upload button).</summary>
    public string FormatVatFinalBannerText()
    {
        if (HasPending)
        {
            if (PendingDayCount == 1 && OldestPendingDate.HasValue)
            {
                return $"Auto VAT final at 3:00 AM · pending 1 day ({OldestPendingDate.Value:ddd dd MMM yyyy}). Status only — use Cashier Upload Report for Business Reports, not VAT.";
            }

            if (OldestPendingDate.HasValue && NewestPendingDate.HasValue)
            {
                return $"Auto VAT final at 3:00 AM · pending {PendingDayCount} days ({OldestPendingDate.Value:dd MMM} – {NewestPendingDate.Value:dd MMM}). Status only — Cashier upload is Reports only.";
            }

            return $"Auto VAT final at 3:00 AM · pending {PendingDayCount} day(s). Status only — no manual VAT upload on this page.";
        }

        if (LastSuccessfulUploadDate.HasValue)
        {
            return $"Auto VAT final at 3:00 AM · last OK {LastSuccessfulUploadDate.Value:ddd dd MMM yyyy}. Cashier Upload Report = Business Reports only (not VAT filing).";
        }

        return "Auto VAT final at 3:00 AM. This banner is status only. Cashier Upload Report sends Business Reports — it does not file VAT.";
    }
}
