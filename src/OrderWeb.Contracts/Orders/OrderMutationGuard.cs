using System.Collections.Concurrent;
using OrderWeb.Contracts.Results;

namespace OrderWeb.Contracts.Orders;

/// <summary>
/// Shared idempotency + optimistic concurrency helper for Mother order mutations.
/// </summary>
public sealed class OrderMutationGuard
{
    private readonly ConcurrentDictionary<string, object> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _revisions = new(StringComparer.Ordinal);

    public bool TryGetCachedResult(OrderMutationContext context, out OperationResult<OrderDto>? cached)
    {
        if (_cache.TryGetValue(Key(context), out var value) && value is OperationResult<OrderDto> typed)
        {
            cached = typed;
            return true;
        }

        cached = null;
        return false;
    }

    public OperationResult<OrderDto>? ValidateRevision(OrderMutationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.OrderId))
        {
            return null;
        }

        if (_revisions.TryGetValue(context.OrderId, out var current) && current != context.OrderRevision)
        {
            return OperationResult<OrderDto>.Fail(new OperationError(
                OperationErrorCode.Conflict,
                "Order was changed on another terminal. Refresh and try again.",
                $"expected={context.OrderRevision};actual={current}"));
        }

        return null;
    }

    public OperationResult<OrderDto> Complete(OrderMutationContext context, OrderDto order)
    {
        _revisions[order.Id] = order.Revision;
        var result = OperationResult<OrderDto>.Ok(order);
        _cache[Key(context)] = result;
        return result;
    }

    public OperationResult<OrderDto> Fail(OrderMutationContext context, OperationError error)
    {
        var result = OperationResult<OrderDto>.Fail(error);
        _cache[Key(context)] = result;
        return result;
    }

    public void Track(string orderId, long revision) => _revisions[orderId] = revision;

    private static string Key(OrderMutationContext context) => $"{context.TerminalId}|{context.RequestId}";
}
