using OrderWeb.Contracts.Customers;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class CollectionCustomerModal : ContentPage
{
    private readonly CollectionCustomerService _customerService;
    private readonly ICollectionDetailsService _collectionDetails;
    private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private readonly CollectionDetailsView _detailsView = new();
    private bool _isContinuing;

    public CollectionCustomerModal()
    {
        InitializeComponent();
        _customerService = new CollectionCustomerService();
        _collectionDetails = ServiceHelper.GetService<ICollectionDetailsService>()
            ?? new MotherCollectionDetailsService(
                ServiceHelper.GetService<ICustomerDirectoryService>()
                ?? new MotherCustomerDirectoryService(ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService()));
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
        _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
            ?? new OrderServiceAvailabilityService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(), AuthenticationService.Instance);

        DetailsHost.Content = _detailsView;
        _detailsView.SearchRequested += OnSearchRequested;
        _detailsView.ContinueRequested += OnContinueRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var settings = await _orderServiceAvailabilityService.GetAsync(forceRefresh: true);
        if (!settings.CollectionEnabled)
        {
            await AppAlertService.ShowAlertAsync("Collection Unavailable", "Collection orders are disabled by the Administrator.");
            await _navigationCoordinator.GoBackAsync(animated: false);
            return;
        }

        var state = await _collectionDetails.GetAsync();
        if (state.IsSuccess && state.Value != null)
        {
            _detailsView.Apply(state.Value);
        }
    }

    private async void OnSearchRequested(object? sender, CustomerSearchRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.Phone))
        {
            await ToastNotification.ShowAsync("Required", "Please enter customer name or phone number to search.", NotificationType.Warning, 3000);
            return;
        }

        _detailsView.Apply(new CollectionDetailsDto(
            CustomerId: null,
            Name: request.Name,
            Phone: request.Phone,
            PickupTime: null,
            Notes: null,
            SearchResults: null,
            SyncStatus: new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live"),
            IsLoading: true,
            LoadingMessage: "Searching customers…"));

        var result = await _collectionDetails.SearchAsync(request);
        if (result.IsSuccess && result.Value != null)
        {
            _detailsView.Apply(result.Value);
            return;
        }

        await ToastNotification.ShowAsync("Error", result.Error?.Message ?? "Customer search failed.", NotificationType.Error, 4000);
    }

    private async void OnContinueRequested(object? sender, CollectionDetailsDto draft)
    {
        if (_isContinuing)
        {
            return;
        }

        var name = draft.Name?.Trim();
        var phone = draft.Phone?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            await ToastNotification.ShowAsync("Required", "Customer Name is required to continue.", NotificationType.Warning, 3000);
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            await ToastNotification.ShowAsync("Required", "Phone Number is required to continue.", NotificationType.Warning, 3000);
            return;
        }

        _isContinuing = true;
        _detailsView.Apply(draft with { IsLoading = true, LoadingMessage = "Opening order…" });

        try
        {
            var customer = await _customerService.SaveCustomerAsync(name, phone);
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
                    null,
                    null,
                    null,
                    null,
                    null,
                    CustomerOrderKind.Collection,
                    customer.Name));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectionCustomerModal] Customer Data save: {ex.Message}");
            }

            var orderPlacementPage = new OrderPlacementPageSimple("COL", 1, "Staff", 1);
            orderPlacementPage.SetCollectionOrderInfo(customer.Id, customer.Name, customer.PhoneNumber);
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
