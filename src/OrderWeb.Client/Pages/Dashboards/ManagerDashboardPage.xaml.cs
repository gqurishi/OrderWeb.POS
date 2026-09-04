namespace OrderWeb.Client.Pages.Dashboards;

using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;

public partial class ManagerDashboardPage : ContentPage
{
    private bool _sidebarOpen;

    public ManagerDashboardPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await ToggleSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await DisplayAlert("Restaurant POS", "Logout returns to login.", "OK");
        Sidebar.MenuItemSelected += async (_, menu) => await SelectMenuAsync(menu);
        Sidebar.UpdateAllClicked += async (_, _) => await UpdateAllAsync();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        var compact = Width > 0 && Width < 900;
        var iconSize = compact ? 150 : 190;
        DashboardContent.Padding = 30;
        MainActionsGrid.ColumnSpacing = compact ? 38 : 150;
        MainActionsGrid.RowSpacing = compact ? 42 : 80;
        MainActionsGrid.RowDefinitions.Clear();
        MainActionsGrid.ColumnDefinitions.Clear();
        MainActionsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        MainActionsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        MainActionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        if (compact)
        {
            MainActionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MoveMainAction(TileFor(DeliveryIcon), 0, 1);
            MoveMainAction(TileFor(CollectionIcon), 1, 0);
            MoveMainAction(TileFor(LiveOrderIcon), 1, 1);
        }
        else
        {
            MainActionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MainActionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MoveMainAction(TileFor(DeliveryIcon), 0, 1);
            MoveMainAction(TileFor(CollectionIcon), 0, 2);
            MoveMainAction(TileFor(LiveOrderIcon), 1, 1);
        }

        MoveMainAction(TileFor(RestaurantIcon), 0, 0);
        foreach (var image in new[] { RestaurantIcon, DeliveryIcon, CollectionIcon, LiveOrderIcon })
        {
            image.WidthRequest = iconSize;
            image.HeightRequest = iconSize;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyResponsiveLayout();
    }

    private static VisualElement? TileFor(Image image)
    {
        return image.Parent?.Parent as VisualElement;
    }

    private static void MoveMainAction(VisualElement? view, int row, int column)
    {
        if (view is null)
        {
            return;
        }

        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
    }

    private async Task ToggleSidebarAsync()
    {
        if (_sidebarOpen)
        {
            await CloseSidebarAsync();
            return;
        }

        _sidebarOpen = true;
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 240, Easing.CubicOut);
    }

    private async Task CloseSidebarAsync()
    {
        if (!_sidebarOpen)
        {
            return;
        }

        await Sidebar.TranslateTo(-280, 0, 180, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
        _sidebarOpen = false;
    }

    private async Task SelectMenuAsync(string menu)
    {
        Sidebar.SelectedMenu = menu;
        await CloseSidebarAsync();
        switch (menu)
        {
            case "Dashboard":
                return;
            case "Cash Drawer":
                await OpenPageAsync(new CashDrawerPage());
                return;
            case "Live Order":
            case "Web Orders":
                await OpenPageAsync(new LiveOrderPage());
                return;
            case "Restaurant":
                await OpenPageAsync(new TableLayoutPage());
                return;
            case "Collection":
                await OpenPageAsync(new CollectionOrderPage());
                return;
            case "Delivery":
                await OpenPageAsync(new DeliveryOrderPage());
                return;
            case "Gift Cards":
                await OpenPageAsync(new GiftCardPage());
                return;
            case "Loyalty Points":
                await OpenPageAsync(new LoyaltyPage());
                return;
            case "Reservation":
                await OpenPageAsync(new ReservationPage());
                return;
            case "Order History":
                await OpenPageAsync(new OrderHistoryPage());
                return;
        }
    }

    private async Task OpenPageAsync(Page page)
    {
        await Navigation.PushAsync(page, false);
    }

    private async Task UpdateAllAsync()
    {
        await CloseSidebarAsync();
        await DisplayAlert("Restaurant POS", "Client POS updates from Mother POS during pairing/bootstrap and sync.", "OK");
    }

    private async void OnSidebarBackdropTapped(object sender, TappedEventArgs e)
    {
        await CloseSidebarAsync();
    }

    private async void OnRestaurantTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new TableLayoutPage());
    }

    private async void OnDeliveryTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new DeliveryOrderPage());
    }

    private async void OnCollectionTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new CollectionOrderPage());
    }

    private async void OnLiveOrderTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new LiveOrderPage());
    }

    private async void OnWebOrdersTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new OnlineOrdersPage());
    }

    private async void OnGiftCardsTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new GiftCardPage());
    }

    private async void OnLoyaltyTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new LoyaltyPage());
    }

    private async void OnReservationTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new ReservationPage());
    }

    private async void OnOrderHistoryTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new OrderHistoryPage());
    }

    private async void OnCashDrawerTapped(object sender, TappedEventArgs e)
    {
        await OpenPageAsync(new CashDrawerPage());
    }
}
