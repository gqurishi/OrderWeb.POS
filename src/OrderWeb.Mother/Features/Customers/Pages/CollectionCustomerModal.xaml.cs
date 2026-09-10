using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class CollectionCustomerModal : ContentPage
{
    private readonly CollectionCustomerService _customerService;
    private readonly CustomerDataService _customerDataService;
    private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private bool _isContinuing;

    public CollectionCustomerModal()
    {
        InitializeComponent();
        _customerService = new CollectionCustomerService();
        _customerDataService = ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService();
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
        _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
            ?? new OrderServiceAvailabilityService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(), AuthenticationService.Instance);

        Entry.SearchRequested += OnSearchRequested;
        Entry.ContinueRequested += OnContinueRequested;
        Entry.CancelRequested += OnCancelRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var settings = await _orderServiceAvailabilityService.GetAsync(forceRefresh: true);
        if (!settings.CollectionEnabled)
        {
            await AppAlertService.ShowAlertAsync("Collection Unavailable", "Collection orders are disabled by the Administrator.");
            await _navigationCoordinator.GoBackAsync(animated: false);
        }
    }

    private async void OnSearchRequested(object? sender, (string? Name, string? Phone) e)
    {
        var searchName = e.Name?.Trim();
        var searchPhone = e.Phone?.Trim();

        if (string.IsNullOrWhiteSpace(searchName) && string.IsNullOrWhiteSpace(searchPhone))
        {
            await ToastNotification.ShowAsync("Required", "Please enter customer name or phone number to search.", NotificationType.Warning, 3000);
            return;
        }

        Entry.SetSearchBusy(true);
        try
        {
            var results = await _customerDataService.SearchForCollectionAsync(searchName, searchPhone);
            Entry.SetSearchResults(results.Select(ToSearchItem));
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to search customers: {ex.Message}", NotificationType.Error, 4000);
        }
        finally
        {
            Entry.SetSearchBusy(false);
        }
    }

    private async void OnContinueRequested(object? sender, CustomerEntryResult e)
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
            var customer = await _customerService.SaveCustomerAsync(e.Name, e.Phone);

            if (customer == null)
            {
                await ToastNotification.ShowAsync("Error", "Failed to save customer information.", NotificationType.Error, 4000);
                return;
            }

            try
            {
                await _customerDataService.UpsertCollectionCustomerAsync(e.Name, e.Phone);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectionCustomerModal] Customer Data save: {ex.Message}");
            }

            // Navigate to order placement page with customer info
            var orderPlacementPage = new OrderPlacementPageSimple("COL", 1, "Staff", 1);
            orderPlacementPage.SetCollectionOrderInfo(customer.Id, customer.Name, customer.PhoneNumber);

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
        new(record.Name, record.PhoneNumber);
}
