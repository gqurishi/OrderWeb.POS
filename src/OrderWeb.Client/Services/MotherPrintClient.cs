using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

/// <summary>Routes printing to Mother; Client never pretends that a document printed.</summary>
public sealed class MotherPrintClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache = new();

    public async Task<PrintRequestState> RequestPrintAsync(string printType, string? orderId, LoginSession? session, string? requestId = null)
    {
        requestId ??= Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToString("O");
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings == null || session == null || string.IsNullOrWhiteSpace(orderId))
        {
            return new PrintRequestState(requestId, printType, orderId, "failed", "Printing requires a synchronized Mother order and active session.", now, now);
        }

        var documentType = printType.Equals("bill", StringComparison.OrdinalIgnoreCase) ||
                           printType.Contains("receipt", StringComparison.OrdinalIgnoreCase)
            ? "customer_receipt"
            : printType.Equals("reprint", StringComparison.OrdinalIgnoreCase) ? "reprint" : "kitchen_ticket";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
        try
        {
            using var response = await client.PostAsJsonAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/prints", new { requestId, orderId, documentType }, JsonOptions);
            var json = await response.Content.ReadAsStringAsync();
            var envelope = JsonSerializer.Deserialize<PrintEnvelope>(json, JsonOptions);
            var print = envelope?.Print;
            return new PrintRequestState(requestId, printType, orderId,
                response.IsSuccessStatusCode && envelope?.Success == true ? print?.Status ?? "queued" : "failed",
                print?.Message ?? envelope?.Message ?? "Mother could not process the print request.", now, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (HttpRequestException)
        {
            return new PrintRequestState(requestId, printType, orderId, "unknown", "Mother POS could not be reached. Check Mother print history before retrying.", now, now);
        }
        catch (TaskCanceledException)
        {
            return new PrintRequestState(requestId, printType, orderId, "unknown", "Mother POS did not respond. Check Mother print history before retrying.", now, now);
        }
    }

    public async Task<PrintRequestState> GetPrintStatusAsync(string requestId, string? orderId, LoginSession? session)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings == null || session == null || string.IsNullOrWhiteSpace(requestId))
            return new PrintRequestState(requestId, "status", orderId, "unknown", "Mother print history requires a paired terminal and active session.", now, now);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
        try
        {
            using var response = await client.GetAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/prints/{Uri.EscapeDataString(requestId)}");
            var json = await response.Content.ReadAsStringAsync();
            var envelope = JsonSerializer.Deserialize<PrintEnvelope>(json, JsonOptions);
            return new PrintRequestState(requestId, envelope?.Print?.DocumentType ?? "status", orderId,
                envelope?.Print?.Status ?? "unknown", envelope?.Print?.Message ?? envelope?.Message ?? "Mother has no final print result yet.", now, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (HttpRequestException) { return new PrintRequestState(requestId, "status", orderId, "unknown", "Mother could not be reached to check print history.", now, now); }
        catch (TaskCanceledException) { return new PrintRequestState(requestId, "status", orderId, "unknown", "Mother did not respond to the print-history check.", now, now); }
    }

    // Kept for existing callers: only Mother may advance a print status.
    public Task<PrintRequestState> AdvanceStatusAsync(PrintRequestState request) => Task.FromResult(request);

    private sealed record PrintEnvelope(bool Success, string? Message, PrintState? Print);
    private sealed record PrintState(string? PrintJobId, string? Status, string? Message, string? DocumentType = null);
}
