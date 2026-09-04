namespace OrderWeb.Client.Models;

public sealed record LoginRequest(string Method, string? Pin);

public sealed record LoginSession(
    string UserId,
    string UserName,
    string Role,
    IReadOnlyList<string> Permissions,
    string SessionToken,
    DateTimeOffset ExpiresAtUtc)
{
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
