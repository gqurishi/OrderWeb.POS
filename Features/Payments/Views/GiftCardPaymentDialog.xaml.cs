using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;
using POS_in_NET.Models;
using POS_in_NET.Services;

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
        private readonly LoyaltyService? _loyaltyService;
        private bool _isUpdatingApplyAmount;

        public GiftCardPaymentDialog()
        {
            InitializeComponent();
            _loyaltyService = Application.Current?.Handler?.MauiContext?.Services.GetService(typeof(LoyaltyService)) as LoyaltyService;
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

        private async void OnCheckBalanceClicked(object sender, EventArgs e)
        {
            _cardNumber = GiftCardNumberEntry.Text?.Trim();
            
            if (string.IsNullOrEmpty(_cardNumber))
            {
                SetStatus("Enter or scan a gift card number.", true);
                return;
            }

            if (_loyaltyService == null)
            {
                SetStatus("Gift card service is not available.", true);
                return;
            }

            await RunBusyAsync("CHECKING...", async () =>
            {
                SetStatus("Checking OrderWeb gift card...", false);
                var result = await _loyaltyService.CheckGiftCardBalanceAsync(_cardNumber);

                if (!result.Success || result.GiftCard == null)
                {
                    ResetCardState();
                    SetStatus(result.Error ?? "Gift card not found.", true);
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
            if (_loyaltyService == null || _giftCard == null || string.IsNullOrWhiteSpace(_cardNumber))
            {
                SetStatus("Check the gift card first.", true);
                return;
            }

            if (!TryParseGiftCardAmount(ApplyAmountEntry.Text, out decimal applyAmount))
            {
                SetStatus("Enter a valid amount to apply.", true);
                return;
            }

            if (applyAmount <= 0 || applyAmount > _cardBalance)
            {
                SetStatus($"Amount must be between £0.01 and £{_cardBalance:F2}.", true);
                return;
            }

            await RunBusyAsync("REDEEMING...", async () =>
            {
                SetStatus("Confirming latest OrderWeb balance...", false);
                var latestLookup = await _loyaltyService.CheckGiftCardBalanceAsync(_cardNumber);
                if (!latestLookup.Success || latestLookup.GiftCard == null)
                {
                    SetStatus(latestLookup.Error ?? "Could not confirm gift card balance.", true);
                    return;
                }

                if (Math.Abs(latestLookup.GiftCard.Balance - _cardBalance) > 0.009m)
                {
                    ShowGiftCard(latestLookup.GiftCard);
                    SetStatus("Balance changed. Review the amount and apply again.", true);
                    return;
                }

                var orderReference = string.IsNullOrWhiteSpace(_orderReference)
                    ? DateTime.Now.ToString("yyyyMMddHHmmss")
                    : _orderReference;
                var transactionId = !string.IsNullOrWhiteSpace(_paymentTransactionId)
                    ? _paymentTransactionId
                    : $"gift-card:{orderReference}:{_cardNumber}:{applyAmount:F2}";
                var description = $"POS gift card payment - {orderReference}";
                var redeemResult = await _loyaltyService.RedeemGiftCardAsync(
                    _cardNumber,
                    applyAmount,
                    description,
                    transactionId,
                    orderReference);

                if (!redeemResult.Success)
                {
                    SetStatus(redeemResult.Error ?? "OrderWeb rejected the gift card redemption.", true);
                    return;
                }

                var amountApplied = redeemResult.EffectiveAmountRedeemed ?? applyAmount;
                var newBalance = redeemResult.EffectiveRemainingBalance ?? Math.Max(0, _cardBalance - amountApplied);
                var remaining = Math.Max(0, _amountDue - amountApplied);

                var result = new GiftCardPaymentResult
                {
                    Success = true,
                    GiftCardNumber = _cardNumber,
                    AmountApplied = amountApplied,
                    Remaining = remaining,
                    NewCardBalance = newBalance,
                    PreviousCardBalance = _cardBalance,
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
                && applyAmount > 0
                && applyAmount <= _cardBalance + 0.009m;

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
