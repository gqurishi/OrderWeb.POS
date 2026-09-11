using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Helpers;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class TipSelectionDialog : ContentView
    {
        private TaskCompletionSource<decimal>? _taskCompletionSource;
        private Grid? _parentGrid;
        private decimal _orderTotal;
        private decimal _selectedTip;
        private bool _isCustomTipKeyboardOpen;

        public TipSelectionDialog()
        {
            InitializeComponent();
            TabletLayoutHelper.AttachDialog(this, DialogCard, 560, 720, ApplyResponsiveLayout);
        }

        private void ApplyResponsiveLayout(bool tablet, bool shortWindow)
        {
            DialogContent.Spacing = shortWindow ? 10 : tablet ? 12 : 16;
            DialogTitle.FontSize = tablet ? 24 : 27;
            CloseButton.WidthRequest = tablet ? 78 : 84;
            CloseButton.HeightRequest = 45;
            OrderTotalCard.Padding = shortWindow ? 12 : tablet ? 16 : 20;
            OrderTotalLabel.FontSize = shortWindow ? 28 : tablet ? 31 : 34;
            TipOptionsGrid.ColumnSpacing = tablet ? 10 : 15;
            TipOptionsGrid.RowSpacing = tablet ? 10 : 15;
            ContinueButton.HeightRequest = tablet ? 54 : 60;
        }

        public void SetOrderTotal(decimal total)
        {
            _orderTotal = total;
            OrderTotalLabel.Text = $"£{total:F2}";
            _selectedTip = 0;
            CustomTipEntry.Text = string.Empty;
            TipAmountLabel.Text = "£0.00";
        }

        public async Task<decimal> ShowAsync()
        {
            using var idleGuard = POS_in_NET.Services.ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<decimal>();
            
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

        private void UpdateTipDisplay()
        {
            TipAmountLabel.Text = $"£{_selectedTip:F2}";
        }

        private void OnNoTipClicked(object sender, EventArgs e)
        {
            CustomTipEntry.Text = string.Empty;
            _selectedTip = 0;
            UpdateTipDisplay();
        }

        private void OnTip5Clicked(object sender, EventArgs e)
        {
            CustomTipEntry.Text = string.Empty;
            _selectedTip = 5;
            UpdateTipDisplay();
        }

        private void OnTip10Clicked(object sender, EventArgs e)
        {
            CustomTipEntry.Text = string.Empty;
            _selectedTip = 10;
            UpdateTipDisplay();
        }

        private void OnTip20Clicked(object sender, EventArgs e)
        {
            CustomTipEntry.Text = string.Empty;
            _selectedTip = 20;
            UpdateTipDisplay();
        }

        private void OnCustomTipChanged(object sender, TextChangedEventArgs e)
        {
            if (TryParseTipAmount(e.NewTextValue, out decimal customTip) && customTip >= 0)
            {
                _selectedTip = Math.Round(customTip, 2);
                UpdateTipDisplay();
            }
            else if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                _selectedTip = 0;
                UpdateTipDisplay();
            }
        }

        private async void OnCustomTipFocused(object? sender, FocusEventArgs e)
        {
            CustomTipEntry.Unfocus();
            await ShowCustomTipKeyboardAsync();
        }

        private async void OnCustomTipButtonClicked(object? sender, EventArgs e)
        {
            await ShowCustomTipKeyboardAsync();
        }

        private async Task ShowCustomTipKeyboardAsync()
        {
            if (_isCustomTipKeyboardOpen)
            {
                return;
            }

            _isCustomTipKeyboardOpen = true;
            try
            {
                var keyboard = new OrderWeb.SharedUI.Controls.NumericKeyboardDialog();
                var amount = await keyboard.ShowCurrencyAsync(_selectedTip > 0 ? _selectedTip : null);
                if (amount.HasValue)
                {
                    _selectedTip = amount.Value;
                    CustomTipEntry.Text = amount.Value.ToString("0.00");
                    UpdateTipDisplay();
                }
            }
            finally
            {
                _isCustomTipKeyboardOpen = false;
            }
        }

        private void OnContinueClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(_selectedTip);
            CloseDialog();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(-1); // -1 indicates cancelled
            CloseDialog();
        }

        private void CloseDialog()
        {
            if (_parentGrid != null)
            {
                _parentGrid.Children.Remove(this);
            }
        }

        private static bool TryParseTipAmount(string? input, out decimal amount)
        {
            var normalized = (input ?? string.Empty)
                .Replace("£", string.Empty)
                .Replace(",", string.Empty)
                .Trim();

            return decimal.TryParse(normalized, out amount);
        }
    }
}
