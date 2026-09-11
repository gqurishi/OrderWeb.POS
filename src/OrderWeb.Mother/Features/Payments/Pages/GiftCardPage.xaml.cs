using System.Globalization;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;
using OrderWeb.SharedUI.Views;

namespace POS_in_NET.Pages;

public partial class GiftCardPage : ContentPage
{
    private readonly OrderWebGiftCardApiService _giftCardApiService;
    private readonly GiftCardActivationQueueService _activationQueueService;
    private readonly ReceiptService? _receiptService;
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;

    private GiftCardFlowKind _activeFlow = GiftCardFlowKind.Redeem;
    private string? _activateCardNumber;
    private string? _activateCapturedPaymentKey;
    private string? _sellCapturedPaymentKey;
    private string? _topUpCapturedPaymentKey;
    private GiftCard? _topUpGiftCard;
    private GiftCard? _redeemGiftCard;

    public GiftCardPage()
    {
        InitializeComponent();

        TopBar.SetPageTitle("Gift Cards");

        _giftCardApiService = ServiceHelper.GetService<OrderWebGiftCardApiService>()
            ?? throw new InvalidOperationException("OrderWebGiftCardApiService not found");
        _activationQueueService = ServiceHelper.GetService<GiftCardActivationQueueService>()
            ?? throw new InvalidOperationException("GiftCardActivationQueueService not found");
        _receiptService = ServiceHelper.GetService<ReceiptService>();
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();

        Gift.FlowChanged += (_, flow) =>
        {
            _activeFlow = flow;
            TopBar.SetPageTitle($"Gift Cards - {Gift.FlowTitle}");
        };
        Gift.CloseRequested += OnCloseClicked;
        Gift.ActivateLookupRequested += OnActivateLookupClicked;
        Gift.ActivateRequested += OnActivateCardClicked;
        Gift.GenerateSellCardRequested += OnGenerateSellCardClicked;
        Gift.SellRequested += OnSellCardClicked;
        Gift.TopUpLookupRequested += OnTopUpLookupClicked;
        Gift.TopUpRequested += OnTopUpCardClicked;
        Gift.RedeemLookupRequested += OnRedeemLookupClicked;
        Gift.RedeemRequested += OnRedeemCardClicked;

        Gift.ShowFlow(GiftCardFlowKind.Redeem);
        TopBar.SetPageTitle($"Gift Cards - {Gift.FlowTitle}");
        NotificationService.Instance.NotificationRequested += OnNotificationRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!await SessionAccessGuard.RequireSignedInAsync(_authService))
        {
            return;
        }

        if (!_roleAccessService.IsManagerOrAdmin(_authService.CurrentUser?.Role))
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Only Manager and Admin can access Gift Cards.");
            await NavigationCoordinator.Shared.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
            return;
        }

        _ = FlushQueuedActivationsQuietlyAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        NotificationService.Instance.NotificationRequested -= OnNotificationRequested;
    }

    private async Task FlushQueuedActivationsQuietlyAsync()
    {
        try
        {
            var result = await _activationQueueService.FlushAsync();
            if (result.Success && result.FlushedCount > 0 && _activeFlow == GiftCardFlowKind.Activate)
            {
                GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, result.Message, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gift card activation queue flush skipped: {ex.Message}");
        }
    }

    private void OnNotificationRequested(object? sender, NotificationEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await ToastNotification.ShowAsync(e.Title, e.Message, e.Type, e.DurationMs);
        });
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        try
        {
            var dashboardRoute = _roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role);
            await NavigationCoordinator.Shared.NavigateShellAsync(dashboardRoute, animated: false, source: sender as VisualElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gift card close navigation failed: {ex.Message}");
            await AppAlertService.ShowAlertAsync("Navigation Error", "Could not return to the dashboard. Please try again.");
        }
    }

    private void OnGenerateSellCardClicked(object? sender, EventArgs e)
    {
        Gift.SellCardEntry.Text = BuildPosGiftCardNumber();
        _sellCapturedPaymentKey = null;
        Gift.SellReceiptLabel.Text = string.Empty;
        GiftCardView.SetFlowStatus(Gift.SellStatusLabel, "New POS gift card number generated.", false);
    }

    private async void OnActivateLookupClicked(object? sender, EventArgs e)
    {
        var cardNumber = Gift.ActivateCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurpose.Activate,
            Gift.ActivateLookupButton,
            Gift.ActivateCardEntry,
            Gift.ActivateStatusLabel,
            "Checking...");

        if (lookup == null)
        {
            _activateCardNumber = null;
            Gift.ActivateActionButton.IsEnabled = false;
            return;
        }

        _activateCardNumber = cardNumber;
        _activateCapturedPaymentKey = null;
        Gift.ActivateActionButton.IsEnabled = true;
        Gift.ActivateReceiptLabel.Text = string.Empty;
        ApplySuggestedAmount(lookup, Gift.ActivateAmountEntry);
    }

    private async void OnActivateCardClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activateCardNumber))
        {
            GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, "Lookup the stock card before activation.", true);
            return;
        }

        if (!TryReadAmount(Gift.ActivateAmountEntry.Text, out var amount))
        {
            GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, "Enter a valid activation amount.", true);
            return;
        }

        var orderId = GetOrCreateOrderId(Gift.ActivateOrderIdEntry, "ACT");
        var paymentMethod = GetPaymentMethod(Gift.ActivatePaymentMethodPicker);
        var paymentKey = BuildGiftCardPaymentKey(_activateCardNumber, amount, paymentMethod, orderId);
        if (!string.Equals(_activateCapturedPaymentKey, paymentKey, StringComparison.Ordinal))
        {
            if (!await TakeLocalPaymentAsync(paymentMethod, amount, Gift.ActivateStatusLabel, "activated"))
            {
                return;
            }

            _activateCapturedPaymentKey = paymentKey;
        }
        else
        {
            GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, "Local payment already taken. Retrying OrderWeb activation.", false);
        }

        await RunBusyAsync(Gift.ActivateActionButton, "Activating...", async () =>
        {
            var activationRequest = new GiftCardActivateRequest
            {
                CardNumber = _activateCardNumber,
                Amount = amount,
                PaymentMethod = paymentMethod,
                OrderId = orderId,
                TillOrderId = orderId,
                Description = "POS stock card activation"
            };

            var result = await _giftCardApiService.ActivateAsync(activationRequest, orderId);

            if (!result.Success)
            {
                var error = result.Error ?? result.Message ?? "Activation failed.";
                if (!result.CanQueueForRetry)
                {
                    GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, error, true);
                    return true;
                }

                var queueResult = await _activationQueueService.QueueAsync(activationRequest, orderId, error);
                if (queueResult.Success)
                {
                    GiftCardView.SetFlowStatus(
                        Gift.ActivateStatusLabel,
                        $"{queueResult.Message} Local payment was already taken; do not take payment again. Last error: {error}",
                        true);
                }
                else
                {
                    GiftCardView.SetFlowStatus(
                        Gift.ActivateStatusLabel,
                        $"Activation failed after local payment and could not be queued. {queueResult.Message} Last error: {error}",
                        true);
                }

                return true;
            }

            GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, result.Message ?? $"Activated {FormatMoney(amount)}.", false);
            ShowReceipt(Gift.ActivateReceiptLabel, result);
            await PrintOrderWebReceiptAsync(result, orderId, Gift.ActivateStatusLabel, "Activation");
            _activateCapturedPaymentKey = null;
            return false;
        });
    }

    private async void OnSellCardClicked(object? sender, EventArgs e)
    {
        var cardNumber = Gift.SellCardEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            cardNumber = BuildPosGiftCardNumber();
            Gift.SellCardEntry.Text = cardNumber;
            _sellCapturedPaymentKey = null;
            GiftCardView.SetFlowStatus(Gift.SellStatusLabel, "New POS gift card number generated.", false);
        }

        if (!TryReadAmount(Gift.SellAmountEntry.Text, out var amount))
        {
            GiftCardView.SetFlowStatus(Gift.SellStatusLabel, "Enter a valid sale amount.", true);
            return;
        }

        var orderId = GetOrCreateOrderId(Gift.SellOrderIdEntry, "SELL");
        var paymentMethod = GetPaymentMethod(Gift.SellPaymentMethodPicker);
        var paymentKey = BuildGiftCardPaymentKey(cardNumber, amount, paymentMethod, orderId);
        if (!string.Equals(_sellCapturedPaymentKey, paymentKey, StringComparison.Ordinal))
        {
            if (!await TakeLocalPaymentAsync(paymentMethod, amount, Gift.SellStatusLabel, "sold"))
            {
                return;
            }

            _sellCapturedPaymentKey = paymentKey;
        }
        else
        {
            GiftCardView.SetFlowStatus(Gift.SellStatusLabel, "Local payment already taken. Retrying OrderWeb sale.", false);
        }

        await RunBusyAsync(Gift.SellActionButton, "Selling...", async () =>
        {
            var result = await _giftCardApiService.SellAsync(
                new GiftCardSellRequest
                {
                    CardNumber = cardNumber,
                    Amount = amount,
                    PaymentMethod = paymentMethod,
                    OrderId = orderId,
                    TillOrderId = orderId,
                    Description = "POS new gift card sale"
                },
                orderId);

            if (!result.Success)
            {
                GiftCardView.SetFlowStatus(Gift.SellStatusLabel, result.Error ?? result.Message ?? "Gift card sale failed.", true);
                return true;
            }

            var soldCardNumber = result.GiftCard?.CardNumber ?? cardNumber;
            GiftCardView.SetFlowStatus(Gift.SellStatusLabel, result.Message ?? $"Sold card {soldCardNumber} for {FormatMoney(amount)}.", false);
            ShowReceipt(Gift.SellReceiptLabel, result);
            await PrintOrderWebReceiptAsync(result, orderId, Gift.SellStatusLabel, "Sale");
            _sellCapturedPaymentKey = null;
            return false;
        });
    }

    private async void OnTopUpLookupClicked(object? sender, EventArgs e)
    {
        var cardNumber = Gift.TopUpCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurpose.TopUp,
            Gift.TopUpLookupButton,
            Gift.TopUpCardEntry,
            Gift.TopUpStatusLabel,
            "Checking...");

        if (lookup?.GiftCard == null)
        {
            _topUpGiftCard = null;
            Gift.TopUpActionButton.IsEnabled = false;
            return;
        }

        _topUpGiftCard = lookup.GiftCard;
        _topUpCapturedPaymentKey = null;
        Gift.TopUpActionButton.IsEnabled = true;
        Gift.TopUpReceiptLabel.Text = string.Empty;
        ApplySuggestedAmount(lookup, Gift.TopUpAmountEntry);
    }

    private async void OnTopUpCardClicked(object? sender, EventArgs e)
    {
        if (_topUpGiftCard == null)
        {
            GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, "Lookup the gift card before top-up.", true);
            return;
        }

        if (!TryReadAmount(Gift.TopUpAmountEntry.Text, out var amount))
        {
            GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, "Enter a valid top-up amount.", true);
            return;
        }

        var orderId = GetOrCreateOrderId(Gift.TopUpOrderIdEntry, "TOPUP");
        var paymentMethod = GetPaymentMethod(Gift.TopUpPaymentMethodPicker);
        var paymentKey = BuildGiftCardPaymentKey(_topUpGiftCard.CardNumber, amount, paymentMethod, orderId);
        if (!string.Equals(_topUpCapturedPaymentKey, paymentKey, StringComparison.Ordinal))
        {
            if (!await TakeLocalPaymentAsync(paymentMethod, amount, Gift.TopUpStatusLabel, "topped up"))
            {
                return;
            }

            _topUpCapturedPaymentKey = paymentKey;
        }
        else
        {
            GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, "Local payment already taken. Retrying OrderWeb top-up.", false);
        }

        await RunBusyAsync(Gift.TopUpActionButton, "Topping up...", async () =>
        {
            var result = await _giftCardApiService.TopUpAsync(
                new GiftCardTopUpRequest
                {
                    CardNumber = _topUpGiftCard.CardNumber,
                    Amount = amount,
                    PaymentMethod = paymentMethod,
                    OrderId = orderId,
                    TillOrderId = orderId,
                    Description = "POS gift card top-up"
                },
                orderId);

            if (!result.Success)
            {
                GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, result.Error ?? result.Message ?? "Top-up failed.", true);
                return true;
            }

            var balance = result.EffectiveBalance.HasValue
                ? $" New balance: {FormatMoney(result.EffectiveBalance.Value)}."
                : string.Empty;
            GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, (result.Message ?? $"Added {FormatMoney(amount)}.") + balance, false);
            ShowReceipt(Gift.TopUpReceiptLabel, result);
            await PrintOrderWebReceiptAsync(result, orderId, Gift.TopUpStatusLabel, "Top-up");
            _topUpCapturedPaymentKey = null;
            return false;
        });
    }

    private async void OnRedeemLookupClicked(object? sender, EventArgs e)
    {
        var cardNumber = Gift.RedeemCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurpose.Redeem,
            Gift.RedeemLookupButton,
            Gift.RedeemCardEntry,
            Gift.RedeemStatusLabel,
            "Checking...");

        if (lookup?.GiftCard == null)
        {
            _redeemGiftCard = null;
            Gift.RedeemActionButton.IsEnabled = false;
            Gift.RedeemBalanceLabel.Text = "GBP 0.00";
            Gift.RedeemCardStatusLabel.Text = "No card checked yet";
            return;
        }

        _redeemGiftCard = lookup.GiftCard;
        Gift.RedeemActionButton.IsEnabled = true;
        Gift.RedeemBalanceLabel.Text = FormatMoney(_redeemGiftCard.Balance);
        Gift.RedeemCardStatusLabel.Text = $"{_redeemGiftCard.CardNumber} - {_redeemGiftCard.StatusDisplay.Trim()}";
        Gift.FocusRedeemAmountForKeypad();
    }

    private async void OnRedeemCardClicked(object? sender, EventArgs e)
    {
        if (_redeemGiftCard == null)
        {
            GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, "Lookup the gift card before redemption.", true);
            return;
        }

        if (!TryReadAmount(Gift.RedeemAmountEntry.Text, out var amount))
        {
            GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, "Enter a valid redemption amount.", true);
            return;
        }

        var orderId = GetOrCreateOrderId(Gift.RedeemOrderIdEntry, "REDEEM");
        await RunBusyAsync(Gift.RedeemActionButton, "Redeeming...", async () =>
        {
            GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, "Checking latest OrderWeb balance...", false);
            var latestLookup = await _giftCardApiService.LookupAsync(_redeemGiftCard.CardNumber, GiftCardLookupPurpose.Redeem);
            if (!latestLookup.Success || latestLookup.GiftCard == null)
            {
                GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, latestLookup.StatusMessage ?? "Gift card cannot be redeemed.", true);
                return true;
            }

            var latestCard = latestLookup.GiftCard;
            var latestCardNumber = string.IsNullOrWhiteSpace(latestCard.CardNumber)
                ? _redeemGiftCard.CardNumber
                : latestCard.CardNumber;
            _redeemGiftCard = latestCard;
            Gift.RedeemBalanceLabel.Text = FormatMoney(latestCard.Balance);
            Gift.RedeemCardStatusLabel.Text = $"{latestCardNumber} - {latestCard.StatusDisplay.Trim()}";

            if (amount > latestCard.Balance)
            {
                GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, $"Amount exceeds latest OrderWeb balance. Max: {FormatMoney(latestCard.Balance)}.", true);
                return true;
            }

            var result = await _giftCardApiService.RedeemAsync(
                new GiftCardRedeemRequest
                {
                    CardNumber = latestCardNumber,
                    Amount = amount,
                    OrderId = orderId,
                    Description = "POS gift card redemption"
                },
                orderId);

            if (!result.Success)
            {
                GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, result.Error ?? result.Message ?? "Redemption failed.", true);
                return true;
            }

            var redeemedAmount = result.EffectiveAmountRedeemed ?? amount;
            var remainingBalance = result.EffectiveRemainingBalance ?? Math.Max(0, latestCard.Balance - redeemedAmount);
            _redeemGiftCard.Balance = remainingBalance;
            Gift.RedeemBalanceLabel.Text = FormatMoney(remainingBalance);
            GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, $"Redeemed {FormatMoney(redeemedAmount)}. Remaining: {FormatMoney(remainingBalance)}.", false);
            return remainingBalance > 0;
        });
    }

    private async Task<GiftCardLookupResponse?> LookupForFlowAsync(
        string? cardNumber,
        string purpose,
        Button lookupButton,
        Entry cardEntry,
        Label statusLabel,
        string busyText)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            GiftCardView.SetFlowStatus(statusLabel, "Enter or scan a gift card number.", true);
            return null;
        }

        var previousText = lookupButton.Text;
        lookupButton.Text = busyText;
        lookupButton.IsEnabled = false;
        cardEntry.IsEnabled = false;

        try
        {
            var result = await _giftCardApiService.LookupAsync(cardNumber, purpose);
            var message = result.StatusMessage ?? "Lookup completed.";

            if (!result.CanProceed || !result.Success)
            {
                GiftCardView.SetFlowStatus(statusLabel, message, true);
                return null;
            }

            GiftCardView.SetFlowStatus(statusLabel, message, false);
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gift card lookup failed: {ex.Message}");
            GiftCardView.SetFlowStatus(statusLabel, "Gift card lookup failed. Check the connection and try again.", true);
            return null;
        }
        finally
        {
            lookupButton.Text = previousText;
            lookupButton.IsEnabled = true;
            cardEntry.IsEnabled = true;
        }
    }

    private async Task<bool> TakeLocalPaymentAsync(
        string paymentMethod,
        decimal amount,
        Label statusLabel,
        string actionPastTense)
    {
        switch (paymentMethod.Trim().ToLowerInvariant())
        {
            case "cash":
                GiftCardView.SetFlowStatus(statusLabel, $"Take cash payment locally: {FormatMoney(amount)}.", false);
                var cashDialog = new CashPaymentDialog();
                cashDialog.SetAmountDue(amount);
                var cashResult = await cashDialog.ShowAsync();
                if (cashResult.Success)
                {
                    GiftCardView.SetFlowStatus(statusLabel, $"Cash payment taken. Change: {FormatMoney(cashResult.Change)}.", false);
                    return true;
                }

                GiftCardView.SetFlowStatus(statusLabel, $"Cash payment was cancelled. Card was not {actionPastTense}.", true);
                return false;

            case "card":
                GiftCardView.SetFlowStatus(statusLabel, $"Take card payment locally: {FormatMoney(amount)}.", false);
                var cardDialog = new CardPaymentDialog();
                cardDialog.SetAmount(amount);
                var cardResult = await cardDialog.ShowAsync();
                if (cardResult.Success)
                {
                    GiftCardView.SetFlowStatus(statusLabel, "Card payment confirmed locally.", false);
                    return true;
                }

                GiftCardView.SetFlowStatus(statusLabel, $"Card payment was not completed. Card was not {actionPastTense}.", true);
                return false;

            default:
                GiftCardView.SetFlowStatus(statusLabel, "Choose cash or card as the local payment method.", true);
                return false;
        }
    }

    private async Task PrintOrderWebReceiptAsync(
        GiftCardTransactionResponse result,
        string orderId,
        Label statusLabel,
        string operationName)
    {
        if (result.Receipt == null || result.Receipt.Lines.Count == 0)
        {
            NotificationService.Instance.ShowWarning(
                "OrderWeb did not return receipt lines to print.",
                "Receipt Not Printed");
            return;
        }

        if (_receiptService == null)
        {
            NotificationService.Instance.ShowWarning(
                "Receipt printer service is not available.",
                "Receipt Not Printed");
            return;
        }

        var receiptText = string.Join(Environment.NewLine, result.Receipt.Lines);
        var printed = await _receiptService.PrintReceiptTextAsync(receiptText, orderId);
        if (printed)
        {
            GiftCardView.SetFlowStatus(statusLabel, $"{statusLabel.Text} Receipt queued.", false);
            NotificationService.Instance.ShowSuccess("Gift card receipt queued.", "Receipt");
        }
        else
        {
            NotificationService.Instance.ShowWarning(
                $"{operationName} succeeded, but the receipt could not be printed.",
                "Receipt Not Printed");
        }
    }

    private async Task RunBusyAsync(Button button, string busyText, Func<Task<bool>> action)
    {
        var previousText = button.Text;
        var wasEnabled = button.IsEnabled;
        var keepEnabled = wasEnabled;
        button.Text = busyText;
        button.IsEnabled = false;

        try
        {
            keepEnabled = await action();
        }
        finally
        {
            button.Text = previousText;
            button.IsEnabled = wasEnabled && keepEnabled;
        }
    }

    private static void ApplySuggestedAmount(GiftCardLookupResponse lookup, Entry amountEntry)
    {
        var suggestedAmount = lookup.Till?.SuggestedAmounts.FirstOrDefault(amount => amount > 0);
        if (suggestedAmount.HasValue && string.IsNullOrWhiteSpace(amountEntry.Text))
        {
            amountEntry.Text = suggestedAmount.Value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }

    private static bool TryReadAmount(string? input, out decimal amount)
    {
        var filtered = new string((input ?? string.Empty)
            .Where(c => char.IsDigit(c) || c == '.' || c == ',')
            .ToArray())
            .Replace(",", string.Empty);

        return decimal.TryParse(filtered, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
            && amount > 0;
    }

    private static string GetPaymentMethod(Picker picker)
    {
        return picker.SelectedItem?.ToString() ?? "cash";
    }

    private static string GetOrCreateOrderId(Entry orderEntry, string suffix)
    {
        if (!string.IsNullOrWhiteSpace(orderEntry.Text))
        {
            return orderEntry.Text.Trim();
        }

        var orderId = $"POS-GIFTCARD-{suffix}-{DateTime.Now:yyyyMMddHHmmss}";
        orderEntry.Text = orderId;
        return orderId;
    }

    private static string BuildGiftCardPaymentKey(
        string cardNumber,
        decimal amount,
        string paymentMethod,
        string orderId)
    {
        return string.Join(
            "|",
            cardNumber.Trim().ToUpperInvariant(),
            amount.ToString("F2", CultureInfo.InvariantCulture),
            paymentMethod.Trim().ToLowerInvariant(),
            orderId.Trim());
    }

    private static string BuildPosGiftCardNumber()
    {
        return $"POSGC{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid():N}".Substring(0, 26).ToUpperInvariant();
    }

    private static string FormatMoney(decimal amount)
    {
        return $"GBP {amount:F2}";
    }

    private static void ShowReceipt(Label receiptLabel, GiftCardTransactionResponse result)
    {
        if (result.Receipt?.Lines.Count > 0)
        {
            receiptLabel.Text = string.Join(Environment.NewLine, result.Receipt.Lines);
            return;
        }

        receiptLabel.Text = result.EffectiveBalance.HasValue
            ? $"Balance: {FormatMoney(result.EffectiveBalance.Value)}"
            : string.Empty;
    }
}

