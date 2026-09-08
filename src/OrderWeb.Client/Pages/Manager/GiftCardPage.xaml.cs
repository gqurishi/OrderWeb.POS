namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Views;

public partial class GiftCardPage : ContentPage
{
    public GiftCardPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);

        Gift.ActivateLookupRequested += async (_, _) => await ShowMotherOnlyAsync();
        Gift.ActivateRequested += async (_, _) => await ShowMotherOnlyAsync();
        Gift.GenerateSellCardRequested += async (_, _) => await ShowMotherOnlyAsync();
        Gift.SellRequested += async (_, _) => await ShowMotherOnlyAsync();
        Gift.TopUpLookupRequested += async (_, _) => await ShowMotherOnlyAsync();
        Gift.TopUpRequested += async (_, _) => await ShowMotherOnlyAsync();
        Gift.RedeemLookupRequested += async (_, _) => await ShowMotherOnlyAsync();
        Gift.RedeemRequested += async (_, _) => await ShowMotherOnlyAsync();
    }

    private async Task ShowMotherOnlyAsync() =>
        await DisplayAlert("Gift Cards", "Gift-card operations are available through Mother POS.", "OK");

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private async Task OpenSidebarAsync()
    {
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async Task CloseSidebarAsync()
    {
        await Sidebar.TranslateTo(-280, 0, 160, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
    }

    private async Task NavigateFromSidebarAsync(string menu)
    {
        await CloseSidebarAsync();
        if (string.Equals(menu, "Dashboard", StringComparison.OrdinalIgnoreCase))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        if (!ClientHostAccess.CanOpenMenu(menu))
        {
            return;
        }

        await Navigation.PushAsync(menu switch
        {
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Collection" => new CollectionOrderPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Live Order" => new LiveOrderPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            "Order History" => new OrderHistoryPage(),
            _ => new GiftCardPage()
        }, false);
    }
}
