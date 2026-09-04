using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;

namespace OrderWeb.Client.Pages.Orders;

public partial class DeliveryOrderPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherCustomerClient _customerClient = new();
    private readonly MotherOrderClient _orderClient = new();
    private DeliveryZoneQuote? _deliveryQuote;
    private CachedCustomer? _selectedCustomer;

    public DeliveryOrderPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
        Sidebar.UpdateAllClicked += async (_, _) => await UpdateAllAsync();
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
        ShowStatus("Connect to Mother POS to refresh all client cache data.", "#718096");
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private async void OnSearchPostcodeClicked(object sender, EventArgs e)
    {
        var term = AddressLookupEntry.Text?.Trim();
        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(term) &&
            string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(phone))
        {
            ShowStatus("Enter a postcode, address, name, or phone to search.", "#DC2626");
            return;
        }

        SearchPostcodeButton.Text = "Searching...";
        SearchPostcodeButton.IsEnabled = false;
        AddressResultsBorder.IsVisible = false;
        SearchResultsBorder.IsVisible = false;
        NoResultsPanel.IsVisible = false;

        try
        {
            var customerResults = await SearchCustomersInternalAsync(name, phone, term);
            if (customerResults.Count > 0)
            {
                SearchResultsCollection.ItemsSource = customerResults;
                SearchResultsBorder.IsVisible = true;
                ShowStatus($"Found {customerResults.Count} customer(s). Select one to fill the form.", "#10B981");
                return;
            }

            if (string.IsNullOrWhiteSpace(term))
            {
                NoResultsPanel.IsVisible = true;
                ShowStatus("No customer match. Enter a postcode to look up addresses.", "#718096");
                return;
            }

            var addresses = await _customerClient.LookupAddressesAsync(term);
            if (addresses.Count > 0)
            {
                AddressResultsCollection.ItemsSource = addresses;
                AddressResultsBorder.IsVisible = true;
                ShowStatus($"Found {addresses.Count} address(es). Select one to fill the form.", "#3B82F6");
            }
            else
            {
                NoResultsPanel.IsVisible = true;
                ShowStatus($"No addresses found for {term}. You can enter the address manually.", "#718096");
            }

            await QuoteDeliveryZoneAsync(false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Search failed: {ex.Message}. You can still enter the address manually.", "#DC2626");
        }
        finally
        {
            SearchPostcodeButton.Text = "Search";
            SearchPostcodeButton.IsEnabled = true;
        }
    }

    private async void OnSearchCustomerClicked(object sender, EventArgs e)
    {
        var results = await SearchCustomersInternalAsync(
            CustomerNameEntry.Text?.Trim(),
            PhoneNumberEntry.Text?.Trim(),
            AddressLookupEntry.Text?.Trim());

        AddressResultsBorder.IsVisible = false;
        SearchResultsCollection.ItemsSource = results;
        SearchResultsBorder.IsVisible = results.Count > 0;
        NoResultsPanel.IsVisible = results.Count == 0;
        ShowStatus(results.Count == 0 ? "No customers found matching your search." : $"Found {results.Count} customer(s).", results.Count == 0 ? "#718096" : "#10B981");
    }

    private async Task<IReadOnlyList<CachedCustomer>> SearchCustomersInternalAsync(string? name, string? phone, string? addressOrPostcode)
    {
        if (string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(phone) &&
            string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            ShowStatus("Enter customer name, phone, address, or postcode to search.", "#DC2626");
            return Array.Empty<CachedCustomer>();
        }

        ShowStatus("Searching customers...", "#718096");
        var request = new CustomerSearchRequest("Delivery", name, phone, addressOrPostcode);
        var cached = await _cache.SearchCachedCustomersAsync(request);
        var mother = await _customerClient.SearchCustomersAsync(request);
        var results = mother
            .Concat(cached)
            .GroupBy(customer => string.IsNullOrWhiteSpace(customer.MotherId) ? customer.Id.ToString() : customer.MotherId)
            .Select(group => group.First())
            .Take(10)
            .ToList();

        await _cache.CacheCustomersAsync(results);
        return results;
    }

    private void OnAddressSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not AddressSuggestion address)
        {
            return;
        }

        AddressLine1Entry.Text = string.Join(", ", new[] { address.AddressLine1, address.AddressLine2, address.AddressLine3 }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        CityEntry.Text = address.City;
        CountyEntry.Text = address.County;
        PostcodeResultEntry.Text = address.Postcode;
        AddressLookupEntry.Text = address.Postcode;
        AddressResultsBorder.IsVisible = false;
        ((CollectionView)sender).SelectedItem = null;
        _ = QuoteDeliveryZoneAsync(true);
    }

    private void OnCustomerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CachedCustomer customer)
        {
            return;
        }

        ApplyCustomerToForm(customer);
        SearchResultsBorder.IsVisible = false;
        NoResultsPanel.IsVisible = false;
        ((CollectionView)sender).SelectedItem = null;
    }

    private void ApplyCustomerToForm(CachedCustomer customer)
    {
        _selectedCustomer = customer;
        CustomerNameEntry.Text = customer.Name;
        PhoneNumberEntry.Text = customer.Phone;
        AddressLookupEntry.Text = customer.Postcode ?? customer.Address;
        ApplyAddressParts(customer.Address, customer.Postcode);
        if (!string.IsNullOrWhiteSpace(customer.Postcode))
        {
            _ = QuoteDeliveryZoneAsync(true);
        }
    }

    private void ApplyAddressParts(string? address, string? postcode)
    {
        AddressLine1Entry.Text = string.Empty;
        CityEntry.Text = string.Empty;
        CountyEntry.Text = string.Empty;
        PostcodeResultEntry.Text = postcode ?? string.Empty;

        var lines = (address ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            AddressLine1Entry.Text = address ?? string.Empty;
            return;
        }

        AddressLine1Entry.Text = lines[0];
        if (lines.Length >= 3)
        {
            CityEntry.Text = lines[1];
            PostcodeResultEntry.Text = string.IsNullOrWhiteSpace(postcode) ? lines[^1] : postcode;
        }

        if (lines.Length >= 4)
        {
            CountyEntry.Text = lines[2];
        }
    }

    private async Task QuoteDeliveryZoneAsync(bool showStatus)
    {
        var postcode = PostcodeResultEntry.Text;
        if (string.IsNullOrWhiteSpace(postcode))
        {
            postcode = AddressLookupEntry.Text;
        }

        if (string.IsNullOrWhiteSpace(postcode))
        {
            return;
        }

        _deliveryQuote = await _customerClient.QuoteDeliveryZoneAsync(postcode);
        if (!string.IsNullOrWhiteSpace(_deliveryQuote.Postcode))
        {
            PostcodeResultEntry.Text = _deliveryQuote.Postcode;
        }

        ZoneBorder.IsVisible = true;
        ZoneLabel.Text = _deliveryQuote.IsKnownZone
            ? $"{_deliveryQuote.ZoneName}: £{_deliveryQuote.DeliveryFee:F2} delivery fee"
            : $"No delivery zone found for {_deliveryQuote.Postcode}. Mother will mark it for admin review.";
        ZoneLabel.TextColor = _deliveryQuote.IsKnownZone ? Color.FromArgb("#1D4ED8") : Color.FromArgb("#B45309");

        if (showStatus)
        {
            ShowStatus(_deliveryQuote.IsKnownZone ? "Delivery zone checked." : "No delivery zone found.", _deliveryQuote.IsKnownZone ? "#10B981" : "#B45309");
        }
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();
        var normalizedPostcode = MotherCustomerClient.NormalizePostcode(PostcodeResultEntry.Text);

        if (string.IsNullOrWhiteSpace(AddressLine1Entry.Text))
        {
            ShowStatus("Delivery address is required to continue.", "#DC2626");
            AddressLine1Entry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedPostcode))
        {
            ShowStatus("Full postcode is required for delivery zone pricing.", "#DC2626");
            PostcodeResultEntry.Focus();
            return;
        }

        name = string.IsNullOrWhiteSpace(name) ? "Delivery Customer" : name;
        phone = string.IsNullOrWhiteSpace(phone) ? "N/A" : phone;

        if (_deliveryQuote == null)
        {
            await QuoteDeliveryZoneAsync(false);
        }

        var address = BuildFullAddress(normalizedPostcode);
        var customer = _selectedCustomer ?? new CachedCustomer(0, string.Empty, name, phone, null, address, normalizedPostcode, 0);
        customer = customer with
        {
            Name = name,
            Phone = phone,
            Address = address,
            Postcode = normalizedPostcode
        };

        var draft = new CustomerOrderDraft(
            "Delivery",
            customer,
            null,
            string.IsNullOrWhiteSpace(ScheduledTimeEntry.Text) ? "ASAP" : ScheduledTimeEntry.Text.Trim(),
            NotesEditor.Text?.Trim(),
            address,
            normalizedPostcode,
            _deliveryQuote?.ZoneName,
            _deliveryQuote?.DeliveryFee ?? 0m);

        try
        {
            ShowStatus("Saving customer with Mother POS...", "#718096");
            var savedCustomer = await _customerClient.SaveCustomerAsync(draft);
            await _cache.CacheCustomersAsync(new[] { savedCustomer });

            ShowStatus("Opening delivery order...", "#718096");
            var session = await _cache.GetCurrentLoginSessionAsync();
            var orderResult = await _orderClient.CreateCustomerOrderAsync(draft with { Customer = savedCustomer }, session);
            await _cache.SaveOrderStateAsync(orderResult.State);

            await Navigation.PushAsync(new OrderPage(), false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to continue: {ex.Message}", "#DC2626");
        }
    }

    private string BuildFullAddress(string postcode)
    {
        var parts = new[]
        {
            AddressLine1Entry.Text?.Trim(),
            CityEntry.Text?.Trim(),
            CountyEntry.Text?.Trim(),
            postcode
        }.Where(part => !string.IsNullOrWhiteSpace(part));

        return string.Join("\n", parts);
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync(false);
    }

    private void ShowStatus(string message, string color)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = Color.FromArgb(color);
    }
}
