using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother-only payment boundary for Client terminals. It records only an
/// authoritative ledger result; it never fabricates an approved payment.
/// Provider-backed methods intentionally remain unavailable until a provider
/// implementation is registered here.
/// </summary>
public sealed class MotherPaymentService : IPaymentService
{
    private readonly OrderService _orders;

    public MotherPaymentService()
        : this(new OrderService())
    {
    }

    public MotherPaymentService(OrderService orders)
    {
        _orders = orders;
    }

    public async Task<OperationResult<PaymentResultDto>> TakePaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) ||
            string.IsNullOrWhiteSpace(request.OrderId) ||
            request.Amount <= 0)
        {
            return Failure(OperationErrorCode.Validation, "A payment request ID, order ID, and positive amount are required.");
        }

        var method = request.Method.Trim().ToLowerInvariant();
        if (method is not "cash")
        {
            // A card/gift-card flow must be completed by its actual provider.
            // Returning an error is safer than recording a pretend approval.
            return Failure(OperationErrorCode.ServerError, $"{request.Method} payments are not configured on Mother POS.");
        }

        var order = await _orders.GetOrderByExternalIdAsync(request.OrderId);
        if (order == null)
        {
            return Failure(OperationErrorCode.NotFound, "Mother POS could not find this order.");
        }

        // The client receives this value with the order snapshot.  Rejecting a
        // stale snapshot prevents a terminal charging a changed/closed order.
        if (request.ExpectedOrderRevision.HasValue && request.ExpectedOrderRevision.Value != order.UpdatedAt.Ticks)
        {
            return Failure(OperationErrorCode.Conflict, "This order changed on Mother POS. Refresh it before taking payment.");
        }

        var reference = $"client:{request.TerminalId}:{request.RequestId}";
        var existing = await _orders.FindPaymentByReferenceAsync(reference);
        if (existing is not null)
        {
            var payment = existing.Value.Payment;
            if (!string.Equals(payment.PaymentMethod, method, StringComparison.OrdinalIgnoreCase) || payment.Amount != request.Amount)
            {
                return Failure(OperationErrorCode.Conflict, "This payment request ID was already used with different payment details.");
            }
            return OperationResult<PaymentResultDto>.Success(new PaymentResultDto(
                request.RequestId,
                existing.Value.OrderId,
                payment.Status,
                payment.Amount,
                payment.Reference));
        }

        var recorded = await _orders.RecordPaymentLineAsync(
            request.OrderId,
            method,
            request.Amount,
            "approved",
            reference: reference,
            createdBy: request.TerminalId,
            metadata: new { source = "client", requestId = request.RequestId, terminalId = request.TerminalId, correlationId = request.CorrelationId },
            maximumApprovedTotal: order.TotalAmount);

        if (!recorded)
        {
            return Failure(OperationErrorCode.Conflict, "Mother POS did not approve the payment. It may already be paid or exceed the remaining balance.");
        }

        return OperationResult<PaymentResultDto>.Success(
            new PaymentResultDto(request.RequestId, request.OrderId, "approved", request.Amount, reference));
    }

    public async Task<OperationResult<PaymentResultDto>> GetResultAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return Failure(OperationErrorCode.Validation, "A payment request ID is required.");
        }

        var found = await _orders.FindPaymentByReferenceAsync(paymentId);
        if (found is null)
        {
            return Failure(OperationErrorCode.NotFound, "Mother POS has no final result for this payment request yet.");
        }

        var (orderId, payment) = found.Value;
        return OperationResult<PaymentResultDto>.Success(new PaymentResultDto(
            paymentId,
            orderId,
            payment.Status,
            payment.Amount,
            payment.Reference));
    }

    private static OperationResult<PaymentResultDto> Failure(OperationErrorCode code, string message) =>
        OperationResult<PaymentResultDto>.Failure(new OperationError(code, message));
}
