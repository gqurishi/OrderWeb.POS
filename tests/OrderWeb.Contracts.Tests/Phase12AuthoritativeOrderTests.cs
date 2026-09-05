using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using Xunit;

namespace OrderWeb.Contracts.Tests;

public sealed class Phase12AuthoritativeOrderTests
{
    private static OrderMutationContext Ctx(string? orderId, long revision, string requestId)
        => new(requestId, "term-A", "session-1", orderId, revision, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Duplicate_request_id_returns_cached_result_without_double_apply()
    {
        var sut = new AuthoritativeOrderService();
        var open = await sut.OpenOrCreateAsync(new OpenOrderRequest(Ctx(null, 0, "open-1"), "12", 2, "Table"));
        Assert.True(open.IsSuccess);
        var order = open.Value!;

        var addReq = new AddOrderLineRequest(Ctx(order.Id, order.Revision, "add-1"), "prod-burger", 1, null, Array.Empty<string>());
        var first = await sut.AddLineAsync(addReq);
        Assert.True(first.IsSuccess);
        Assert.Single(first.Value!.Lines.Where(l => !l.IsVoided));

        var second = await sut.AddLineAsync(addReq);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Revision, second.Value!.Revision);
        Assert.Single(second.Value!.Lines.Where(l => !l.IsVoided));
    }

    [Fact]
    public async Task Collection_order_opens_without_table_and_send_returns_authoritative_totals()
    {
        var sut = new AuthoritativeOrderService();
        var open = await sut.OpenOrCreateAsync(
            new OpenOrderRequest(Ctx(null, 0, "open-col-1"), TableId: null, GuestCount: 1, OrderType: "Collection"));
        Assert.True(open.IsSuccess);
        Assert.Equal(0m, open.Value!.Totals.ServiceChargeTotal);

        var withLine = await sut.AddLineAsync(
            new AddOrderLineRequest(Ctx(open.Value.Id, open.Value.Revision, "add-col-1"), "prod-burger", 1, null, Array.Empty<string>()));
        Assert.True(withLine.IsSuccess);

        var sent = await sut.SendAsync(new SendOrderRequest(Ctx(withLine.Value!.Id, withLine.Value.Revision, "send-col-1")));
        Assert.True(sent.IsSuccess);
        Assert.False(sent.Value!.Totals.IsDisplayEstimate);
        Assert.True(sent.Value.Totals.GrandTotal > 0);
    }

    [Fact]
    public async Task Stale_revision_conflicts_so_terminals_cannot_overwrite_each_other()
    {
        var sut = new AuthoritativeOrderService();
        var open = await sut.OpenOrCreateAsync(new OpenOrderRequest(Ctx(null, 0, "open-2"), "1", 2, "Table"));
        var order = open.Value!;

        var ok = await sut.AddLineAsync(new AddOrderLineRequest(Ctx(order.Id, order.Revision, "a1"), "prod-cola", 1, null, Array.Empty<string>()));
        Assert.True(ok.IsSuccess);

        var stale = await sut.AddLineAsync(new AddOrderLineRequest(Ctx(order.Id, order.Revision, "a2"), "prod-burger", 1, null, Array.Empty<string>()));
        Assert.False(stale.IsSuccess);
        Assert.Equal(OperationErrorCode.Conflict, stale.Error!.Code);
    }

    [Fact]
    public async Task Mother_revalidates_unavailable_product_and_rejects_it()
    {
        var sut = new AuthoritativeOrderService();
        var open = await sut.OpenOrCreateAsync(new OpenOrderRequest(Ctx(null, 0, "open-3"), "2", 1, "Table"));
        var order = open.Value!;

        var result = await sut.AddLineAsync(new AddOrderLineRequest(
            Ctx(order.Id, order.Revision, "bad-prod"),
            "prod-unavailable",
            1,
            null,
            Array.Empty<string>()));

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.Validation, result.Error!.Code);
    }

    [Fact]
    public async Task Void_requires_manager_approval_then_succeeds()
    {
        var sut = new AuthoritativeOrderService();
        var open = await sut.OpenOrCreateAsync(new OpenOrderRequest(Ctx(null, 0, "open-4"), "1", 1, "Table"));
        var order = open.Value!;
        var added = await sut.AddLineAsync(new AddOrderLineRequest(Ctx(order.Id, order.Revision, "add"), "prod-burger", 1, null, Array.Empty<string>()));
        order = added.Value!;
        var lineId = order.Lines.Single(l => !l.IsVoided).Id;

        var denied = await sut.VoidLineAsync(new VoidOrderLineRequest(Ctx(order.Id, order.Revision, "void-1"), lineId, "waste", null));
        Assert.False(denied.IsSuccess);
        Assert.Equal(OperationErrorCode.PermissionDenied, denied.Error!.Code);

        var approved = await sut.VoidLineAsync(new VoidOrderLineRequest(
            Ctx(order.Id, order.Revision, "void-2"),
            lineId,
            "waste",
            AuthoritativeOrderService.ManagerApprovalCode));
        Assert.True(approved.IsSuccess);
        Assert.DoesNotContain(approved.Value!.Lines, l => !l.IsVoided);
    }

    [Fact]
    public async Task Mother_returns_authoritative_totals_not_display_estimates()
    {
        var sut = new AuthoritativeOrderService();
        var open = await sut.OpenOrCreateAsync(new OpenOrderRequest(Ctx(null, 0, "open-5"), "1", 1, "Table"));
        var order = open.Value!;
        var added = await sut.AddLineAsync(new AddOrderLineRequest(Ctx(order.Id, order.Revision, "add"), "prod-burger", 2, "no onion", Array.Empty<string>()));
        Assert.True(added.IsSuccess);
        Assert.False(added.Value!.Totals.IsDisplayEstimate);
        Assert.Equal(28.00m, added.Value!.Totals.Subtotal);
        Assert.Equal(5.60m, added.Value!.Totals.TaxTotal);
        Assert.Equal(33.60m, added.Value!.Totals.GrandTotal);
    }
}
