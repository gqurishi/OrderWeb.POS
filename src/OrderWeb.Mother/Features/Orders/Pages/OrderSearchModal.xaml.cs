using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Pages
{
    public partial class OrderSearchModal : ContentPage
    {
        public event Action<string>? SearchSubmitted;

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
                SearchEntry.Focus();
            });
        }

        private async void OnSearchClicked(object? sender, EventArgs e)
        {
            var searchText = SearchEntry.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                await AppAlertService.ShowAlertAsync("Search", "Type an order number or phone number.");
                SearchEntry.Focus();
                return;
            }

            SearchSubmitted?.Invoke(searchText);
            await Navigation.PopModalAsync();
        }

        private async void OnCancelClicked(object? sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}
