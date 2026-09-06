namespace OrderWeb.Contracts.Dtos;

public sealed record AuthenticationRequest(string Method, string? Pin, string? Credential = null);

public sealed record AuthenticatedUser(
    string UserId,
    string DisplayName,
    string Role,
    IReadOnlySet<string> Permissions);

public sealed record UserSession(
    string SessionId,
    AuthenticatedUser User,
    DateTimeOffset ExpiresAtUtc,
    bool IsAuthoritative);
