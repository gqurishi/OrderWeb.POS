namespace OrderWeb.Contracts.Navigation;

/// <summary>
/// Resolves the effective Client capability set from a Mother login session.
/// Prefer explicit <c>client.*</c> grants; otherwise use role defaults.
/// </summary>
public static class ClientCapabilityProjector
{
    public static IReadOnlyList<string> EffectiveCapabilities(
        string? role,
        IReadOnlyCollection<string>? sessionPermissions)
    {
        var permissions = sessionPermissions ?? Array.Empty<string>();
        var explicitClient = permissions
            .Where(key => key.StartsWith("client.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (explicitClient.Count > 0)
        {
            return explicitClient;
        }

        return PosNavigationCatalog.DefaultClientCapabilitiesForRole(role);
    }

    /// <summary>
    /// Mother login should always emit explicit <c>client.*</c> keys so Client
    /// navigation is capability-driven rather than role-heuristic.
    /// </summary>
    public static IReadOnlyList<string> ForMotherLogin(string? role) =>
        PosNavigationCatalog.DefaultClientCapabilitiesForRole(role);

    public static IReadOnlyList<string> MergeWithRolePermissions(
        string? role,
        IReadOnlyCollection<string>? rolePermissions)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rolePermissions is not null)
        {
            foreach (var permission in rolePermissions)
            {
                if (!string.IsNullOrWhiteSpace(permission))
                {
                    merged.Add(permission.Trim());
                }
            }
        }

        foreach (var capability in ForMotherLogin(role))
        {
            merged.Add(capability);
        }

        return merged.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
