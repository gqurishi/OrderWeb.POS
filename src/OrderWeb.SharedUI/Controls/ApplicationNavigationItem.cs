namespace OrderWeb.SharedUI.Controls;

public sealed class ApplicationNavigationItem
{
    public ApplicationNavigationItem(string route, string title, string iconSource, params string[] allowedRoles)
    {
        Route = route;
        Title = title;
        IconSource = iconSource;
        AllowedRoles = allowedRoles ?? [];
        RequiredCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RequiredFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public string Route { get; }
    public string Title { get; }
    public string IconSource { get; }
    public IReadOnlyList<string> AllowedRoles { get; }
    public IReadOnlySet<string> RequiredCapabilities { get; init; }
    public IReadOnlySet<string> RequiredFeatures { get; init; }
    public bool IsEnabled { get; init; } = true;

    public bool IsAllowedFor(string? role) =>
        AllowedRoles.Count == 0 || AllowedRoles.Any(value => string.Equals(value, role, StringComparison.OrdinalIgnoreCase));

    public bool IsAllowed(string? role, IReadOnlySet<string>? capabilities, IReadOnlySet<string>? features = null)
    {
        if (RequiredCapabilities.Count > 0)
        {
            if (capabilities is null || RequiredCapabilities.Any(key => !capabilities.Contains(key)))
            {
                return false;
            }
        }
        else if (!IsAllowedFor(role))
        {
            return false;
        }

        if (RequiredFeatures.Count > 0 && features is { Count: > 0 } &&
            !RequiredFeatures.Any(features.Contains))
        {
            return false;
        }

        return true;
    }
}

public sealed class NavigationRequestedEventArgs(ApplicationNavigationItem item) : EventArgs
{
    public ApplicationNavigationItem Item { get; } = item;
    public string Route => Item.Route;
}
