using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother-only payment boundary for Client terminals. It records only an
/// authoritative ledger result; it never fabricates an approved payment.
/// Cash is recorded locally. Gift card and loyalty redeem through OrderWeb cloud first.
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

        var method = NormalizeMethod(request.Method);
        if (method is not ("cash" or "card" or "gift_card" or "loyalty"))
        {
            return Failure(OperationErrorCode.ServerError, $"{request.Method} payments are not configured on Mother POS.");
        }

        if (method == "gift_card" && string.IsNullOrWhiteSpace(request.GiftCardNumber))
        {
            return Failure(OperationErrorCode.Validation, "A gift card number is required for gift card payments.");
        }

        if (method == "loyalty")
        {
            if (string.IsNullOrWhiteSpace(request.LoyaltyLookup) || request.LoyaltyPoints is null or <= 0)
            {
                return Failure(OperationErrorCode.Validation, "A loyalty lookup and positive points amount are required.");
            }

            var expectedAmount = Math.Round(request.LoyaltyPoints.Value / 100m, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(request.Amount - expectedAmount) > 0.009m)
            {
                return Failure(
                    OperationErrorCode.Validation,
                    $"Loyalty amount must match points (100 pts = £1). Expected £{expectedAmount:F2} for {request.LoyaltyPoints.Value} points.");
            }
        }

        var order = await _orders.GetOrderByExternalIdAsync(request.OrderId);
        if (order == null)
        {
            return Failure(OperationErrorCode.NotFound, "Mother POS could not find this order.");
        }

        if (request.ExpectedOrderRevision.HasValue)
        {
            var expected = request.ExpectedOrderRevision.Value;
            var stamp = order.UpdatedAt == default ? order.CreatedAt : order.UpdatedAt;
            var matchesTicks = expected == stamp.Ticks;
            var matchesVersion = expected == OrderWeb.Contracts.Access.CollectionOrderHubRules.ComputeOrderVersion(
                order.UpdatedAt,
                order.CreatedAt);
            if (!matchesTicks && !matchesVersion)
            {
                return Failure(OperationErrorCode.Conflict, "This order changed on Mother POS. Refresh it before taking payment.");
            }
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
                payment.Reference,
                order.GiftCardNumberMasked,
                order.GiftCardRemainingBalance,
                order.CustomerName,
                order.LoyaltyPointsRedeemed > 0 ? order.LoyaltyPointsRedeemed : null,
                order.LoyaltyBalanceAfter));
        }

        string? giftMasked = null;
        decimal? giftRemaining = null;
        string? giftRedeemMessage = null;
        decimal? previousBalance = null;
        decimal? newBalance = null;

        string? loyaltyCustomerName = null;
        int? loyaltyPointsRedeemed = null;
        int? loyaltyPointsRemaining = null;
        string? loyaltyRedeemMessage = null;

        if (method == "gift_card")
        {
            var giftApi = ServiceHelper.GetService<OrderWebGiftCardApiService>();
            if (giftApi == null)
            {
                return Failure(OperationErrorCode.ServerError, "Mother gift-card service is not available. Check OrderWeb cloud settings.");
            }

            var redeemIdempotency = string.IsNullOrWhiteSpace(request.GiftCardIdempotencyKey)
                ? $"gift-card:{request.OrderId}:{request.RequestId}:{request.Amount:F2}"
                : request.GiftCardIdempotencyKey.Trim();

            var redeem = await giftApi.RedeemAsync(
                new GiftCardRedeemRequest
                {
                    CardNumber = request.GiftCardNumber!.Trim(),
                    Amount = request.Amount,
                    OrderId = request.OrderId,
                    Description = $"Client POS gift card payment - {request.OrderId}"
                },
                redeemIdempotency);

            if (!redeem.Success)
            {
                return Failure(
                    OperationErrorCode.Validation,
                    redeem.Error ?? redeem.Message ?? "OrderWeb rejected the gift card redemption.");
            }

            giftRemaining = redeem.EffectiveRemainingBalance;
            previousBalance = redeem.PreviousBalance;
            newBalance = redeem.EffectiveRemainingBalance;
            giftRedeemMessage = redeem.Message;
            giftMasked = ClientPosGiftCardService.MaskCardNumber(request.GiftCardNumber);
        }
        else if (method == "loyalty")
        {
            var loyalty = ClientPosLoyaltyService.TryResolve();
            if (loyalty == null)
            {
                return Failure(OperationErrorCode.ServerError, "Mother loyalty service is not available. Check OrderWeb cloud settings.");
            }

            var points = request.LoyaltyPoints!.Value;
            var lookup = request.LoyaltyLookup!.Trim();
            var redeemIdempotency = string.IsNullOrWhiteSpace(request.LoyaltyIdempotencyKey)
                ? $"loyalty:{request.OrderId}:{lookup}:{points}"
                : request.LoyaltyIdempotencyKey.Trim();

            var redeem = await loyalty.RedeemPointsAsync(
                lookup,
                points,
                $"Client POS loyalty payment - {request.OrderId}",
                redeemIdempotency);

            if (!redeem.Success || redeem.Customer == null)
            {
                return Failure(
                    OperationErrorCode.Validation,
                    redeem.Error ?? redeem.Message ?? "OrderWeb rejected the loyalty redemption.");
            }

            loyaltyPointsRedeemed = points;
            loyaltyPointsRemaining = redeem.Customer.PointsBalance;
            loyaltyCustomerName = string.IsNullOrWhiteSpace(redeem.Customer.CustomerName)
                ? (redeem.Customer.Name ?? redeem.Customer.Phone)
                : redeem.Customer.CustomerName;
            loyaltyRedeemMessage = redeem.Message;
        }

        var tipAmount = Math.Max(0m, request.TipAmount);
        var tipTotal = Math.Max(tipAmount, Math.Max(0m, request.TipTotal));
        var recorded = await _orders.RecordPaymentLineAsync(
            request.OrderId,
            method,
            request.Amount,
            "approved",
            tipAmount: tipAmount,
            reference: reference,
            createdBy: request.TerminalId,
            metadata: new
            {
                source = "client",
                requestId = request.RequestId,
                terminalId = request.TerminalId,
                correlationId = request.CorrelationId,
                giftCardNumber = giftMasked,
                previousCardBalance = previousBalance,
                newCardBalance = newBalance,
                orderWebMessage = giftRedeemMessage ?? loyaltyRedeemMessage,
                giftCardIdempotencyKey = request.GiftCardIdempotencyKey,
                loyaltyLookup = request.LoyaltyLookup,
                loyaltyPoints = loyaltyPointsRedeemed,
                loyaltyPointsRemaining,
                loyaltyCustomerName,
                loyaltyIdempotencyKey = request.LoyaltyIdempotencyKey,
                tipAmount,
                tipTotal
            },
            // Mother UI allows approved amounts up to bill + tip (tip lives in amount + tip_amount column).
            maximumApprovedTotal: order.TotalAmount + tipTotal);

        if (!recorded)
        {
            return Failure(
                OperationErrorCode.Conflict,
                method == "gift_card"
                    ? "Gift card was redeemed in OrderWeb, but Mother could not record the payment line. Check the order on Mother before retrying."
                    : method == "loyalty"
                        ? "Loyalty points were redeemed in OrderWeb, but Mother could not record the payment line. Check the order on Mother before retrying."
                        : "Mother POS did not approve the payment. It may already be paid or exceed the remaining balance.");
        }

        if (method == "gift_card")
        {
            await TryUpdateOrderGiftSummaryAsync(order, giftMasked, request.Amount, giftRemaining);
        }
        else if (method == "loyalty")
        {
            await TryUpdateOrderLoyaltySummaryAsync(
                order,
                request.LoyaltyLookup!,
                loyaltyCustomerName,
                loyaltyPointsRedeemed ?? 0,
                request.Amount,
                loyaltyPointsRemaining);
        }
        else if (method is "cash" or "card")
        {
            await TryUpdateOrderTenderMethodAsync(order, method);
        }

        await TryCompleteTableOrderAfterPaymentAsync(order);

        return OperationResult<PaymentResultDto>.Success(
            new PaymentResultDto(
                request.RequestId,
                request.OrderId,
                "approved",
                request.Amount,
                reference,
                giftMasked,
                giftRemaining,
                loyaltyCustomerName,
                loyaltyPointsRedeemed,
                loyaltyPointsRemaining));
    }

    private async Task TryUpdateOrderGiftSummaryAsync(
        Order order,
        string? maskedNumber,
        decimal amountPaid,
        decimal? remainingBalance)
    {
        try
        {
            order.GiftCardNumberMasked = maskedNumber ?? order.GiftCardNumberMasked;
            order.GiftCardAmountPaid = (order.GiftCardAmountPaid ?? 0m) + amountPaid;
            order.GiftCardRemainingBalance = remainingBalance ?? order.GiftCardRemainingBalance;
            if (string.IsNullOrWhiteSpace(order.PaymentMethod) ||
                string.Equals(order.PaymentMethod, "cash", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.PaymentMethod, "card", StringComparison.OrdinalIgnoreCase))
            {
                order.PaymentMethod = "gift_card";
            }
            else if (!order.PaymentMethod.Contains("gift", StringComparison.OrdinalIgnoreCase))
            {
                order.PaymentMethod = $"{order.PaymentMethod}+gift_card";
            }

            await _orders.UpdateOrderAsync(order);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MotherPayment] Gift summary update failed: {ex.Message}");
        }
    }

    private async Task TryUpdateOrderLoyaltySummaryAsync(
        Order order,
        string lookup,
        string? customerName,
        int pointsRedeemed,
        decimal amountPaid,
        int? remainingPoints)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customerName))
            {
                order.CustomerName = customerName;
            }

            if (!string.IsNullOrWhiteSpace(lookup) && string.IsNullOrWhiteSpace(order.CustomerPhone))
            {
                order.CustomerPhone = lookup.Trim();
            }

            order.LoyaltyPointsRedeemed += pointsRedeemed;
            order.LoyaltyPointsDiscount += amountPaid;
            order.LoyaltyBalanceAfter = remainingPoints ?? order.LoyaltyBalanceAfter;

            if (string.IsNullOrWhiteSpace(order.PaymentMethod) ||
                string.Equals(order.PaymentMethod, "cash", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.PaymentMethod, "card", StringComparison.OrdinalIgnoreCase))
            {
                order.PaymentMethod = "loyalty";
            }
            else if (!order.PaymentMethod.Contains("loyalty", StringComparison.OrdinalIgnoreCase) &&
                     !order.PaymentMethod.Contains("points", StringComparison.OrdinalIgnoreCase))
            {
                order.PaymentMethod = $"{order.PaymentMethod}+loyalty";
            }

            await _orders.UpdateOrderAsync(order);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MotherPayment] Loyalty summary update failed: {ex.Message}");
        }
    }

    private async Task TryUpdateOrderTenderMethodAsync(Order order, string method)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(order.PaymentMethod) ||
                string.Equals(order.PaymentMethod, "cash", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.PaymentMethod, "card", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.PaymentMethod, "partial", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.PaymentMethod, "split", StringComparison.OrdinalIgnoreCase))
            {
                order.PaymentMethod = method;
            }
            else if (!order.PaymentMethod.Contains(method, StringComparison.OrdinalIgnoreCase))
            {
                order.PaymentMethod = $"{order.PaymentMethod}+{method}";
            }

            await _orders.UpdateOrderAsync(order);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MotherPayment] Tender method update failed: {ex.Message}");
        }
    }

    private async Task TryCompleteTableOrderAfterPaymentAsync(Order order)
    {
        try
        {
            var payments = await _orders.GetOrderPaymentsAsync(order.Id);
            var approved = payments
                .Where(payment => string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase))
                .Sum(payment => payment.Amount);
            if (approved + 0.009m < order.TotalAmount)
            {
                return;
            }

            await _orders.UpdateOrderStatusAsync(order.Id, OrderStatus.Completed);

            if (order.TableSessionId is > 0)
            {
                await new TableSessionService().CloseSessionForOrderAsync(
                    order.TableSessionId.Value,
                    "paid",
                    "Client POS");
            }

            // Mother UI prints receipt after full pay — same for Client→Mother payments.
            await TryPrintCustomerReceiptAsync(order);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MotherPayment] Table session release after pay failed: {ex.Message}");
        }
    }

    private static async Task TryPrintCustomerReceiptAsync(Order order)
    {
        try
        {
            var receiptService = ServiceHelper.GetService<ReceiptService>();
            if (receiptService is null)
            {
                System.Diagnostics.Debug.WriteLine("[MotherPayment] Receipt service unavailable after Client payment.");
                return;
            }

            var printed = await receiptService.PrintFullCustomerReceiptAsync(order);
            System.Diagnostics.Debug.WriteLine(
                printed
                    ? $"[MotherPayment] Customer receipt queued for order {order.OrderId}."
                    : $"[MotherPayment] Customer receipt failed for order {order.OrderId}.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MotherPayment] Receipt print after pay failed: {ex.Message}");
        }
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

    private static string NormalizeMethod(string? method)
    {
        var value = (method ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        return value switch
        {
            "gift" or "giftcard" or "gift_card" => "gift_card",
            "loyalty" or "points" or "loyalty_points" or "customer_points" => "loyalty",
            "cash" => "cash",
            "card" => "card",
            "split" => "split",
            _ => value
        };
    }

    private static OperationResult<PaymentResultDto> Failure(OperationErrorCode code, string message) =>
        OperationResult<PaymentResultDto>.Failure(new OperationError(code, message));
}
