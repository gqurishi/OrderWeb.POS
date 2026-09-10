using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System.Text.RegularExpressions;

namespace POS_in_NET.Pages;

public partial class DeliveryCustomerModal : ContentPage
{
    private readonly DeliveryCustomerService _customerService;
    private readonly PostcodeLookupService _postcodeLookupService;
    private readonly DeliveryZoneService _deliveryZoneService;
    private readonly CustomerDataService _customerDataService;
    private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
    private readonly NavigationCoordinator _navigationCoordinator;
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

        Entry.AddressSearchRequested += OnAddressSearchRequested;
        Entry.CustomerSearchRequested += OnCustomerSearchRequested;
        Entry.ContinueRequested += OnContinueRequested;
        Entry.CancelRequested += OnCancelRequested;
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

    private async void OnAddressSearchRequested(object? sender, (string? Term, string? Name, string? Phone) e)
    {
        var addressQuery = e.Term?.Trim();
        var name = e.Name?.Trim();
        var phone = e.Phone?.Trim();

        if (string.IsNullOrWhiteSpace(addressQuery)
            && string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(phone))
        {
            await ToastNotification.ShowAsync("Required", "Enter a postcode, address, name, or phone to search.", NotificationType.Warning, 3000);
            return;
        }

        Entry.SetAddressSearchBusy(true);
        try
        {
            // 1. Check central customer data first (no API credit used).
            var localRecords = await _customerDataService.SearchForDeliveryAsync(addressQuery, name, phone);
            if (localRecords.Count > 0)
            {
                Entry.SetCustomerResults(localRecords.Select(ToSearchItem));
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
                Entry.SetCustomerResults(Array.Empty<CustomerEntrySearchItem>());
                await ToastNotification.ShowAsync(
                    "No Local Match",
                    "No matching customers found locally. Enter a postcode to look up addresses.",
                    NotificationType.Info,
                    4000);
                return;
            }

            var addresses = await _postcodeLookupService.LookupPostcodeAsync(addressQuery);

            if (addresses.Count > 0)
            {
                Entry.SetAddressSuggestions(addresses.Select(ToSuggestionItem));
                await ToastNotification.ShowAsync(
                    "OrderWeb",
                    $"Found {addresses.Count} address(es). Select one to fill the form.",
                    NotificationType.Info,
                    3000);
            }
            else
            {
                Entry.SetAddressSuggestions(Array.Empty<DeliveryAddressSuggestionItem>());
                await ToastNotification.ShowAsync("No Results", $"No addresses found for: {addressQuery}", NotificationType.Info, 3000);
            }
        }
        catch (AddressLookupException ex)
        {
            Entry.SetAddressSuggestions(Array.Empty<DeliveryAddressSuggestionItem>());
            var message = ex.Suggestions.Count > 0
                ? $"{ex.Message}\n\nTry: {string.Join(", ", ex.Suggestions)}"
                : ex.Message;
            await ToastNotification.ShowAsync("Address Lookup", message, NotificationType.Error, 5000);
        }
        catch (Exception ex)
        {
            Entry.SetAddressSuggestions(Array.Empty<DeliveryAddressSuggestionItem>());
            await ToastNotification.ShowAsync("Error", $"Search failed: {ex.Message}\n\nYou can still enter the address manually.", NotificationType.Error, 5000);
        }
        finally
        {
            Entry.SetAddressSearchBusy(false);
        }
    }

    private async void OnCustomerSearchRequested(object? sender, (string? Name, string? Phone, string? AddressOrPostcode) e)
    {
        var searchName = e.Name?.Trim();
        var searchPhone = e.Phone?.Trim();
        var searchAddress = e.AddressOrPostcode?.Trim();

        if (string.IsNullOrWhiteSpace(searchName)
            && string.IsNullOrWhiteSpace(searchPhone)
            && string.IsNullOrWhiteSpace(searchAddress))
        {
            await ToastNotification.ShowAsync("Required", "Enter customer name, phone, or address to search.", NotificationType.Warning, 3000);
            return;
        }

        Entry.SetCustomerSearchBusy(true);
        try
        {
            var results = await _customerDataService.SearchForDeliveryAsync(searchAddress, searchName, searchPhone);
            Entry.SetCustomerResults(results.Select(ToSearchItem));
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to search customers: {ex.Message}", NotificationType.Error, 4000);
        }
        finally
        {
            Entry.SetCustomerSearchBusy(false);
        }
    }

    private async void OnContinueRequested(object? sender, DeliveryCustomerEntryResult e)
    {
        if (_isContinuing)
        {
            return;
        }

        _isContinuing = true;
        Entry.SetContinueBusy(true, "Opening order...");
        try
        {
            // Save or get existing customer
            var customer = await _customerService.SaveCustomerAsync(e.Name, e.Phone, e.FormattedAddress);

            if (customer == null)
            {
                await ToastNotification.ShowAsync("Error", "Failed to save customer information.", NotificationType.Error, 4000);
                return;
            }

            // Always save to central Customer Data (name, phone, full address).
            try
            {
                await _customerDataService.UpsertDeliveryCustomerAsync(
                    e.Name,
                    e.Phone,
                    e.FormattedAddress,
                    e.City,
                    county: null,
                    postcode: e.Postcode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeliveryCustomerModal] Customer Data save: {ex.Message}");
            }

            // Navigate to order placement page with customer info
            var orderPlacementPage = new OrderPlacementPageSimple("DEL", 1, "Staff", 1);

            var zoneMatch = await _deliveryZoneService.FindZoneForPostcodeAsync(e.Postcode);
            var deliveryFee = 0m;
            if (zoneMatch == null)
            {
                await _deliveryZoneService.SaveUnassignedPostcodeAsync(e.Postcode);
                await ToastNotification.ShowAsync(
                    "No Delivery Zone",
                    $"No delivery zone found for {e.Postcode}. Saved for admin review.",
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

            await _navigationCoordinator.PushTemporaryPageAsync(orderPlacementPage, source: Entry);
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to proceed: {ex.Message}", NotificationType.Error, 4000);
        }
        finally
        {
            _isContinuing = false;
            Entry.SetContinueBusy(false);
        }
    }

    private async void OnCancelRequested(object? sender, EventArgs e)
    {
        var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        var roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        var dashboardRoute = roleAccessService.ResolveDashboardRoute(authService.CurrentUser?.Role);
        await _navigationCoordinator.NavigateShellAsync(dashboardRoute, source: Entry);
    }

    private static CustomerEntrySearchItem ToSearchItem(CustomerDataRecord record) =>
        new(record.Name, record.PhoneNumber, Tag: new DeliveryCustomerAddressInfo(record.FullAddress, record.Postcode));

    private static DeliveryAddressSuggestionItem ToSuggestionItem(AddressResult address)
    {
        var (premise, road) = SplitLookupAddress(address);
        return new DeliveryAddressSuggestionItem(
            address.DisplayText,
            new DeliveryAddressFields(premise, road, address.City, address.Postcode));
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
