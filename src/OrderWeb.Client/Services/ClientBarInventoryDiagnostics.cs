using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>Last Bar Inventory op for Sync diagnostics (Client → Mother only).</summary>
public static class ClientBarInventoryDiagnostics
{
    private static readonly object Gate = new();
    private static ClientBarInventoryDiagnosticSnapshot _last = ClientBarInventoryDiagnosticSnapshot.Empty;

    public static ClientBarInventoryDiagnosticSnapshot Last
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
        string? stockId = null,
        bool fromCache = false)
    {
        var source = ClassifySource(errorCode, message, fromCache);
        var snapshot = new ClientBarInventoryDiagnosticSnapshot(
            operation,
            success,
            message ?? string.Empty,
            errorCode,
            stockId,
            source,
            fromCache,
            DateTimeOffset.Now);
        lock (Gate)
        {
            _last = snapshot;
        }
    }

    private static string ClassifySource(string? errorCode, string? message, bool fromCache)
    {
        if (fromCache)
        {
            return "Mother cache";
        }

        if (string.Equals(errorCode, BarInventoryErrorCodes.OfflineMother, StringComparison.OrdinalIgnoreCase))
        {
            return "Mother offline";
        }

        if (string.Equals(errorCode, BarInventoryErrorCodes.AccessDenied, StringComparison.OrdinalIgnoreCase))
        {
            return "Mother access";
        }

        var text = $"{errorCode} {message}".ToLowerInvariant();
        if (text.Contains("offline") || text.Contains("reach"))
        {
            return "Mother offline";
        }

        return "Mother";
    }
}

public sealed record ClientBarInventoryDiagnosticSnapshot(
    string Operation,
    bool Success,
    string Message,
    string? ErrorCode,
    string? StockId,
    string Source,
    bool FromCache,
    DateTimeOffset AtLocal)
{
    public static ClientBarInventoryDiagnosticSnapshot Empty { get; } =
        new("none", true, "No bar inventory operations yet.", null, null, "—", false, DateTimeOffset.MinValue);

    public string SummaryLine
    {
        get
        {
            if (AtLocal == DateTimeOffset.MinValue)
            {
                return "Bar Inventory: no operations yet.";
            }

            var state = Success ? (FromCache ? "CACHE" : "OK") : "FAILED";
            var code = string.IsNullOrWhiteSpace(ErrorCode) ? string.Empty : $" ({ErrorCode})";
            var stock = string.IsNullOrWhiteSpace(StockId) ? string.Empty : $" stock={StockId}";
            return $"Bar Inventory {Operation}: {state} via {Source} at {AtLocal:HH:mm:ss}{code}{stock} — {Message}";
        }
    }
}
