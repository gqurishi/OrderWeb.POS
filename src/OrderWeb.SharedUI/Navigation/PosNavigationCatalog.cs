using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Features;
using OrderWeb.Contracts.Navigation;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Navigation;

/// <summary>
/// Canonical POS navigation routes. Hosts filter with capabilities/features;
/// SharedUI never checks Mother vs Client directly.
/// </summary>
public static class PosNavigationCatalog
{
    public static IReadOnlyList<NavigationItemContract> All { get; } =
    [
        Item("dashboard", "Dashboard", "dashboard.png", [], [], 10),
        // Keep the normal order-taking flow together in every host sidebar.
        Item("liveorder", "Live Orders", "liveorder.png", [PosCapabilityKeys.TakeOrders], [PosFeatureKeys.LiveOrders], 55),
        Item("restaurant", "Restaurant", "restaurant.png", [PosCapabilityKeys.OpenTables], [PosFeatureKeys.DineIn], 30),
        Item("collection", "Collection", "collection.png", [PosCapabilityKeys.TakeOrders], [PosFeatureKeys.Collection], 40),
        Item("delivery", "Delivery", "delivery.png", [PosCapabilityKeys.TakeOrders], [PosFeatureKeys.Delivery], 50),
        // Mother sidebar only. ClientHostAccess strips this route from the Client menu.
        Item("weborders", "Rider", "delivery.png", [PosCapabilityKeys.TakeOrders], [PosFeatureKeys.Delivery, PosFeatureKeys.WebOrders], 52),
        Item("customers", "Customers", "customers.png", [PosCapabilityKeys.ManageCustomers], [PosFeatureKeys.Customers], 60),
        Item("payments", "Payments", "giftcards.png", [PosCapabilityKeys.TakePayments], [PosFeatureKeys.Payments], 70),
        Item("cashdrawer", "Cash Drawer", "cashdrawer.png", [PosCapabilityKeys.TakePayments], [PosFeatureKeys.Payments], 20),
        Item("giftcards", "Gift Cards", "giftcards.png", [PosCapabilityKeys.TakePayments], [PosFeatureKeys.GiftCards], 60),
        Item("loyalty", "Loyalty Points", "loyalty.png", [PosCapabilityKeys.ManageCustomers], [PosFeatureKeys.CustomerPoints], 70),
        Item("reservation", "Reservations", "reservation.png", [PosCapabilityKeys.OpenTables], [PosFeatureKeys.Reservations], 80),
        Item("orderhistory", "Order History", "orderhistory.png", [PosCapabilityKeys.TakeOrders], [PosFeatureKeys.Payments], 90),
        Item("report", "Reports", "report.png", [PosCapabilityKeys.ViewReports], [], 140),
        Item("foodmenu", "Menu Admin", "foodmenu.png", [PosCapabilityKeys.EditMenu], [], 150),
        Item("printersetup", "Printer Setup", "printers.png", [PosCapabilityKeys.ConfigurePrinters], [], 160),
        Item("settings", "Settings", "settings.png", [PosCapabilityKeys.AccessAdmin], [], 170),
        Item("staffclock", "Staff Clock", "staff.png", [PosCapabilityKeys.AccessAdmin], [], 180),
        Item("inventory", "Inventory", "inventory.png", [PosCapabilityKeys.ViewReports], [], 190),
        Item("terminalhealth", "Terminal", "tarminal.png", [PosCapabilityKeys.AccessAdmin], [], 200),
        Item("customerdata", "Recent Customers", "customers.png", [PosCapabilityKeys.ManageCustomers], [PosFeatureKeys.Customers], 110)
    ];

    public static IReadOnlyList<ApplicationNavigationItem> Filter(
        IReadOnlySet<string> capabilities,
        IReadOnlySet<string>? features = null,
        IReadOnlySet<string>? hostRoutes = null)
    {
        return All
            .Where(item => hostRoutes is null || hostRoutes.Contains(item.Route))
            .Where(item => item.RequiredCapabilities.Count == 0 ||
                           item.RequiredCapabilities.All(capabilities.Contains))
            .Where(item => item.RequiredFeatures.Count == 0 ||
                           features is null ||
                           item.RequiredFeatures.Any(features.Contains))
            .OrderBy(item => item.SortOrder)
            .Select(item => new ApplicationNavigationItem(item.Route, item.Title, item.IconKey)
            {
                RequiredCapabilities = item.RequiredCapabilities,
                RequiredFeatures = item.RequiredFeatures
            })
            .ToList();
    }

    private static NavigationItemContract Item(
        string route,
        string title,
        string icon,
        IEnumerable<string> capabilities,
        IEnumerable<string> features,
        int sort) =>
        new(route, title, icon,
            new HashSet<string>(capabilities, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(features, StringComparer.OrdinalIgnoreCase),
            sort);
}

/// <summary>
/// Mother User / Manager flyout routes. Client uses the same sets so both
/// hosts stay in sync when a sidebar item is added or removed.
/// </summary>
public static class PosRoleMenus
{
    public static IReadOnlySet<string> User { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dashboard", "restaurant", "collection", "delivery", "liveorder", "reservation"
    };

    public static IReadOnlySet<string> Manager { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dashboard", "cashdrawer", "restaurant", "collection", "delivery", "liveorder", "reservation",
        "weborders", "orderhistory", "giftcards", "loyalty", "customerdata"
    };

    public static IReadOnlySet<string>? ForRole(string? role)
    {
        if (string.Equals(role, "User", StringComparison.OrdinalIgnoreCase))
        {
            return User;
        }

        if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            return Manager;
        }

        return null;
    }
}
