using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Views;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Pages
{
    public partial class OrderSearchModal : ContentPage
    {
        private bool _isOpeningKeyboard;

        public event Action<string>? SearchSubmitted;

        public OrderSearchModal()
        {
            InitializeComponent();
        }

        private async Task OpenKeyboardForSearchAsync()
        {
            if (_isOpeningKeyboard)
            {
                return;
            }

            _isOpeningKeyboard = true;
            try
            {
                SearchEntry.Unfocus();

                var keyboard = new VirtualKeyboardDialog();
                keyboard.SetPrompt("Search orders", "SEARCH");
                keyboard.SetInitialText(SearchEntry.Text ?? string.Empty);

                var result = await keyboard.ShowAsync(this);
                if (result != null)
                {
                    SearchEntry.Text = result.Trim();
                }
            }
            finally
            {
                _isOpeningKeyboard = false;
            }
        }

        private async void OnSearchFieldTapped(object sender, EventArgs e)
        {
            await OpenKeyboardForSearchAsync();
        }

        private async void OnSearchClicked(object sender, EventArgs e)
        {
            var searchText = SearchEntry.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                await AppAlertService.ShowAlertAsync("Search", "Type an order number or phone number.");
                return;
            }

            SearchSubmitted?.Invoke(searchText);
            await Navigation.PopModalAsync();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}
