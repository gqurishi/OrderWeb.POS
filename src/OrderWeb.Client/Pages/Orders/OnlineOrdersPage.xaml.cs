using OrderWeb.Client.Models;
using OrderWeb.Client.Services;

namespace OrderWeb.Client.Pages.Orders;

public partial class OnlineOrdersPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherOnlineOrderClient _onlineOrderClient = new();

    public OnlineOrdersPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await DisplayAlert("Restaurant POS", "Sidebar opens from dashboard/live screens.", "OK");
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        OrderDetails.ViewClicked += async (_, order) => await DisplayAlert(order.OrderNumber, $"{order.CustomerName}\n{order.OrderType} · {order.Status}\nDue {order.DueTime}\n£{order.Total:F2}", "OK");
        OrderDetails.AcceptClicked += async (_, order) => await UpdateStatusAsync(order, "Accepted");
        OrderDetails.PreparingClicked += async (_, order) => await UpdateStatusAsync(order, "Preparing");
        OrderDetails.ReadyClicked += async (_, order) => await UpdateStatusAsync(order, "Ready");
        OrderDetails.CompleteClicked += async (_, order) => await UpdateStatusAsync(order, "Completed");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshOrdersAsync();
    }

    private async Task RefreshOrdersAsync()
    {
        var orders = await _cache.GetOnlineOrdersAsync();
        OrderDetails.SetOrders(orders);
        StatusLabel.Text = $"{orders.Count} online order(s)";
    }

    private async Task UpdateStatusAsync(CachedOnlineOrder order, string status)
    {
        var updated = await _onlineOrderClient.UpdateStatusAsync(order, status, null);
        await _cache.SaveOnlineOrderAsync(updated);
        await RefreshOrdersAsync();
    }
}
