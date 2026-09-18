namespace POS_in_NET.Services;

/// <summary>
/// Phase 1 print reliability — one meaning of done for kitchen, online, and receipt jobs.
/// Physical "printed" is only after the queue successfully sends to the printer (best effort),
/// never merely because bytes were accepted into <c>network_print_queue</c>.
/// </summary>
public static class PrintJobLifecycle
{
    /// <summary>Accepted into durable queue; not yet sent to the printer.</summary>
    public const string Queued = "queued";

    /// <summary>Claimed / actively sending to the printer.</summary>
    public const string Sending = "sending";

    /// <summary>Best-effort success: SendToPrinter reported OK; when DLE status replied, paper was known ready first.</summary>
    public const string Printed = "printed";

    /// <summary>Transient failure; will auto-retry while under max attempts.</summary>
    public const string Failed = "failed";

    /// <summary>Retries exhausted or unrecoverable — staff must see and act.</summary>
    public const string NeedsAttention = "needs_attention";

    /// <summary>DB row status while waiting in <c>network_print_queue</c>.</summary>
    public const string DbPending = "pending";

    /// <summary>DB row status while claimed for send.</summary>
    public const string DbPrinting = "printing";

    /// <summary>DB row status after successful send.</summary>
    public const string DbCompleted = "completed";

    /// <summary>DB row status for permanent / staff-visible failure.</summary>
    public const string DbNeedsAttention = "needs_attention";

    public static string FromQueueStatus(string? dbStatus) =>
        (dbStatus ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            DbPending => Queued,
            DbPrinting => Sending,
            DbCompleted => Printed,
            "failed" => Failed,
            DbNeedsAttention => NeedsAttention,
            _ => Failed
        };

    public static bool IsPhysicallyPrinted(string? dbOrLifecycleStatus)
    {
        var value = (dbOrLifecycleStatus ?? string.Empty).Trim().ToLowerInvariant();
        return value is Printed or DbCompleted;
    }

    /// <summary>Still not done — must remain visible (queue UI / Needs attention).</summary>
    public static bool IsNotPrintedVisible(string? dbStatus) =>
        (dbStatus ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            DbPending or DbPrinting or "failed" or DbNeedsAttention => true,
            _ => false
        };

    public static bool IsKitchenQueueJob(string? jobType) =>
        (jobType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "kitchen" or "bar" or "kitchen_takeaway" or "takeaway_ticket" => true,
            _ => false
        };

    public static bool IsReceiptQueueJob(string? jobType) =>
        (jobType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "receipt" or "customer_receipt" or "customer_receipt_reprint" or "online_receipt" => true,
            _ => false
        };

    /// <summary>Staff UI label: Kitchen / Receipt / Online.</summary>
    public static string FormatStaffCategory(string? jobType)
    {
        var value = (jobType ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "takeaway_ticket" or "online_receipt" => "Online",
            "kitchen" or "bar" or "kitchen_takeaway" => "Kitchen",
            "receipt" or "customer_receipt" or "customer_receipt_reprint" => "Receipt",
            _ when value.Contains("online", StringComparison.Ordinal) => "Online",
            _ when value.Contains("kitchen", StringComparison.Ordinal) || value == "bar" => "Kitchen",
            _ when value.Contains("receipt", StringComparison.Ordinal) => "Receipt",
            _ => string.IsNullOrEmpty(value) ? "Print" : char.ToUpperInvariant(value[0]) + value[1..]
        };
    }
}
