using Microsoft.Maui.Controls;
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

        public TipSelectionDialog()
        {
            InitializeComponent();
        }

        public void SetOrderTotal(decimal total)
        {
            _orderTotal = total;
            OrderTotalLabel.Text = $"£{total:F2}";
            _selectedTip = 0;
            TipAmountLabel.Text = "£0.00";
        }

        public async Task<decimal> ShowAsync()
        {
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
            _selectedTip = 0;
            UpdateTipDisplay();
        }

        private void OnTip5Clicked(object sender, EventArgs e)
        {
            _selectedTip = 5;
            UpdateTipDisplay();
        }

        private void OnTip10Clicked(object sender, EventArgs e)
        {
            _selectedTip = 10;
            UpdateTipDisplay();
        }

        private void OnTip20Clicked(object sender, EventArgs e)
        {
            _selectedTip = 20;
            UpdateTipDisplay();
        }

        private void OnCustomTipClicked(object sender, EventArgs e)
        {
            if (decimal.TryParse(CustomTipEntry.Text, out decimal customTip) && customTip >= 0)
            {
                _selectedTip = Math.Round(customTip, 2);
                UpdateTipDisplay();
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
    }
}
