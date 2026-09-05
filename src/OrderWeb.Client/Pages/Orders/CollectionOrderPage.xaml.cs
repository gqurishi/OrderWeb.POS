using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;

namespace OrderWeb.Client.Pages.Orders;

public partial class CollectionOrderPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherCustomerClient _customerClient = new();
    private readonly MotherOrderClient _orderClient = new();
    private CachedCustomer? _selectedCustomer;
    private bool _isContinuing;

    public CollectionOrderPage()
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
        ShowStatus("Connect to Mother POS to refresh all client cache data.", "#64748B");
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private async void OnSearchClicked(object sender, EventArgs e)
    {
        var searchName = CustomerNameEntry.Text?.Trim();
        var searchPhone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(searchName) && string.IsNullOrWhiteSpace(searchPhone))
        {
            ShowStatus("Please enter customer name or phone number to search.", "#DC2626");
            return;
        }

        var originalText = SearchButton.Text;
        SearchButton.Text = "Searching...";
        SearchButton.IsEnabled = false;
        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;

        try
        {
            var request = new CustomerSearchRequest("Collection", searchName, searchPhone, null);
            var cached = await _cache.SearchCachedCustomersAsync(request);
            var mother = await _customerClient.SearchCustomersAsync(request);
            var results = mother
                .Concat(cached)
                .GroupBy(customer => string.IsNullOrWhiteSpace(customer.MotherId) ? customer.Id.ToString() : customer.MotherId)
                .Select(group => group.First())
                .Take(10)
                .ToList();

            await _cache.CacheCustomersAsync(results);
            SearchResultsCollection.ItemsSource = results;
            SearchResultsBorder.IsVisible = results.Count > 0;
            NoResultsLabel.IsVisible = results.Count == 0;
            ShowStatus(results.Count == 0 ? "No existing customer found." : $"Found {results.Count} customer(s).", results.Count == 0 ? "#64748B" : "#10B981");
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to search customers: {ex.Message}", "#DC2626");
        }
        finally
        {
            SearchButton.Text = originalText;
            SearchButton.IsEnabled = true;
        }
    }

    private void OnCustomerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CachedCustomer customer)
        {
            return;
        }

        ApplyCustomer(customer);
        ((CollectionView)sender).SelectedItem = null;
    }

    private void OnCustomerTapped(object sender, EventArgs e)
    {
        if (sender is VisualElement element && element.BindingContext is CachedCustomer customer)
        {
            ApplyCustomer(customer);
        }
    }

    private void ApplyCustomer(CachedCustomer customer)
    {
        _selectedCustomer = customer;
        CustomerNameEntry.Text = customer.Name;
        PhoneNumberEntry.Text = customer.Phone;
        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;
        ShowStatus("Existing customer selected.", "#10B981");
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        if (_isContinuing)
        {
            return;
        }

        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowStatus("Customer Name is required to continue.", "#DC2626");
            CustomerNameEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            ShowStatus("Phone Number is required to continue.", "#DC2626");
            PhoneNumberEntry.Focus();
            return;
        }

        var customer = _selectedCustomer ?? new CachedCustomer(0, string.Empty, name, phone, null, string.Empty, null, 0);
        customer = customer with
        {
            Name = name,
            Phone = phone
        };

        var draft = new CustomerOrderDraft(
            "Collection",
            customer,
            "ASAP",
            null,
            string.Empty,
            null,
            null,
            null,
            0m);

        var button = sender as Button;
        var originalText = button?.Text;
        _isContinuing = true;
        if (button != null)
        {
            button.IsEnabled = false;
            button.Text = "Opening order...";
        }

        try
        {
            ShowStatus("Saving customer with Mother POS...", "#64748B");
            var savedCustomer = await _customerClient.SaveCustomerAsync(draft);
            await _cache.CacheCustomersAsync(new[] { savedCustomer });

            ShowStatus("Opening collection order...", "#64748B");
            var session = await _cache.GetCurrentLoginSessionAsync();
            var orderResult = await _orderClient.CreateCustomerOrderAsync(draft with { Customer = savedCustomer }, session);
            await _cache.SaveOrderStateAsync(orderResult.State);

            if (LegacyOrderEntryAccess.PreferLegacyRollback)
                await Navigation.PushAsync(SharedOrderEntryPage.CreateLegacyRollback(), false);
            else
                await Navigation.PushAsync(SharedOrderEntryPage.ForServiceType("Collection"), false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to continue: {ex.Message}", "#DC2626");
        }
        finally
        {
            _isContinuing = false;
            if (button != null)
            {
                button.Text = originalText ?? "Continue to Order";
                button.IsEnabled = true;
            }
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync(false);
    }

    private void ShowStatus(string message, string color)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = Color.FromArgb(color);
        StatusLabel.IsVisible = true;
    }
}
