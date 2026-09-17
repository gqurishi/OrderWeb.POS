namespace POS_in_NET.Models;

/// <summary>
/// Phase 0–4 locked rules: Cashier Upload Report → cloud Reports (operations);
/// auto 3 AM → cloud VAT final. Same reportDate = restatement, not a second day.
/// Cashier day = current trading day (TradingDayHelper.GetBusinessDate / rolls at 3:00 AM).
/// Payload flags: purpose + trigger (shared body builder; callers only change these).
/// Safety: block Cashier/auto upload while open orders remain; Cashier success does not
/// clear VAT backlog — 3 AM still posts purpose=vat for that date.
/// </summary>
public static class OrderWebDailyReportUploadRules
{
    /// <summary>Cashier / manual — Business Reports only (not VAT filing final).</summary>
    public const string PurposeOperations = "operations";

    /// <summary>Nightly auto — VAT → Sales → POS / Return final.</summary>
    public const string PurposeVat = "vat";

    public const string TriggerCashier = "cashier";
    public const string TriggerAutomatic3Am = "automatic_3am";

    public static bool IsVatFinalTrigger(string? trigger) =>
        string.Equals(NormalizeTrigger(trigger), TriggerAutomatic3Am, StringComparison.OrdinalIgnoreCase);

    public static bool IsVatFinalPurpose(string? purpose) =>
        string.Equals(NormalizePurpose(purpose), PurposeVat, StringComparison.OrdinalIgnoreCase);

    /// <summary>Map Mother / cloud aliases to canonical purpose.</summary>
    public static string NormalizePurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return PurposeOperations;
        }

        var value = purpose.Trim();
        if (value.Equals(PurposeVat, StringComparison.OrdinalIgnoreCase)
            || value.Equals("vat_final", StringComparison.OrdinalIgnoreCase)
            || value.Equals("filing", StringComparison.OrdinalIgnoreCase))
        {
            return PurposeVat;
        }

        return PurposeOperations;
    }

    /// <summary>Map Mother / cloud aliases to canonical trigger.</summary>
    public static string NormalizeTrigger(string? trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger))
        {
            return TriggerCashier;
        }

        var value = trigger.Trim();
        if (value.Equals(TriggerAutomatic3Am, StringComparison.OrdinalIgnoreCase)
            || value.Equals("automatic", StringComparison.OrdinalIgnoreCase)
            || value.Equals("scheduled", StringComparison.OrdinalIgnoreCase)
            || value.Equals("3am", StringComparison.OrdinalIgnoreCase))
        {
            return TriggerAutomatic3Am;
        }

        if (value.Equals(TriggerCashier, StringComparison.OrdinalIgnoreCase)
            || value.Equals("manual", StringComparison.OrdinalIgnoreCase)
            || value.Equals("operations", StringComparison.OrdinalIgnoreCase))
        {
            return TriggerCashier;
        }

        return value;
    }

    public static void ApplyFlags(OrderWebDailyReportPayload payload, string purpose, string trigger)
    {
        payload.Purpose = NormalizePurpose(purpose);
        payload.Trigger = NormalizeTrigger(trigger);
    }
}
