namespace OrderWeb.Contracts.Compatibility;

public enum ClientCompatibilityStatus
{
    Supported = 0,
    UpdateRecommended,
    UpdateRequired
}

public sealed record ClientCompatibilityRequest(
    string AppVersion,
    string BuildNumber,
    string Platform,
    string TerminalId,
    int PayloadVersion,
    int SchemaVersion);

public sealed record ClientCompatibilityResult(
    ClientCompatibilityStatus Status,
    string MinimumSupportedVersion,
    string? Message = null);
