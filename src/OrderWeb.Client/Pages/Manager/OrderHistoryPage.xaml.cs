namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Services;
using OrderWeb.SharedUI.Views;

public partial class OrderHistoryPage : ContentPage
{
    private readonly IOrderHistoryService _history = ClientServiceProvider.OrderHistory;
    private readonly IOrderSearchService _orderSearch = ClientServiceProvider.OrderSearch;
    private readonly OrderHistoryView _historyView = new();
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private OpenOrderChannelKind _selectedChannel = OpenOrderChannelKind.All;

    public OrderHistoryPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);

        _historyView.DateFilterRequested += async (_, date) => await PickDateAsync(date);
        _historyView.ChannelChanged += async (_, channel) =>
        {
            _selectedChannel = channel;
            await LoadHistoryAsync();
        };
        _historyView.SearchRequested += async (_, query) => await SearchOrdersAsync(query);
        _historyView.SearchOverlayOpened += async (_, _) => await PrimeSearchOverlayAsync();
        _historyView.HistoryItemSelected += async (_, item) =>
            await DisplayAlert(item.OrderNumber ?? item.OrderId, BuildDetails(item), "OK");

        ContentHost.Content = _historyView;
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        var result = await _history.GetHistoryAsync(_selectedDate, _selectedChannel);
        if (result.IsSuccess && result.Value is not null)
        {
            _historyView.Apply(result.Value);
        }
    }

    private async Task PickDateAsync(DateOnly current)
    {
        var picked = await DisplayPromptAsync(
            "Filter by Date",
            "Enter date (yyyy-MM-dd)",
            initialValue: current.ToString("yyyy-MM-dd"),
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(picked) || !DateOnly.TryParse(picked, out var date))
        {
            return;
        }

        _selectedDate = date;
        await LoadHistoryAsync();
    }

    private async Task PrimeSearchOverlayAsync()
    {
        var sync = await ClientServiceProvider.SyncStatus.GetSyncStatusAsync();
        _historyView.ApplySearch(new OrderSearchResultDto(null, Array.Empty<OrderSearchHitDto>(), sync));
    }

    private async Task SearchOrdersAsync(string query)
    {
        var result = await _orderSearch.SearchOrdersAsync(new OrderSearchRequestDto(query, _selectedDate));
        if (result.IsSuccess && result.Value is not null)
        {
            _historyView.ApplySearch(result.Value);
        }
    }

    private static string BuildDetails(OrderHistoryItemDto item) =>
        string.Join('\n', new[]
        {
            item.ChannelLabel,
            item.CustomerDisplay,
            $"Total: £{item.TotalAmount:F2}",
            item.StatusDisplay,
            item.PaymentDisplay
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

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
        await Navigation.PushAsync(menu switch
        {
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Collection" => new CollectionOrderPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Live Order" => new LiveOrderPage(),
            "Web Orders" => new OnlineOrdersPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            _ => new OrderHistoryPage()
        }, false);
    }
}
