namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Views;

public partial class LoyaltyPage : ContentPage
{
    public LoyaltyPage()
    {
        InitializeComponent();

        Loyalty.ShowDiagnostics = false;
        Loyalty.SetCustomer(null);
        Loyalty.SearchRequested += async (_, _) => await DisplayAlert("Loyalty", "Loyalty data is available only through Mother POS.", "OK");
        Loyalty.NewCustomerRequested += async (_, _) => await DisplayAlert("Loyalty", "Create customers from Mother POS.", "OK");
        Loyalty.CreateCustomerRequested += async (_, _) => await DisplayAlert("Loyalty", "Create customers from Mother POS.", "OK");
        Loyalty.AddPointsRequested += async (_, _) => await DisplayAlert("Loyalty", "Connect to Mother POS before adding points.", "OK");
        Loyalty.RedeemPointsRequested += async (_, _) => await DisplayAlert("Loyalty", "Connect to Mother POS before redeeming points.", "OK");
        Loyalty.ViewHistoryRequested += async (_, _) => await DisplayAlert("Loyalty", "Points history is available on Mother POS.", "OK");
        Loyalty.SendStatementRequested += async (_, _) => await DisplayAlert("Loyalty", "Statements are sent from Mother POS.", "OK");

        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

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
}
