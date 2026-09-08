namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;

public partial class GiftCardPage : ContentPage
{
    public GiftCardPage()
    {
        InitializeComponent();
        LoadEmptyState();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

    private async void OnLookupClicked(object sender, EventArgs e) => await DisplayAlert("Gift Cards", "Gift-card data is available only through Mother POS.", "OK");
    private async void OnRedeemClicked(object sender, EventArgs e) => await DisplayAlert("Gift Cards", "Connect to Mother POS before redeeming a gift card.", "OK");
    private async void OnAddBalanceClicked(object sender, EventArgs e) => await DisplayAlert("Gift Cards", "Gift-card top-up must be completed by Mother POS.", "OK");
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

    private void LoadEmptyState()
    {
        GiftCardNameLabel.Text = "No gift card selected";
        GiftCardStatusLabel.Text = "Connect to Mother POS";
        GiftCardNumberLabel.Text = "Card: -";
        GiftCardBalanceLabel.Text = "£0.00";
    }
}
