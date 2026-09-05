using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Services;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class DeliveryCustomerModal : ContentPage
{
    private readonly DeliveryCustomerService _customerService;
    private readonly IDeliveryDetailsService _deliveryDetails;
    private readonly DeliveryZoneService _deliveryZoneService;
    private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private readonly DeliveryDetailsView _detailsView = new();
    private bool _isContinuing;

    public DeliveryCustomerModal()
    {
        InitializeComponent();
        _customerService = new DeliveryCustomerService();
        _deliveryDetails = ServiceHelper.GetService<IDeliveryDetailsService>()
            ?? new MotherDeliveryDetailsService(
                ServiceHelper.GetService<ICustomerDirectoryService>()
                    ?? new MotherCustomerDirectoryService(ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService()),
                ServiceHelper.GetService<PostcodeLookupService>()
                    ?? new PostcodeLookupService(new DatabaseService()),
                ServiceHelper.GetService<DeliveryZoneService>() ?? new DeliveryZoneService(new DatabaseService()));
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
        _deliveryZoneService = ServiceHelper.GetService<DeliveryZoneService>() ?? new DeliveryZoneService(new DatabaseService());
        _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
            ?? new OrderServiceAvailabilityService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(), AuthenticationService.Instance);

        DetailsHost.Content = _detailsView;
        _detailsView.CustomerSearchRequested += OnCustomerSearchRequested;
        _detailsView.AddressLookupRequested += OnAddressLookupRequested;
        _detailsView.ContinueRequested += OnContinueRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var settings = await _orderServiceAvailabilityService.GetAsync(forceRefresh: true);
        if (!settings.DeliveryEnabled)
        {
            await AppAlertService.ShowAlertAsync("Delivery Unavailable", "Delivery orders are disabled by the Administrator.");
            await _navigationCoordinator.GoBackAsync(animated: false);
            return;
        }

        var state = await _deliveryDetails.GetAsync();
        if (state.IsSuccess && state.Value != null)
        {
            _detailsView.Apply(state.Value);
        }
    }

    private async void OnCustomerSearchRequested(object? sender, CustomerSearchRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            && string.IsNullOrWhiteSpace(request.Phone)
            && string.IsNullOrWhiteSpace(request.AddressOrPostcode))
        {
            await ToastNotification.ShowAsync("Required", "Enter customer name, phone, or address to search.", NotificationType.Warning, 3000);
            return;
        }

        _detailsView.Apply(new DeliveryDetailsDto(
            CustomerId: null,
            Name: request.Name,
            Phone: request.Phone,
            AddressLine: request.AddressOrPostcode,
            FlatOrHouse: null,
            Road: null,
            City: null,
            Postcode: request.AddressOrPostcode,
            DeliveryFee: null,
            ZoneName: null,
            AddressSuggestions: Array.Empty<AddressSuggestionDto>(),
            SearchResults: null,
            SyncStatus: new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live"),
            IsLoading: true,
            LoadingMessage: "Searching customers…"));

        var result = await _deliveryDetails.SearchCustomersAsync(request);
        if (result.IsSuccess && result.Value != null)
        {
            _detailsView.Apply(result.Value);
            return;
        }

        await ToastNotification.ShowAsync("Error", result.Error?.Message ?? "Customer search failed.", NotificationType.Error, 4000);
    }

    private async void OnAddressLookupRequested(object? sender, string addressOrPostcode)
    {
        _detailsView.Apply(new DeliveryDetailsDto(
            CustomerId: null,
            Name: null,
            Phone: null,
            AddressLine: null,
            FlatOrHouse: null,
            Road: null,
            City: null,
            Postcode: addressOrPostcode,
            DeliveryFee: null,
            ZoneName: null,
            AddressSuggestions: Array.Empty<AddressSuggestionDto>(),
            SearchResults: null,
            SyncStatus: new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live"),
            IsLoading: true,
            LoadingMessage: "Looking up address…"));

        var result = await _deliveryDetails.LookupAddressAsync(addressOrPostcode);
        if (result.IsSuccess && result.Value != null)
        {
            _detailsView.Apply(result.Value);
            if (result.Value.AddressSuggestions.Count > 0)
            {
                await ToastNotification.ShowAsync(
                    "OrderWeb",
                    $"Found {result.Value.AddressSuggestions.Count} address(es). Select one to fill the form.",
                    NotificationType.Info,
                    3000);
            }

            return;
        }

        await ToastNotification.ShowAsync("Address Lookup", result.Error?.Message ?? "No addresses found.", NotificationType.Error, 5000);
    }

    private async void OnContinueRequested(object? sender, DeliveryDetailsDto draft)
    {
        if (_isContinuing)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(draft.Name) ? "Delivery Customer" : draft.Name.Trim();
        var phone = string.IsNullOrWhiteSpace(draft.Phone) ? "N/A" : draft.Phone.Trim();
        var addressLine = draft.AddressLine?.Trim();
        var city = draft.City?.Trim();
        var normalizedPostcode = DeliveryZoneService.NormalizePostcode(draft.Postcode);

        if (string.IsNullOrWhiteSpace(addressLine))
        {
            await ToastNotification.ShowAsync("Required", "Delivery address is required to continue.", NotificationType.Warning, 3000);
            return;
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            await ToastNotification.ShowAsync("Required", "City is required to continue.", NotificationType.Warning, 3000);
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedPostcode))
        {
            await ToastNotification.ShowAsync("Required", "Full postcode is required for delivery zone pricing.", NotificationType.Warning, 3000);
            return;
        }

        var address = string.Join('\n', new[] { addressLine, city, normalizedPostcode });
        _isContinuing = true;
        _detailsView.Apply(draft with { IsLoading = true, LoadingMessage = "Opening order…" });

        try
        {
            var customer = await _customerService.SaveCustomerAsync(name, phone, address);
            if (customer == null)
            {
                await ToastNotification.ShowAsync("Error", "Failed to save customer information.", NotificationType.Error, 4000);
                return;
            }

            try
            {
                var directory = ServiceHelper.GetService<ICustomerDirectoryService>()
                    ?? new MotherCustomerDirectoryService(ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService());
                await directory.UpsertCustomerAsync(new CustomerSummaryDto(
                    customer.Id.ToString(),
                    customer.Id.ToString(),
                    customer.Name,
                    customer.PhoneNumber,
                    null,
                    address,
                    city,
                    null,
                    normalizedPostcode,
                    null,
                    CustomerOrderKind.Delivery,
                    customer.Name));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeliveryCustomerModal] Customer Data save: {ex.Message}");
            }

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

            orderPlacementPage.SetDeliveryOrderInfo(customer.Id, customer.Name, customer.PhoneNumber, customer.Address, deliveryFee);
            await _navigationCoordinator.PushTemporaryPageAsync(orderPlacementPage);
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to proceed: {ex.Message}", NotificationType.Error, 4000);
        }
        finally
        {
            _isContinuing = false;
            _detailsView.Apply(draft with { IsLoading = false });
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        var roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        var dashboardRoute = roleAccessService.ResolveDashboardRoute(authService.CurrentUser?.Role);
        await _navigationCoordinator.NavigateShellAsync(dashboardRoute, source: sender as VisualElement);
    }
}
