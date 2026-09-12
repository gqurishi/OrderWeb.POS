using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>Lightweight last loyalty operation diagnostics for Sync / Loyalty UX.</summary>
public static class ClientLoyaltyDiagnostics
{
    private static readonly object Gate = new();
    private static ClientLoyaltyDiagnosticSnapshot _last = ClientLoyaltyDiagnosticSnapshot.Empty;

    public static ClientLoyaltyDiagnosticSnapshot Last
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
        var snapshot = new ClientLoyaltyDiagnosticSnapshot(
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
        if (string.Equals(errorCode, LoyaltyErrorCodes.OfflineMother, StringComparison.OrdinalIgnoreCase))
        {
            return "Mother";
        }

        if (string.Equals(errorCode, LoyaltyErrorCodes.CloudDown, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, LoyaltyErrorCodes.Queued, StringComparison.OrdinalIgnoreCase))
        {
            return "OrderWeb cloud";
        }

        if (string.Equals(errorCode, LoyaltyErrorCodes.AccessDenied, StringComparison.OrdinalIgnoreCase))
        {
            return "Mother access";
        }

        if (string.Equals(errorCode, LoyaltyErrorCodes.AlreadyEarned, StringComparison.OrdinalIgnoreCase))
        {
            return "Mother";
        }

        var text = $"{errorCode} {message}".ToLowerInvariant();
        if (text.Contains("orderweb") || text.Contains("cloud") || text.Contains("queued"))
        {
            return "OrderWeb cloud";
        }

        if (text.Contains("mother") || text.Contains("offline") || text.Contains("reach"))
        {
            return "Mother";
        }

        return string.IsNullOrWhiteSpace(errorCode) ? "Mother" : "Mother / cloud";
    }
}

public sealed record ClientLoyaltyDiagnosticSnapshot(
    string Operation,
    bool Success,
    bool Queued,
    string Message,
    string? ErrorCode,
    string Source,
    DateTimeOffset AtLocal)
{
    public static ClientLoyaltyDiagnosticSnapshot Empty { get; } =
        new("none", true, false, "No loyalty operations yet.", null, "—", DateTimeOffset.MinValue);

    public string SummaryLine
    {
        get
        {
            if (AtLocal == DateTimeOffset.MinValue)
            {
                return "Loyalty: no operations yet.";
            }

            var state = Success ? (Queued ? "QUEUED" : "OK") : (Queued ? "QUEUED" : "FAILED");
            var code = string.IsNullOrWhiteSpace(ErrorCode) ? string.Empty : $" ({ErrorCode})";
            return $"Loyalty {Operation}: {state} via {Source} at {AtLocal:HH:mm:ss}{code} — {Message}";
        }
    }
}
