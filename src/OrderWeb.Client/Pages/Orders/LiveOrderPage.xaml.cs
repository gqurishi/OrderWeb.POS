using System.Globalization;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Services;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

public partial class LiveOrderPage : ContentPage
{
    private readonly IOpenOrderListService _openOrders = ClientServiceProvider.OpenOrders;
    private readonly OpenOrderListView _listView = new();
    private readonly IDispatcherTimer _clockTimer;
    private OpenOrderChannelKind _selectedChannel = OpenOrderChannelKind.All;

    public LiveOrderPage()
    {
        InitializeComponent();
        Sidebar.MenuItemSelected += async (_, label) => await SelectSidebarItemAsync(label);

        _listView.ChannelChanged += async (_, channel) =>
        {
            _selectedChannel = channel;
            await RefreshOrdersAsync();
        };
        _listView.OrderSelected += async (_, card) =>
            await DisplayAlert(card.OrderNumber ?? card.OrderId, BuildOrderDetails(card), "OK");

        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        UpdateClock();
        ContentHost.Content = _listView;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshOrdersAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer.Stop();
    }

    private async Task RefreshOrdersAsync()
    {
        var result = await _openOrders.GetOpenOrdersAsync(_selectedChannel);
        if (result.IsSuccess && result.Value is not null)
        {
            _listView.Apply(result.Value with { SelectedChannel = _selectedChannel });
        }
    }

    private static string BuildOrderDetails(OpenOrderCardDto card) =>
        string.Join('\n', new[]
        {
            card.ChannelLabel,
            card.CustomerName,
            card.TableLabel,
            $"Total: £{card.TotalAmount:F2}",
            card.TimeDisplay
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private async void OnRefreshClicked(object sender, EventArgs e) => await RefreshOrdersAsync();

    private async void OnMenuClicked(object sender, EventArgs e) => await OpenSidebarAsync();

    private async void OnLogoutClicked(object sender, EventArgs e) => await Navigation.PopToRootAsync(false);

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private void UpdateClock()
    {
        var now = DateTime.Now;
        DateLabel.Text = now.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        TimeLabel.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private async Task SelectSidebarItemAsync(string label)
    {
        await CloseSidebarAsync();
        if (label == "Live Order")
        {
            return;
        }

        await Navigation.PushAsync(label switch
        {
            "Dashboard" => new Pages.Dashboards.ManagerDashboardPage(),
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Collection" => new CollectionOrderPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Web Orders" => new OnlineOrdersPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            "Order History" => new OrderHistoryPage(),
            _ => new LiveOrderPage()
        }, false);
    }

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
}
