namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;

public partial class LoyaltyPage : ContentPage
{
    public LoyaltyPage()
    {
        InitializeComponent();
        LoadEmptyState();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

    private async void OnLookupClicked(object sender, EventArgs e) => await DisplayAlert("Loyalty", "Loyalty data is available only through Mother POS.", "OK");
    private async void OnAddPointsClicked(object sender, EventArgs e) => await DisplayAlert("Loyalty", "Connect to Mother POS before adding points.", "OK");
    private async void OnRedeemClicked(object sender, EventArgs e) => await DisplayAlert("Loyalty", "Connect to Mother POS before redeeming points.", "OK");
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
            "Gift Cards" => new GiftCardPage(),
            "Reservation" => new ReservationPage(),
            "Order History" => new OrderHistoryPage(),
            _ => new LoyaltyPage()
        }, false);
    }

    private void LoadEmptyState()
    {
        CustomerNameLabel.Text = "No customer selected";
        CustomerPhoneLabel.Text = "Connect to Mother POS";
        PointsBalanceLabel.Text = "0";
    }
}
