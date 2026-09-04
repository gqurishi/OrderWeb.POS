using POS_in_NET.Services;
using POS_in_NET.Pages;
using POS_in_NET.Models;
using POS_in_NET.Views;
using System.ComponentModel;
using System.Linq;
using OrderWeb.SharedUI.Controls;

namespace POS_in_NET;

public partial class AppShell : Shell, INotifyPropertyChanged
{
    private string _currentDateTime = string.Empty;
    private System.Timers.Timer? _timer;
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly InactivityService _inactivityService;
    private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private bool _isSyncingDatabase;
    private bool _isShellNavigationChanging;
    private string? _pendingShellTarget;

    public string CurrentDateTime
    {
        get => _currentDateTime;
        set
        {
            _currentDateTime = value;
            OnPropertyChanged(nameof(CurrentDateTime));
        }
    }

    public AppShell()
    {
        InitializeComponent();
        BindingContext = this;
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _inactivityService = ServiceHelper.GetService<InactivityService>() ?? new InactivityService(_authService, _roleAccessService);
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
        _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
            ?? new OrderServiceAvailabilityService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(), _authService);
        _orderServiceAvailabilityService.SettingsChanged += OnOrderServicesChanged;
        AppDataRefreshService.DataChanged += OnAppDataChanged;

        // Register modal pages for navigation
        Routing.RegisterRoute(nameof(CustomColorPickerPage), typeof(CustomColorPickerPage));
        Routing.RegisterRoute(nameof(FoodMenuManagement), typeof(FoodMenuManagement));
        Routing.RegisterRoute("reportdetails", typeof(ReportOrderDetailsPage));
        Routing.RegisterRoute("collection", typeof(CollectionCustomerModal));
        Routing.RegisterRoute("delivery", typeof(DeliveryCustomerModal));
        Routing.RegisterRoute("printtemplates", typeof(PrintTemplatesPage));

        // Register User Dashboard route
        Routing.RegisterRoute("userdashboard", typeof(UserDashboardPage));
        Routing.RegisterRoute("managerdashboard", typeof(ManagerDashboardPage));

        // Initialize date/time
        UpdateDateTime();

        // Setup timer to update every second
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += (s, e) => UpdateDateTime();
        _timer.Start();

        // Subscribe to navigation events to update user info
        this.Navigated += OnShellNavigated;
        this.Navigating += OnShellNavigating;
        _inactivityService.Start();
        _inactivityService.TrackPage(CurrentPage);
        ApplyRoleBasedMenuVisibility();
        ConfigureSharedSidebar();
        _ = RefreshOrderServiceAvailabilityAsync();
    }

    private void UpdateDateTime()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentDateTime = DateTime.Now.ToString("dddd, MMMM dd, yyyy - HH:mm:ss");
        });
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        _isShellNavigationChanging = false;
        _pendingShellTarget = null;
        // Update user info when navigating (handled by TopBar component now)
        System.Diagnostics.Debug.WriteLine($"Navigated to: {e.Current.Location}");
        _inactivityService.ResetActivity();
        _inactivityService.TrackPage(CurrentPage);
        ApplyRoleBasedMenuVisibility();
    }

    private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        try
        {
            _inactivityService.ResetActivity();
            var target = e.Target.Location.OriginalString;

            if (_isShellNavigationChanging)
            {
                System.Diagnostics.Debug.WriteLine($"Ignored duplicate navigation to {target}; {_pendingShellTarget} is still opening.");
                e.Cancel();
                return;
            }

            _isShellNavigationChanging = true;
            _pendingShellTarget = target;
            _ = ResetNavigationGuardAfterTimeoutAsync(target);
            if (!TryResolveRoute(target, out var route))
            {
                return;
            }

            if (!IsRouteAllowed(route))
            {
                e.Cancel();
                _isShellNavigationChanging = false;
                _pendingShellTarget = null;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access this page.");
                });
            }
            else if (!_orderServiceAvailabilityService.IsRouteEnabled(route))
            {
                e.Cancel();
                _isShellNavigationChanging = false;
                _pendingShellTarget = null;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await AppAlertService.ShowAlertAsync("Service Unavailable", "This order service is disabled in Business Information settings.");
                });
            }
        }
        catch (Exception ex)
        {
            _isShellNavigationChanging = false;
            _pendingShellTarget = null;
            System.Diagnostics.Debug.WriteLine($"Role guard navigation error: {ex.Message}");
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        _inactivityService.ResetActivity();

        // Stop the timer
        _timer?.Stop();

        // Get authentication service
        var authService = ServiceHelper.GetService<AuthenticationService>();
        if (authService != null)
        {
            await authService.LogoutAsync();
        }

        // Navigate to login page immediately
        await _navigationCoordinator.NavigateShellAsync("login", animated: false, source: sender as VisualElement);

        // Restart timer
        _timer?.Start();
    }

    private async void OnSyncDatabaseClicked(object sender, EventArgs e)
    {
        _inactivityService.ResetActivity();

        if (_isSyncingDatabase)
        {
            return;
        }

        using var idleGuard = _inactivityService.BeginCriticalActivity();
        SetSyncingState(true);

        try
        {
            // Close the flyout
            Shell.Current.FlyoutIsPresented = false;

            var databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
            var schemaResult = await databaseService.EnsureProductionSchemaAsync();
            if (!schemaResult.Success)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                    "Database Update Required",
                    schemaResult.Message);
                return;
            }

            var cloudSyncMessage = await TrySyncCloudOrdersAsync(databaseService);

            OrderPlacementPageSimple.InvalidateMenuCache();
            AppDataRefreshService.RequestRefresh(AppDataChangeKind.All);

            // Show success message
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                "Update Complete",
                $"All active screens were asked to reload fresh data.\n{cloudSyncMessage}");
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(" Sync Error", $"Failed to sync: {ex.Message}");
        }
        finally
        {
            SetSyncingState(false);
        }
    }

    private static async Task<string> TrySyncCloudOrdersAsync(DatabaseService databaseService)
    {
        var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(databaseService);
        if (!onlineMasterCheck.Allowed)
        {
            return $"Cloud sync skipped: {onlineMasterCheck.Reason}";
        }

        try
        {
            var cloudOrderService = ServiceHelper.GetService<CloudOrderService>();
            if (cloudOrderService == null)
            {
                return "Cloud sync skipped: cloud order service is not available.";
            }

            var syncFromDate = DateTime.Today.AddDays(-7);
            var syncResult = await cloudOrderService.SyncOrdersByDateAsync(syncFromDate);
            if (!syncResult.Success)
            {
                return $"Cloud sync failed: {syncResult.Message}";
            }

            return $"Cloud sync complete: {syncResult.Message}";
        }
        catch (Exception ex)
        {
            return $"Cloud sync failed: {ex.Message}";
        }
    }

    private void SetSyncingState(bool isSyncing)
    {
        _isSyncingDatabase = isSyncing;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SharedSidebar.IsUpdating = isSyncing;
        });
    }

    private async void OnMenuItemTapped(object sender, EventArgs e)
    {
        try
        {
            _inactivityService.ResetActivity();

            // Clear all menu item selections first
            ClearMenuSelections();

            // Highlight the selected menu item
            if (sender is StackLayout stackLayout)
            {
                stackLayout.BackgroundColor = Color.FromArgb("#E3F2FD"); // Light blue selection
            }

            if (sender is View view && view.GestureRecognizers.FirstOrDefault() is TapGestureRecognizer tapGesture)
            {
                var route = tapGesture.CommandParameter?.ToString();
                if (!string.IsNullOrEmpty(route))
                {
                    route = _roleAccessService.ResolveRouteForRole(_authService.CurrentUser?.Role, route);

                    if (route.Equals("cashdrawer", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!_roleAccessService.CanOpenCashDrawer(_authService.CurrentUser?.Role))
                        {
                            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only managers and admins can open the cash drawer.");
                            return;
                        }

                        Shell.Current.FlyoutIsPresented = false;
                        await OpenCashDrawerFromSidebarAsync();
                        return;
                    }

                    if (!IsRouteAllowed(route))
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access this feature.");
                        return;
                    }

                    if (!_orderServiceAvailabilityService.IsRouteEnabled(route))
                    {
                        await AppAlertService.ShowAlertAsync("Service Unavailable", "This order service is disabled in Business Information settings.");
                        return;
                    }

                    // Close the flyout
                    Shell.Current.FlyoutIsPresented = false;

                    if (NavigationCoordinator.IsTemporaryRoute(route))
                    {
                        // Navigate to modal route without //
                        await _navigationCoordinator.NavigateTemporaryRouteAsync(route, source: view);
                    }
                    else
                    {
                        if (route.Equals("visuallayout", StringComparison.OrdinalIgnoreCase))
                        {
                            ClearShellDetailStacks();
                        }

                        // Navigate to shell content route with //
                        await _navigationCoordinator.NavigateShellAsync(route, animated: false, source: view);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
            await AppAlertService.ShowAlertAsync("Navigation Error", ex.Message);
        }
    }

    private bool IsRouteAllowed(string route)
    {
        return _roleAccessService.CanAccessRoute(_authService.CurrentUser?.Role, route);
    }

    private void OnSharedNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        _ = NavigateFromSharedSidebarAsync(e.Route);
    }

    private async Task NavigateFromSharedSidebarAsync(string route)
    {
        try
        {
            _inactivityService.ResetActivity();
            route = _roleAccessService.ResolveRouteForRole(_authService.CurrentUser?.Role, route);
            if (route.Equals("cashdrawer", StringComparison.OrdinalIgnoreCase))
            {
                if (!_roleAccessService.CanOpenCashDrawer(_authService.CurrentUser?.Role))
                {
                    await AppAlertService.ShowAlertAsync("Access Denied", "Only managers and admins can open the cash drawer.");
                    return;
                }
                Shell.Current.FlyoutIsPresented = false;
                await OpenCashDrawerFromSidebarAsync();
                return;
            }
            if (!IsRouteAllowed(route))
            {
                await AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access this feature.");
                return;
            }
            if (!_orderServiceAvailabilityService.IsRouteEnabled(route))
            {
                await AppAlertService.ShowAlertAsync("Service Unavailable", "This order service is disabled in Business Information settings.");
                return;
            }
            Shell.Current.FlyoutIsPresented = false;
            if (NavigationCoordinator.IsTemporaryRoute(route))
                await _navigationCoordinator.NavigateTemporaryRouteAsync(route, source: SharedSidebar);
            else
                await _navigationCoordinator.NavigateShellAsync(route, animated: false, source: SharedSidebar);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Navigation Error", ex.Message);
        }
    }

    private async Task ResetNavigationGuardAfterTimeoutAsync(string target)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_isShellNavigationChanging
                && string.Equals(_pendingShellTarget, target, StringComparison.Ordinal))
            {
                System.Diagnostics.Debug.WriteLine($"Navigation guard timed out for {target}; accepting new navigation.");
                _isShellNavigationChanging = false;
                _pendingShellTarget = null;
            }
        });
    }

    private async Task RefreshOrderServiceAvailabilityAsync()
    {
        try
        {
            await _orderServiceAvailabilityService.GetAsync(forceRefresh: true);
            await MainThread.InvokeOnMainThreadAsync(ApplyRoleBasedMenuVisibility);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Order service availability load error: {ex.Message}");
        }
    }

    private void OnOrderServicesChanged(object? sender, OrderServiceAvailabilitySettings settings)
    {
        MainThread.BeginInvokeOnMainThread(ApplyRoleBasedMenuVisibility);
    }

    private async void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
    {
        if (e.IsFromCurrentTerminal || !e.HasKind(AppDataChangeKind.Settings))
        {
            return;
        }

        ServiceHelper.GetService<BusinessSettingsService>()?.InvalidateCache();
        try
        {
            await _orderServiceAvailabilityService.GetAsync(forceRefresh: true);
            await MainThread.InvokeOnMainThreadAsync(ApplyRoleBasedMenuVisibility);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings refresh warning: {ex.Message}");
        }
    }

    private static void ClearShellDetailStacks()
    {
        NavigationCoordinator.PruneTemporaryPages();
    }

    private bool TryResolveRoute(string location, out string route)
    {
        route = string.Empty;
        if (string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        return _roleAccessService.TryResolveRoute(location, out route);
    }

    private void ApplyRoleBasedMenuVisibility()
    {
        ConfigureSharedSidebar();
    }

    private void ConfigureSharedSidebar()
    {
        if (SharedSidebar == null) return;
        var role = _authService.CurrentUser?.Role;
        var roleName = role?.ToString() ?? string.Empty;
        var terminal = TerminalConfigurationService.GetConfiguration();
        SharedSidebar.CurrentRole = roleName;
        SharedSidebar.UserName = _authService.CurrentUser?.Name ?? "No user";
        SharedSidebar.TerminalName = terminal.TerminalName;
        SharedSidebar.ConnectionStatus = TerminalConnectionStateService.IsMotherDisconnectedBannerVisible ? "Mother Offline" : "Connected";
        SharedSidebar.ItemsSource = MotherNavigationItems(role);
        SharedSidebar.Refresh();
    }

    private IReadOnlyList<ApplicationNavigationItem> MotherNavigationItems(UserRole? role)
    {
        var items = new List<ApplicationNavigationItem>();
        void Add(string route, string title, string icon, bool visible)
        {
            if (visible) items.Add(new ApplicationNavigationItem(route, title, icon));
        }
        Add("dashboard", "Dashboard", "dashboard.png", role != null);
        Add("cashdrawer", "Cash Drawer", "cashdrawer.png", _roleAccessService.CanOpenCashDrawer(role));
        Add("foodmenu", "Food Menu", "foodmenu.png", _roleAccessService.CanAccessFeature(role, "foodmenu"));
        Add("liveorder", "Live Order", "liveorder.png", _roleAccessService.CanAccessFeature(role, "liveorder"));
        Add("restaurant", "Restaurant", "restaurant.png", _roleAccessService.CanAccessFeature(role, "restaurant") && _orderServiceAvailabilityService.IsEnabled(PosOrderService.Table));
        Add("collection", "Collection", "collection.png", _roleAccessService.CanAccessFeature(role, "collection") && _orderServiceAvailabilityService.IsEnabled(PosOrderService.Collection));
        Add("delivery", "Delivery", "delivery.png", _roleAccessService.CanAccessFeature(role, "delivery") && _orderServiceAvailabilityService.IsEnabled(PosOrderService.Delivery));
        Add("weborders", "Rider", "weborders.png", _roleAccessService.CanAccessFeature(role, "weborders"));
        Add("giftcards", "Gift Cards", "giftcards.png", _roleAccessService.CanAccessFeature(role, "giftcards"));
        Add("loyalty", "Loyalty Points", "loyalty.png", _roleAccessService.CanAccessFeature(role, "loyalty"));
        Add("reservation", "Reservation", "reservation.png", _roleAccessService.CanAccessFeature(role, "reservation"));
        Add("orderhistory", "Order History", "orderhistory.png", _roleAccessService.CanAccessFeature(role, "orderhistory"));
        Add("report", "Report", "report.png", _roleAccessService.CanAccessFeature(role, "report"));
        Add("staffclock", "Staff Clock", "staff.png", _roleAccessService.CanAccessFeature(role, "staffclock"));
        Add("inventory", "Inventory", "inventory.png", _roleAccessService.CanAccessFeature(role, "inventory"));
        Add("printersetup", "Printers", "printers.png", _roleAccessService.CanAccessFeature(role, "printersetup"));
        Add("settings", "Settings", "settings.png", _roleAccessService.CanAccessFeature(role, "settings"));
        Add("terminalhealth", "Terminal Health", "tarminal.png", _roleAccessService.CanAccessFeature(role, "terminalhealth"));
        Add("customerdata", "Recent Customers", "customers.png", _roleAccessService.CanAccessFeature(role, "customerdata"));
        return items;
    }

    private void ClearMenuSelections()
    {
        SharedSidebar.SelectedRoute = string.Empty;
    }

    private async Task OpenCashDrawerFromSidebarAsync()
    {
        var flowService = ServiceHelper.GetService<CashDrawerFlowService>()
            ?? new CashDrawerFlowService(
                ServiceHelper.GetService<CashDrawerService>() ?? new CashDrawerService(
                    ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(),
                    ServiceHelper.GetService<NetworkPrinterService>() ?? new NetworkPrinterService(),
                    _authService),
                ServiceHelper.GetService<TillExpenseService>() ?? new TillExpenseService(
                    ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(),
                    _authService),
                _authService,
                _inactivityService);

        await flowService.RunAsync(new CashDrawerFlowContext { SourceArea = "sidebar" });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
        _timer?.Dispose();
        _orderServiceAvailabilityService.SettingsChanged -= OnOrderServicesChanged;
        AppDataRefreshService.DataChanged -= OnAppDataChanged;
    }
}
