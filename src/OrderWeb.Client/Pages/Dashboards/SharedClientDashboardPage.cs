using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Access;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Navigation;
using OrderWeb.SharedUI.ViewModels;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Dashboards;

/// <summary>Shared Mother-style dashboard hosted by Client for all roles.</summary>
public class SharedClientDashboardPage : ContentPage
{
    private readonly ApplicationShellFrame _shell;

    protected SharedClientDashboardPage(LoginSession session, string title)
    {
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Colors.White;

        // Apply Mother-granted features from this login (UserSession wrappers used to drop them).
        ClientHostAccess.ApplyFromSession(session);

        var capabilities = ClientCapabilityResolver.ForRole(session.Role, session.Permissions);
        var features = ClientHostAccess.FeaturesForRole(session.Role);
        var dashboardRoutes = ClientHostAccess.DashboardRoutesForRole(session.Role);

        var dashboardVm = new DashboardViewModel
        {
            Title = title,
            Subtitle = string.Empty
        };
        dashboardVm.ApplyCapabilities(capabilities, features, dashboardRoutes);
        dashboardVm.TileSelected += async (_, tile) => await NavigateRouteAsync(tile.Route);

        var dashboard = new DashboardView { ViewModel = dashboardVm };
        _shell = new ApplicationShellFrame
        {
            PageTitle = title,
            MainContent = dashboard,
            RestaurantName = "Restaurant POS",
            RestaurantLogo = "mainlogo.png",
            UserName = session.UserName,
            UserRole = session.Role,
            TerminalName = "Client POS",
            ConnectionStatus = "Connected",
            SelectedRoute = "dashboard",
            AvailableCapabilities = capabilities,
            AvailableFeatures = features,
            MenuItems = PosNavigationCatalog.Filter(capabilities, features, ClientHostAccess.RoutesForRole(session.Role)),
            ShowUpdateButton = string.Equals(session.Role, "Manager", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase),
            ShowWelcomeBrand = true,
            ShowIdentity = false,
            ShowConnection = false,
            ShowMinimize = true
        };
        _shell.NavigationRequested += async (_, e) => await NavigateRouteAsync(e.Route);
        _shell.LogoutRequested += async (_, _) => await ClientSignOut.RequestAsync(this);
        _shell.MinimizeRequested += (_, _) => ClientWindowService.MinimizeMainWindow();
        _shell.UpdateRequested += async (_, _) => await OnUpdateAllAsync();
        Content = _shell;
    }

    private async Task OnUpdateAllAsync()
    {
        try
        {
            var access = await new MotherAccessClient().RefreshAccessAsync();
            if (access.Success)
            {
                await DisplayAlertAsync(
                    "Update All",
                    "Client access refreshed from Mother. Menus may appear or hide. Open Gift Cards again if it was already open.",
                    "OK");
            }
            else
            {
                await DisplayAlertAsync("Update All", access.Message, "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Update All", ex.Message, "OK");
        }
    }

    private async Task NavigateRouteAsync(string route)
    {
        if (string.Equals(route, "dashboard", StringComparison.OrdinalIgnoreCase))
        {
            _shell.SelectedRoute = "dashboard";
            return;
        }

        if (string.Equals(route, "weborders", StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlertAsync(
                "Rider",
                "Rider board is on Mother POS. Open Rider there.",
                "OK");
            _shell.SelectedRoute = "dashboard";
            return;
        }

        if (!ClientAccessPolicy.IsRouteAllowed(route) ||
            (!ClientHostAccess.Routes.Contains(route) &&
             !string.Equals(route, "reservation", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(route, "weborders", StringComparison.OrdinalIgnoreCase)))
        {
            _shell.SelectedRoute = "dashboard";
            return;
        }

        if (string.Equals(route, "cashdrawer", StringComparison.OrdinalIgnoreCase))
        {
            _shell.SelectedRoute = "dashboard";
            await ClientCashDrawerOpen.RunAsync(this, new ClientCacheService(), cashier: false);
            return;
        }

        Page? page = route.ToLowerInvariant() switch
        {
            "restaurant" => new RestaurantPage(),
            "collection" => new CollectionOrderPage(),
            "delivery" => new DeliveryOrderPage(),
            "liveorder" => new LiveOrderPage(),
            "reservation" => new ReservationPage(),
            "giftcards" => new GiftCardPage(),
            "loyalty" => new LoyaltyPage(),
            "cashdrawer" => null,
            "orderhistory" => new OrderHistoryPage(),
            "customerdata" => new RecentCustomersPage(),
            "customers" => new RecentCustomersPage(),
            _ => null
        };

        if (page is null)
        {
            _shell.SelectedRoute = "dashboard";
            return;
        }

        ClientPageChrome.HideSystemBackChrome(page);

        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopToRootAsync(false);
        }

        if (page is CollectionOrderPage or DeliveryOrderPage)
        {
            await ClientSideNavigation.PushFromSideAsync(Navigation, page);
            return;
        }

        await Navigation.PushAsync(page, false);
    }
}

public class UserDashboardPage : SharedClientDashboardPage
{
    public UserDashboardPage()
        : this(new LoginSession("0", "User", "User", Array.Empty<string>(), string.Empty, DateTimeOffset.UtcNow.AddHours(1)))
    {
    }

    public UserDashboardPage(LoginSession session) : base(session, "Dashboard")
    {
    }

    public UserDashboardPage(UserSession session)
        : this(new LoginSession(session.User.UserId, session.User.DisplayName, session.User.Role, session.User.Permissions.ToList(), session.SessionId, session.ExpiresAtUtc))
    {
    }
}

public class ManagerDashboardPage : SharedClientDashboardPage
{
    public ManagerDashboardPage()
        : this(new LoginSession("0", "Manager", "Manager", Array.Empty<string>(), string.Empty, DateTimeOffset.UtcNow.AddHours(1)))
    {
    }

    public ManagerDashboardPage(LoginSession session) : base(session, "Dashboard")
    {
    }

    public ManagerDashboardPage(UserSession session)
        : this(new LoginSession(session.User.UserId, session.User.DisplayName, session.User.Role, session.User.Permissions.ToList(), session.SessionId, session.ExpiresAtUtc))
    {
    }
}

public class AdminDashboardPage : SharedClientDashboardPage
{
    public AdminDashboardPage()
        : this(new LoginSession("0", "Admin", "Admin", Array.Empty<string>(), string.Empty, DateTimeOffset.UtcNow.AddHours(1)))
    {
    }

    public AdminDashboardPage(LoginSession session) : base(session, "Admin Dashboard")
    {
    }

    public AdminDashboardPage(UserSession session)
        : this(new LoginSession(session.User.UserId, session.User.DisplayName, session.User.Role, session.User.Permissions.ToList(), session.SessionId, session.ExpiresAtUtc))
    {
    }
}
