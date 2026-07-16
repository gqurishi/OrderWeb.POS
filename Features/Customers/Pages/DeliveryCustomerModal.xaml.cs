using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;

namespace POS_in_NET.Pages;

public partial class DeliveryCustomerModal : ContentPage
{
    private const int MaxVisibleAddressResults = 10;
    private const double AddressResultRowHeight = 54;
    private const double AddressResultsHeightPadding = 8;

    private readonly DeliveryCustomerService _customerService;
    private readonly PostcodeLookupService _postcodeLookupService;
    private readonly DeliveryZoneService _deliveryZoneService;
    private readonly CustomerDataService _customerDataService = new();
    private DeliveryCustomer? _selectedCustomer;
    private bool _isOpeningKeyboard;

    public DeliveryCustomerModal()
    {
        InitializeComponent();
        _customerService = new DeliveryCustomerService();
        _postcodeLookupService = ServiceHelper.GetService<PostcodeLookupService>()
            ?? new PostcodeLookupService(new DatabaseService());
        _deliveryZoneService = new DeliveryZoneService(new DatabaseService());
    }

    private async void OnCustomerNameFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(CustomerNameEntry);
    }

    private async void OnPhoneNumberFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(PhoneNumberEntry);
    }

    private async void OnPostcodeFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(PostcodeEntry);
    }

    private async void OnAddressLine1FieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(AddressLine1Entry);
    }

    private async void OnCityFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(CityEntry);
    }

    private async void OnCountyFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(CountyEntry);
    }

    private async void OnPostcodeResultFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(PostcodeResultEntry);
    }

    private async Task OpenKeyboardForEntryAsync(Entry entry)
    {
        if (_isOpeningKeyboard)
        {
            return;
        }

        _isOpeningKeyboard = true;
        try
        {
            entry.Unfocus();

            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetPrompt(GetKeyboardTitle(entry), "DONE");
            keyboard.SetInitialText(entry.Text ?? string.Empty);

            var result = await keyboard.ShowAsync(this);
            if (result != null)
            {
                entry.Text = result.Trim();
            }
        }
        finally
        {
            _isOpeningKeyboard = false;
        }
    }

    private string GetKeyboardTitle(Entry entry)
    {
        if (entry == CustomerNameEntry)
        {
            return "Customer name";
        }

        if (entry == PhoneNumberEntry)
        {
            return "Phone number";
        }

        if (entry == PostcodeEntry)
        {
            return "Address search";
        }

        if (entry == AddressLine1Entry)
        {
            return "Street address";
        }

        if (entry == CityEntry)
        {
            return "City";
        }

        if (entry == CountyEntry)
        {
            return "County";
        }

        if (entry == PostcodeResultEntry)
        {
            return "Postcode";
        }

        return "Keyboard";
    }

    private async void OnSearchPostcodeClicked(object sender, EventArgs e)
    {
        var addressQuery = PostcodeEntry.Text?.Trim();
        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(addressQuery)
            && string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(phone))
        {
            await ToastNotification.ShowAsync("Required", "Enter a postcode, address, name, or phone to search.", NotificationType.Warning, 3000);
            return;
        }

        SearchPostcodeButton.Text = "Searching...";
        SearchPostcodeButton.IsEnabled = false;

        try
        {
            // 1. Check central customer data first (no API credit used).
            var localRecords = await _customerDataService.SearchForDeliveryAsync(addressQuery, name, phone);
            if (localRecords.Count > 0)
            {
                AddressResultsBorder.IsVisible = false;
                SearchResultsCollection.ItemsSource = localRecords.Select(ToDeliveryCustomer).ToList();
                SearchResultsBorder.IsVisible = true;
                NoResultsLabel.IsVisible = false;
                await ToastNotification.ShowAsync(
                    "Local Match",
                    $"Found {localRecords.Count} customer(s) in local cache. Select one to fill the form.",
                    NotificationType.Success,
                    3500);
                return;
            }

            // 2. No local match — ask OrderWeb for addresses (postcode lookup).
            if (string.IsNullOrWhiteSpace(addressQuery))
            {
                SearchResultsBorder.IsVisible = false;
                NoResultsLabel.IsVisible = true;
                await ToastNotification.ShowAsync(
                    "No Local Match",
                    "No matching customers found locally. Enter a postcode to look up addresses.",
                    NotificationType.Info,
                    4000);
                return;
            }

            var addresses = await _postcodeLookupService.LookupPostcodeAsync(addressQuery);

            SearchResultsBorder.IsVisible = false;
            NoResultsLabel.IsVisible = false;

            if (addresses.Count > 0)
            {
                ShowAddressResults(addresses);
                await ToastNotification.ShowAsync(
                    "OrderWeb",
                    $"Found {addresses.Count} address(es). Select one to fill the form.",
                    NotificationType.Info,
                    3000);
            }
            else
            {
                AddressResultsBorder.IsVisible = false;
                await ToastNotification.ShowAsync("No Results", $"No addresses found for: {addressQuery}", NotificationType.Info, 3000);
            }
        }
        catch (AddressLookupException ex)
        {
            AddressResultsBorder.IsVisible = false;
            var message = ex.Suggestions.Count > 0
                ? $"{ex.Message}\n\nTry: {string.Join(", ", ex.Suggestions)}"
                : ex.Message;
            await ToastNotification.ShowAsync("Address Lookup", message, NotificationType.Error, 5000);
        }
        catch (Exception ex)
        {
            AddressResultsBorder.IsVisible = false;
            await ToastNotification.ShowAsync("Error", $"Search failed: {ex.Message}\n\nYou can still enter the address manually.", NotificationType.Error, 5000);
        }
        finally
        {
            SearchPostcodeButton.Text = "Search";
            SearchPostcodeButton.IsEnabled = true;
        }
    }

    private void OnAddressSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is AddressResult address)
        {
            AddressLine1Entry.Text = address.AddressLine1;
            if (!string.IsNullOrWhiteSpace(address.AddressLine2))
            {
                AddressLine1Entry.Text = string.IsNullOrWhiteSpace(AddressLine1Entry.Text)
                    ? address.AddressLine2
                    : $"{AddressLine1Entry.Text}, {address.AddressLine2}";
            }

            CityEntry.Text = address.City;
            CountyEntry.Text = address.County;
            PostcodeResultEntry.Text = address.Postcode;

            AddressResultsBorder.IsVisible = false;
            AddressResultsCollection.SelectedItem = null;
        }
    }

    private void ShowAddressResults(IReadOnlyCollection<AddressResult> addresses)
    {
        var visibleRows = Math.Min(Math.Max(addresses.Count, 1), MaxVisibleAddressResults);
        var height = (visibleRows * AddressResultRowHeight) + AddressResultsHeightPadding;

        AddressResultsBorder.HeightRequest = height;
        AddressResultsCollection.HeightRequest = height;
        AddressResultsCollection.ItemsSource = addresses.ToList();
        AddressResultsBorder.IsVisible = true;
    }

    private async void OnSearchClicked(object sender, EventArgs e)
    {
        var searchName = CustomerNameEntry.Text?.Trim();
        var searchPhone = PhoneNumberEntry.Text?.Trim();
        var searchAddress = PostcodeEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(searchName)
            && string.IsNullOrWhiteSpace(searchPhone)
            && string.IsNullOrWhiteSpace(searchAddress))
        {
            await ToastNotification.ShowAsync("Required", "Enter customer name, phone, or address to search.", NotificationType.Warning, 3000);
            return;
        }

        var button = (Button)sender;
        var originalText = button.Text;
        button.Text = "Searching...";
        button.IsEnabled = false;

        try
        {
            var results = await _customerDataService.SearchForDeliveryAsync(searchAddress, searchName, searchPhone);

            AddressResultsBorder.IsVisible = false;

            if (results.Count > 0)
            {
                SearchResultsCollection.ItemsSource = results.Select(ToDeliveryCustomer).ToList();
                SearchResultsBorder.IsVisible = true;
                NoResultsLabel.IsVisible = false;
            }
            else
            {
                SearchResultsBorder.IsVisible = false;
                NoResultsLabel.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to search customers: {ex.Message}", NotificationType.Error, 4000);
        }
        finally
        {
            button.Text = originalText;
            button.IsEnabled = true;
        }
    }

    private void OnCustomerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is DeliveryCustomer customer)
        {
            ApplyCustomerToForm(customer);
            SearchResultsBorder.IsVisible = false;
        }
    }

    private void OnCustomerTapped(object sender, EventArgs e)
    {
        if (sender is VisualElement element && element.BindingContext is DeliveryCustomer customer)
        {
            ApplyCustomerToForm(customer);
            SearchResultsBorder.IsVisible = false;
        }
    }

    private void ApplyCustomerToForm(DeliveryCustomer customer)
    {
        _selectedCustomer = customer;
        CustomerNameEntry.Text = customer.Name;
        PhoneNumberEntry.Text = customer.PhoneNumber;

        var addressLines = customer.Address?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();

        AddressLine1Entry.Text = string.Empty;
        CityEntry.Text = string.Empty;
        CountyEntry.Text = string.Empty;
        PostcodeResultEntry.Text = string.Empty;

        if (addressLines.Length == 0)
        {
            return;
        }

        AddressLine1Entry.Text = addressLines[0];

        if (addressLines.Length == 2)
        {
            PostcodeResultEntry.Text = addressLines[1];
            PostcodeEntry.Text = addressLines[1];
            return;
        }

        if (addressLines.Length >= 3)
        {
            CityEntry.Text = addressLines[1];
        }

        if (addressLines.Length >= 4)
        {
            CountyEntry.Text = addressLines[2];
            PostcodeResultEntry.Text = addressLines[3];
            PostcodeEntry.Text = addressLines[3];
        }
        else if (addressLines.Length == 3)
        {
            PostcodeResultEntry.Text = addressLines[2];
            PostcodeEntry.Text = addressLines[2];
        }
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(AddressLine1Entry.Text))
        {
            await ToastNotification.ShowAsync("Required", "Delivery address is required to continue.", NotificationType.Warning, 3000);
            AddressLine1Entry.Focus();
            return;
        }

        var normalizedPostcode = DeliveryZoneService.NormalizePostcode(PostcodeResultEntry.Text);
        if (string.IsNullOrWhiteSpace(normalizedPostcode))
        {
            await ToastNotification.ShowAsync("Required", "Full postcode is required for delivery zone pricing.", NotificationType.Warning, 3000);
            PostcodeResultEntry.Focus();
            return;
        }
        
        // Build address from structured fields
        var addressParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(AddressLine1Entry.Text)) addressParts.Add(AddressLine1Entry.Text.Trim());
        if (!string.IsNullOrWhiteSpace(CityEntry.Text)) addressParts.Add(CityEntry.Text.Trim());
        if (!string.IsNullOrWhiteSpace(CountyEntry.Text)) addressParts.Add(CountyEntry.Text.Trim());
        addressParts.Add(normalizedPostcode);
        
        var address = string.Join("\n", addressParts);

        // Default values for walk-in customers
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Delivery Customer";
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            phone = "N/A";
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            await ToastNotification.ShowAsync("Required", "Please enter a delivery address.", NotificationType.Warning, 3000);
            return;
        }

        try
        {
            // Save or get existing customer
            var customer = await _customerService.SaveCustomerAsync(name, phone, address);

            if (customer == null)
            {
                await ToastNotification.ShowAsync("Error", "Failed to save customer information.", NotificationType.Error, 4000);
                return;
            }

            // Always save to central Customer Data (name, phone, full address).
            try
            {
                await _customerDataService.UpsertDeliveryCustomerAsync(
                    name,
                    phone,
                    address,
                    CityEntry.Text?.Trim(),
                    CountyEntry.Text?.Trim(),
                    normalizedPostcode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeliveryCustomerModal] Customer Data save: {ex.Message}");
            }

            // Navigate to order placement page with customer info
            var orderPlacementPage = new OrderPlacementPageSimple("DEL", 1, "Staff", 1);

            var zoneMatch = await _deliveryZoneService.FindZoneForPostcodeAsync(normalizedPostcode);
            var deliveryFee = 0m;
            if (zoneMatch == null)
            {
                await _deliveryZoneService.SaveUnassignedPostcodeAsync(normalizedPostcode);
                await ToastNotification.ShowAsync(
                    "No Delivery Zone",
                    $"No delivery zone found for {normalizedPostcode}. Saved for admin review.",
                    NotificationType.Warning,
                    3500);
            }
            else
            {
                deliveryFee = zoneMatch.DeliveryFee;
                await ToastNotification.ShowAsync(
                    "Delivery Zone",
                    $"{zoneMatch.ZoneName}: £{deliveryFee:F2} delivery fee added.",
                    NotificationType.Success,
                    2500);
            }
            
            // Pass customer info to order placement page
            orderPlacementPage.SetDeliveryOrderInfo(customer.Id, customer.Name, customer.PhoneNumber, customer.Address, deliveryFee);

            await Navigation.PushAsync(orderPlacementPage);
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to proceed: {ex.Message}", NotificationType.Error, 4000);
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        var roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        var dashboardRoute = roleAccessService.ResolveDashboardRoute(authService.CurrentUser?.Role);
        await Shell.Current.GoToAsync($"//{dashboardRoute}");
    }

    private static DeliveryCustomer ToDeliveryCustomer(CustomerDataRecord record)
    {
        var address = !string.IsNullOrWhiteSpace(record.FullAddress)
            ? record.FullAddress
            : string.Join("\n", new[] { record.City, record.County, record.Postcode }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        return new DeliveryCustomer
        {
            Id = record.Id,
            Name = record.Name,
            PhoneNumber = record.PhoneNumber,
            Address = address,
            CreatedAt = record.CreatedAt,
            LastOrderDate = record.LastOrderDate
        };
    }
}
