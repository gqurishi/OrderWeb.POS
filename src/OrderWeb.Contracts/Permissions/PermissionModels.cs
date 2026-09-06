namespace OrderWeb.Contracts.Permissions;

public sealed record PermissionDefinition(string Key, string DisplayName);

public sealed record PermissionGrant(string Key, bool IsAllowed);
