using OrderWeb.Contracts.Printing;
using OrderWeb.Contracts.Results;
using Xunit;

namespace OrderWeb.Contracts.Tests;

public sealed class Phase11AuthoritativePrintTests
{
    private static PrintRequestDto Req(
        string requestId,
        PrintKind kind,
        string? orderId,
        string terminalId = "term-A",
        string sessionId = "session-1") =>
        new(requestId, terminalId, sessionId, kind, orderId, RequestedAtUtc: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Duplicate_request_id_returns_cached_result_without_double_print()
    {
        var sut = new AuthoritativePrintService();
        var request = Req("print-1", PrintKind.KitchenTicket, "order-1");

        var first = await sut.SubmitAsync(request);
        Assert.True(first.IsSuccess);
        Assert.Equal(PrintStatus.Printed, first.Value!.Status);

        var second = await sut.SubmitAsync(request);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.AuditId, second.Value!.AuditId);
        Assert.Equal(first.Value.Status, second.Value.Status);
        Assert.Equal(first.Value.JobIds, second.Value.JobIds);
    }

    [Fact]
    public async Task Printer_unavailable_returns_printer_unavailable_not_printed()
    {
        var sut = new AuthoritativePrintService();
        sut.SetPrinterUnavailable(PrintKind.CustomerReceipt);

        var result = await sut.SubmitAsync(Req("print-2", PrintKind.CustomerReceipt, "order-2"));
        Assert.True(result.IsSuccess);
        Assert.Equal(PrintStatus.PrinterUnavailable, result.Value!.Status);
        Assert.Equal("printer unavailable", result.Value.StatusLabel);
        Assert.True(result.Value.IsTerminal);
    }

    [Fact]
    public async Task Partial_kitchen_failure_returns_partial_failure_with_routes()
    {
        var sut = new AuthoritativePrintService { SimulatePartialKitchenFailure = true };
        var result = await sut.SubmitAsync(Req("print-3", PrintKind.KitchenTicket, "order-3"));

        Assert.True(result.IsSuccess);
        Assert.Equal(PrintStatus.PartialFailure, result.Value!.Status);
        Assert.Equal("partial failure", result.Value.StatusLabel);
        Assert.NotNull(result.Value.FailedRoutes);
        Assert.NotEmpty(result.Value.FailedRoutes!);
        Assert.NotNull(result.Value.Audit);
        Assert.Equal(result.Value.AuditId, result.Value.Audit!.AuditId);
    }

    [Fact]
    public async Task Missing_order_id_is_rejected_for_tickets_and_receipts()
    {
        var sut = new AuthoritativePrintService();
        var result = await sut.SubmitAsync(Req("print-4", PrintKind.KitchenTicket, orderId: null));

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Validation, result.Error!.Code);
    }

    [Fact]
    public async Task Cash_drawer_does_not_require_order_id_and_prints()
    {
        var sut = new AuthoritativePrintService();
        var result = await sut.SubmitAsync(Req("drawer-1", PrintKind.CashDrawer, orderId: null));

        Assert.True(result.IsSuccess);
        Assert.Equal(PrintStatus.Printed, result.Value!.Status);
        Assert.Contains(result.Value.AuditId, await AuditIds(sut));
    }

    [Fact]
    public async Task Get_returns_mother_audit_result_for_known_request()
    {
        var sut = new AuthoritativePrintService();
        var submitted = await sut.SubmitAsync(Req("print-5", PrintKind.Reprint, "order-5"));
        Assert.True(submitted.IsSuccess);

        var fetched = await sut.GetAsync("print-5");
        Assert.True(fetched.IsSuccess);
        Assert.Equal(submitted.Value!.Status, fetched.Value!.Status);
        Assert.Equal(submitted.Value.AuditId, fetched.Value.AuditId);
    }

    private static async Task<IReadOnlyList<string>> AuditIds(AuthoritativePrintService sut)
    {
        var audit = await sut.GetRecentAuditAsync();
        Assert.True(audit.IsSuccess);
        return audit.Value!.Select(a => a.AuditId).ToList();
    }
}
