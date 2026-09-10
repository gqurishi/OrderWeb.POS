using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Views;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Pages
{
    public partial class OrderSearchModal : ContentPage
    {
        public event Action<string>? SearchSubmitted;

        private bool _keyboardOpen;
        private bool _searchCompleted;

        public OrderSearchModal()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Dispatcher.Dispatch(async () =>
            {
                await Task.Delay(80);
                if (!_searchCompleted)
                {
                    await OpenNumberKeyboardAsync();
                }
            });
        }

        private async void OnSearchEntryFocused(object? sender, FocusEventArgs e)
        {
            if (!e.IsFocused || _keyboardOpen || _searchCompleted)
            {
                return;
            }

            SearchEntry.Unfocus();
            await OpenNumberKeyboardAsync();
        }

        private async void OnSearchFieldTapped(object? sender, EventArgs e)
        {
            if (_keyboardOpen || _searchCompleted)
            {
                return;
            }

            await OpenNumberKeyboardAsync();
        }

        private async Task OpenNumberKeyboardAsync()
        {
            if (_keyboardOpen || _searchCompleted)
            {
                return;
            }

            _keyboardOpen = true;
            try
            {
                SearchEntry.Unfocus();
                var keyboard = new NumericKeyboardDialog();
                var value = await keyboard.ShowDigitsAsync(
                    SearchEntry.Text,
                    title: "Order number or phone",
                    maxDigits: 24,
                    hostPage: this);

                // CANCEL → stay on Search Orders so they can tap the field again.
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                SearchEntry.Text = value.Trim();

                // DONE → close keypad and run search immediately (no second tap).
                await SubmitSearchAsync(SearchEntry.Text);
            }
            finally
            {
                _keyboardOpen = false;
            }
        }

        private async void OnSearchClicked(object? sender, EventArgs e)
        {
            var searchText = SearchEntry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                await AppAlertService.ShowAlertAsync("Search", "Type an order number or phone number.");
                await OpenNumberKeyboardAsync();
                return;
            }

            await SubmitSearchAsync(searchText);
        }

        private async Task SubmitSearchAsync(string searchText)
        {
            if (_searchCompleted)
            {
                return;
            }

            _searchCompleted = true;
            SearchSubmitted?.Invoke(searchText);
            await Navigation.PopModalAsync();
        }

        private async void OnCancelClicked(object? sender, EventArgs e)
        {
            if (_searchCompleted)
            {
                return;
            }

            _searchCompleted = true;
            await Navigation.PopModalAsync();
        }
    }
}
