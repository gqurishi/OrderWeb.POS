namespace OrderWeb.Contracts.Dtos;

public sealed record ConfigurationSectionDto(
    string Section,
    string Version,
    IReadOnlyDictionary<string, string?> Values);

public enum ConnectionState
{
    Unknown,
    Connecting,
    Online,
    Synchronizing,
    Offline,
    ReconnectRequired
}

public sealed record ConnectionStatusDto(ConnectionState State, string? Message, DateTimeOffset ChangedAtUtc);

public enum DialogKind
{
    Information,
    Confirmation,
    Warning,
    Error,
    ManagerApproval
}

public sealed record DialogRequest(DialogKind Kind, string Title, string Message, string? ConfirmText = null, string? CancelText = null);

public sealed record DialogResultDto(bool Confirmed, string? Value = null);
