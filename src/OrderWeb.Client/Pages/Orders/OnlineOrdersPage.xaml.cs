using OrderWeb.Client.Models;
using OrderWeb.Client.Services;

namespace OrderWeb.Client.Pages.Orders;

public partial class OnlineOrdersPage : ContentPage
{
    public OnlineOrdersPage()
    {
        InitializeComponent();
        Title = "Restaurant POS";
        Content = new Label
        {
            Text = "Web orders stay on Mother POS.",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync(false);
        }
    }
}
