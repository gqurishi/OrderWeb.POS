using OrderWeb.Contracts.Access;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client presentation uses the feature/route list Mother sent at login.
/// Nothing is hardcoded. Web Orders is never shown.
/// </summary>
public static class ClientHostAccess
{
    private static IReadOnlySet<string> _features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlySet<string> _routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dashboard" };

    public static IReadOnlySet<string> Features => _features;
    public static IReadOnlySet<string> Routes => _routes;

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
    }

    public static void ApplyFromSession(OrderWeb.Client.Models.LoginSession? session)
    {
        if (session?.Features is null)
        {
            Apply(Array.Empty<string>(), ["dashboard"]);
            return;
        }

        Apply(session.Features, session.Routes);
    }

    public static void Clear()
    {
        Apply(Array.Empty<string>(), ["dashboard"]);
    }

    public static bool CanOpenMenu(string? menuTitle)
    {
        var route = RouteForTitle(menuTitle);
        if (string.IsNullOrWhiteSpace(route) ||
            string.Equals(route, "weborders", StringComparison.OrdinalIgnoreCase) ||
            !ClientAccessPolicy.IsRouteAllowed(route))
        {
            return false;
        }

        return Routes.Contains(route);
    }

    public static string RouteForTitle(string? title) => (title ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "manager dashboard" or "dashboard" => "dashboard",
        "cash drawer" => "cashdrawer",
        "live order" => "liveorder",
        "restaurant" => "restaurant",
        "collection" => "collection",
        "delivery" => "delivery",
        "web orders" => "weborders",
        "gift cards" => "giftcards",
        "loyalty points" or "loyalty" => "loyalty",
        "reservation" or "reservations" => "reservation",
        "order history" => "orderhistory",
        "customers" or "recent customers" => "customers",
        "payment" or "payments" => "payments",
        _ => string.Empty
    };
}
