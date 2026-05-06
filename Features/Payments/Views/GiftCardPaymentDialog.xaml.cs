using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public class GiftCardPaymentResult
    {
        public bool Success { get; set; }
        public string? GiftCardNumber { get; set; }
        public decimal AmountApplied { get; set; }
        public decimal Remaining { get; set; }
        public decimal NewCardBalance { get; set; }
    }

    public partial class GiftCardPaymentDialog : ContentView
    {
        private TaskCompletionSource<GiftCardPaymentResult>? _taskCompletionSource;
        private Grid? _parentGrid;
        private decimal _amountDue;
        private decimal _cardBalance;
        private string? _cardNumber;

        public GiftCardPaymentDialog()
        {
            InitializeComponent();
        }

        public void SetAmountDue(decimal amount)
        {
            _amountDue = amount;
            AmountDueLabel.Text = $"£{amount:F2}";
        }

        public async Task<GiftCardPaymentResult> ShowAsync()
        {
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
                // Show error
                return;
            }

            // TODO: In real implementation, call API to get gift card balance
            // For now, simulate a gift card with random balance
            await Task.Delay(300); // Simulate API call
            
            // Simulated balance (in real app, fetch from database/API)
            _cardBalance = new Random().Next(10, 100);
            
            BalanceFrame.IsVisible = true;
            BalanceLabel.Text = $"£{_cardBalance:F2}";
            
            ApplyAmountSection.IsVisible = true;
            ApplyAmountEntry.Text = Math.Min(_cardBalance, _amountDue).ToString("F2");
            
            ApplyButton.IsEnabled = true;
            ApplyButton.BackgroundColor = Color.FromArgb("#8B5CF6");
            
            UpdateRemainingDisplay();
        }

        private void OnUseFullClicked(object sender, EventArgs e)
        {
            decimal maxApply = Math.Min(_cardBalance, _amountDue);
            ApplyAmountEntry.Text = maxApply.ToString("F2");
            UpdateRemainingDisplay();
        }

        private void UpdateRemainingDisplay()
        {
            if (decimal.TryParse(ApplyAmountEntry.Text, out decimal applyAmount))
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
        }

        private void OnApplyClicked(object sender, EventArgs e)
        {
            if (!decimal.TryParse(ApplyAmountEntry.Text, out decimal applyAmount))
            {
                return;
            }

            // Validate amount
            if (applyAmount <= 0 || applyAmount > _cardBalance)
            {
                return;
            }

            decimal remaining = Math.Max(0, _amountDue - applyAmount);
            decimal newBalance = _cardBalance - applyAmount;

            var result = new GiftCardPaymentResult
            {
                Success = true,
                GiftCardNumber = _cardNumber,
                AmountApplied = applyAmount,
                Remaining = remaining,
                NewCardBalance = newBalance
            };

            _taskCompletionSource?.TrySetResult(result);
            CloseDialog();
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
    }
}
