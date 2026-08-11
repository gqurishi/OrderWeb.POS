using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Helpers;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public class CardPaymentResult
    {
        public bool Success { get; set; }
        public decimal AmountPaid { get; set; }
        public string? TerminalReference { get; set; }
    }

    public partial class CardPaymentDialog : ContentView
    {
        private TaskCompletionSource<CardPaymentResult>? _taskCompletionSource;
        private Grid? _parentGrid;
        private decimal _amount;

        public CardPaymentDialog()
        {
            InitializeComponent();
            TabletLayoutHelper.AttachDialog(this, DialogCard, 600, 760);
        }

        public void SetAmount(decimal amount)
        {
            _amount = amount;
            AmountLabel.Text = $"£{amount:F2}";
            ApprovalCheckBox.IsChecked = false;
            TerminalReferenceEntry.Text = string.Empty;
            UpdateConfirmationState();
        }

        public async Task<CardPaymentResult> ShowAsync()
        {
            using var idleGuard = POS_in_NET.Services.ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<CardPaymentResult>();
            
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

        private void OnYesClicked(object sender, EventArgs e)
        {
            var terminalReference = TerminalReferenceEntry.Text?.Trim();
            if (!ApprovalCheckBox.IsChecked || string.IsNullOrWhiteSpace(terminalReference))
            {
                StatusLabel.Text = "Confirm the approved matching amount and enter the card receipt/reference.";
                StatusLabel.TextColor = Color.FromArgb("#B91C1C");
                UpdateConfirmationState();
                return;
            }

            _taskCompletionSource?.TrySetResult(new CardPaymentResult
            {
                Success = true,
                AmountPaid = _amount,
                TerminalReference = terminalReference
            });
            CloseDialog();
        }

        private void OnConfirmationChanged(object? sender, EventArgs e) => UpdateConfirmationState();

        private void UpdateConfirmationState()
        {
            YesButton.IsEnabled = ApprovalCheckBox.IsChecked
                && !string.IsNullOrWhiteSpace(TerminalReferenceEntry.Text);
            YesButton.Opacity = YesButton.IsEnabled ? 1 : 0.5;
        }

        private void OnNoClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(new CardPaymentResult { Success = false });
            CloseDialog();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(new CardPaymentResult { Success = false });
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
