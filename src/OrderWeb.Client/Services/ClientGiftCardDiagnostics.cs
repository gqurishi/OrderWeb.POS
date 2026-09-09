using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>Lightweight last gift-card operation diagnostics for Sync / Gift Cards UX.</summary>
public static class ClientGiftCardDiagnostics
{
    private static readonly object Gate = new();
    private static ClientGiftCardDiagnosticSnapshot _last = ClientGiftCardDiagnosticSnapshot.Empty;

    public static ClientGiftCardDiagnosticSnapshot Last
    {
        get
        {
            lock (Gate)
            {
                return _last;
            }
        }
    }

    public static void Record(
        string operation,
        bool success,
        string? message,
        string? errorCode = null,
        bool queued = false)
    {
        var source = ClassifySource(errorCode, message);
        var snapshot = new ClientGiftCardDiagnosticSnapshot(
            operation,
            success,
            queued,
            message ?? string.Empty,
            errorCode,
            source,
            DateTimeOffset.Now);
        lock (Gate)
        {
            _last = snapshot;
        }
    }

    private static string ClassifySource(string? errorCode, string? message)
    {
        if (string.Equals(errorCode, GiftCardErrorCodes.OfflineMother, StringComparison.OrdinalIgnoreCase))
        {
            return "Mother";
        }

        if (string.Equals(errorCode, GiftCardErrorCodes.CloudDown, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, GiftCardErrorCodes.Queued, StringComparison.OrdinalIgnoreCase))
        {
            return "OrderWeb cloud";
        }

        if (string.Equals(errorCode, GiftCardErrorCodes.AccessDenied, StringComparison.OrdinalIgnoreCase))
        {
            return "Mother access";
        }

        var text = $"{errorCode} {message}".ToLowerInvariant();
        if (text.Contains("orderweb") || text.Contains("cloud"))
        {
            return "OrderWeb cloud";
        }

        if (text.Contains("mother") || text.Contains("offline") || text.Contains("reach"))
        {
            return "Mother";
        }

        return successOrUnknown(errorCode);

        static string successOrUnknown(string? code) =>
            string.IsNullOrWhiteSpace(code) ? "Mother" : "Mother / cloud";
    }
}

public sealed record ClientGiftCardDiagnosticSnapshot(
    string Operation,
    bool Success,
    bool Queued,
    string Message,
    string? ErrorCode,
    string Source,
    DateTimeOffset AtLocal)
{
    public static ClientGiftCardDiagnosticSnapshot Empty { get; } =
        new("none", true, false, "No gift-card operations yet.", null, "—", DateTimeOffset.MinValue);

    public string SummaryLine
    {
        get
        {
            if (AtLocal == DateTimeOffset.MinValue)
            {
                return "Gift card: no operations yet.";
            }

            var state = Success ? (Queued ? "QUEUED" : "OK") : "FAILED";
            var code = string.IsNullOrWhiteSpace(ErrorCode) ? string.Empty : $" ({ErrorCode})";
            return $"Gift card {Operation}: {state} via {Source} at {AtLocal:HH:mm:ss}{code} — {Message}";
        }
    }
}
