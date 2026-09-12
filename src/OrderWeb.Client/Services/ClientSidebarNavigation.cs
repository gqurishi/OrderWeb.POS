using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;

namespace OrderWeb.Client.Services;

/// <summary>
/// Shared sidebar title → page mapping so Client matches PosNavigationCatalog
/// titles (Live Orders, Reservations, Recent Customers) on every host page.
/// Live Order UI is SharedUI <see cref="OrderWeb.SharedUI.Views.LiveOrderBoardView"/> —
/// MainPage embeds it in-place; push hosts use <see cref="Pages.Orders.LiveOrderPage"/>.
/// </summary>
public static class ClientSidebarNavigation
{
    private static string? _pendingRootRoute;

    /// <summary>
    /// Creates the ContentPage for a catalog/sidebar title.
    /// Returns null for Dashboard (caller pops to root).
    /// </summary>
    public static Page? CreatePage(string? menuTitle)
    {
        return ClientHostAccess.RouteForTitle(menuTitle) switch
        {
            "cashdrawer" => null,
            "restaurant" => new RestaurantPage(),
            "collection" => new CollectionOrderPage(),
            "delivery" => new DeliveryOrderPage(),
            // Same SharedUI Live Order board as MainPage (thin page host).
            "liveorder" => new LiveOrderPage(),
            "giftcards" => new GiftCardPage(),
            "loyalty" => new LoyaltyPage(),
            "reservation" => new ReservationPage(),
            "orderhistory" => new OrderHistoryPage(),
            "customerdata" => new RecentCustomersPage(),
            "customers" => new RecentCustomersPage(),
            _ => null
        };
    }

    public static bool IsDashboard(string? menuTitle) =>
        ClientHostAccess.IsMenuRoute(menuTitle, "dashboard");

    /// <summary>
    /// Route MainPage should open after <see cref="SwitchAsync"/> pops to root
    /// (Live Order / Restaurant stay MainPage-owned).
    /// </summary>
    public static string? ConsumePendingRootRoute()
    {
        var route = _pendingRootRoute;
        _pendingRootRoute = null;
        return route;
    }

    /// <summary>Drop a queued sidebar open so logout cannot land on the dashboard.</summary>
    public static void ClearPendingRootRoute() => _pendingRootRoute = null;

    /// <summary>
    /// Replace the current pushed manager/order page with another sidebar target.
    /// Always clears the stack first so Gift Cards → Loyalty (and User Collection → Live Order) work.
    /// </summary>
    /// <param name="host">Page that owns the sidebar tap.</param>
    /// <param name="menuTitle">Catalog title from the sidebar.</param>
    /// <param name="currentRoute">Route of the host page (e.g. giftcards); same-route taps no-op.</param>
    public static async Task SwitchAsync(Page host, string? menuTitle, string? currentRoute = null)
    {
        var route = ClientHostAccess.RouteForTitle(menuTitle);
        if (string.IsNullOrWhiteSpace(route))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentRoute) &&
            string.Equals(route, currentRoute, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (await TryHandleMotherOnlyAsync(host, menuTitle))
        {
            return;
        }

        if (!IsDashboard(menuTitle) && !ClientHostAccess.CanOpenMenu(menuTitle))
        {
            return;
        }

        // Open from MainPage using the catalog title (CreatePage maps titles, not raw routes).
        await PopToRootAndResumeMainAsync(host, menuTitle!.Trim());
    }

    private static async Task PopToRootAndResumeMainAsync(Page host, string rootRoute)
    {
        _pendingRootRoute = rootRoute;
        var navigation = host.Navigation;
        if (navigation.NavigationStack.Count > 1)
        {
            await navigation.PopToRootAsync(false);
        }

        // WinUI sometimes skips Appearing on root after PopToRoot — nudge MainPage directly.
        if (navigation.NavigationStack.FirstOrDefault() is MainPage main)
        {
            main.Dispatcher.Dispatch(main.TryConsumePendingSidebarRoute);
        }
    }

    /// <summary>Rider stays on Mother POS. Shows a notice; returns true if handled.</summary>
    public static async Task<bool> TryHandleMotherOnlyAsync(Page host, string? menuTitle)
    {
        if (!ClientHostAccess.IsMotherOnlyMenu(menuTitle))
        {
            return false;
        }

        await host.DisplayAlert(
            "Rider",
            "Rider board is on Mother POS. Open Rider there.",
            "OK");
        return true;
    }
}
