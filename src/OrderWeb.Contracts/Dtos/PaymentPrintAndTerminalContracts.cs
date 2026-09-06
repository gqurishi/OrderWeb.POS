namespace OrderWeb.Contracts.Dtos;

public sealed record PaymentRequest(
    string RequestId,
    string TerminalId,
    string SessionId,
    string OrderId,
    string Method,
    decimal Amount,
    DateTimeOffset RequestedAtUtc,
    long? ExpectedOrderRevision = null,
    string? CorrelationId = null);

public sealed record PaymentResultDto(
    string PaymentId,
    string OrderId,
    string Status,
    decimal ConfirmedAmount,
    string? ProviderReference = null);

public sealed record PrintRequest(
    string RequestId,
    string TerminalId,
    string SessionId,
    string DocumentType,
    string EntityId,
    int Copies = 1);

public sealed record PrintResultDto(string PrintJobId, string Status, string? Message = null);

public sealed record TerminalIdentityDto(
    string TerminalId,
    string TerminalName,
    string RestaurantId,
    string MotherId,
    string Platform,
    string AppVersion,
    bool IsPaired);
