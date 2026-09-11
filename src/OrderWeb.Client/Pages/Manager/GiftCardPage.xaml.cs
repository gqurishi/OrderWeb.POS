namespace OrderWeb.Client.Pages.Manager;

using System.Globalization;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;
using OrderWeb.SharedUI.Views;

public partial class GiftCardPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy;
    private readonly MotherGiftCardClient _giftCards;
    private string? _activateCardNumber;
    private string? _topUpCardNumber;
    private string? _redeemCardNumber;
    private decimal? _redeemBalance;
    private bool _busy;
    private string? _activateIdempotencyKey;
    private string? _activateIdempotencyFingerprint;
    private string? _sellIdempotencyKey;
    private string? _sellIdempotencyFingerprint;
    private string? _topUpIdempotencyKey;
    private string? _topUpIdempotencyFingerprint;
    private string? _redeemIdempotencyKey;
    private string? _redeemIdempotencyFingerprint;

    public GiftCardPage()
    {
        InitializeComponent();
        _offlinePolicy = new ClientOfflinePolicy(_cache);
        _giftCards = new MotherGiftCardClient(_cache, _offlinePolicy);

        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);

        Gift.FlowChanged += (_, _) => TopBar.SetPageTitle($"Gift Cards - {Gift.FlowTitle}");
        Gift.CloseRequested += async (_, _) => await Navigation.PopAsync(false);
        Gift.ActivateLookupRequested += async (_, _) => await OnActivateLookupAsync();
        Gift.ActivateRequested += async (_, _) => await OnActivateAsync();
        Gift.GenerateSellCardRequested += (_, _) => OnGenerateSellCard();
        Gift.SellRequested += async (_, _) => await OnSellAsync();
        Gift.TopUpLookupRequested += async (_, _) => await OnTopUpLookupAsync();
        Gift.TopUpRequested += async (_, _) => await OnTopUpAsync();
        Gift.RedeemLookupRequested += async (_, _) => await OnRedeemLookupAsync();
        Gift.RedeemRequested += async (_, _) => await OnRedeemAsync();

        Gift.ShowFlow(GiftCardFlowKind.Redeem);
        TopBar.SetPageTitle($"Gift Cards - {Gift.FlowTitle}");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Mother Terminal Access can change while this till is still signed in.
        // Refresh before gating so Save access applies without a new PIN login.
        await RefreshAccessFromMotherAsync();
        if (!HasGiftCardAccess())
        {
            await DisplayAlert(
                "Gift Cards",
                "This Client terminal is not allowed to use gift cards. On Mother: Terminal Health → Access → Gift cards ON → Save, then try again (or Update All).",
                "OK");
            await Navigation.PopAsync(false);
        }
    }

    private void OnGenerateSellCard()
    {
        Gift.SellCardEntry.Text = BuildPosGiftCardNumber();
        Gift.SellReceiptLabel.Text = string.Empty;
        GiftCardView.SetFlowStatus(Gift.SellStatusLabel, "New POS gift card number generated.", false);
    }

    private async Task OnActivateLookupAsync()
    {
        var cardNumber = Gift.ActivateCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurposes.Activate,
            Gift.ActivateLookupButton,
            Gift.ActivateCardEntry,
            Gift.ActivateStatusLabel);
        if (lookup?.GiftCard == null)
        {
            _activateCardNumber = null;
            Gift.ActivateActionButton.IsEnabled = false;
            return;
        }

        _activateCardNumber = cardNumber;
        Gift.ActivateActionButton.IsEnabled = true;
        Gift.ActivateReceiptLabel.Text = string.Empty;
        ApplySuggestedAmount(lookup, Gift.ActivateAmountEntry);
    }

    private async Task OnActivateAsync()
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

        var paymentMethod = GetPaymentMethod(Gift.ActivatePaymentMethodPicker);
        if (!await ConfirmLocalPaymentAsync(paymentMethod, amount, "activate"))
        {
            return;
        }

        var orderId = GetOrCreateOrderId(Gift.ActivateOrderIdEntry, "ACT");
        var fingerprint = $"activate|{orderId}|{_activateCardNumber}|{amount:F2}|{paymentMethod}";
        var idempotencyKey = GetStickyKey(ref _activateIdempotencyKey, ref _activateIdempotencyFingerprint, fingerprint, $"client-activate:{orderId}:{_activateCardNumber}:{amount:F2}:{paymentMethod}");
        await RunBusyAsync(Gift.ActivateActionButton, "Activating...", async () =>
        {
            var result = await _giftCards.ActivateAsync(
                _activateCardNumber,
                amount,
                paymentMethod,
                orderId,
                "Client POS gift card activation",
                idempotencyKey);
            if (!result.Success)
            {
                ClientGiftCardDiagnostics.Record("activate", false, result.Error ?? result.Message, result.ErrorCode);
                GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, FormatError(result.Error, result.Message, result.ErrorCode), true);
                return;
            }

            if (result.Queued)
            {
                var queuedMessage = result.QueueMessage
                    ?? "Activation queued on Mother for OrderWeb cloud retry. Do not take payment again — retry uses the same key.";
                ClientGiftCardDiagnostics.Record("activate", true, queuedMessage, GiftCardErrorCodes.Queued, queued: true);
                GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, queuedMessage, false);
                ShowReceipt(Gift.ActivateReceiptLabel, result.ReceiptLines);
                return;
            }

            var message = result.Message ?? $"Activated {FormatMoney(amount)}.";
            if (result.GiftCard != null)
            {
                message += $" Balance {FormatMoney(result.Balance ?? result.GiftCard.Balance)} ({result.GiftCard.CardNumberMasked}).";
            }

            ClientGiftCardDiagnostics.Record("activate", true, message);
            GiftCardView.SetFlowStatus(Gift.ActivateStatusLabel, message, false);
            ShowReceipt(Gift.ActivateReceiptLabel, result.ReceiptLines);
            ClearStickyKey(ref _activateIdempotencyKey, ref _activateIdempotencyFingerprint);
        });
    }

    private async Task OnSellAsync()
    {
        var cardNumber = Gift.SellCardEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            cardNumber = BuildPosGiftCardNumber();
            Gift.SellCardEntry.Text = cardNumber;
        }

        if (!TryReadAmount(Gift.SellAmountEntry.Text, out var amount))
        {
            GiftCardView.SetFlowStatus(Gift.SellStatusLabel, "Enter a valid sale amount.", true);
            return;
        }

        var paymentMethod = GetPaymentMethod(Gift.SellPaymentMethodPicker);
        if (!await ConfirmLocalPaymentAsync(paymentMethod, amount, "sell"))
        {
            return;
        }

        var orderId = GetOrCreateOrderId(Gift.SellOrderIdEntry, "SELL");
        var fingerprint = $"sell|{orderId}|{cardNumber}|{amount:F2}|{paymentMethod}";
        var idempotencyKey = GetStickyKey(ref _sellIdempotencyKey, ref _sellIdempotencyFingerprint, fingerprint, $"client-sell:{orderId}:{cardNumber}:{amount:F2}:{paymentMethod}");
        await RunBusyAsync(Gift.SellActionButton, "Selling...", async () =>
        {
            var result = await _giftCards.SellAsync(
                cardNumber,
                amount,
                paymentMethod,
                orderId,
                "Client POS gift card sale",
                idempotencyKey);
            if (!result.Success)
            {
                ClientGiftCardDiagnostics.Record("sell", false, result.Error ?? result.Message, result.ErrorCode);
                GiftCardView.SetFlowStatus(Gift.SellStatusLabel, FormatError(result.Error, result.Message, result.ErrorCode), true);
                return;
            }

            var masked = result.GiftCard?.CardNumberMasked ?? MaskLocal(cardNumber);
            var balance = result.Balance ?? result.GiftCard?.Balance ?? amount;
            var message = result.Message ?? $"Sold {FormatMoney(amount)}. Card {masked}. Balance {FormatMoney(balance)}.";
            ClientGiftCardDiagnostics.Record("sell", true, message);
            GiftCardView.SetFlowStatus(Gift.SellStatusLabel, message, false);
            ShowReceipt(Gift.SellReceiptLabel, result.ReceiptLines);
            ClearStickyKey(ref _sellIdempotencyKey, ref _sellIdempotencyFingerprint);
        });
    }

    private async Task OnTopUpLookupAsync()
    {
        var cardNumber = Gift.TopUpCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurposes.TopUp,
            Gift.TopUpLookupButton,
            Gift.TopUpCardEntry,
            Gift.TopUpStatusLabel);
        if (lookup?.GiftCard == null)
        {
            _topUpCardNumber = null;
            Gift.TopUpActionButton.IsEnabled = false;
            return;
        }

        _topUpCardNumber = cardNumber;
        Gift.TopUpActionButton.IsEnabled = true;
        Gift.TopUpReceiptLabel.Text = string.Empty;
        ApplySuggestedAmount(lookup, Gift.TopUpAmountEntry);
        GiftCardView.SetFlowStatus(
            Gift.TopUpStatusLabel,
            $"{lookup.Message ?? "Card ready."} Balance {FormatMoney(lookup.GiftCard.Balance)} ({lookup.GiftCard.CardNumberMasked}).",
            false);
    }

    private async Task OnTopUpAsync()
    {
        if (string.IsNullOrWhiteSpace(_topUpCardNumber))
        {
            GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, "Lookup the gift card before top-up.", true);
            return;
        }

        if (!TryReadAmount(Gift.TopUpAmountEntry.Text, out var amount))
        {
            GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, "Enter a valid top-up amount.", true);
            return;
        }

        var paymentMethod = GetPaymentMethod(Gift.TopUpPaymentMethodPicker);
        if (!await ConfirmLocalPaymentAsync(paymentMethod, amount, "top up"))
        {
            return;
        }

        var orderId = GetOrCreateOrderId(Gift.TopUpOrderIdEntry, "TOPUP");
        var fingerprint = $"topup|{orderId}|{_topUpCardNumber}|{amount:F2}|{paymentMethod}";
        var idempotencyKey = GetStickyKey(ref _topUpIdempotencyKey, ref _topUpIdempotencyFingerprint, fingerprint, $"client-topup:{orderId}:{_topUpCardNumber}:{amount:F2}:{paymentMethod}");
        await RunBusyAsync(Gift.TopUpActionButton, "Topping up...", async () =>
        {
            var result = await _giftCards.TopUpAsync(
                _topUpCardNumber,
                amount,
                paymentMethod,
                orderId,
                "Client POS gift card top-up",
                idempotencyKey);
            if (!result.Success)
            {
                ClientGiftCardDiagnostics.Record("top-up", false, result.Error ?? result.Message, result.ErrorCode);
                GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, FormatError(result.Error, result.Message, result.ErrorCode), true);
                return;
            }

            var balance = result.Balance ?? result.GiftCard?.Balance;
            var message = (result.Message ?? $"Added {FormatMoney(amount)}.") + (balance.HasValue ? $" Balance {FormatMoney(balance.Value)}." : string.Empty);
            ClientGiftCardDiagnostics.Record("top-up", true, message);
            GiftCardView.SetFlowStatus(Gift.TopUpStatusLabel, message, false);
            ShowReceipt(Gift.TopUpReceiptLabel, result.ReceiptLines);
            ClearStickyKey(ref _topUpIdempotencyKey, ref _topUpIdempotencyFingerprint);
        });
    }

    private async Task OnRedeemLookupAsync()
    {
        var cardNumber = Gift.RedeemCardEntry.Text?.Trim();
        var lookup = await LookupForFlowAsync(
            cardNumber,
            GiftCardLookupPurposes.Redeem,
            Gift.RedeemLookupButton,
            Gift.RedeemCardEntry,
            Gift.RedeemStatusLabel);
        if (lookup?.GiftCard == null)
        {
            _redeemCardNumber = null;
            _redeemBalance = null;
            Gift.RedeemBalanceLabel.Text = "GBP 0.00";
            Gift.RedeemCardStatusLabel.Text = "No card checked yet";
            Gift.RedeemActionButton.IsEnabled = false;
            return;
        }

        _redeemCardNumber = cardNumber;
        _redeemBalance = lookup.GiftCard.Balance;
        Gift.RedeemBalanceLabel.Text = FormatMoney(lookup.GiftCard.Balance);
        Gift.RedeemCardStatusLabel.Text = string.IsNullOrWhiteSpace(lookup.GiftCard.Status)
            ? (lookup.GiftCard.CardNumberMasked ?? cardNumber ?? string.Empty)
            : $"{lookup.GiftCard.CardNumberMasked} · {lookup.GiftCard.Status}";
        Gift.RedeemActionButton.IsEnabled = lookup.GiftCard.CanUse;
        ApplySuggestedAmount(lookup, Gift.RedeemAmountEntry);
        Gift.FocusRedeemAmountForKeypad();
    }

    private async Task OnRedeemAsync()
    {
        if (string.IsNullOrWhiteSpace(_redeemCardNumber))
        {
            GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, "Lookup the gift card before redeem.", true);
            return;
        }

        if (!TryReadAmount(Gift.RedeemAmountEntry.Text, out var amount))
        {
            GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, "Enter a valid redeem amount.", true);
            return;
        }

        if (_redeemBalance.HasValue && amount > _redeemBalance.Value)
        {
            GiftCardView.SetFlowStatus(
                Gift.RedeemStatusLabel,
                $"Amount exceeds balance ({FormatMoney(_redeemBalance.Value)}).",
                true);
            return;
        }

        var orderId = GetOrCreateOrderId(Gift.RedeemOrderIdEntry, "RDM");
        var fingerprint = $"redeem|{orderId}|{_redeemCardNumber}|{amount:F2}";
        var idempotencyKey = GetStickyKey(ref _redeemIdempotencyKey, ref _redeemIdempotencyFingerprint, fingerprint, $"client-redeem:{orderId}:{_redeemCardNumber}:{amount:F2}");
        await RunBusyAsync(Gift.RedeemActionButton, "Redeeming...", async () =>
        {
            var result = await _giftCards.RedeemAsync(
                _redeemCardNumber,
                amount,
                orderId,
                "Client POS gift card redeem",
                idempotencyKey,
                _redeemBalance);
            if (!result.Success)
            {
                ClientGiftCardDiagnostics.Record("redeem", false, result.Error ?? result.Message, result.ErrorCode);
                GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, FormatError(result.Error, result.Message, result.ErrorCode), true);
                return;
            }

            _redeemBalance = result.RemainingBalance;
            Gift.RedeemBalanceLabel.Text = result.RemainingBalance.HasValue
                ? FormatMoney(result.RemainingBalance.Value)
                : "GBP 0.00";
            var message = result.Message ?? $"Redeemed {FormatMoney(result.AmountRedeemed ?? amount)}. Remaining {FormatMoney(result.RemainingBalance ?? 0m)}.";
            ClientGiftCardDiagnostics.Record("redeem", true, message);
            GiftCardView.SetFlowStatus(Gift.RedeemStatusLabel, message, false);
            ClearStickyKey(ref _redeemIdempotencyKey, ref _redeemIdempotencyFingerprint);
        });
    }

    private async Task<ClientGiftCardLookupResponseDto?> LookupForFlowAsync(
        string? cardNumber,
        string purpose,
        Button lookupButton,
        Entry cardEntry,
        Label statusLabel)
    {
        if (_busy)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            GiftCardView.SetFlowStatus(statusLabel, "Enter or scan a gift card number.", true);
            return null;
        }

        if (!await EnsureReadyAsync(statusLabel))
        {
            return null;
        }

        _busy = true;
        var previousText = lookupButton.Text;
        lookupButton.Text = "Checking...";
        lookupButton.IsEnabled = false;
        cardEntry.IsEnabled = false;
        try
        {
            var result = await _giftCards.LookupAsync(cardNumber, purpose);
            if (!result.Success || !result.CanProceed || result.GiftCard == null)
            {
                ClientGiftCardDiagnostics.Record("lookup", false, result.Error ?? result.Message, result.ErrorCode);
                GiftCardView.SetFlowStatus(statusLabel, FormatError(result.Error, result.Message, result.ErrorCode), true);
                return null;
            }

            ClientGiftCardDiagnostics.Record("lookup", true, result.Message ?? "Lookup completed.");
            GiftCardView.SetFlowStatus(statusLabel, result.Message ?? "Lookup completed.", false);
            return result;
        }
        finally
        {
            lookupButton.Text = previousText;
            lookupButton.IsEnabled = true;
            cardEntry.IsEnabled = true;
            _busy = false;
        }
    }

    private static bool HasGiftCardAccess() =>
        ClientHostAccess.Features.Contains(PosFeatureKeys.GiftCards) ||
        ClientHostAccess.CanOpenMenu("Gift Cards");

    private async Task<MotherAccessRefreshResult> RefreshAccessFromMotherAsync()
    {
        var accessClient = new MotherAccessClient(_cache);
        return await accessClient.RefreshAccessAsync();
    }

    private async Task<bool> EnsureReadyAsync(Label statusLabel)
    {
        var access = await RefreshAccessFromMotherAsync();
        if (!HasGiftCardAccess())
        {
            var message = access.Success
                ? "Gift cards access is not granted for this Client terminal. On Mother: Terminal Health → Access → Gift cards ON → Save, then try again."
                : $"Could not refresh Client access from Mother. {access.Message}";
            GiftCardView.SetFlowStatus(statusLabel, message, true);
            return false;
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var decision = _offlinePolicy.Evaluate(ClientOperation.GiftCard, online);
        if (!decision.Allowed)
        {
            GiftCardView.SetFlowStatus(statusLabel, decision.Message, true);
            return false;
        }

        return true;
    }

    private async Task<bool> ConfirmLocalPaymentAsync(string paymentMethod, decimal amount, string action)
    {
        var method = paymentMethod.Equals("card", StringComparison.OrdinalIgnoreCase) ? "card" : "cash";
        return await DisplayAlert(
            "Confirm payment",
            $"Confirm {method} {FormatMoney(amount)} was taken before you {action} this gift card on OrderWeb cloud.",
            "Payment taken",
            "Cancel");
    }

    private async Task RunBusyAsync(Button button, string busyText, Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        var previous = button.Text;
        button.Text = busyText;
        button.IsEnabled = false;
        try
        {
            await action();
        }
        finally
        {
            button.Text = previous;
            button.IsEnabled = true;
            _busy = false;
        }
    }

    private static string GetStickyKey(ref string? key, ref string? fingerprint, string nextFingerprint, string createKey)
    {
        if (!string.Equals(fingerprint, nextFingerprint, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(key))
        {
            fingerprint = nextFingerprint;
            key = createKey;
        }

        return key;
    }

    private static void ClearStickyKey(ref string? key, ref string? fingerprint)
    {
        key = null;
        fingerprint = null;
    }

    private static void ApplySuggestedAmount(ClientGiftCardLookupResponseDto lookup, Entry amountEntry)
    {
        if (!string.IsNullOrWhiteSpace(amountEntry.Text))
        {
            return;
        }

        var suggested = lookup.SuggestedAmounts?.FirstOrDefault(a => a > 0)
            ?? (lookup.GiftCard?.Balance > 0 ? lookup.GiftCard.Balance : 0m);
        if (suggested > 0)
        {
            amountEntry.Text = suggested.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    private static void ShowReceipt(Label receiptLabel, IReadOnlyList<string>? lines)
    {
        receiptLabel.Text = lines is { Count: > 0 }
            ? string.Join(Environment.NewLine, lines)
            : string.Empty;
    }

    private static string FormatError(string? error, string? message, string? errorCode)
    {
        var text = error ?? message ?? "Gift card request failed.";
        if (string.Equals(errorCode, GiftCardErrorCodes.OfflineMother, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return string.IsNullOrWhiteSpace(errorCode) ? text : $"{text} ({errorCode})";
    }

    private static bool TryReadAmount(string? text, out decimal amount)
    {
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) && amount > 0)
        {
            return true;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;
    }

    private static string GetPaymentMethod(Picker picker)
    {
        var value = picker.SelectedItem?.ToString()?.Trim().ToLowerInvariant() ?? "cash";
        return value.Contains("card") ? "card" : "cash";
    }

    private static string GetOrCreateOrderId(Entry entry, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(entry.Text))
        {
            return entry.Text.Trim();
        }

        var id = $"{prefix}-{DateTime.Now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
        entry.Text = id;
        return id;
    }

    private static string BuildPosGiftCardNumber() =>
        $"POS{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";

    private static string FormatMoney(decimal amount) =>
        amount.ToString("C", CultureInfo.GetCultureInfo("en-GB"));

    private static string MaskLocal(string? cardNumber)
    {
        var trimmed = (cardNumber ?? string.Empty).Trim();
        if (trimmed.Length <= 4)
        {
            return trimmed;
        }

        return new string('*', Math.Min(trimmed.Length - 4, 12)) + trimmed[^4..];
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private async Task OpenSidebarAsync()
    {
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async Task CloseSidebarAsync()
    {
        await Sidebar.TranslateTo(-280, 0, 160, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
    }

    private async Task NavigateFromSidebarAsync(string menu)
    {
        await CloseSidebarAsync();
        if (ClientSidebarNavigation.IsDashboard(menu))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        if (await ClientSidebarNavigation.TryHandleMotherOnlyAsync(this, menu))
        {
            return;
        }

        if (ClientHostAccess.IsMenuRoute(menu, "giftcards") ||
            !ClientHostAccess.CanOpenMenu(menu))
        {
            return;
        }

        if (ClientSidebarNavigation.IsCustomerSurface(menu))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        var page = ClientSidebarNavigation.CreatePage(menu);
        if (page is not null)
        {
            await Navigation.PushAsync(page, false);
        }
    }
}
