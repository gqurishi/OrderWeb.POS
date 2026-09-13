namespace POS_in_NET.Models;

public sealed record ToshibaNetworkTimeouts(
    TimeSpan ConnectionTimeout,
    TimeSpan DataSendTimeout,
    TimeSpan StatusResponseTimeout,
    TimeSpan CompleteJobTimeout)
{
    public static ToshibaNetworkTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));

    public ToshibaNetworkTimeouts Validate()
    {
        if (ConnectionTimeout <= TimeSpan.Zero || DataSendTimeout <= TimeSpan.Zero
            || StatusResponseTimeout <= TimeSpan.Zero || CompleteJobTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ToshibaNetworkTimeouts), "All network timeouts must be positive.");
        if (CompleteJobTimeout < ConnectionTimeout || CompleteJobTimeout < DataSendTimeout)
            throw new ArgumentException("The complete-job timeout must cover each individual network stage.");
        return this;
    }
}

public enum ToshibaNetworkPhase { EndpointQueue, Connect, Send, Status, Complete }

public sealed record ToshibaNetworkResult(
    string Endpoint,
    bool NetworkReachable,
    bool PortReachable,
    bool ExpectedProtocolConfirmed,
    bool DataAccepted,
    bool PhysicalLabelConfirmed,
    byte[]? PrinterStatusResponse,
    ToshibaNetworkPhase Phase,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt)
{
    public static ToshibaNetworkResult Failed(string endpoint, ToshibaNetworkPhase phase, DateTimeOffset started, string error, bool network = false, bool port = false) =>
        new(endpoint, network, port, false, false, false, null, phase, error, started, DateTimeOffset.UtcNow);

    public static ToshibaNetworkResult DataSent(string endpoint, DateTimeOffset started, string? warning = null, byte[]? response = null) =>
        new(endpoint, true, true, false, true, false, response, ToshibaNetworkPhase.Status, warning, started, DateTimeOffset.UtcNow);

    public static ToshibaNetworkResult ProtocolConfirmed(string endpoint, DateTimeOffset started, byte[] status) =>
        new(endpoint, true, true, true, true, false, status, ToshibaNetworkPhase.Complete, null, started, DateTimeOffset.UtcNow);

    public ToshibaNetworkResult ConfirmPhysicalLabel() => this with { PhysicalLabelConfirmed = true };
}
