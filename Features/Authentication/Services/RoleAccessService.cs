using POS_in_NET.Models;

namespace POS_in_NET.Services;

public class RoleAccessService
{
    private static readonly string[] KnownRoutesByPriority =
    {
        "managerdashboard",
        "userdashboard",
        "visuallayout",
        "reportdetails",
        "orderhistory",
        "printersetup",
        "weborders",
        "giftcards",
        "reservation",
        "inventory",
        "restaurant",
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
                "login", "userdashboard", "collection", "delivery", "liveorder", "visuallayout",
                "reservation"
            },
            [UserRole.Staff] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login"
            },
            [UserRole.Manager] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login", "managerdashboard", "restaurant", "collection", "delivery", "liveorder",
                "visuallayout", "floor", "table", "giftcards", "loyalty", "reservation",
                "weborders", "orderhistory"
            },
            [UserRole.Admin] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "login", "dashboard", "managerdashboard", "userdashboard", "restaurant",
                "collection", "delivery", "liveorder", "visuallayout", "floor", "table",
                "weborders", "giftcards", "loyalty", "reservation", "orderhistory",
                "report", "reportdetails", "inventory", "foodmenu", "printersetup", "settings",
                "terminalhealth", "customerdata", "staffclock"
            }
        };

    public string ResolveDashboardRoute(UserRole? role)
    {
        return role switch
        {
            UserRole.User => "userdashboard",
            UserRole.Manager => "managerdashboard",
            UserRole.Admin => "dashboard",
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

        if (normalized.Equals("restaurant", StringComparison.OrdinalIgnoreCase))
        {
            return "visuallayout";
        }

        return normalized;
    }

    public bool CanAccessFeature(UserRole? role, string route)
    {
        var resolvedRoute = ResolveRouteForRole(role, route);
        return CanAccessRoute(role, resolvedRoute);
    }

    public bool CanOpenCashDrawer(UserRole? role) =>
        role is UserRole.User or UserRole.Manager or UserRole.Admin;

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

    public bool CanViewZReport(UserRole? role) => role == UserRole.Admin;

    public bool CanPrintZReport(UserRole? role) => role == UserRole.Admin;

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
