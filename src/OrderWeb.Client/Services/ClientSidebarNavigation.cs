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
    /// <summary>
    /// Creates the ContentPage for a catalog/sidebar title.
    /// Returns null for Dashboard (caller pops to root) and for customer
    /// routes that only MainPage hosts via SharedAppFrame.
    /// </summary>
    public static Page? CreatePage(string? menuTitle)
    {
        return ClientHostAccess.RouteForTitle(menuTitle) switch
        {
            "cashdrawer" => new CashDrawerPage(),
            "restaurant" => new RestaurantPage(),
            "collection" => new CollectionOrderPage(),
            "delivery" => new DeliveryOrderPage(),
            // Same SharedUI Live Order board as MainPage (thin page host).
            "liveorder" => new LiveOrderPage(),
            "giftcards" => new GiftCardPage(),
            "loyalty" => new LoyaltyPage(),
            "reservation" => new ReservationPage(),
            "orderhistory" => new OrderHistoryPage(),
            _ => null
        };
    }

    public static bool IsDashboard(string? menuTitle) =>
        ClientHostAccess.IsMenuRoute(menuTitle, "dashboard");

    public static bool IsCustomerSurface(string? menuTitle) =>
        ClientHostAccess.IsMenuRoute(menuTitle, "customerdata") ||
        ClientHostAccess.IsMenuRoute(menuTitle, "customers");

    /// <summary>Rider (and any future Mother-only sidebar labels). Shows a notice; returns true if handled.</summary>
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
