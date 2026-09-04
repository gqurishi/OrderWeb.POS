namespace POS_in_NET.Models;

public sealed class CashDrawerOpenRequest
{
    public string Reason { get; set; } = "Manual open";
    public string SourceArea { get; set; } = "pos";
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public int? TableSessionId { get; set; }
    public string? TableNumber { get; set; }
    public int? TillExpenseId { get; set; }
}

public sealed class CashDrawerOpenResult
{
    public bool Success { get; set; }
    public int? AuditId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PrinterName { get; set; }
}

public sealed class CashDrawerAuditEntry
{
    public int Id { get; set; }
    public DateTime EventAt { get; set; }
    public int? RequestedByUserId { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public string RequestedByRole { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string SourceArea { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public int? TableSessionId { get; set; }
    public string? TableNumber { get; set; }
    public string DeviceType { get; set; } = "receipt_printer_rj11";
    public int? PrinterId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public string PrinterIp { get; set; } = string.Empty;
    public int? PrinterPort { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
