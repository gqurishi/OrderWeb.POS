using POS_in_NET.Services;
using POS_in_NET.Pages;
using POS_in_NET.Models;
using POS_in_NET.Views;
using System.ComponentModel;
using System.Linq;

namespace POS_in_NET;

public partial class AppShell : Shell, INotifyPropertyChanged
{
    private string _currentDateTime = string.Empty;
    private System.Timers.Timer? _timer;
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly InactivityService _inactivityService;
    private bool _isSyncingDatabase;

    private static readonly HashSet<string> ModalRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "collection",
        "delivery"
    };

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
            if (!TryResolveRoute(target, out var route))
            {
                return;
            }

            if (!IsRouteAllowed(route))
            {
                e.Cancel();
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access this page.");
                });
            }
        }
        catch (Exception ex)
        {
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
        await Shell.Current.GoToAsync("//login");

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
            if (SyncDatabaseButton == null)
            {
                return;
            }

            SyncDatabaseButton.IsEnabled = !isSyncing;
            SyncDatabaseButton.Text = isSyncing ? "Syncing..." : "Update All";
            SyncDatabaseButton.BackgroundColor = isSyncing ? Color.FromArgb("#F59E0B") : Color.FromArgb("#6366F1");
            SyncDatabaseButton.TextColor = Colors.White;
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

                    // Close the flyout
                    Shell.Current.FlyoutIsPresented = false;

                    if (ModalRoutes.Contains(route))
                    {
                        // Navigate to modal route without //
                        await Shell.Current.GoToAsync(route);
                    }
                    else
                    {
                        if (route.Equals("visuallayout", StringComparison.OrdinalIgnoreCase))
                        {
                            ClearShellDetailStacks();
                        }

                        // Navigate to shell content route with //
                        await Shell.Current.GoToAsync($"//{route}", false);
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

    private static void ClearShellDetailStacks()
    {
        if (Shell.Current is not Shell shell)
        {
            return;
        }

        foreach (var shellItem in shell.Items)
        {
            foreach (var shellSection in shellItem.Items)
            {
                var nav = shellSection.Navigation;
                if (nav?.NavigationStack == null || nav.NavigationStack.Count <= 1)
                {
                    continue;
                }

                foreach (var page in nav.NavigationStack.Skip(1).ToList())
                {
                    nav.RemovePage(page);
                }
            }
        }
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
        var role = _authService.CurrentUser?.Role;
        var isLoggedIn = role != null;

        DashboardMenuItem.IsVisible = isLoggedIn;
        LiveOrderMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "liveorder");
        RestaurantMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "restaurant");
        CollectionMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "collection");
        DeliveryMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "delivery");

        WebOrdersMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "weborders");
        GiftCardsMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "giftcards");
        LoyaltyMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "loyalty");
        ReservationMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "reservation");
        OrderHistoryMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "orderhistory");
        CashDrawerMenuItem.IsVisible = _roleAccessService.CanOpenCashDrawer(role);

        ReportMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "report");
        StaffClockMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "staffclock");
        InventoryMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "inventory");
        FoodMenuMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "foodmenu");
        PrintersMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "printersetup");
        TerminalHealthMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "terminalhealth");
        CustomerDataMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "customerdata");
        SettingsMenuItem.IsVisible = _roleAccessService.CanAccessFeature(role, "settings");
    }

    private void ClearMenuSelections()
    {
        // Clear all menu item background colors
        var menuItems = new[]
        {
                DashboardMenuItem, RestaurantMenuItem, FoodMenuMenuItem, WebOrdersMenuItem,
                SettingsMenuItem, CollectionMenuItem, DeliveryMenuItem, OrderHistoryMenuItem,
                LiveOrderMenuItem, GiftCardsMenuItem, LoyaltyMenuItem, ReservationMenuItem,
                CashDrawerMenuItem, ReportMenuItem, StaffClockMenuItem, InventoryMenuItem, PrintersMenuItem,
                TerminalHealthMenuItem, CustomerDataMenuItem
            };

        foreach (var item in menuItems)
        {
            if (item != null)
            {
                item.BackgroundColor = Colors.Transparent;
            }
        }
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
    }
}
