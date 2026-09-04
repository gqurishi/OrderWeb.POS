using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Helpers;
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
            TabletLayoutHelper.AttachDialog(this, DialogCard, 700, 760, ApplyResponsiveLayout);
        }

        private void ApplyResponsiveLayout(bool tablet, bool shortWindow)
        {
            DialogContent.Spacing = shortWindow ? 12 : tablet ? 17 : 25;
            TitleLabel.FontSize = tablet ? 25 : 30;
            CloseButton.WidthRequest = tablet ? 82 : 90;
            CloseButton.HeightRequest = 45;
            AmountDueCard.Padding = shortWindow ? 14 : tablet ? 17 : 20;
            AmountDueLabel.FontSize = shortWindow ? 29 : tablet ? 32 : 36;
            PaymentButtonsGrid.ColumnSpacing = tablet ? 12 : 20;
            var buttonHeight = shortWindow ? 88 : tablet ? 104 : 130;
            CashButton.HeightRequest = buttonHeight;
            CardButton.HeightRequest = buttonHeight;
            GiftCardButton.HeightRequest = buttonHeight;
            CashButton.FontSize = tablet ? 24 : 28;
            CardButton.FontSize = tablet ? 24 : 28;
            GiftCardButton.FontSize = tablet ? 21 : 24;
        }

        public void SetAmountDue(decimal amount, decimal remaining = 0, string? title = null)
        {
            _amountDue = amount;
            TitleLabel.Text = string.IsNullOrWhiteSpace(title)
                ? "SELECT PAYMENT METHOD"
                : title;
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
            using var idleGuard = POS_in_NET.Services.ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
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
