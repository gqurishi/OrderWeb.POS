using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Services;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

public partial class DeliveryOrderPage : ContentPage
{
    private readonly IDeliveryDetailsService _details = ClientServiceProvider.DeliveryDetails;
    private readonly ClientCacheService _cache = ClientServiceProvider.Cache;
    private readonly MotherOrderClient _orderClient = new();
    private readonly DeliveryDetailsView _detailsView = new();
    private CustomerSummaryDto? _selectedCustomer;
    private DeliveryZoneQuote? _deliveryQuote;

    public DeliveryOrderPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
        Sidebar.UpdateAllClicked += async (_, _) => await UpdateAllAsync();

        _detailsView.CustomerSearchRequested += async (_, request) => await SearchCustomersAsync(request);
        _detailsView.AddressLookupRequested += async (_, term) => await LookupAddressAsync(term);
        _detailsView.CustomerSelected += (_, customer) => _selectedCustomer = customer;
        _detailsView.AddressSelected += async (_, suggestion) => await QuoteZoneAsync(suggestion.Postcode);
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

    private async Task SearchCustomersAsync(CustomerSearchRequestDto request)
    {
        var result = await _details.SearchCustomersAsync(request);
        if (result.IsSuccess && result.Value is not null)
        {
            _detailsView.Apply(result.Value);
        }
    }

    private async Task LookupAddressAsync(string term)
    {
        var result = await _details.LookupAddressAsync(term);
        if (result.IsSuccess && result.Value is not null)
        {
            _detailsView.Apply(result.Value);
            await QuoteZoneAsync(term);
        }
    }

    private async Task QuoteZoneAsync(string? postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
        {
            return;
        }

        _deliveryQuote = await ClientServiceProvider.MotherCustomers.QuoteDeliveryZoneAsync(postcode);
    }

    private async Task ContinueAsync(DeliveryDetailsDto draft)
    {
        var name = draft.Name?.Trim();
        var phone = draft.Phone?.Trim();
        var normalizedPostcode = MotherCustomerClient.NormalizePostcode(draft.Postcode);

        if (string.IsNullOrWhiteSpace(draft.AddressLine))
        {
            await DisplayAlert("Delivery Order", "Delivery address is required to continue.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedPostcode))
        {
            await DisplayAlert("Delivery Order", "Full postcode is required for delivery zone pricing.", "OK");
            return;
        }

        name = string.IsNullOrWhiteSpace(name) ? "Delivery Customer" : name;
        phone = string.IsNullOrWhiteSpace(phone) ? "N/A" : phone;

        if (_deliveryQuote is null)
        {
            await QuoteZoneAsync(normalizedPostcode);
        }

        var address = BuildFullAddress(draft, normalizedPostcode);
        var customer = _selectedCustomer ?? new CustomerSummaryDto(
            "0",
            null,
            name,
            phone,
            null,
            address,
            draft.City,
            null,
            normalizedPostcode,
            null,
            CustomerOrderKind.Delivery,
            null);
        customer = customer with
        {
            Name = name,
            Phone = phone,
            Address = address,
            City = draft.City,
            Postcode = normalizedPostcode
        };

        var cached = new CachedCustomer(
            int.TryParse(customer.Id, out var id) ? id : 0,
            customer.MotherId ?? string.Empty,
            name,
            phone,
            customer.Email,
            address,
            normalizedPostcode,
            customer.LoyaltyPoints ?? 0);

        var orderDraft = new CustomerOrderDraft(
            "Delivery",
            cached,
            null,
            "ASAP",
            null,
            address,
            normalizedPostcode,
            _deliveryQuote?.ZoneName ?? draft.ZoneName,
            _deliveryQuote?.DeliveryFee ?? draft.DeliveryFee ?? 0m);

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
                    Phone = customer.Phone ?? cached.Phone,
                    Address = customer.Address ?? cached.Address,
                    Postcode = customer.Postcode ?? cached.Postcode
                };
            }

            var session = await _cache.GetCurrentLoginSessionAsync();
            var orderResult = await _orderClient.CreateCustomerOrderAsync(orderDraft with { Customer = cached }, session);
            await _cache.SaveOrderStateAsync(orderResult.State);
            await Navigation.PushAsync(new OrderPage(), false);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Delivery Order", $"Failed to continue: {ex.Message}", "OK");
        }
    }

    private static string BuildFullAddress(DeliveryDetailsDto draft, string postcode)
    {
        var parts = new[]
        {
            draft.AddressLine?.Trim(),
            draft.City?.Trim(),
            postcode
        }.Where(part => !string.IsNullOrWhiteSpace(part));

        return string.Join("\n", parts);
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
        if (menu == "Delivery")
        {
            return;
        }

        await Navigation.PushAsync(menu switch
        {
            "Dashboard" => new Pages.Dashboards.ManagerDashboardPage(),
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Collection" => new CollectionOrderPage(),
            "Live Order" => new LiveOrderPage(),
            "Web Orders" => new OnlineOrdersPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            "Order History" => new OrderHistoryPage(),
            _ => new DeliveryOrderPage()
        }, false);
    }

    private async Task UpdateAllAsync()
    {
        await CloseSidebarAsync();
        await DisplayAlert("Delivery Order", "Connect to Mother POS to refresh all client cache data.", "OK");
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();
}
