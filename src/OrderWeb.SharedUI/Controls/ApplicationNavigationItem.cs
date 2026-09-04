namespace OrderWeb.SharedUI.Controls;

public sealed class ApplicationNavigationItem
{
    public ApplicationNavigationItem(string route, string title, string iconSource, params string[] allowedRoles)
    {
        Route = route;
        Title = title;
        IconSource = iconSource;
        AllowedRoles = allowedRoles ?? [];
    }

    public string Route { get; }
    public string Title { get; }
    public string IconSource { get; }
    public IReadOnlyList<string> AllowedRoles { get; }
    public bool IsEnabled { get; init; } = true;

    public bool IsAllowedFor(string? role) =>
        AllowedRoles.Count == 0 || AllowedRoles.Any(value => string.Equals(value, role, StringComparison.OrdinalIgnoreCase));
}

public sealed class NavigationRequestedEventArgs(ApplicationNavigationItem item) : EventArgs
{
    public ApplicationNavigationItem Item { get; } = item;
    public string Route => Item.Route;
}
