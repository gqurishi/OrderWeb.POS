namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;
using OrderWeb.SharedUI.Views;

public partial class RecentCustomersPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy;
    private readonly MotherRecentCustomersClient _customers;

    private RecentCustomerFilter _filter = RecentCustomerFilter.All;
    private bool _busy;
    private bool _isVisible;
    private bool _pendingReload;

    public RecentCustomersPage()
    {
        InitializeComponent();
        ClientPageChrome.HideSystemBackChrome(this);
        TopBar.SetPageTitle("Recent Customers");

        _offlinePolicy = new ClientOfflinePolicy(_cache);
        _customers = new MotherRecentCustomersClient(_cache, _offlinePolicy);

        Board.RefreshRequested += async (_, _) => await LoadAsync();
        Board.RetrySyncRequested += async (_, _) => await RetrySyncAsync();
        Board.SearchChanged += async (_, _) => await LoadAsync();
        Board.FilterChanged += async (_, e) =>
        {
            _filter = e.Filter;
            await LoadAsync();
        };
        Board.RemoveCacheRequested += async (_, e) => await RemoveCacheAsync(e.Row);

        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await ClientSignOut.RequestAsync(this);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClientPageChrome.HideSystemBackChrome(this);
        TopBar.SetPageTitle("Recent Customers");

        if (!HasCustomersAccess())
        {
            await DisplayAlert(
                "Recent Customers",
                "This Client terminal is not allowed to use Recent Customers. Ask Mother to grant Customers access.",
                "OK");
            await Navigation.PopAsync(false);
            return;
        }

        _isVisible = true;
        MotherEventClient.SharedAuthoritativeDataChanged += OnMotherDataChanged;
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;
        MotherEventClient.SharedAuthoritativeDataChanged -= OnMotherDataChanged;
    }

    private static bool HasCustomersAccess() =>
        ClientHostAccess.Features.Contains(PosFeatureKeys.Customers) ||
        ClientHostAccess.CanOpenMenu("Recent Customers") ||
        ClientHostAccess.CanOpenMenu("Customers");

    private async void OnMotherDataChanged(object? sender, MotherDataChangedEventArgs e)
    {
        if (!_isVisible || !IsCustomerRefreshEvent(e.EventType))
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(async () => await LoadAsync());
    }

    private static bool IsCustomerRefreshEvent(string? eventType) =>
        string.Equals(eventType, "customer.updated", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "customers.updated", StringComparison.OrdinalIgnoreCase);

    private async Task LoadAsync()
    {
        if (_busy)
        {
            _pendingReload = true;
            return;
        }

        try
        {
            _busy = true;
            Board.SetBusy(true);

            var response = await _customers.GetRecentAsync(
                Board.SearchText,
                RecentCustomerFilterCodes.ToApiCode(_filter));

            if (!response.Success)
            {
                Board.SetSummary(new RecentCustomerSyncSummaryPresentation(0, 0, 0, "Never", response.Error ?? response.Message ?? "Could not load."));
                Board.SetRows([]);
                await DisplayAlert("Recent Customers", response.Error ?? response.Message ?? "Could not load recent customers.", "OK");
                return;
            }

            var summary = response.Summary ?? new ClientRecentCustomersSummaryDto();
            var rows = response.Customers ?? Array.Empty<ClientRecentCustomerItemDto>();
            Board.SetSummary(new RecentCustomerSyncSummaryPresentation(
                summary.SyncedCount,
                summary.PendingCount,
                summary.FailedCount,
                summary.LastCloudSyncDisplay ?? "Never",
                $"{rows.Count} of {summary.TotalRecent} recent customer(s) shown · queue {summary.QueueCount}"));
            Board.SetRows(rows.Select(ToRow).ToList());
        }
        finally
        {
            _busy = false;
            Board.SetBusy(false);
            if (_pendingReload)
            {
                _pendingReload = false;
                await LoadAsync();
            }
        }
    }

    private async Task RetrySyncAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            _busy = true;
            Board.SetBusy(true);
            var result = await _customers.RetrySyncAsync();
            await DisplayAlert("Customer Sync", result.Message ?? result.Error ?? "Sync complete.", "OK");
            if (result.Success)
            {
                _busy = false;
                await LoadAsync();
                return;
            }
        }
        finally
        {
            _busy = false;
            Board.SetBusy(false);
        }
    }

    private async Task RemoveCacheAsync(RecentCustomerRowPresentation row)
    {
        if (row.Tag is not int id || id <= 0)
        {
            return;
        }

        var confirm = await DisplayAlert(
            "Remove local cache",
            $"Remove {row.Name} from Mother's 7-day cache?\n\nOrderWeb cloud records are not deleted.",
            "Remove",
            "Cancel");

        if (!confirm)
        {
            return;
        }

        var result = await _customers.DeleteCacheAsync(id);
        if (!result.Success)
        {
            await DisplayAlert("Remove Failed", result.Error ?? result.Message ?? "Could not remove this cache row.", "OK");
            return;
        }

        await LoadAsync();
    }

    private static RecentCustomerRowPresentation ToRow(ClientRecentCustomerItemDto dto) =>
        new(
            string.IsNullOrWhiteSpace(dto.Name) ? "Customer" : dto.Name!,
            string.IsNullOrWhiteSpace(dto.ContactDetail)
                ? (string.IsNullOrWhiteSpace(dto.PhoneNumber) ? string.Empty : $" - {dto.PhoneNumber}")
                : dto.ContactDetail!,
            dto.ShowCollectionBadge,
            dto.ShowDeliveryBadge,
            dto.ShowSyncedBadge,
            dto.ShowPendingBadge,
            dto.ShowFailedBadge,
            dto.Id);

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

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
        await ClientSidebarNavigation.SwitchAsync(this, menu, currentRoute: "customerdata");
    }
}
