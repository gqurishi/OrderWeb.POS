using Microsoft.Maui.Controls;
using System;
using System.Globalization;
using System.Threading.Tasks;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Helpers;

namespace POS_in_NET.Views
{
    public class GiftCardPaymentResult
    {
        public bool Success { get; set; }
        public string? GiftCardNumber { get; set; }
        public decimal AmountApplied { get; set; }
        public decimal Remaining { get; set; }
        public decimal NewCardBalance { get; set; }
        public decimal PreviousCardBalance { get; set; }
        public string? OrderWebMessage { get; set; }
    }

    public partial class GiftCardPaymentDialog : ContentView
    {
        private TaskCompletionSource<GiftCardPaymentResult>? _taskCompletionSource;
        private Grid? _parentGrid;
        private decimal _amountDue;
        private decimal _cardBalance;
        private string? _cardNumber;
        private string? _orderReference;
        private string? _paymentTransactionId;
        private GiftCard? _giftCard;
        private readonly OrderWebGiftCardApiService? _giftCardApiService;
        private bool _isUpdatingApplyAmount;
        private bool _keyboardOpen;

        public GiftCardPaymentDialog()
        {
            InitializeComponent();
            TabletLayoutHelper.AttachDialog(this, DialogCard, 650, 760);
            _giftCardApiService = Application.Current?.Handler?.MauiContext?.Services.GetService(typeof(OrderWebGiftCardApiService)) as OrderWebGiftCardApiService;
        }

        public void SetAmountDue(decimal amount, string? orderReference = null, string? paymentTransactionId = null)
        {
            _amountDue = amount;
            _orderReference = orderReference;
            _paymentTransactionId = paymentTransactionId;
            AmountDueLabel.Text = $"£{amount:F2}";
            UpdateApplyButtonState();
        }

        public async Task<GiftCardPaymentResult> ShowAsync()
        {
            using var idleGuard = POS_in_NET.Services.ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<GiftCardPaymentResult>();
            
            if (Application.Current?.MainPage != null)
            {
                var pageContent = GetPageContent(Application.Current.MainPage);
                if (pageContent is Grid mainGrid)
                {
                    _parentGrid = mainGrid;
                    
                    Grid.SetRowSpan(this, mainGrid.RowDefinitions.Count > 0 ? mainGrid.RowDefinitions.Count : 1);
                    Grid.SetColumnSpan(this, mainGrid.ColumnDefinitions.Count > 0 ? mainGrid.ColumnDefinitions.Count : 1);
                    Grid.SetRow(this, 0);
                    Grid.SetColumn(this, 0);
                    
                    mainGrid.Children.Add(this);
                }
            }

            return await _taskCompletionSource.Task;
        }

        private View? GetPageContent(Page page)
        {
            if (page is Shell shell && shell.CurrentPage is ContentPage currentPage)
            {
                return currentPage.Content;
            }
            else if (page is ContentPage contentPage)
            {
                return contentPage.Content;
            }
            return null;
        }

        private void OnGiftCardNumberFocused(object? sender, FocusEventArgs e)
        {
            if (e.IsFocused)
            {
                GiftCardNumberEntry.Unfocus();
                _ = OpenGiftCardNumberKeyboardAsync();
            }
        }

        private async void OnGiftCardNumberTapped(object? sender, EventArgs e) =>
            await OpenGiftCardNumberKeyboardAsync();

        private async Task OpenGiftCardNumberKeyboardAsync()
        {
            if (_keyboardOpen)
            {
                return;
            }

            _keyboardOpen = true;
            try
            {
                GiftCardNumberEntry.Unfocus();
                var keyboard = new OrderWeb.SharedUI.Controls.NumericKeyboardDialog();
                var value = await keyboard.ShowDigitsAsync(
                    GiftCardNumberEntry.Text,
                    "Gift card number",
                    maxDigits: 24);
                if (value != null)
                {
                    GiftCardNumberEntry.Text = value;
                }
            }
            finally
            {
                _keyboardOpen = false;
            }
        }

        private void OnApplyAmountFocused(object? sender, FocusEventArgs e)
        {
            if (e.IsFocused)
            {
                ApplyAmountEntry.Unfocus();
                _ = OpenApplyAmountKeyboardAsync();
            }
        }

        private async void OnApplyAmountTapped(object? sender, EventArgs e) =>
            await OpenApplyAmountKeyboardAsync();

        private async Task OpenApplyAmountKeyboardAsync()
        {
            if (_keyboardOpen || !ApplyAmountSection.IsVisible)
            {
                return;
            }

            _keyboardOpen = true;
            try
            {
                ApplyAmountEntry.Unfocus();
                decimal? initial = TryParseGiftCardAmount(ApplyAmountEntry.Text, out var parsed) ? parsed : null;
                var keyboard = new OrderWeb.SharedUI.Controls.NumericKeyboardDialog();
                var value = await keyboard.ShowCurrencyAsync(initial, "Amount to apply");
                if (value.HasValue)
                {
                    _isUpdatingApplyAmount = true;
                    ApplyAmountEntry.Text = value.Value.ToString("F2", CultureInfo.InvariantCulture);
                    _isUpdatingApplyAmount = false;
                    UpdateRemainingDisplay();
                    UpdateApplyButtonState();
                }
            }
            finally
            {
                _keyboardOpen = false;
            }
        }

        private async void OnCheckBalanceClicked(object sender, EventArgs e)
        {
            _cardNumber = GiftCardNumberEntry.Text?.Trim();
            
            if (string.IsNullOrEmpty(_cardNumber))
            {
                SetStatus("Enter or scan a gift card number.", true);
                return;
            }

            if (_giftCardApiService == null)
            {
                SetStatus("Gift card service is not available.", true);
                return;
            }

            await RunBusyAsync("CHECKING...", async () =>
            {
                SetStatus("Checking OrderWeb gift card...", false);
                var result = await _giftCardApiService.LookupAsync(_cardNumber, GiftCardLookupPurpose.Redeem);

                if (!result.Success || result.GiftCard == null)
                {
                    ResetCardState();
                    SetStatus(result.StatusMessage ?? "Gift card not found.", true);
                    return;
                }

                ShowGiftCard(result.GiftCard);
            });
        }

        private void OnUseFullClicked(object sender, EventArgs e)
        {
            decimal maxApply = Math.Min(_cardBalance, _amountDue);
            _isUpdatingApplyAmount = true;
            ApplyAmountEntry.Text = maxApply.ToString("F2");
            _isUpdatingApplyAmount = false;
            UpdateRemainingDisplay();
            UpdateApplyButtonState();
        }

        private void UpdateRemainingDisplay()
        {
            if (TryParseGiftCardAmount(ApplyAmountEntry.Text, out decimal applyAmount))
            {
                decimal remaining = _amountDue - applyAmount;
                if (remaining > 0)
                {
                    RemainingFrame.IsVisible = true;
                    RemainingLabel.Text = $"£{remaining:F2}";
                }
                else
                {
                    RemainingFrame.IsVisible = false;
                }
            }
            else
            {
                RemainingFrame.IsVisible = false;
            }
        }

        private async void OnApplyClicked(object sender, EventArgs e)
        {
            if (_giftCardApiService == null || _giftCard == null || string.IsNullOrWhiteSpace(_cardNumber))
            {
                SetStatus("Check the gift card first.", true);
                return;
            }

            if (!TryParseGiftCardAmount(ApplyAmountEntry.Text, out decimal applyAmount))
            {
                SetStatus("Enter a valid amount to apply.", true);
                return;
            }

            if (applyAmount <= 0)
            {
                SetStatus("Amount must be at least GBP 0.01.", true);
                return;
            }

            if (applyAmount < 0)
            {
                SetStatus($"Amount must be between £0.01 and £{_cardBalance:F2}.", true);
                return;
            }

            await RunBusyAsync("REDEEMING...", async () =>
            {
                SetStatus("Confirming latest OrderWeb balance...", false);
                var latestLookup = await _giftCardApiService.LookupAsync(_cardNumber, GiftCardLookupPurpose.Redeem);
                if (!latestLookup.Success || latestLookup.GiftCard == null)
                {
                    SetStatus(latestLookup.StatusMessage ?? "Could not confirm gift card balance.", true);
                    return;
                }

                var latestCard = latestLookup.GiftCard;
                var latestCardNumber = string.IsNullOrWhiteSpace(latestCard.CardNumber)
                    ? _cardNumber
                    : latestCard.CardNumber;
                ShowGiftCard(latestCard);

                if (applyAmount > latestCard.Balance)
                {
                    SetStatus($"Amount exceeds latest OrderWeb balance. Max: GBP {latestCard.Balance:F2}.", true);
                    return;
                }

                var orderReference = string.IsNullOrWhiteSpace(_orderReference)
                    ? DateTime.Now.ToString("yyyyMMddHHmmss")
                    : _orderReference;
                var transactionId = !string.IsNullOrWhiteSpace(_paymentTransactionId)
                    ? _paymentTransactionId
                    : $"gift-card:{orderReference}:{_cardNumber}:{applyAmount:F2}";
                var description = $"POS gift card payment - {orderReference}";
                var redeemResult = await _giftCardApiService.RedeemAsync(
                    new GiftCardRedeemRequest
                    {
                        CardNumber = latestCardNumber,
                        Amount = applyAmount,
                        Description = description,
                        OrderId = orderReference
                    },
                    transactionId);

                if (!redeemResult.Success)
                {
                    SetStatus(redeemResult.Error ?? redeemResult.Message ?? "OrderWeb rejected the gift card redemption.", true);
                    return;
                }

                var amountApplied = redeemResult.EffectiveAmountRedeemed ?? applyAmount;
                var newBalance = redeemResult.EffectiveRemainingBalance ?? Math.Max(0, latestCard.Balance - amountApplied);
                var remaining = Math.Max(0, _amountDue - amountApplied);

                var result = new GiftCardPaymentResult
                {
                    Success = true,
                    GiftCardNumber = _cardNumber,
                    AmountApplied = amountApplied,
                    Remaining = remaining,
                    NewCardBalance = newBalance,
                    PreviousCardBalance = latestCard.Balance,
                    OrderWebMessage = redeemResult.Message
                };

                _taskCompletionSource?.TrySetResult(result);
                CloseDialog();
            });
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(new GiftCardPaymentResult { Success = false });
            CloseDialog();
        }

        private void CloseDialog()
        {
            if (_parentGrid != null)
            {
                _parentGrid.Children.Remove(this);
            }
        }

        private void ShowGiftCard(GiftCard giftCard)
        {
            _giftCard = giftCard;
            _cardBalance = giftCard.Balance;

            BalanceFrame.IsVisible = true;
            BalanceLabel.Text = giftCard.BalanceDisplay;
            ApplyAmountSection.IsVisible = giftCard.IsActive;
            _isUpdatingApplyAmount = true;
            ApplyAmountEntry.Text = Math.Min(_cardBalance, _amountDue).ToString("F2");
            _isUpdatingApplyAmount = false;

            SetStatus(giftCard.IsActive
                ? "Gift card verified with OrderWeb."
                : $"Gift card cannot be used: {giftCard.StatusDisplay}", !giftCard.IsActive);
            UpdateRemainingDisplay();
            UpdateApplyButtonState();
        }

        private void ResetCardState()
        {
            _giftCard = null;
            _cardBalance = 0;
            BalanceFrame.IsVisible = false;
            ApplyAmountSection.IsVisible = false;
            RemainingFrame.IsVisible = false;
            UpdateApplyButtonState();
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessageLabel.Text = message;
            StatusMessageLabel.TextColor = isError ? Color.FromArgb("#DC2626") : Color.FromArgb("#047857");
            StatusMessageLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
        }

        private async Task RunBusyAsync(string busyText, Func<Task> action)
        {
            var previousCheckText = CheckBalanceButton.Text;
            var previousApplyText = ApplyButton.Text;

            CheckBalanceButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            GiftCardNumberEntry.IsEnabled = false;
            ApplyAmountEntry.IsEnabled = false;
            CheckBalanceButton.Text = busyText;
            ApplyButton.Text = busyText;

            try
            {
                await action();
            }
            finally
            {
                CheckBalanceButton.Text = previousCheckText;
                ApplyButton.Text = previousApplyText;
                CheckBalanceButton.IsEnabled = true;
                GiftCardNumberEntry.IsEnabled = true;
                ApplyAmountEntry.IsEnabled = true;
                UpdateApplyButtonState();
            }
        }

        private void OnApplyAmountChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingApplyAmount)
            {
                return;
            }

            UpdateRemainingDisplay();
            UpdateApplyButtonState();
        }

        private void UpdateApplyButtonState()
        {
            var hasActiveCard = _giftCard?.IsActive == true;
            var hasAmount = TryParseGiftCardAmount(ApplyAmountEntry.Text, out var applyAmount);
            var canApply = hasActiveCard
                && hasAmount
                && applyAmount > 0;

            ApplyButton.IsEnabled = canApply;
            ApplyButton.BackgroundColor = canApply
                ? Color.FromArgb("#059669")
                : Color.FromArgb("#9CA3AF");
            ApplyButton.Text = canApply
                ? "APPLY GIFT CARD"
                : !hasActiveCard
                    ? "CHECK GIFT CARD FIRST"
                    : !hasAmount || applyAmount <= 0
                        ? "ENTER AMOUNT"
                        : "AMOUNT TOO HIGH";
        }

        private static bool TryParseGiftCardAmount(string? input, out decimal amount)
        {
            var normalized = (input ?? string.Empty)
                .Replace("£", string.Empty)
                .Replace(",", string.Empty)
                .Trim();

            return decimal.TryParse(normalized, out amount);
        }
    }
}
