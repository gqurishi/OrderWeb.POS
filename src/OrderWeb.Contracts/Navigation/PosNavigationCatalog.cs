namespace OrderWeb.Contracts.Navigation;

/// <summary>
/// Capability keys Mother grants to Client terminals. Navigation and actions
/// must check these rather than inventing Client-only menu rules.
/// </summary>
public static class PosCapabilityKeys
{
    public const string Dashboard = "client.dashboard";
    public const string OpenOrders = "client.live_orders";
    public const string FloorTables = "client.restaurant";
    public const string CreateOrder = "client.order.create";
    public const string Collection = "client.collection";
    public const string Delivery = "client.delivery";
    public const string Payment = "client.payment";
    public const string CashDrawer = "client.cash_drawer.open";
    public const string WebOrders = "client.online_orders.manage";
    public const string GiftCards = "client.gift_cards";
    public const string Loyalty = "client.loyalty";
    public const string Reservations = "client.reservations";
    public const string OrderHistory = "client.order_history";
    public const string Customers = "client.customers";
    public const string Reports = "client.reports";
    public const string Settings = "client.settings";
}

/// <summary>
/// Declares one shared POS navigation route and its capability / connectivity rules.
/// </summary>
public sealed record PosNavigationEntry(
    string Route,
    string Title,
    string IconSource,
    string? RequiredCapability,
    bool RequiresMotherConnection,
    bool AllowsCachedOffline,
    bool AvailableOnClient,
    bool AvailableOnMother,
    params string[] FallbackRoles);

/// <summary>
/// Runtime inputs used to resolve visible navigation for a host.
/// </summary>
public sealed record PosNavigationContext(
    string? Role,
    IReadOnlyCollection<string> Capabilities,
    bool IsMotherConnected,
    IReadOnlyDictionary<string, bool>? FeatureFlags = null);

/// <summary>
/// Shared Mother/Client navigation catalog. Hosts filter with
/// <see cref="Resolve"/> and render via SharedUI <c>ApplicationSidebar</c>.
/// </summary>
public static class PosNavigationCatalog
{
    public static IReadOnlyList<PosNavigationEntry> All { get; } =
    [
        new("dashboard", "Dashboard", "dashboard.png",
            PosCapabilityKeys.Dashboard, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("restaurant", "Restaurant", "restaurant.png",
            PosCapabilityKeys.FloorTables, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("liveorder", "Live Order", "liveorder.png",
            PosCapabilityKeys.OpenOrders, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("collection", "Collection", "collection.png",
            PosCapabilityKeys.Collection, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("delivery", "Delivery", "delivery.png",
            PosCapabilityKeys.Delivery, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("orderentry", "New Order", "foodmenu.png",
            PosCapabilityKeys.CreateOrder, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("payment", "Payment", "cashdrawer.png",
            PosCapabilityKeys.Payment, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("cashdrawer", "Cash Drawer", "cashdrawer.png",
            PosCapabilityKeys.CashDrawer, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("weborders", "Web Orders", "weborders.png",
            PosCapabilityKeys.WebOrders, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: true, AvailableOnMother: true,
            "Manager", "Admin"),

        new("giftcards", "Gift Cards", "giftcards.png",
            PosCapabilityKeys.GiftCards, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: true, AvailableOnMother: true,
            "Manager", "Admin"),

        new("loyalty", "Loyalty Points", "loyalty.png",
            PosCapabilityKeys.Loyalty, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: true, AvailableOnMother: true,
            "Manager", "Admin"),

        new("reservation", "Reservation", "reservation.png",
            PosCapabilityKeys.Reservations, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("orderhistory", "Order History", "orderhistory.png",
            PosCapabilityKeys.OrderHistory, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "Manager", "Admin"),

        new("customers", "Customers", "customers.png",
            PosCapabilityKeys.Customers, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: true, AvailableOnMother: true,
            "User", "Manager", "Admin"),

        new("report", "Report", "report.png",
            PosCapabilityKeys.Reports, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: true, AvailableOnMother: true,
            "Manager", "Admin"),

        new("settings", "Settings", "settings.png",
            PosCapabilityKeys.Settings, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: true, AvailableOnMother: true,
            "Admin"),

        // Mother-only operational admin routes (not shown on Client terminals).
        new("foodmenu", "Food Menu", "foodmenu.png",
            null, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: false, AvailableOnMother: true,
            "Manager", "Admin"),

        new("staffclock", "Staff Clock", "staff.png",
            null, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: false, AvailableOnMother: true,
            "Manager", "Admin"),

        new("inventory", "Inventory", "inventory.png",
            null, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: false, AvailableOnMother: true,
            "Manager", "Admin"),

        new("printersetup", "Printers", "printers.png",
            null, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: false, AvailableOnMother: true,
            "Admin"),

        new("terminalhealth", "Terminal Health", "tarminal.png",
            null, RequiresMotherConnection: true, AllowsCachedOffline: false,
            AvailableOnClient: false, AvailableOnMother: true,
            "Admin"),

        new("customerdata", "Recent Customers", "customers.png",
            null, RequiresMotherConnection: false, AllowsCachedOffline: true,
            AvailableOnClient: false, AvailableOnMother: true,
            "Manager", "Admin")
    ];

    public static IReadOnlyList<PosNavigationEntry> ClientEntries { get; } =
        All.Where(entry => entry.AvailableOnClient).ToList();

    public static IReadOnlyList<PosNavigationEntry> MotherEntries { get; } =
        All.Where(entry => entry.AvailableOnMother).ToList();

    public static IReadOnlyList<ResolvedPosNavigationItem> Resolve(
        PosNavigationContext context,
        bool forClient)
    {
        ArgumentNullException.ThrowIfNull(context);

        var source = forClient ? ClientEntries : MotherEntries;
        var resolved = new List<ResolvedPosNavigationItem>();

        foreach (var entry in source)
        {
            if (!IsCapabilityAllowed(entry, context))
            {
                continue;
            }

            if (!IsFeatureAllowed(entry, context))
            {
                continue;
            }

            var enabled = !entry.RequiresMotherConnection || context.IsMotherConnected;
            resolved.Add(new ResolvedPosNavigationItem(
                entry.Route,
                entry.Title,
                entry.IconSource,
                entry.RequiredCapability,
                entry.RequiresMotherConnection,
                entry.AllowsCachedOffline,
                enabled,
                entry.FallbackRoles));
        }

        return resolved;
    }

    /// <summary>
    /// Default Client capabilities for a role when Mother has not yet issued
    /// explicit <c>client.*</c> grants. Mother should prefer sending explicit keys.
    /// </summary>
    public static IReadOnlyList<string> DefaultClientCapabilitiesForRole(string? role)
    {
        var normalized = role?.Trim() ?? string.Empty;
        if (normalized.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return ClientEntries
                .Select(entry => entry.RequiredCapability)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (normalized.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                PosCapabilityKeys.Dashboard,
                PosCapabilityKeys.OpenOrders,
                PosCapabilityKeys.FloorTables,
                PosCapabilityKeys.CreateOrder,
                PosCapabilityKeys.Collection,
                PosCapabilityKeys.Delivery,
                PosCapabilityKeys.Payment,
                PosCapabilityKeys.CashDrawer,
                PosCapabilityKeys.WebOrders,
                PosCapabilityKeys.GiftCards,
                PosCapabilityKeys.Loyalty,
                PosCapabilityKeys.Reservations,
                PosCapabilityKeys.OrderHistory,
                PosCapabilityKeys.Customers,
                PosCapabilityKeys.Reports
            ];
        }

        // Basic User / Staff terminal defaults.
        return
        [
            PosCapabilityKeys.Dashboard,
            PosCapabilityKeys.OpenOrders,
            PosCapabilityKeys.FloorTables,
            PosCapabilityKeys.CreateOrder,
            PosCapabilityKeys.Collection,
            PosCapabilityKeys.Delivery,
            PosCapabilityKeys.Payment,
            PosCapabilityKeys.CashDrawer,
            PosCapabilityKeys.Reservations,
            PosCapabilityKeys.Customers
        ];
    }

    private static bool IsCapabilityAllowed(PosNavigationEntry entry, PosNavigationContext context)
    {
        if (string.IsNullOrWhiteSpace(entry.RequiredCapability))
        {
            return entry.FallbackRoles.Length == 0 ||
                   entry.FallbackRoles.Any(role =>
                       string.Equals(role, context.Role, StringComparison.OrdinalIgnoreCase));
        }

        if (context.Capabilities.Contains(entry.RequiredCapability, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // When Mother has issued any client.* grants, missing keys hide the route.
        // Otherwise fall back to role until explicit capabilities are present.
        if (HasExplicitClientCapabilities(context))
        {
            return false;
        }

        return entry.FallbackRoles.Any(role =>
            string.Equals(role, context.Role, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasExplicitClientCapabilities(PosNavigationContext context) =>
        context.Capabilities.Any(key =>
            key.StartsWith("client.", StringComparison.OrdinalIgnoreCase));

    private static bool IsFeatureAllowed(PosNavigationEntry entry, PosNavigationContext context)
    {
        if (context.FeatureFlags is null || context.FeatureFlags.Count == 0)
        {
            return true;
        }

        var flag = entry.Route switch
        {
            "restaurant" => "tables.enabled",
            "collection" => "collection.enabled",
            "delivery" => "delivery.enabled",
            "weborders" => "online_orders.enabled",
            "giftcards" => "gift_cards.enabled",
            "loyalty" => "loyalty.enabled",
            "reservation" => "reservations.enabled",
            _ => null
        };

        if (flag is null)
        {
            return true;
        }

        return !context.FeatureFlags.TryGetValue(flag, out var enabled) || enabled;
    }
}

public sealed record ResolvedPosNavigationItem(
    string Route,
    string Title,
    string IconSource,
    string? RequiredCapability,
    bool RequiresMotherConnection,
    bool AllowsCachedOffline,
    bool IsEnabled,
    IReadOnlyList<string> FallbackRoles);
