namespace OrderWeb.Client.Pages.Dashboards;

public partial class UserDashboardPage : ContentPage
{
    private bool _sidebarOpen;

    public UserDashboardPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await ToggleSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await DisplayAlert("Restaurant POS", "Logout returns to login.", "OK");
        Sidebar.MenuItemSelected += async (_, menu) => await SelectMenuAsync(menu);
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        var compact = Width > 0 && Width < 900;
        var iconSize = compact ? 150 : 190;
        TileGrid.Padding = 30;
        TileGrid.RowSpacing = compact ? 42 : 80;
        TileGrid.ColumnSpacing = compact ? 38 : 150;
        TileGrid.RowDefinitions.Clear();
        TileGrid.ColumnDefinitions.Clear();
        TileGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TileGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        TileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        TileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetRow(RestaurantTile, 0);
        Grid.SetColumn(RestaurantTile, 0);
        Grid.SetRow(DeliveryTile, 0);
        Grid.SetColumn(DeliveryTile, 1);
        Grid.SetRow(CollectionTile, 0);
        Grid.SetColumn(CollectionTile, 2);
        Grid.SetRow(LiveOrderTile, 1);
        Grid.SetColumn(LiveOrderTile, 1);

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
        await DisplayAlert("Restaurant POS", $"{menu} navigation is not available yet.", "OK");
    }

    private async void OnSidebarBackdropTapped(object sender, TappedEventArgs e)
    {
        await CloseSidebarAsync();
    }

    private async void OnRestaurantTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert("Restaurant POS", "Open Restaurant/Table layout.", "OK");
    }

    private async void OnDeliveryTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert("Restaurant POS", "Open Delivery flow.", "OK");
    }

    private async void OnCollectionTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert("Restaurant POS", "Open Collection flow.", "OK");
    }

    private async void OnLiveOrderTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert("Restaurant POS", "Open Live orders screen.", "OK");
    }
}
