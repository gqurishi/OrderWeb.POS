using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Helpers;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public class CashPaymentResult
    {
        public bool Success { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal AmountReceived { get; set; }
        public decimal Change { get; set; }
        public decimal Remaining { get; set; }
    }

    public partial class CashPaymentDialog : ContentView
    {
        private TaskCompletionSource<CashPaymentResult>? _taskCompletionSource;
        private Grid? _parentGrid;
        private decimal _amountDue;
        private decimal _amountReceived;
        private bool _isUpdatingEntry;
        private bool _keyboardOpen;

        public CashPaymentDialog()
        {
            InitializeComponent();
            TabletLayoutHelper.AttachDialog(this, DialogCard, 650, 760, ApplyResponsiveLayout);
        }

        private void ApplyResponsiveLayout(bool tablet, bool shortWindow)
        {
            DialogContent.Spacing = shortWindow ? 9 : tablet ? 12 : 18;
            DialogTitle.FontSize = tablet ? 25 : 30;
            CloseButton.WidthRequest = tablet ? 82 : 90;
            CloseButton.HeightRequest = 45;
            AmountDueCard.Padding = shortWindow ? 12 : tablet ? 15 : 18;
            AmountDueLabel.FontSize = shortWindow ? 27 : tablet ? 30 : 32;
            QuickCashGrid.ColumnSpacing = tablet ? 8 : 12;
            ConfirmButton.HeightRequest = tablet ? 54 : 60;
        }

        public void SetAmountDue(decimal amount)
        {
            _amountDue = amount;
            AmountDueLabel.Text = $"£{amount:F2}";
            _amountReceived = 0;
            AmountReceivedEntry.Text = string.Empty;
            SetQuickCashButtons();
            UpdateDisplay();
        }

        public async Task<CashPaymentResult> ShowAsync()
        {
            using var idleGuard = POS_in_NET.Services.ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<CashPaymentResult>();
            
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

        private void SetAmount(decimal amount)
        {
            _amountReceived = amount;
            _isUpdatingEntry = true;
            AmountReceivedEntry.Text = amount.ToString("F2");
            _isUpdatingEntry = false;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (_amountReceived >= _amountDue)
            {
                decimal change = _amountReceived - _amountDue;
                ChangeLabel.Text = $"£{change:F2}";
                ChangeTitleLabel.Text = change == 0 ? "READY:" : "CHANGE:";
                ChangeFrame.BackgroundColor = Color.FromArgb("#ECFDF5");
                ChangeFrame.Stroke = new SolidColorBrush(Color.FromArgb("#10B981"));
                ChangeTitleLabel.TextColor = Color.FromArgb("#047857");
                ChangeLabel.TextColor = Color.FromArgb("#047857");
                RemainingFrame.IsVisible = false;
                ConfirmButton.IsEnabled = true;
                ConfirmButton.BackgroundColor = Color.FromArgb("#059669");
                ConfirmButton.Text = "CONFIRM CASH PAYMENT";
            }
            else if (_amountReceived > 0)
            {
                decimal shortAmount = _amountDue - _amountReceived;
                ChangeTitleLabel.Text = "SHORT:";
                ChangeLabel.Text = $"£{shortAmount:F2}";
                ChangeFrame.BackgroundColor = Color.FromArgb("#FEF2F2");
                ChangeFrame.Stroke = new SolidColorBrush(Color.FromArgb("#EF4444"));
                ChangeTitleLabel.TextColor = Color.FromArgb("#DC2626");
                ChangeLabel.TextColor = Color.FromArgb("#DC2626");
                RemainingFrame.IsVisible = false;
                ConfirmButton.IsEnabled = false;
                ConfirmButton.BackgroundColor = Color.FromArgb("#9CA3AF");
                ConfirmButton.Text = "ENTER ENOUGH CASH";
            }
            else
            {
                ChangeTitleLabel.Text = "CHANGE:";
                ChangeLabel.Text = "£0.00";
                ChangeFrame.BackgroundColor = Color.FromArgb("#F8FAFC");
                ChangeFrame.Stroke = new SolidColorBrush(Color.FromArgb("#CBD5E1"));
                ChangeTitleLabel.TextColor = Color.FromArgb("#64748B");
                ChangeLabel.TextColor = Color.FromArgb("#334155");
                RemainingFrame.IsVisible = false;
                ConfirmButton.IsEnabled = false;
                ConfirmButton.BackgroundColor = Color.FromArgb("#9CA3AF");
                ConfirmButton.Text = "CONFIRM CASH PAYMENT";
            }
        }

        private void OnExactAmountClicked(object sender, EventArgs e) => SetAmount(_amountDue);

        private void OnAmountEntryFocused(object? sender, FocusEventArgs e)
        {
            if (e.IsFocused)
            {
                AmountReceivedEntry.Unfocus();
                _ = OpenAmountKeyboardAsync();
            }
        }

        private async void OnAmountEntryTapped(object? sender, EventArgs e) =>
            await OpenAmountKeyboardAsync();

        private async Task OpenAmountKeyboardAsync()
        {
            if (_keyboardOpen)
            {
                return;
            }

            _keyboardOpen = true;
            try
            {
                AmountReceivedEntry.Unfocus();
                var keyboard = new OrderWeb.SharedUI.Controls.NumericKeyboardDialog();
                var value = await keyboard.ShowCurrencyAsync(
                    _amountReceived > 0 ? _amountReceived : null,
                    "Amount received");
                if (value.HasValue)
                {
                    SetAmount(value.Value);
                }
            }
            finally
            {
                _keyboardOpen = false;
            }
        }

        private void OnAmountChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingEntry)
            {
                return;
            }

            if (TryParseCashAmount(e.NewTextValue, out var amount))
            {
                _amountReceived = amount;
            }
            else
            {
                _amountReceived = 0;
            }

            UpdateDisplay();
        }

        private void OnQuickAmountClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is decimal amount)
            {
                SetAmount(amount);
            }
        }

        private void OnConfirmClicked(object sender, EventArgs e)
        {
            if (_amountReceived < _amountDue)
            {
                return;
            }

            decimal change = Math.Max(0, _amountReceived - _amountDue);

            var result = new CashPaymentResult
            {
                Success = true,
                AmountPaid = _amountDue,
                AmountReceived = _amountReceived,
                Change = change,
                Remaining = 0
            };

            _taskCompletionSource?.TrySetResult(result);
            CloseDialog();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(new CashPaymentResult { Success = false });
            CloseDialog();
        }

        private void CloseDialog()
        {
            if (_parentGrid != null)
            {
                _parentGrid.Children.Remove(this);
            }
        }

        private void SetQuickCashButtons()
        {
            QuickExactButton.Text = $"Exact £{_amountDue:F2}";

            var quickAmounts = BuildQuickCashAmounts(_amountDue);
            SetQuickButton(QuickAmount1Button, quickAmounts[0]);
            SetQuickButton(QuickAmount2Button, quickAmounts[1]);
            SetQuickButton(QuickAmount3Button, quickAmounts[2]);
        }

        private static void SetQuickButton(Button button, decimal amount)
        {
            button.Text = $"£{amount:0.##}";
            button.CommandParameter = amount;
        }

        private static decimal[] BuildQuickCashAmounts(decimal amountDue)
        {
            var roundedToTen = Math.Ceiling(amountDue / 10m) * 10m;
            if (roundedToTen <= amountDue)
            {
                roundedToTen += 10m;
            }

            return new[]
            {
                roundedToTen,
                roundedToTen + 10m,
                roundedToTen + 20m
            };
        }

        private static bool TryParseCashAmount(string? input, out decimal amount)
        {
            var normalized = (input ?? string.Empty)
                .Replace("£", string.Empty)
                .Replace(",", string.Empty)
                .Trim();

            return decimal.TryParse(normalized, out amount) && amount > 0;
        }
    }
}
