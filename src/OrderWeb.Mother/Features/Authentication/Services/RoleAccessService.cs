using POS_in_NET.Models;

namespace POS_in_NET.Services;

public class RoleAccessService
{
    private static readonly string[] KnownRoutesByPriority =
    {
        // This must be before the generic "dashboard" substring; otherwise a
        // Cashier login is incorrectly treated as an Admin dashboard request.
        "cashierdashboard",
        "managerdashboard",
        "userdashboard",
        "visuallayout",
        "reportdetails",
        "orderhistory",
        "advanceorders",
        "printersetup",
        "weborders",
        "giftcards",
        "reservation",
        "inventory",
        "restaurant",
        "layout",
        "collection",
        "delivery",
        "liveorder",
        "foodmenu",
        "settings",
        "loyalty",
        "report",
        "dashboard",
        "table",
        "floor",
        "terminalsetup",
        "initialadminsetup",
        "terminalhealth",
        "customerdata",
        "staffclock",
        "login"
    };

    private static readonly IReadOnlyDictionary<UserRole, HashSet<string>> AllowedRoutesByRole =
        new Dictionary<UserRole, HashSet<string>>
        {
            [UserRole.User] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login", "userdashboard", "restaurant", "collection", "delivery", "visuallayout",
                "liveorder", "reservation"
            },
            [UserRole.Staff] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login", "weborders"
            },
            [UserRole.Manager] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login", "managerdashboard", "restaurant", "collection", "delivery", "visuallayout",
                "liveorder", "reservation", "weborders", "orderhistory", "advanceorders", "giftcards", "loyalty", "customerdata"
            },
            [UserRole.Cashier] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login", "cashierdashboard", "report", "cashdrawer", "reconciliation"
            },
            [UserRole.Admin] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login", "dashboard", "managerdashboard", "userdashboard", "restaurant", "layout",
                "collection", "delivery", "liveorder", "visuallayout", "floor", "table",
                "weborders", "giftcards", "loyalty", "reservation", "orderhistory", "advanceorders",
                "report", "reportdetails", "inventory", "foodmenu", "printersetup", "settings",
                "terminalhealth", "customerdata", "staffclock"
            },
            // Bar Manager: Bar Inventory workspace only (Mother + Client).
            [UserRole.BarManager] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login", "inventory"
            }
        };

    public string ResolveDashboardRoute(UserRole? role)
    {
        return role switch
        {
            UserRole.User => "userdashboard",
            UserRole.Manager => "managerdashboard",
            UserRole.Cashier => "cashierdashboard",
            UserRole.Admin => "dashboard",
            UserRole.BarManager => "inventory",
            UserRole.Staff => "login",
            _ => "login"
        };
    }

    public string ResolveRouteForRole(UserRole? role, string route)
    {
        var normalized = NormalizeRoute(route);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Equals("dashboard", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveDashboardRoute(role);
        }

        // Restaurant = live floor for all roles. Layout hub stays on route "layout".
        return normalized;
    }

    public bool CanAccessFeature(UserRole? role, string route)
    {
        var resolvedRoute = ResolveRouteForRole(role, route);
        return CanAccessRoute(role, resolvedRoute);
    }

    public bool CanOpenCashDrawer(UserRole? role) =>
        role is UserRole.User or UserRole.Cashier or UserRole.Manager or UserRole.Admin;

    public bool CanAccessRoute(UserRole? role, string route)
    {
        var normalized = NormalizeRoute(route);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Equals("login", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("terminalsetup", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("initialadminsetup", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (role == null)
        {
            return false;
        }

        return AllowedRoutesByRole.TryGetValue(role.Value, out var allowed)
               && allowed.Contains(normalized);
    }

    public bool TryResolveRoute(string location, out string route)
    {
        route = string.Empty;
        if (string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        foreach (var knownRoute in KnownRoutesByPriority)
        {
            if (location.Contains(knownRoute, StringComparison.OrdinalIgnoreCase))
            {
                route = knownRoute;
                return true;
            }
        }

        return false;
    }

    public bool IsAdmin(UserRole? role) => role == UserRole.Admin;
    public bool IsManagerOrAdmin(UserRole? role) => role == UserRole.Manager || role == UserRole.Admin;

    public bool CanViewZReport(UserRole? role) => role is UserRole.Admin or UserRole.Cashier;

    public bool CanPrintZReport(UserRole? role) => role is UserRole.Admin or UserRole.Cashier;

    private static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return string.Empty;
        }

        var normalized = route.Trim().Trim('/');
        var queryIndex = normalized.IndexOf('?');
        if (queryIndex >= 0)
        {
            normalized = normalized[..queryIndex];
        }

        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        return normalized;
    }
}
