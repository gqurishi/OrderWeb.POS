using System.Collections.Concurrent;
using OrderWeb.Contracts.Results;

namespace OrderWeb.Contracts.Printing;

/// <summary>
/// Mother-authoritative print processor. Duplicate <see cref="PrintRequestDto.RequestId"/>
/// returns the cached prior result. Client must never invent Printed/Failed locally.
/// </summary>
public sealed class AuthoritativePrintService : IPrintService
{
    private readonly ConcurrentDictionary<string, PrintResultDto> _byRequestId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PrintAuditDto> _audits =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool KitchenPrinterAvailable { get; set; } = true;
    public bool ReceiptPrinterAvailable { get; set; } = true;
    public bool CashDrawerAvailable { get; set; } = true;
    public bool SimulatePartialKitchenFailure { get; set; }

    public void SetPrinterUnavailable(PrintKind kind, bool unavailable = true)
    {
        switch (kind)
        {
            case PrintKind.KitchenTicket:
                KitchenPrinterAvailable = !unavailable;
                break;
            case PrintKind.CustomerReceipt:
            case PrintKind.Reprint:
                ReceiptPrinterAvailable = !unavailable;
                break;
            case PrintKind.CashDrawer:
                CashDrawerAvailable = !unavailable;
                break;
        }
    }

    public Task<OperationResult<PrintResultDto>> SubmitAsync(
        PrintRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return Task.FromResult(OperationResult<PrintResultDto>.Fail(
                OperationError.Validation("RequestId is required for print idempotency.")));
        }

        if (string.IsNullOrWhiteSpace(request.TerminalId) || string.IsNullOrWhiteSpace(request.SessionId))
        {
            return Task.FromResult(OperationResult<PrintResultDto>.Fail(
                OperationError.Validation("TerminalId and SessionId are required.")));
        }

        if (request.Kind is PrintKind.KitchenTicket or PrintKind.CustomerReceipt or PrintKind.Reprint
            && string.IsNullOrWhiteSpace(request.OrderId))
        {
            return Task.FromResult(OperationResult<PrintResultDto>.Fail(
                OperationError.Validation("OrderId is required for ticket and receipt printing.")));
        }

        lock (_gate)
        {
            if (_byRequestId.TryGetValue(request.RequestId, out var cached))
            {
                return Task.FromResult(OperationResult<PrintResultDto>.Ok(cached));
            }

            var now = request.RequestedAtUtc ?? DateTimeOffset.UtcNow;
            var auditId = Guid.NewGuid().ToString("N");

            if (IsUnavailable(request.Kind))
            {
                var unavailable = CreateResult(
                    request,
                    auditId,
                    PrintStatus.PrinterUnavailable,
                    $"{FormatKind(request.Kind)} printer is unavailable on Mother.",
                    now,
                    isTerminal: true);
                Store(unavailable);
                return Task.FromResult(OperationResult<PrintResultDto>.Ok(unavailable));
            }

            var queued = CreateResult(
                request,
                auditId,
                PrintStatus.Queued,
                $"Mother queued {FormatKind(request.Kind)}.",
                now,
                isTerminal: false,
                jobIds: [$"job-{auditId[..8]}"]);
            Store(queued);

            var final = ProcessOnMother(request, queued, now);
            Store(final);
            return Task.FromResult(OperationResult<PrintResultDto>.Ok(final));
        }
    }

    public Task<OperationResult<PrintResultDto>> GetAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return Task.FromResult(OperationResult<PrintResultDto>.Fail(
                OperationError.Validation("RequestId is required.")));
        }

        if (_byRequestId.TryGetValue(requestId, out var result))
        {
            return Task.FromResult(OperationResult<PrintResultDto>.Ok(result));
        }

        return Task.FromResult(OperationResult<PrintResultDto>.Fail(
            OperationError.NotFound($"Print request '{requestId}' was not found.")));
    }

    public Task<OperationResult<IReadOnlyList<PrintAuditDto>>> GetRecentAuditAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PrintAuditDto> list = _audits.Values
            .OrderByDescending(a => a.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToList();
        return Task.FromResult(OperationResult<IReadOnlyList<PrintAuditDto>>.Ok(list));
    }

    private PrintResultDto ProcessOnMother(PrintRequestDto request, PrintResultDto queued, DateTimeOffset now)
    {
        if (request.Kind == PrintKind.KitchenTicket && SimulatePartialKitchenFailure)
        {
            IReadOnlyList<PrintRouteFailureDto> failures =
            [
                new("bar", "Printer offline on route."),
                new("dessert", "Printer offline on route.")
            ];

            return CreateResult(
                request,
                queued.AuditId,
                PrintStatus.PartialFailure,
                "Kitchen ticket partially printed. Some routes failed.",
                now.AddMilliseconds(40),
                isTerminal: true,
                jobIds: queued.JobIds,
                failedRoutes: failures);
        }

        return CreateResult(
            request,
            queued.AuditId,
            PrintStatus.Printed,
            request.Kind switch
            {
                PrintKind.KitchenTicket => "Kitchen ticket printed by Mother.",
                PrintKind.CustomerReceipt => "Customer receipt printed by Mother.",
                PrintKind.Reprint => "Reprint completed by Mother.",
                PrintKind.CashDrawer => "Cash drawer opened by Mother.",
                _ => "Print completed by Mother."
            },
            now.AddMilliseconds(40),
            isTerminal: true,
            jobIds: queued.JobIds);
    }

    private bool IsUnavailable(PrintKind kind) => kind switch
    {
        PrintKind.KitchenTicket => !KitchenPrinterAvailable,
        PrintKind.CustomerReceipt or PrintKind.Reprint => !ReceiptPrinterAvailable,
        PrintKind.CashDrawer => !CashDrawerAvailable,
        _ => false
    };

    private void Store(PrintResultDto result)
    {
        _byRequestId[result.RequestId] = result;
        if (result.Audit is not null)
        {
            _audits[result.AuditId] = result.Audit;
        }
    }

    private static PrintResultDto CreateResult(
        PrintRequestDto request,
        string auditId,
        PrintStatus status,
        string message,
        DateTimeOffset at,
        bool isTerminal,
        IReadOnlyList<string>? jobIds = null,
        IReadOnlyList<PrintRouteFailureDto>? failedRoutes = null)
    {
        var audit = new PrintAuditDto(
            auditId,
            request.RequestId,
            request.Kind,
            status,
            request.OrderId,
            request.TerminalId,
            message,
            at,
            at,
            jobIds,
            failedRoutes);

        return new PrintResultDto(
            request.RequestId,
            auditId,
            request.Kind,
            status,
            message,
            request.OrderId,
            at,
            isTerminal,
            jobIds,
            failedRoutes,
            audit);
    }

    private static string FormatKind(PrintKind kind) => kind switch
    {
        PrintKind.KitchenTicket => "kitchen ticket",
        PrintKind.CustomerReceipt => "customer receipt",
        PrintKind.Reprint => "reprint",
        PrintKind.CashDrawer => "cash drawer open",
        _ => kind.ToString()
    };
}
