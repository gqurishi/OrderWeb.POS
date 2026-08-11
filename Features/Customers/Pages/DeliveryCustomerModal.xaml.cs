using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;
using System.Text.RegularExpressions;

namespace POS_in_NET.Pages;

public partial class DeliveryCustomerModal : ContentPage
{
    private const int MaxVisibleAddressResults = 10;
    private const double AddressResultRowHeight = 54;
    private const double AddressResultsHeightPadding = 8;

    private readonly DeliveryCustomerService _customerService;
    private readonly PostcodeLookupService _postcodeLookupService;
    private readonly DeliveryZoneService _deliveryZoneService;
    private readonly CustomerDataService _customerDataService;
    private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private DeliveryCustomer? _selectedCustomer;
    private bool _isOpeningKeyboard;
    private bool _isContinuing;

    public DeliveryCustomerModal()
    {
        InitializeComponent();
        _customerService = new DeliveryCustomerService();
        _customerDataService = ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService();
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
        _postcodeLookupService = ServiceHelper.GetService<PostcodeLookupService>()
            ?? new PostcodeLookupService(new DatabaseService());
        _deliveryZoneService = ServiceHelper.GetService<DeliveryZoneService>() ?? new DeliveryZoneService(new DatabaseService());
        _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
            ?? new OrderServiceAvailabilityService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(), AuthenticationService.Instance);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var settings = await _orderServiceAvailabilityService.GetAsync(forceRefresh: true);
        if (!settings.DeliveryEnabled)
        {
            await AppAlertService.ShowAlertAsync("Delivery Unavailable", "Delivery orders are disabled by the Administrator.");
            await _navigationCoordinator.GoBackAsync(animated: false);
        }
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

    private async void OnHouseNumberFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(HouseNumberEntry);
    }

    private async void OnRoadNameFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(RoadNameEntry);
    }

    private async void OnCityFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(CityEntry);
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

        if (entry == HouseNumberEntry)
        {
            return "Flat or house number";
        }

        if (entry == RoadNameEntry)
        {
            return "Road name";
        }

        if (entry == CityEntry)
        {
            return "City";
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
            var (premise, road) = SplitLookupAddress(address);
            HouseNumberEntry.Text = premise;
            RoadNameEntry.Text = road;

            CityEntry.Text = address.City;
            PostcodeResultEntry.Text = address.Postcode;
            PostcodeEntry.Text = address.Postcode;

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

        HouseNumberEntry.Text = string.Empty;
        RoadNameEntry.Text = string.Empty;
        CityEntry.Text = string.Empty;
        PostcodeResultEntry.Text = string.Empty;

        if (addressLines.Length == 0)
        {
            CityEntry.Text = customer.City;
            PostcodeResultEntry.Text = customer.Postcode;
            PostcodeEntry.Text = customer.Postcode;
            return;
        }

        var (premise, road) = SplitPremiseAndRoad(addressLines[0]);
        HouseNumberEntry.Text = premise;
        RoadNameEntry.Text = road;

        // New addresses are stored as street, city, postcode. Older records may
        // also contain a county before the postcode; the county is intentionally ignored.
        if (addressLines.Length >= 3)
        {
            CityEntry.Text = addressLines[1];
        }

        var postcode = addressLines.Length > 1 ? addressLines[^1] : customer.Postcode;
        PostcodeResultEntry.Text = postcode;
        PostcodeEntry.Text = postcode;
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        if (_isContinuing)
        {
            return;
        }

        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(HouseNumberEntry.Text))
        {
            await ToastNotification.ShowAsync("Required", "Flat or house number is required to continue.", NotificationType.Warning, 3000);
            HouseNumberEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(RoadNameEntry.Text))
        {
            await ToastNotification.ShowAsync("Required", "Road name is required to continue.", NotificationType.Warning, 3000);
            RoadNameEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(CityEntry.Text))
        {
            await ToastNotification.ShowAsync("Required", "City is required to continue.", NotificationType.Warning, 3000);
            CityEntry.Focus();
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
        addressParts.Add($"{HouseNumberEntry.Text.Trim()} {RoadNameEntry.Text.Trim()}".Trim());
        addressParts.Add(CityEntry.Text.Trim());
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
                    county: null,
                    postcode: normalizedPostcode);
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

            await _navigationCoordinator.PushTemporaryPageAsync(orderPlacementPage, source: button);
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to proceed: {ex.Message}", NotificationType.Error, 4000);
        }
        finally
        {
            _isContinuing = false;
            if (button != null)
            {
                button.Text = originalText ?? "Continue";
                button.IsEnabled = true;
            }
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        var roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        var dashboardRoute = roleAccessService.ResolveDashboardRoute(authService.CurrentUser?.Role);
        await _navigationCoordinator.NavigateShellAsync(dashboardRoute, source: sender as VisualElement);
    }

    private static DeliveryCustomer ToDeliveryCustomer(CustomerDataRecord record)
    {
        return new DeliveryCustomer
        {
            Id = record.Id,
            Name = record.Name,
            PhoneNumber = record.PhoneNumber,
            Address = record.FullAddress,
            City = record.City,
            Postcode = record.Postcode,
            CreatedAt = record.CreatedAt,
            LastOrderDate = record.LastOrderDate
        };
    }

    private static (string Premise, string Road) SplitLookupAddress(AddressResult address)
    {
        var line1 = address.AddressLine1?.Trim() ?? string.Empty;
        var remainingLines = new[] { address.AddressLine2, address.AddressLine3 }
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();

        if (remainingLines.Count > 0 && IsPremiseOnly(line1))
        {
            return (line1, string.Join(", ", remainingLines));
        }

        var (premise, road) = SplitPremiseAndRoad(line1);
        if (remainingLines.Count > 0)
        {
            road = string.Join(", ", new[] { road }.Concat(remainingLines)
                .Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        return (premise, road);
    }

    private static (string Premise, string Road) SplitPremiseAndRoad(string? streetAddress)
    {
        var value = streetAddress?.Trim().Trim(',') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return (string.Empty, string.Empty);
        }

        var numberedAddress = Regex.Match(
            value,
            @"^(?<premise>(?:(?:flat|apartment|unit|suite)\s+[a-z0-9-]+(?:\s*,?\s*)?)?\d+[a-z]?)\s*,?\s+(?<road>.+)$",
            RegexOptions.IgnoreCase);
        if (numberedAddress.Success)
        {
            return (
                numberedAddress.Groups["premise"].Value.Trim().TrimEnd(','),
                numberedAddress.Groups["road"].Value.Trim());
        }

        var namedProperty = Regex.Match(
            value,
            @"^(?<premise>.+?\b(?:cottage|house|lodge|farm|hall|manor|court))\s*,?\s+(?<road>.+)$",
            RegexOptions.IgnoreCase);
        if (namedProperty.Success)
        {
            return (
                namedProperty.Groups["premise"].Value.Trim().TrimEnd(','),
                namedProperty.Groups["road"].Value.Trim());
        }

        return (string.Empty, value);
    }

    private static bool IsPremiseOnly(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(value, @"^\d+[a-z]?$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(value, @"^(flat|apartment|unit|suite)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(value, @"\b(cottage|house|lodge|farm|hall|manor|court)$", RegexOptions.IgnoreCase);
    }
}
