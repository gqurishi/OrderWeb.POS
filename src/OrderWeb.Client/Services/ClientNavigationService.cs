using OrderWeb.Client.Models;
using OrderWeb.Contracts.Navigation;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.Client.Services;

/// <summary>
/// Builds Client sidebar items from <see cref="PosNavigationCatalog"/> using
/// Mother-issued capabilities and connection state.
/// </summary>
public static class ClientNavigationService
{
    public static IReadOnlyList<ApplicationNavigationItem> BuildMenuItems(
        LoginSession? session,
        bool isMotherConnected,
        IReadOnlyDictionary<string, bool>? featureFlags = null) =>
        BuildMenuItems(
            session?.Role,
            session?.Permissions,
            isMotherConnected,
            featureFlags);

    public static IReadOnlyList<ApplicationNavigationItem> BuildMenuItems(
        string? role,
        IReadOnlyCollection<string>? permissions,
        bool isMotherConnected,
        IReadOnlyDictionary<string, bool>? featureFlags = null)
    {
        var capabilities = ClientCapabilityProjector.EffectiveCapabilities(role, permissions);
        var resolved = PosNavigationCatalog.Resolve(
            new PosNavigationContext(
                Role: role,
                Capabilities: capabilities,
                IsMotherConnected: isMotherConnected,
                FeatureFlags: featureFlags),
            forClient: true);

        return resolved
            .Select(item => new ApplicationNavigationItem(
                item.Route,
                item.Title,
                item.IconSource)
            {
                IsEnabled = item.IsEnabled
            })
            .ToList();
    }

    public static bool IsMotherConnected(string? connectionStatus) =>
        !string.Equals(connectionStatus, "Mother Offline", StringComparison.OrdinalIgnoreCase);
}
