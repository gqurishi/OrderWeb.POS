using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public enum PaymentMethod
    {
        None,
        Cash,
        Card,
        GiftCard,
        Cancelled
    }

    public partial class PaymentMethodDialog : ContentView
    {
        private TaskCompletionSource<PaymentMethod>? _taskCompletionSource;
        private Grid? _parentGrid;
        private decimal _amountDue;

        public PaymentMethodDialog()
        {
            InitializeComponent();
        }

        public void SetAmountDue(decimal amount, decimal remaining = 0)
        {
            _amountDue = amount;
            AmountDueLabel.Text = $"£{amount:F2}";
            
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

        public async Task<PaymentMethod> ShowAsync()
        {
            _taskCompletionSource = new TaskCompletionSource<PaymentMethod>();
            
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

        private void OnCashClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(PaymentMethod.Cash);
            CloseDialog();
        }

        private void OnCardClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(PaymentMethod.Card);
            CloseDialog();
        }

        private void OnGiftCardClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(PaymentMethod.GiftCard);
            CloseDialog();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(PaymentMethod.Cancelled);
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
