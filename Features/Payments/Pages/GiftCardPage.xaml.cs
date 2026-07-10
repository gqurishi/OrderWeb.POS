using System.Globalization;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;

namespace POS_in_NET.Pages;

public partial class GiftCardPage : ContentPage
{
    private static readonly string[] PaymentMethods = { "cash", "card" };

    private readonly OrderWebGiftCardApiService _giftCardApiService;
    private readonly GiftCardActivationQueueService _activationQueueService;
    private readonly ReceiptService? _receiptService;
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;

    private GiftCardFlow _activeFlow = GiftCardFlow.Activate;
    private string? _activateCardNumber;
    private string? _activateCapturedPaymentKey;
    private string? _sellCapturedPaymentKey;
    private string? _topUpCapturedPaymentKey;
    private GiftCard? _topUpGiftCard;
    private GiftCard? _redeemGiftCard;

    public GiftCardPage()
    {
        InitializeComponent();

        TopBar.SetPageTitle("Gift Cards - Activate");

        _giftCardApiService = ServiceHelper.GetService<OrderWebGiftCardApiService>()
            ?? throw new InvalidOperationException("OrderWebGiftCardApiService not found");
        _activationQueueService = ServiceHelper.GetService<GiftCardActivationQueueService>()
            ?? throw new InvalidOperationException("GiftCardActivationQueueService not found");
        _receiptService = ServiceHelper.GetService<ReceiptService>();
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();

        InitializePaymentPickers();
        ShowFlow(GiftCardFlow.Activate);

        NotificationService.Instance.NotificationRequested += OnNotificationRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_roleAccessService.IsManagerOrAdmin(_authService.CurrentUser?.Role))
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Only Manager and Admin can access Gift Cards.");
            await Shell.Current.GoToAsync($"//{_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role)}");
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
            if (result.Success && result.FlushedCount > 0 && _activeFlow == GiftCardFlow.Activate)
            {
                SetFlowStatus(ActivateStatusLabel, result.Message, false);
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
            await Shell.Current.GoToAsync($"//{dashboardRoute}", false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gift card close navigation failed: {ex.Message}");
            await AppAlertService.ShowAlertAsync("Navigation Error", "Could not return to the dashboard. Please try again.");
        }
    }

    private void OnActivateFlowClicked(object sender, EventArgs e) => ShowFlow(GiftCardFlow.Activate);

    private void OnSellFlowClicked(object sender, EventArgs e) => ShowFlow(GiftCardFlow.Sell);

    private void OnTopUpFlowClicked(object sender, EventArgs e) => ShowFlow(GiftCardFlow.TopUp);

    private void OnRedeemFlowClicked(object sender, EventArgs e) => ShowFlow(GiftCardFlow.Redeem);

    private void OnGenerateSellCardClicked(object sender, EventArgs e)
    {
        SellCardEntry.Text = BuildPosGiftCardNumber();
        _sellCapturedPaymentKey = null;
        SellReceiptLabel.Text = string.Empty;
        SetFlowStatus(SellStatusLabel, "New POS gift card number generated.", false);
    }

    private async void OnActivateLookupClicked(object sender, EventArgs e)
    {
        var cardNumber = ActivateCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurpose.Activate,
            ActivateLookupButton,
            ActivateCardEntry,
            ActivateStatusLabel,
            "Checking...");

        if (lookup == null)
        {
            _activateCardNumber = null;
            ActivateActionButton.IsEnabled = false;
            return;
        }

        _activateCardNumber = cardNumber;
        _activateCapturedPaymentKey = null;
        ActivateActionButton.IsEnabled = true;
        ActivateReceiptLabel.Text = string.Empty;
        ApplySuggestedAmount(lookup, ActivateAmountEntry);
    }

    private async void OnActivateCardClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activateCardNumber))
        {
            SetFlowStatus(ActivateStatusLabel, "Lookup the stock card before activation.", true);
            return;
        }

        if (!TryReadAmount(ActivateAmountEntry.Text, out var amount))
        {
            SetFlowStatus(ActivateStatusLabel, "Enter a valid activation amount.", true);
            return;
        }

        var orderId = GetOrCreateOrderId(ActivateOrderIdEntry, "ACT");
        var paymentMethod = GetPaymentMethod(ActivatePaymentMethodPicker);
        var paymentKey = BuildGiftCardPaymentKey(_activateCardNumber, amount, paymentMethod, orderId);
        if (!string.Equals(_activateCapturedPaymentKey, paymentKey, StringComparison.Ordinal))
        {
            if (!await TakeLocalPaymentAsync(paymentMethod, amount, ActivateStatusLabel, "activated"))
            {
                return;
            }

            _activateCapturedPaymentKey = paymentKey;
        }
        else
        {
            SetFlowStatus(ActivateStatusLabel, "Local payment already taken. Retrying OrderWeb activation.", false);
        }

        await RunBusyAsync(ActivateActionButton, "Activating...", async () =>
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
                    SetFlowStatus(ActivateStatusLabel, error, true);
                    return true;
                }

                var queueResult = await _activationQueueService.QueueAsync(activationRequest, orderId, error);
                if (queueResult.Success)
                {
                    SetFlowStatus(
                        ActivateStatusLabel,
                        $"{queueResult.Message} Local payment was already taken; do not take payment again. Last error: {error}",
                        true);
                }
                else
                {
                    SetFlowStatus(
                        ActivateStatusLabel,
                        $"Activation failed after local payment and could not be queued. {queueResult.Message} Last error: {error}",
                        true);
                }

                return true;
            }

            SetFlowStatus(ActivateStatusLabel, result.Message ?? $"Activated {FormatMoney(amount)}.", false);
            ShowReceipt(ActivateReceiptLabel, result);
            await PrintOrderWebReceiptAsync(result, orderId, ActivateStatusLabel, "Activation");
            _activateCapturedPaymentKey = null;
            return false;
        });
    }

    private async void OnSellCardClicked(object sender, EventArgs e)
    {
        var cardNumber = SellCardEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            cardNumber = BuildPosGiftCardNumber();
            SellCardEntry.Text = cardNumber;
            _sellCapturedPaymentKey = null;
            SetFlowStatus(SellStatusLabel, "New POS gift card number generated.", false);
        }

        if (!TryReadAmount(SellAmountEntry.Text, out var amount))
        {
            SetFlowStatus(SellStatusLabel, "Enter a valid sale amount.", true);
            return;
        }

        var orderId = GetOrCreateOrderId(SellOrderIdEntry, "SELL");
        var paymentMethod = GetPaymentMethod(SellPaymentMethodPicker);
        var paymentKey = BuildGiftCardPaymentKey(cardNumber, amount, paymentMethod, orderId);
        if (!string.Equals(_sellCapturedPaymentKey, paymentKey, StringComparison.Ordinal))
        {
            if (!await TakeLocalPaymentAsync(paymentMethod, amount, SellStatusLabel, "sold"))
            {
                return;
            }

            _sellCapturedPaymentKey = paymentKey;
        }
        else
        {
            SetFlowStatus(SellStatusLabel, "Local payment already taken. Retrying OrderWeb sale.", false);
        }

        await RunBusyAsync(SellActionButton, "Selling...", async () =>
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
                SetFlowStatus(SellStatusLabel, result.Error ?? result.Message ?? "Gift card sale failed.", true);
                return true;
            }

            var soldCardNumber = result.GiftCard?.CardNumber ?? cardNumber;
            SetFlowStatus(SellStatusLabel, result.Message ?? $"Sold card {soldCardNumber} for {FormatMoney(amount)}.", false);
            ShowReceipt(SellReceiptLabel, result);
            await PrintOrderWebReceiptAsync(result, orderId, SellStatusLabel, "Sale");
            _sellCapturedPaymentKey = null;
            return false;
        });
    }

    private async void OnTopUpLookupClicked(object sender, EventArgs e)
    {
        var cardNumber = TopUpCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurpose.TopUp,
            TopUpLookupButton,
            TopUpCardEntry,
            TopUpStatusLabel,
            "Checking...");

        if (lookup?.GiftCard == null)
        {
            _topUpGiftCard = null;
            TopUpActionButton.IsEnabled = false;
            return;
        }

        _topUpGiftCard = lookup.GiftCard;
        _topUpCapturedPaymentKey = null;
        TopUpActionButton.IsEnabled = true;
        TopUpReceiptLabel.Text = string.Empty;
        ApplySuggestedAmount(lookup, TopUpAmountEntry);
    }

    private async void OnTopUpCardClicked(object sender, EventArgs e)
    {
        if (_topUpGiftCard == null)
        {
            SetFlowStatus(TopUpStatusLabel, "Lookup the gift card before top-up.", true);
            return;
        }

        if (!TryReadAmount(TopUpAmountEntry.Text, out var amount))
        {
            SetFlowStatus(TopUpStatusLabel, "Enter a valid top-up amount.", true);
            return;
        }

        var orderId = GetOrCreateOrderId(TopUpOrderIdEntry, "TOPUP");
        var paymentMethod = GetPaymentMethod(TopUpPaymentMethodPicker);
        var paymentKey = BuildGiftCardPaymentKey(_topUpGiftCard.CardNumber, amount, paymentMethod, orderId);
        if (!string.Equals(_topUpCapturedPaymentKey, paymentKey, StringComparison.Ordinal))
        {
            if (!await TakeLocalPaymentAsync(paymentMethod, amount, TopUpStatusLabel, "topped up"))
            {
                return;
            }

            _topUpCapturedPaymentKey = paymentKey;
        }
        else
        {
            SetFlowStatus(TopUpStatusLabel, "Local payment already taken. Retrying OrderWeb top-up.", false);
        }

        await RunBusyAsync(TopUpActionButton, "Topping up...", async () =>
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
                SetFlowStatus(TopUpStatusLabel, result.Error ?? result.Message ?? "Top-up failed.", true);
                return true;
            }

            var balance = result.EffectiveBalance.HasValue
                ? $" New balance: {FormatMoney(result.EffectiveBalance.Value)}."
                : string.Empty;
            SetFlowStatus(TopUpStatusLabel, (result.Message ?? $"Added {FormatMoney(amount)}.") + balance, false);
            ShowReceipt(TopUpReceiptLabel, result);
            await PrintOrderWebReceiptAsync(result, orderId, TopUpStatusLabel, "Top-up");
            _topUpCapturedPaymentKey = null;
            return false;
        });
    }

    private async void OnRedeemLookupClicked(object sender, EventArgs e)
    {
        var cardNumber = RedeemCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurpose.Redeem,
            RedeemLookupButton,
            RedeemCardEntry,
            RedeemStatusLabel,
            "Checking...");

        if (lookup?.GiftCard == null)
        {
            _redeemGiftCard = null;
            RedeemActionButton.IsEnabled = false;
            RedeemBalanceLabel.Text = "GBP 0.00";
            RedeemCardStatusLabel.Text = "No card selected";
            return;
        }

        _redeemGiftCard = lookup.GiftCard;
        RedeemActionButton.IsEnabled = true;
        RedeemBalanceLabel.Text = FormatMoney(_redeemGiftCard.Balance);
        RedeemCardStatusLabel.Text = $"{_redeemGiftCard.CardNumber} - {_redeemGiftCard.StatusDisplay.Trim()}";
    }

    private async void OnRedeemCardClicked(object sender, EventArgs e)
    {
        if (_redeemGiftCard == null)
        {
            SetFlowStatus(RedeemStatusLabel, "Lookup the gift card before redemption.", true);
            return;
        }

        if (!TryReadAmount(RedeemAmountEntry.Text, out var amount))
        {
            SetFlowStatus(RedeemStatusLabel, "Enter a valid redemption amount.", true);
            return;
        }

        var orderId = GetOrCreateOrderId(RedeemOrderIdEntry, "REDEEM");
        await RunBusyAsync(RedeemActionButton, "Redeeming...", async () =>
        {
            SetFlowStatus(RedeemStatusLabel, "Checking latest OrderWeb balance...", false);
            var latestLookup = await _giftCardApiService.LookupAsync(_redeemGiftCard.CardNumber, GiftCardLookupPurpose.Redeem);
            if (!latestLookup.Success || latestLookup.GiftCard == null)
            {
                SetFlowStatus(RedeemStatusLabel, latestLookup.StatusMessage ?? "Gift card cannot be redeemed.", true);
                return true;
            }

            var latestCard = latestLookup.GiftCard;
            var latestCardNumber = string.IsNullOrWhiteSpace(latestCard.CardNumber)
                ? _redeemGiftCard.CardNumber
                : latestCard.CardNumber;
            _redeemGiftCard = latestCard;
            RedeemBalanceLabel.Text = FormatMoney(latestCard.Balance);
            RedeemCardStatusLabel.Text = $"{latestCardNumber} - {latestCard.StatusDisplay.Trim()}";

            if (amount > latestCard.Balance)
            {
                SetFlowStatus(RedeemStatusLabel, $"Amount exceeds latest OrderWeb balance. Max: {FormatMoney(latestCard.Balance)}.", true);
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
                SetFlowStatus(RedeemStatusLabel, result.Error ?? result.Message ?? "Redemption failed.", true);
                return true;
            }

            var redeemedAmount = result.EffectiveAmountRedeemed ?? amount;
            var remainingBalance = result.EffectiveRemainingBalance ?? Math.Max(0, latestCard.Balance - redeemedAmount);
            _redeemGiftCard.Balance = remainingBalance;
            RedeemBalanceLabel.Text = FormatMoney(remainingBalance);
            SetFlowStatus(RedeemStatusLabel, $"Redeemed {FormatMoney(redeemedAmount)}. Remaining: {FormatMoney(remainingBalance)}.", false);
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
            SetFlowStatus(statusLabel, "Enter or scan a gift card number.", true);
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
                SetFlowStatus(statusLabel, message, true);
                return null;
            }

            SetFlowStatus(statusLabel, message, false);
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gift card lookup failed: {ex.Message}");
            SetFlowStatus(statusLabel, "Gift card lookup failed. Check the connection and try again.", true);
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
                SetFlowStatus(statusLabel, $"Take cash payment locally: {FormatMoney(amount)}.", false);
                var cashDialog = new CashPaymentDialog();
                cashDialog.SetAmountDue(amount);
                var cashResult = await cashDialog.ShowAsync();
                if (cashResult.Success)
                {
                    SetFlowStatus(statusLabel, $"Cash payment taken. Change: {FormatMoney(cashResult.Change)}.", false);
                    return true;
                }

                SetFlowStatus(statusLabel, $"Cash payment was cancelled. Card was not {actionPastTense}.", true);
                return false;

            case "card":
                SetFlowStatus(statusLabel, $"Take card payment locally: {FormatMoney(amount)}.", false);
                var cardDialog = new CardPaymentDialog();
                cardDialog.SetAmount(amount);
                var cardResult = await cardDialog.ShowAsync();
                if (cardResult.Success)
                {
                    SetFlowStatus(statusLabel, "Card payment confirmed locally.", false);
                    return true;
                }

                SetFlowStatus(statusLabel, $"Card payment was not completed. Card was not {actionPastTense}.", true);
                return false;

            default:
                SetFlowStatus(statusLabel, "Choose cash or card as the local payment method.", true);
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
            SetFlowStatus(statusLabel, $"{statusLabel.Text} Receipt queued.", false);
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

    private void InitializePaymentPickers()
    {
        foreach (var picker in new[] { ActivatePaymentMethodPicker, SellPaymentMethodPicker, TopUpPaymentMethodPicker })
        {
            picker.Items.Clear();
            foreach (var method in PaymentMethods)
            {
                picker.Items.Add(method);
            }

            picker.SelectedIndex = 0;
        }
    }

    private void ShowFlow(GiftCardFlow flow)
    {
        _activeFlow = flow;
        ActivateScreen.IsVisible = flow == GiftCardFlow.Activate;
        SellScreen.IsVisible = flow == GiftCardFlow.Sell;
        TopUpScreen.IsVisible = flow == GiftCardFlow.TopUp;
        RedeemScreen.IsVisible = flow == GiftCardFlow.Redeem;

        SetFlowButtonState(ActivateFlowButton, flow == GiftCardFlow.Activate);
        SetFlowButtonState(SellFlowButton, flow == GiftCardFlow.Sell);
        SetFlowButtonState(TopUpFlowButton, flow == GiftCardFlow.TopUp);
        SetFlowButtonState(RedeemFlowButton, flow == GiftCardFlow.Redeem);

        TopBar.SetPageTitle($"Gift Cards - {GetFlowTitle(flow)}");
    }

    private static void SetFlowButtonState(Button button, bool active)
    {
        button.BackgroundColor = active ? Color.FromArgb("#111827") : Colors.White;
        button.TextColor = active ? Colors.White : Color.FromArgb("#334155");
        button.BorderColor = active ? Color.FromArgb("#111827") : Color.FromArgb("#CBD5E1");
        button.BorderWidth = 1;
    }

    private static string GetFlowTitle(GiftCardFlow flow)
    {
        return flow switch
        {
            GiftCardFlow.Activate => "Activate",
            GiftCardFlow.Sell => "Sell",
            GiftCardFlow.TopUp => "Top-up",
            GiftCardFlow.Redeem => "Redeem",
            _ => "Gift Cards"
        };
    }

    private static void SetFlowStatus(Label label, string message, bool isError)
    {
        label.Text = message;
        label.TextColor = isError ? Color.FromArgb("#DC2626") : Color.FromArgb("#047857");
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

    private enum GiftCardFlow
    {
        Activate,
        Sell,
        TopUp,
        Redeem
    }
}
