using OrderWeb.Contracts.Navigation;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Emits Mother-controlled <c>client.*</c> capabilities on Client login so
/// Client navigation uses the shared catalog instead of local role menus.
/// </summary>
public static class ClientCapabilityGrants
{
    public static IReadOnlyList<string> Build(
        UserRole role,
        IReadOnlyCollection<string> rolePermissions) =>
        ClientCapabilityProjector.MergeWithRolePermissions(role.ToString(), rolePermissions);
}
