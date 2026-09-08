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
            RestaurantLogo = "companymark.png",
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
        _shell.LogoutRequested += async (_, _) => await Navigation.PopToRootAsync(false);
        _shell.MinimizeRequested += (_, _) => ClientWindowService.MinimizeMainWindow();
        Content = _shell;
    }

    private async Task NavigateRouteAsync(string route)
    {
        if (string.Equals(route, "dashboard", StringComparison.OrdinalIgnoreCase))
        {
            _shell.SelectedRoute = "dashboard";
            return;
        }

        if (string.Equals(route, "weborders", StringComparison.OrdinalIgnoreCase) ||
            !ClientAccessPolicy.IsRouteAllowed(route) ||
            (!ClientHostAccess.Routes.Contains(route) &&
             !string.Equals(route, "reservation", StringComparison.OrdinalIgnoreCase)))
        {
            _shell.SelectedRoute = "dashboard";
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
            "cashdrawer" => new CashDrawerPage(),
            "orderhistory" => new OrderHistoryPage(),
            _ => null
        };

        if (page is null)
        {
            _shell.SelectedRoute = "dashboard";
            return;
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
