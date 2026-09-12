using OrderWeb.Client.Dialogs;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Payments;
using OrderWeb.SharedUI.ViewModels;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Payments;

public partial class PaymentPage : ContentPage
{
    private readonly decimal _totalDue;
    private readonly string? _orderId;
    private readonly long? _expectedOrderRevision;
    private readonly decimal _tipAmount;
    private readonly decimal _tipTotal;
    private readonly ClientPaymentService _paymentService = new();
    private PaymentViewModel? _sharedPayment;
    private string _selectedMethod = "Cash";
    private bool _isSubmitting;

    public PaymentPage() : this(42.80m)
    {
    }

    public PaymentPage(
        decimal totalDue,
        string? orderId = null,
        long? expectedOrderRevision = null,
        bool allowSplit = true,
        decimal tipAmount = 0m,
        decimal tipTotal = 0m)
    {
        _totalDue = totalDue;
        _orderId = orderId;
        _expectedOrderRevision = expectedOrderRevision;
        _tipAmount = Math.Max(0m, tipAmount);
        _tipTotal = Math.Max(_tipAmount, Math.Max(0m, tipTotal));
        InitializeComponent();
        TopBar.LogoutClicked += async (_, _) => await ClientSignOut.RequestAsync(this);
        // Wizard already chose the amount for Table; COL/DEL are full-bill only.
        var (sharedPayment, viewModel) = PaymentTenderSurface.Create(
            _totalDue,
            allowSplit: allowSplit,
            // Supported redeem surface. Order Place tender Loyalty is deferred
            // (OrderPlaceLoyaltyEarnRules.OrderPlacePaymentLoyaltyEnabled = false) — do not delete this path.
            showLoyalty: true); // Supported redeem surface (Order Place tender Loyalty is deferred — see OrderPlaceLoyaltyEarnRules)
        _sharedPayment = viewModel;
        _sharedPayment.StatusCheckRequested += OnSharedPaymentStatusCheckRequested;
        sharedPayment.SubmissionRequested += OnSharedPaymentRequested;
        Content = sharedPayment;
    }

    private async void OnSharedPaymentRequested(object? sender, PaymentSubmission submission)
    {
        if (_sharedPayment is null) return;
        if (string.IsNullOrWhiteSpace(_orderId))
        {
            _sharedPayment.ApplyAuthoritativeResult(false, "This Client order is not yet a Mother order. No payment has been taken.");
            return;
        }

        string? giftCardNumber = null;
        string? loyaltyLookup = null;
        int? loyaltyPoints = null;
        var amount = submission.Amount;
        var method = submission.Method;

        if (string.Equals(NormalizeMethod(method), "gift_card", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeMethod(method), "split", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeMethod(_sharedPayment.SelectedMethod), "gift_card", StringComparison.OrdinalIgnoreCase))
        {
            method = "gift_card";
            var gift = await PromptGiftCardAsync(amount);
            if (gift is null || !gift.Success || string.IsNullOrWhiteSpace(gift.CardNumber))
            {
                _sharedPayment.ApplyAuthoritativeResult(false, gift?.Message ?? "Gift card payment cancelled. No balance was changed.");
                return;
            }

            giftCardNumber = gift.CardNumber;
            amount = gift.AmountApplied;
            _sharedPayment.Message = "Redeeming gift card on Mother / OrderWeb…";
        }
        else if (string.Equals(NormalizeMethod(method), "loyalty", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(NormalizeMethod(method), "split", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(NormalizeMethod(_sharedPayment.SelectedMethod), "loyalty", StringComparison.OrdinalIgnoreCase))
        {
            method = "loyalty";
            var loyalty = await PromptLoyaltyAsync(amount);
            if (loyalty is null || !loyalty.Success || string.IsNullOrWhiteSpace(loyalty.Lookup) || loyalty.Points <= 0)
            {
                _sharedPayment.ApplyAuthoritativeResult(false, loyalty?.Message ?? "Loyalty payment cancelled. No points were changed.");
                return;
            }

            loyaltyLookup = loyalty.Lookup;
            loyaltyPoints = loyalty.Points;
            amount = loyalty.AmountApplied;
            _sharedPayment.Message = "Redeeming loyalty points on Mother / OrderWeb…";
        }

        if (_sharedPayment is { AllowSplit: false } && amount + 0.009m < _totalDue &&
            (string.Equals(NormalizeMethod(method), "gift_card", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(NormalizeMethod(method), "loyalty", StringComparison.OrdinalIgnoreCase)))
        {
            _sharedPayment.ApplyAuthoritativeResult(
                false,
                "Collection and Delivery require payment of the full bill. Use a method that covers the full amount, or pay on Mother.");
            return;
        }

        var result = await _paymentService.TakePaymentAsync(
            _orderId,
            method,
            amount,
            submission.RequestId,
            expectedOrderRevision: _expectedOrderRevision,
            correlationId: submission.CorrelationId,
            giftCardNumber: giftCardNumber,
            giftCardIdempotencyKey: giftCardNumber == null
                ? null
                : $"gift-card:{_orderId}:{giftCardNumber}:{amount:F2}",
            loyaltyLookup: loyaltyLookup,
            loyaltyPoints: loyaltyPoints,
            loyaltyIdempotencyKey: loyaltyLookup == null || loyaltyPoints is null
                ? null
                : $"loyalty:{_orderId}:{loyaltyLookup}:{loyaltyPoints}",
            tipAmount: _tipAmount,
            tipTotal: _tipTotal);
        _sharedPayment.ApplyAuthoritativeResult(result.Approved, result.Message, result.IsUnknown);
        if (giftCardNumber != null)
        {
            ClientGiftCardDiagnostics.Record(
                "order-pay",
                result.Approved,
                result.Message,
                result.Approved ? null : (result.IsUnknown ? GiftCardErrorCodes.OfflineMother : GiftCardErrorCodes.Unknown));
        }

        if (loyaltyLookup != null)
        {
            var queued = !result.Approved &&
                         result.Message?.Contains("queued", StringComparison.OrdinalIgnoreCase) == true;
            ClientLoyaltyDiagnostics.Record(
                "order-pay",
                result.Approved,
                result.Message,
                result.Approved
                    ? null
                    : (result.IsUnknown
                        ? LoyaltyErrorCodes.OfflineMother
                        : queued
                            ? LoyaltyErrorCodes.Queued
                            : LoyaltyErrorCodes.Unknown),
                queued);
        }
    }

    private async Task<GiftCardOrderPaymentResult?> PromptGiftCardAsync(decimal amountDue)
    {
        var gift = await PaymentWizard.ShowGiftCardAsync(amountDue, LookupGiftCardAsync, this);
        if (!gift.Success || string.IsNullOrWhiteSpace(gift.CardNumber) || gift.AmountApplied <= 0)
        {
            return new GiftCardOrderPaymentResult
            {
                Success = false,
                Message = gift.Message ?? "Gift card payment cancelled. No balance was changed."
            };
        }

        return new GiftCardOrderPaymentResult
        {
            Success = true,
            CardNumber = gift.CardNumber,
            AmountApplied = gift.AmountApplied,
            Message = gift.Message
        };
    }

    private static async Task<PaymentGiftCardLookupResult> LookupGiftCardAsync(
        string cardNumber,
        CancellationToken cancellationToken)
    {
        var giftCards = new MotherGiftCardClient();
        var lookup = await giftCards.LookupAsync(cardNumber, GiftCardLookupPurposes.Redeem, cancellationToken);
        if (!lookup.Success || lookup.GiftCard == null || !lookup.CanProceed)
        {
            return new PaymentGiftCardLookupResult
            {
                Success = false,
                CanUse = false,
                Message = lookup.Error ?? lookup.Message ?? "Gift card not found."
            };
        }

        var card = lookup.GiftCard;
        return new PaymentGiftCardLookupResult
        {
            Success = true,
            CanUse = card.CanUse,
            CardNumber = string.IsNullOrWhiteSpace(card.CardNumber) ? cardNumber : card.CardNumber,
            Balance = card.Balance,
            Message = card.CanUse
                ? (lookup.Message ?? "Gift card verified.")
                : $"Gift card cannot be used: {card.Status}"
        };
    }

    private async Task<LoyaltyOrderPaymentResult?> PromptLoyaltyAsync(decimal amountDue)
    {
        var dialog = new LoyaltyOrderPaymentDialog(amountDue);
        await Navigation.PushModalAsync(dialog, false);
        return await dialog.WaitAsync();
    }

    private async void OnSharedPaymentStatusCheckRequested(object? sender, string requestId)
    {
        if (_sharedPayment is null) return;
        var result = await _paymentService.GetPaymentStatusAsync(requestId);
        _sharedPayment.ApplyAuthoritativeResult(result.Approved, result.Message, result.IsUnknown);
    }

    private void OnMethodClicked(object sender, EventArgs e)
    {
        if (sender is Button button && !string.IsNullOrWhiteSpace(button.Text))
        {
            SelectMethod(button.Text);
        }
    }

    private void SelectMethod(string method)
    {
        _selectedMethod = method;
        MethodLabel.Text = $"Method: {method}";
        foreach (var button in new[] { CashButton, CardButton, GiftCardButton, SplitButton })
        {
            button.BackgroundColor = button.Text == method ? Color.FromArgb("#6366F1") : Color.FromArgb("#F1F5F9");
            button.TextColor = button.Text == method ? Colors.White : Color.FromArgb("#334155");
            button.FontFamily = "OpenSansSemibold";
            button.FontSize = 17;
            button.CornerRadius = 8;
            button.HeightRequest = 60;
        }

        TenderedEntry.Text = method == "Cash" ? _totalDue.ToString("F2") : _totalDue.ToString("F2");
        UpdateChange();
    }

    private void OnTenderedChanged(object sender, TextChangedEventArgs e)
    {
        UpdateChange();
    }

    private void UpdateChange()
    {
        var tendered = decimal.TryParse(TenderedEntry.Text, out var parsed) ? parsed : 0m;
        var change = Math.Max(0m, tendered - _totalDue);
        ChangeLabel.Text = Money(change);
    }

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        if (_isSubmitting)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_orderId))
        {
            await DisplayAlert("Payment unavailable", "This Client order is not yet a Mother order. No payment has been taken.", "OK");
            return;
        }

        _isSubmitting = true;
        ConfirmButton.IsEnabled = false;
        PrintStatus.SetStatus("queued", "Waiting for Mother payment result...");
        try
        {
            var amount = _selectedMethod == "Cash" && decimal.TryParse(TenderedEntry.Text, out var tendered)
                ? Math.Min(tendered, _totalDue)
                : _totalDue;
            string? giftCardNumber = null;
            string? loyaltyLookup = null;
            int? loyaltyPoints = null;
            var method = _selectedMethod;
            if (string.Equals(NormalizeMethod(method), "gift_card", StringComparison.OrdinalIgnoreCase))
            {
                var gift = await PromptGiftCardAsync(amount);
                if (gift is null || !gift.Success || string.IsNullOrWhiteSpace(gift.CardNumber))
                {
                    PrintStatus.SetStatus("failed", gift?.Message ?? "Gift card payment cancelled.");
                    return;
                }

                giftCardNumber = gift.CardNumber;
                amount = gift.AmountApplied;
                method = "gift_card";
            }
            else if (string.Equals(NormalizeMethod(method), "loyalty", StringComparison.OrdinalIgnoreCase))
            {
                var loyalty = await PromptLoyaltyAsync(amount);
                if (loyalty is null || !loyalty.Success || string.IsNullOrWhiteSpace(loyalty.Lookup) || loyalty.Points <= 0)
                {
                    PrintStatus.SetStatus("failed", loyalty?.Message ?? "Loyalty payment cancelled.");
                    return;
                }

                loyaltyLookup = loyalty.Lookup;
                loyaltyPoints = loyalty.Points;
                amount = loyalty.AmountApplied;
                method = "loyalty";
            }

            var result = await _paymentService.TakePaymentAsync(
                _orderId,
                method,
                amount,
                giftCardNumber: giftCardNumber,
                loyaltyLookup: loyaltyLookup,
                loyaltyPoints: loyaltyPoints,
                tipAmount: _tipAmount,
                tipTotal: _tipTotal);
            if (!result.Approved)
            {
                PrintStatus.SetStatus("failed", result.Message);
                await DisplayAlert("Payment not approved", result.Message, "OK");
                return;
            }

            PrintStatus.SetStatus(PrintReceiptCheckBox.IsChecked ? "queued" : "sent",
                PrintReceiptCheckBox.IsChecked ? "Payment approved. Receipt must be requested from Mother." : "Payment approved by Mother.");
            await DisplayAlert("Payment approved", result.Message, "OK");
        }
        finally
        {
            _isSubmitting = false;
            ConfirmButton.IsEnabled = true;
        }
    }

    private static string NormalizeMethod(string? method) =>
        (method ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_") switch
        {
            "gift" or "giftcard" or "gift_card" => "gift_card",
            "loyalty" or "points" or "loyalty_points" or "customer_points" => "loyalty",
            var other => other
        };

    private static string Money(decimal value) => $"£{value:F2}";
}
