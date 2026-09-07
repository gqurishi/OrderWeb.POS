using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Features;

namespace OrderWeb.Contracts.Access;

/// <summary>
/// Frozen product rules for Client POS. Mother is the only restaurant server.
/// Client talks only to Mother. Mother talks to MariaDB, printers, and cloud.
/// </summary>
public static class ClientAccessPolicy
{
    public const string StandingRule =
        "Client talks only to Mother. Mother talks to MariaDB, printers, and cloud. Mother decides what each Client can see and do. Web orders stay on Mother. Reservations may go to Client.";

    /// <summary>One Mother POS per restaurant. Client never stores or uses OrderWeb cloud credentials.</summary>
    public const bool OneMotherPerRestaurant = true;

    /// <summary>Client must not call orderweb.net or any tenant cloud API.</summary>
    public const bool ClientConnectsToMotherOnly = true;

    /// <summary>Network and IP printers are configured and driven on Mother only.</summary>
    public const bool PrintersAreMotherOnly = true;

    /// <summary>
    /// Features a new Client terminal gets until Mother changes the list.
    /// Reservations stay off until Mother turns them on for that terminal.
    /// </summary>
    public static readonly IReadOnlySet<string> DefaultGrantedFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PosFeatureKeys.DineIn,
        PosFeatureKeys.Collection,
        PosFeatureKeys.Delivery,
        PosFeatureKeys.LiveOrders,
        PosFeatureKeys.Customers,
        PosFeatureKeys.Payments,
        PosFeatureKeys.GiftCards,
        PosFeatureKeys.CustomerPoints
    };

    public static readonly IReadOnlyList<(string Key, string Label)> EditableFeatures =
    [
        (PosFeatureKeys.DineIn, "Restaurant / tables"),
        (PosFeatureKeys.Collection, "Collection"),
        (PosFeatureKeys.Delivery, "Delivery"),
        (PosFeatureKeys.LiveOrders, "Live orders"),
        (PosFeatureKeys.Reservations, "Reservations"),
        (PosFeatureKeys.Customers, "Customers"),
        (PosFeatureKeys.Payments, "Payments / cash drawer"),
        (PosFeatureKeys.GiftCards, "Gift cards"),
        (PosFeatureKeys.CustomerPoints, "Loyalty")
    ];

    /// <summary>Features Mother may grant a Client terminal. Web Orders is never in this set.</summary>
    public static readonly IReadOnlySet<string> GrantableFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PosFeatureKeys.DineIn,
        PosFeatureKeys.Collection,
        PosFeatureKeys.Delivery,
        PosFeatureKeys.LiveOrders,
        PosFeatureKeys.Reservations,
        PosFeatureKeys.Customers,
        PosFeatureKeys.Payments,
        PosFeatureKeys.GiftCards,
        PosFeatureKeys.CustomerPoints
    };

    /// <summary>Navigation routes Client may show when Mother has granted the matching feature/capability.</summary>
    public static readonly IReadOnlySet<string> GrantableRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dashboard",
        "restaurant",
        "collection",
        "delivery",
        "liveorder",
        "reservation",
        "customers",
        "customerdata",
        "payments",
        "giftcards",
        "loyalty",
        "cashdrawer",
        "orderhistory"
    };

    /// <summary>Never shown or served on Client POS. These stay on Mother.</summary>
    public static readonly IReadOnlySet<string> BlockedRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "weborders",
        "weborder",
        "printersetup",
        "printers",
        "printgroups",
        "printtemplates",
        "foodmenu",
        "menuadmin",
        "menumanagement",
        "users",
        "usermanagement",
        "roles",
        "database",
        "databasemanagement",
        "backup",
        "backups",
        "cloudsettings",
        "cloudlogin",
        "settings",
        "terminalhealth",
        "terminalsetup",
        "initialadminsetup",
        "inventory",
        "report",
        "reportdetails",
        "fullreports",
        "payrollreports",
        "securityreports",
        "staffclock"
    };

    /// <summary>Mother-only product features. Never send these to Client.</summary>
    public static readonly IReadOnlySet<string> MotherOnlyFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PosFeatureKeys.WebOrders
    };

    /// <summary>Capabilities Client sessions may receive. Print receipts is allowed; printer setup is not.</summary>
    public static readonly IReadOnlySet<string> GrantableCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PosCapabilityKeys.ViewDashboard,
        PosCapabilityKeys.ViewCustomers,
        PosCapabilityKeys.CreateOrders,
        PosCapabilityKeys.SubmitOrders,
        PosCapabilityKeys.TakeOrders,
        PosCapabilityKeys.OpenTables,
        PosCapabilityKeys.ManageCustomers,
        PosCapabilityKeys.TakePayments,
        PosCapabilityKeys.TransferTables,
        PosCapabilityKeys.ApplyDiscount,
        PosCapabilityKeys.VoidItems,
        PosCapabilityKeys.VoidOrders,
        PosCapabilityKeys.Refund,
        PosCapabilityKeys.PrintReceipts,
        PosCapabilityKeys.ReprintReceipts,
        PosCapabilityKeys.OpenCashDrawer,
        PosCapabilityKeys.ApproveManagerAction,
        PosCapabilityKeys.ReconcileCashDrawer,
        PosCapabilityKeys.AddReconciliationNotes
    };

    /// <summary>Never issued on a Client session. Mother UI may still use these locally.</summary>
    public static readonly IReadOnlySet<string> BlockedCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PosCapabilityKeys.ConfigurePrinters,
        PosCapabilityKeys.EditMenu,
        PosCapabilityKeys.ManageUsers,
        PosCapabilityKeys.AccessAdmin,
        PosCapabilityKeys.AccessSettings,
        PosCapabilityKeys.EditTables,
        PosCapabilityKeys.ViewReports,
        PosCapabilityKeys.ViewDailyReports,
        PosCapabilityKeys.PreviewZReports,
        PosCapabilityKeys.PrintZReports,
        PosCapabilityKeys.ReprintZReports,
        PosCapabilityKeys.ExportReports,
        PosCapabilityKeys.ViewReportPrintHistory
    };

    public static bool IsRouteAllowed(string? route) =>
        !string.IsNullOrWhiteSpace(route) &&
        GrantableRoutes.Contains(route) &&
        !BlockedRoutes.Contains(route);

    public static bool IsRouteBlocked(string? route) =>
        !string.IsNullOrWhiteSpace(route) && BlockedRoutes.Contains(route.Trim());

    public static bool IsFeatureGrantable(string? feature) =>
        !string.IsNullOrWhiteSpace(feature) &&
        GrantableFeatures.Contains(feature) &&
        !MotherOnlyFeatures.Contains(feature);

    public static bool IsCapabilityAllowed(string? capability) =>
        !string.IsNullOrWhiteSpace(capability) &&
        GrantableCapabilities.Contains(capability) &&
        !BlockedCapabilities.Contains(capability);

    public static IReadOnlySet<string> FilterRoutes(IEnumerable<string>? routes) =>
        Filter(routes, IsRouteAllowed);

    public static IReadOnlySet<string> FilterFeatures(IEnumerable<string>? features) =>
        Filter(features, IsFeatureGrantable);

    public static IReadOnlySet<string> FilterCapabilities(IEnumerable<string>? capabilities) =>
        Filter(capabilities, IsCapabilityAllowed);

    /// <summary>
    /// Routes Mother will allow a Client to show for the granted feature list.
    /// Dashboard is always included. Web Orders is never included.
    /// </summary>
    public static IReadOnlySet<string> RoutesForFeatures(IEnumerable<string>? features)
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dashboard" };
        foreach (var feature in FilterFeatures(features))
        {
            if (!FeatureRouteMap.TryGetValue(feature, out var mapped))
            {
                continue;
            }

            foreach (var route in mapped)
            {
                if (IsRouteAllowed(route))
                {
                    routes.Add(route);
                }
            }
        }

        return routes;
    }

    private static readonly Dictionary<string, string[]> FeatureRouteMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [PosFeatureKeys.DineIn] = ["restaurant"],
        [PosFeatureKeys.Collection] = ["collection"],
        [PosFeatureKeys.Delivery] = ["delivery"],
        [PosFeatureKeys.LiveOrders] = ["liveorder"],
        [PosFeatureKeys.Reservations] = ["reservation"],
        [PosFeatureKeys.Customers] = ["customers", "customerdata"],
        [PosFeatureKeys.Payments] = ["payments", "cashdrawer", "orderhistory"],
        [PosFeatureKeys.GiftCards] = ["giftcards"],
        [PosFeatureKeys.CustomerPoints] = ["loyalty"]
    };

    private static IReadOnlySet<string> Filter(IEnumerable<string>? values, Func<string?, bool> allowed)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return set;
        }

        foreach (var value in values)
        {
            if (allowed(value))
            {
                set.Add(value);
            }
        }

        return set;
    }
}
