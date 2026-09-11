using OrderWeb.Contracts.Access;
using OrderWeb.Contracts.Features;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client presentation uses the feature/route list Mother sent at login.
/// Sidebars mirror Mother User / Manager (including Rider label). Rider board stays on Mother.
/// </summary>
public static class ClientHostAccess
{
    private static IReadOnlySet<string> _features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlySet<string> _routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dashboard" };
    private static string? _sessionRole;

    /// <summary>Mother User sidebar routes (Dashboard + service surface).</summary>
    private static readonly HashSet<string> MotherUserHostRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "dashboard", "restaurant", "collection", "delivery", "liveorder", "reservation"
    };

    /// <summary>Mother Manager sidebar routes (includes Rider / weborders for menu parity).</summary>
    private static readonly HashSet<string> MotherManagerHostRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "dashboard", "cashdrawer", "restaurant", "collection", "delivery", "weborders", "liveorder",
        "reservation", "orderhistory", "giftcards", "loyalty", "customerdata"
    };

    public static IReadOnlySet<string> Features => _features;
    public static IReadOnlySet<string> Routes => _routes;
    public static string? SessionRole => _sessionRole;

    /// <summary>
    /// Keeps the Client User/Manager sidebars identical to Mother POS.
    /// Intersected with Mother-granted terminal routes.
    /// </summary>
    public static IReadOnlySet<string> RoutesForRole(string? role)
    {
        if (string.Equals(role, "User", StringComparison.OrdinalIgnoreCase))
        {
            var userRoutes = new HashSet<string>(MotherUserHostRoutes, StringComparer.OrdinalIgnoreCase);
            userRoutes.IntersectWith(Routes);
            return WithReservationRoute(userRoutes);
        }

        if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            var managerRoutes = new HashSet<string>(MotherManagerHostRoutes, StringComparer.OrdinalIgnoreCase);
            managerRoutes.IntersectWith(Routes);
            return WithRiderRoute(WithReservationRoute(managerRoutes));
        }

        return Routes;
    }

    /// <summary>
    /// Features for sidebar/dashboard filtering. Mother User/Manager always
    /// expose Reservations beside Restaurant.
    /// </summary>
    public static IReadOnlySet<string> FeaturesForRole(string? role)
    {
        if (!string.Equals(role, "User", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            return Features;
        }

        return WithReservationFeature(Features);
    }

    /// <summary>
    /// Mother keeps User and Manager dashboards focused on the four primary
    /// service actions. Additional Manager tools remain in the sidebar.
    /// </summary>
    public static IReadOnlySet<string> DashboardRoutesForRole(string? role)
    {
        if (!string.Equals(role, "User", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            return RoutesForRole(role);
        }

        var dashboardRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "restaurant",
            "collection",
            "delivery",
            "reservation"
        };
        dashboardRoutes.IntersectWith(Routes);
        return WithReservationRoute(dashboardRoutes);
    }

    public static void Apply(IEnumerable<string>? features, IEnumerable<string>? routes = null)
    {
        // Fail closed: only Mother can grant Client features. Missing data is
        // not permission to use the historical defaults.
        _features = ClientAccessPolicy.FilterFeatures(features ?? Array.Empty<string>());
        _routes = routes is null
            ? ClientAccessPolicy.RoutesForFeatures(_features)
            : ClientAccessPolicy.FilterRoutes(routes);

        if (_routes.Count == 0)
        {
            _routes = ClientAccessPolicy.RoutesForFeatures(_features);
        }

        // Mother User/Manager dashboards always include Reservations. Keep the
        // same Client surface whenever Restaurant/tables is already granted
        // (covers older Mother sessions that omitted the Reservations feature).
        if (_features.Contains(PosFeatureKeys.DineIn) ||
            _routes.Contains("restaurant") ||
            _features.Contains(PosFeatureKeys.Reservations) ||
            _routes.Contains("reservation"))
        {
            _features = WithReservationFeature(_features);
            _routes = WithReservationRoute(_routes);
        }

        if (_features.Contains(PosFeatureKeys.Delivery) ||
            _routes.Contains("delivery") ||
            _routes.Contains("weborders"))
        {
            _routes = WithRiderRoute(_routes);
        }
    }

    public static void ApplyFromSession(OrderWeb.Client.Models.LoginSession? session)
    {
        _sessionRole = session?.Role;
        if (session?.Features is null)
        {
            Apply(Array.Empty<string>(), ["dashboard"]);
            return;
        }

        Apply(session.Features, session.Routes);
    }

    public static void Clear()
    {
        _sessionRole = null;
        Apply(Array.Empty<string>(), ["dashboard"]);
    }

    public static bool CanOpenMenu(string? menuTitle, string? role = null)
    {
        var route = RouteForTitle(menuTitle);
        if (string.IsNullOrWhiteSpace(route) ||
            !ClientAccessPolicy.IsRouteAllowed(route))
        {
            return false;
        }

        var effectiveRole = string.IsNullOrWhiteSpace(role) ? _sessionRole : role;
        return RoutesForRole(effectiveRole).Contains(route);
    }

    /// <summary>Rider board is Mother-only; Client may show the menu item for parity.</summary>
    public static bool IsMotherOnlyMenu(string? menuTitle) =>
        IsMenuRoute(menuTitle, "weborders");

    public static bool IsMenuRoute(string? menuTitle, string route) =>
        string.Equals(RouteForTitle(menuTitle), route, StringComparison.OrdinalIgnoreCase);

    public static string RouteForTitle(string? title) => (title ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "manager dashboard" or "dashboard" => "dashboard",
        "cash drawer" => "cashdrawer",
        "live order" or "live orders" => "liveorder",
        "restaurant" => "restaurant",
        "collection" => "collection",
        "delivery" => "delivery",
        "web orders" or "rider" => "weborders",
        "gift cards" => "giftcards",
        "loyalty points" or "loyalty" => "loyalty",
        "reservation" or "reservations" => "reservation",
        "order history" => "orderhistory",
        "recent customers" => "customerdata",
        "customers" => "customers",
        "payment" or "payments" => "payments",
        _ => string.Empty
    };

    private static IReadOnlySet<string> WithReservationFeature(IReadOnlySet<string> features)
    {
        if (features.Contains(PosFeatureKeys.Reservations))
        {
            return features;
        }

        var next = new HashSet<string>(features, StringComparer.OrdinalIgnoreCase)
        {
            PosFeatureKeys.Reservations
        };
        return ClientAccessPolicy.FilterFeatures(next);
    }

    private static IReadOnlySet<string> WithReservationRoute(IReadOnlySet<string> routes)
    {
        // Only expose Reservation when Restaurant/tables (or Reservations) is in play.
        var restaurantSurface =
            _features.Contains(PosFeatureKeys.DineIn) ||
            _features.Contains(PosFeatureKeys.Reservations) ||
            _routes.Contains("restaurant") ||
            _routes.Contains("reservation") ||
            routes.Contains("restaurant") ||
            routes.Contains("reservation");

        if (!restaurantSurface)
        {
            return routes;
        }

        if (routes.Contains("reservation"))
        {
            return routes;
        }

        var next = new HashSet<string>(routes, StringComparer.OrdinalIgnoreCase) { "reservation" };
        return ClientAccessPolicy.FilterRoutes(next);
    }

    private static IReadOnlySet<string> WithRiderRoute(IReadOnlySet<string> routes)
    {
        // Mother Manager shows Rider under Delivery. Mirror that label when Delivery is granted.
        var deliverySurface =
            _features.Contains(PosFeatureKeys.Delivery) ||
            _routes.Contains("delivery") ||
            _routes.Contains("weborders") ||
            routes.Contains("delivery") ||
            routes.Contains("weborders");

        if (!deliverySurface)
        {
            return routes;
        }

        if (routes.Contains("weborders"))
        {
            return routes;
        }

        var next = new HashSet<string>(routes, StringComparer.OrdinalIgnoreCase) { "weborders" };
        return ClientAccessPolicy.FilterRoutes(next);
    }
}
