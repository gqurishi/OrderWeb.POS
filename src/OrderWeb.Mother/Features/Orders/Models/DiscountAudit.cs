namespace POS_in_NET.Models;

public sealed class DiscountApprovalInfo
{
    public int? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class DiscountDialogResult
{
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? Reason { get; set; }
    public string DiscountType { get; set; } = "fixed";
    public bool ApprovalRequired { get; set; }
    public DiscountApprovalInfo? ApprovedBy { get; set; }
}

public sealed class DiscountAuditRequest
{
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public int? TableSessionId { get; set; }
    public string? TableNumber { get; set; }
    public string Action { get; set; } = "applied";
    public string DiscountType { get; set; } = "fixed";
    public decimal SubtotalAmount { get; set; }
    public decimal PreviousDiscountAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TotalAfterDiscount { get; set; }
    public string? Reason { get; set; }
    public string SourceArea { get; set; } = "pos";
    public bool ApprovalRequired { get; set; }
    public DiscountApprovalInfo? ApprovedBy { get; set; }
}

public sealed class DiscountAuditEntry
{
    public int Id { get; set; }
    public DateTime EventAt { get; set; }
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public int? TableSessionId { get; set; }
    public string? TableNumber { get; set; }
    public string Action { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal SubtotalAmount { get; set; }
    public decimal PreviousDiscountAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TotalAfterDiscount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SourceArea { get; set; } = string.Empty;
    public int? RequestedByUserId { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public string RequestedByRole { get; set; } = string.Empty;
    public int? ApprovedByUserId { get; set; }
    public string ApprovedByName { get; set; } = string.Empty;
    public string ApprovedByRole { get; set; } = string.Empty;
    public bool ApprovalRequired { get; set; }
}
