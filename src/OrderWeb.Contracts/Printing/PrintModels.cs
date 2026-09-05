using OrderWeb.Contracts.Results;

namespace OrderWeb.Contracts.Printing;

public enum PrintKind
{
    KitchenTicket = 1,
    CustomerReceipt = 2,
    Reprint = 3,
    CashDrawer = 4
}

/// <summary>
/// Authoritative print lifecycle. Client may only display these values from Mother.
/// </summary>
public enum PrintStatus
{
    Queued = 1,
    Printing = 2,
    Printed = 3,
    PartialFailure = 4,
    Failed = 5,
    PrinterUnavailable = 6
}

public sealed record PrintRequestDto(
    string RequestId,
    string TerminalId,
    string SessionId,
    PrintKind Kind,
    string? OrderId,
    string? Reason = null,
    bool IsReprint = false,
    DateTimeOffset? RequestedAtUtc = null);

public sealed record PrintRouteFailureDto(
    string Route,
    string Reason);

public sealed record PrintAuditDto(
    string AuditId,
    string RequestId,
    PrintKind Kind,
    PrintStatus Status,
    string? OrderId,
    string TerminalId,
    string Message,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string>? JobIds = null,
    IReadOnlyList<PrintRouteFailureDto>? FailedRoutes = null);

public sealed record PrintResultDto(
    string RequestId,
    string AuditId,
    PrintKind Kind,
    PrintStatus Status,
    string Message,
    string? OrderId,
    DateTimeOffset UpdatedAtUtc,
    bool IsTerminal,
    IReadOnlyList<string>? JobIds = null,
    IReadOnlyList<PrintRouteFailureDto>? FailedRoutes = null,
    PrintAuditDto? Audit = null)
{
    public string StatusLabel => Status switch
    {
        PrintStatus.Queued => "queued",
        PrintStatus.Printing => "printing",
        PrintStatus.Printed => "printed",
        PrintStatus.PartialFailure => "partial failure",
        PrintStatus.Failed => "failed",
        PrintStatus.PrinterUnavailable => "printer unavailable",
        _ => "unknown"
    };

    public bool IsSuccessOutcome =>
        Status is PrintStatus.Printed or PrintStatus.Queued or PrintStatus.Printing;
}

public interface IPrintService
{
    Task<OperationResult<PrintResultDto>> SubmitAsync(PrintRequestDto request, CancellationToken cancellationToken = default);
    Task<OperationResult<PrintResultDto>> GetAsync(string requestId, CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<PrintAuditDto>>> GetRecentAuditAsync(int take = 20, CancellationToken cancellationToken = default);
}
