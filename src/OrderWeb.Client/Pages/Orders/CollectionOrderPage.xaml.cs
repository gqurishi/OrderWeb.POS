using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Services;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

public partial class CollectionOrderPage : ContentPage
{
    private readonly ICollectionDetailsService _details = ClientServiceProvider.CollectionDetails;
    private readonly ClientCacheService _cache = ClientServiceProvider.Cache;
    private readonly MotherOrderClient _orderClient = new();
    private readonly CollectionDetailsView _detailsView = new();
    private CustomerSummaryDto? _selectedCustomer;
    private bool _isContinuing;

    public CollectionOrderPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
        Sidebar.UpdateAllClicked += async (_, _) => await UpdateAllAsync();

        _detailsView.SearchRequested += async (_, request) => await SearchAsync(request);
        _detailsView.CustomerSelected += (_, customer) => _selectedCustomer = customer;
        _detailsView.ContinueRequested += async (_, draft) => await ContinueAsync(draft);

        ContentHost.Content = _detailsView;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var result = await _details.GetAsync();
        if (result.IsSuccess && result.Value is not null)
        {
            _detailsView.Apply(result.Value);
        }
    }

    private async Task SearchAsync(CustomerSearchRequestDto request)
    {
        var result = await _details.SearchAsync(request);
        if (result.IsSuccess && result.Value is not null)
        {
            _detailsView.Apply(result.Value);
        }
    }

    private async Task ContinueAsync(CollectionDetailsDto draft)
    {
        if (_isContinuing)
        {
            return;
        }

        var name = draft.Name?.Trim();
        var phone = draft.Phone?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("Collection Order", "Customer Name is required to continue.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            await DisplayAlert("Collection Order", "Phone Number is required to continue.", "OK");
            return;
        }

        var customer = _selectedCustomer ?? new CustomerSummaryDto(
            "0",
            null,
            name,
            phone,
            null,
            null,
            null,
            null,
            null,
            null,
            CustomerOrderKind.Collection,
            null);
        customer = customer with { Name = name, Phone = phone };

        var cached = new CachedCustomer(
            int.TryParse(customer.Id, out var id) ? id : 0,
            customer.MotherId ?? string.Empty,
            name,
            phone,
            customer.Email,
            customer.Address ?? string.Empty,
            customer.Postcode,
            customer.LoyaltyPoints ?? 0);

        var orderDraft = new CustomerOrderDraft(
            "Collection",
            cached,
            draft.PickupTime ?? "ASAP",
            null,
            draft.Notes,
            string.Empty,
            null,
            null,
            0m);

        _isContinuing = true;
        try
        {
            var upsert = await ClientServiceProvider.CustomerDirectory.UpsertCustomerAsync(customer);
            if (upsert.IsSuccess && upsert.Value is not null)
            {
                customer = upsert.Value;
                cached = cached with
                {
                    Id = int.TryParse(customer.Id, out var savedId) ? savedId : cached.Id,
                    MotherId = customer.MotherId ?? cached.MotherId,
                    Name = customer.Name ?? cached.Name,
                    Phone = customer.Phone ?? cached.Phone
                };
            }

            var session = await _cache.GetCurrentLoginSessionAsync();
            var orderResult = await _orderClient.CreateCustomerOrderAsync(orderDraft with { Customer = cached }, session);
            await _cache.SaveOrderStateAsync(orderResult.State);
            await Navigation.PushAsync(new OrderPage(), false);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Collection Order", $"Failed to continue: {ex.Message}", "OK");
        }
        finally
        {
            _isContinuing = false;
        }
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

    private async Task NavigateFromSidebarAsync(string menu)
    {
        await CloseSidebarAsync();
        if (menu == "Collection")
        {
            return;
        }

        await Navigation.PushAsync(menu switch
        {
            "Dashboard" => new Pages.Dashboards.ManagerDashboardPage(),
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Live Order" => new LiveOrderPage(),
            "Web Orders" => new OnlineOrdersPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            "Order History" => new OrderHistoryPage(),
            _ => new CollectionOrderPage()
        }, false);
    }

    private async Task UpdateAllAsync()
    {
        await CloseSidebarAsync();
        await DisplayAlert("Collection Order", "Connect to Mother POS to refresh all client cache data.", "OK");
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();
}
