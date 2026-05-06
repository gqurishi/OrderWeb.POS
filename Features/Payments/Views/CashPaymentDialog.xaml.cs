using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public class CashPaymentResult
    {
        public bool Success { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Change { get; set; }
        public decimal Remaining { get; set; }
    }

    public partial class CashPaymentDialog : ContentView
    {
        private TaskCompletionSource<CashPaymentResult>? _taskCompletionSource;
        private Grid? _parentGrid;
        private decimal _amountDue;
        private decimal _amountReceived;

        public CashPaymentDialog()
        {
            InitializeComponent();
        }

        public void SetAmountDue(decimal amount)
        {
            _amountDue = amount;
            AmountDueLabel.Text = $"£{amount:F2}";
            _amountReceived = 0;
            UpdateDisplay();
        }

        public async Task<CashPaymentResult> ShowAsync()
        {
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
            AmountReceivedEntry.Text = amount.ToString("F2");
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (_amountReceived >= _amountDue)
            {
                // Full payment - show change
                decimal change = _amountReceived - _amountDue;
                ChangeLabel.Text = $"£{change:F2}";
                ChangeFrame.BackgroundColor = Color.FromArgb("#ECFDF5");
                ChangeFrame.Stroke = new SolidColorBrush(Color.FromArgb("#10B981"));
                RemainingFrame.IsVisible = false;
                ConfirmButton.IsEnabled = true;
                ConfirmButton.BackgroundColor = Color.FromArgb("#059669");
            }
            else if (_amountReceived > 0)
            {
                // Partial payment - show remaining
                decimal remaining = _amountDue - _amountReceived;
                ChangeLabel.Text = "£0.00";
                RemainingFrame.IsVisible = true;
                RemainingLabel.Text = $"£{remaining:F2}";
                ConfirmButton.IsEnabled = true;
                ConfirmButton.BackgroundColor = Color.FromArgb("#F59E0B");
                ConfirmButton.Text = "PARTIAL PAYMENT";
            }
            else
            {
                ChangeLabel.Text = "£0.00";
                RemainingFrame.IsVisible = false;
                ConfirmButton.IsEnabled = false;
                ConfirmButton.BackgroundColor = Color.FromArgb("#9CA3AF");
                ConfirmButton.Text = "CONFIRM PAYMENT";
            }
        }

        private void OnQuick20Clicked(object sender, EventArgs e) => SetAmount(20);
        private void OnQuick50Clicked(object sender, EventArgs e) => SetAmount(50);
        private void OnQuick100Clicked(object sender, EventArgs e) => SetAmount(100);
        
        private void OnExactAmountClicked(object sender, EventArgs e) => SetAmount(_amountDue);

        private void OnAmountChanged(object sender, TextChangedEventArgs e)
        {
            // Don't auto-update, wait for Apply button
        }

        private void OnApplyAmountClicked(object sender, EventArgs e)
        {
            if (decimal.TryParse(AmountReceivedEntry.Text, out decimal amount) && amount > 0)
            {
                _amountReceived = amount;
                UpdateDisplay();
            }
        }

        private void OnConfirmClicked(object sender, EventArgs e)
        {
            decimal change = Math.Max(0, _amountReceived - _amountDue);
            decimal remaining = Math.Max(0, _amountDue - _amountReceived);
            decimal actualPaid = Math.Min(_amountReceived, _amountDue);

            var result = new CashPaymentResult
            {
                Success = true,
                AmountPaid = actualPaid,
                Change = change,
                Remaining = remaining
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
    }
}
