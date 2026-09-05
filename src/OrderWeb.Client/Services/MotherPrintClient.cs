using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Printing;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client → Mother print API. Status values come only from Mother responses.
/// Never invents Printed/Failed locally.
/// </summary>
public sealed class MotherPrintClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    private readonly AuthoritativePrintService? _localFallback;

    public MotherPrintClient()
        : this(new ClientCacheService())
    {
    }

    public MotherPrintClient(ClientCacheService cache, AuthoritativePrintService? localFallback = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _localFallback = localFallback;
    }

    public async Task<PrintRequestState> RequestPrintAsync(
        string printType,
        string? orderId,
        LoginSession? session,
        string? requestId = null,
        bool isReprint = false)
    {
        var kind = MapKind(printType, isReprint);
        var id = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId.Trim();
        var settings = await _cache.GetMotherConnectionAsync();
        var terminalId = settings?.TerminalId ?? "unpaired-terminal";
        var sessionId = session?.SessionToken ?? session?.UserId ?? "anonymous-session";

        var request = new PrintRequestDto(
            id,
            terminalId,
            sessionId,
            kind,
            orderId,
            Reason: isReprint ? "reprint" : null,
            IsReprint: isReprint || kind == PrintKind.Reprint,
            RequestedAtUtc: DateTimeOffset.UtcNow);

        PrintResultDto? motherResult = null;
        string? transportError = null;

        if (settings is not null &&
            !string.IsNullOrWhiteSpace(settings.ApiBaseUrl) &&
            !string.IsNullOrWhiteSpace(settings.TerminalToken))
        {
            try
            {
                motherResult = await PostToMotherAsync(settings, request);
            }
            catch (Exception ex)
            {
                transportError = ex.Message;
            }
        }

        if (motherResult is null && _localFallback is not null)
        {
            // Offline/dev fallback still goes through AuthoritativePrintService —
            // never invent Printed on the Client.
            var fallback = await _localFallback.SubmitAsync(request);
            if (fallback.IsSuccess && fallback.Value is not null)
                motherResult = fallback.Value;
            else
                transportError ??= fallback.Error?.Message ?? "Mother print authority rejected the request.";
        }

        if (motherResult is null)
        {
            return new PrintRequestState(
                id,
                FormatKind(kind),
                orderId,
                "failed",
                string.IsNullOrWhiteSpace(transportError)
                    ? "Could not reach Mother print API."
                    : $"Mother print API unreachable: {transportError}",
                DateTimeOffset.UtcNow.ToString("O"),
                DateTimeOffset.UtcNow.ToString("O"));
        }

        return ToState(motherResult);
    }

    public async Task<PrintRequestState?> GetStatusAsync(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;

        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken))
        {
            if (_localFallback is null)
                return null;
            var local = await _localFallback.GetAsync(requestId);
            return local.IsSuccess && local.Value is not null ? ToState(local.Value) : null;
        }

        try
        {
            using var client = CreateClient(settings);
            var url = $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/print/{Uri.EscapeDataString(requestId)}";
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;
            var payload = await response.Content.ReadFromJsonAsync<MotherPrintHttpResponse>(JsonOptions);
            return payload is null ? null : ToState(payload);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deprecated: Client must not invent status transitions. Kept as no-op identity
    /// so legacy callers that still invoke it do not fabricate Printed.
    /// </summary>
    [Obsolete("Do not advance print status on Client. Use Mother responses only.")]
    public Task<PrintRequestState> AdvanceStatusAsync(PrintRequestState request) =>
        Task.FromResult(request);

    /// <summary>
    /// Maps a cached Mother print row into SharedUI <see cref="PrintResultDto"/> presentation.
    /// </summary>
    public static PrintResultDto ToResultDto(PrintRequestState state)
    {
        var kind = MapKind(state.PrintType, isReprint: state.PrintType.Contains("reprint", StringComparison.OrdinalIgnoreCase));
        var status = ParseStatus(state.Status);
        IReadOnlyList<PrintRouteFailureDto>? failures = null;
        if (!string.IsNullOrWhiteSpace(state.FailedRoutesJson))
        {
            try
            {
                failures = JsonSerializer.Deserialize<List<PrintRouteFailureDto>>(state.FailedRoutesJson, JsonOptions);
            }
            catch
            {
                failures = null;
            }
        }

        return new PrintResultDto(
            state.Id,
            state.AuditId ?? string.Empty,
            kind,
            status,
            state.Message,
            state.OrderId,
            DateTimeOffset.TryParse(state.UpdatedUtc, out var updated) ? updated : DateTimeOffset.UtcNow,
            IsTerminal: status is PrintStatus.Printed or PrintStatus.Failed or PrintStatus.PartialFailure or PrintStatus.PrinterUnavailable,
            string.IsNullOrWhiteSpace(state.JobIds) ? null : state.JobIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            failures);
    }

    private async Task<PrintResultDto?> PostToMotherAsync(MotherConnectionSettings settings, PrintRequestDto request)
    {
        using var client = CreateClient(settings);
        var url = $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/print";
        var body = new
        {
            requestId = request.RequestId,
            kind = request.Kind.ToString(),
            printType = FormatKind(request.Kind),
            orderId = request.OrderId,
            sessionToken = request.SessionId,
            reason = request.Reason,
            isReprint = request.IsReprint
        };
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            // Surface Mother validation/rejection as failed — never invent Printed.
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var message = doc.RootElement.TryGetProperty("message", out var msg)
                        ? msg.GetString()
                        : null;
                    return new PrintResultDto(
                        request.RequestId,
                        string.Empty,
                        request.Kind,
                        PrintStatus.Failed,
                        message ?? $"Mother rejected print ({(int)response.StatusCode}).",
                        request.OrderId,
                        DateTimeOffset.UtcNow,
                        IsTerminal: true);
                }
                catch
                {
                    // fall through to throw
                }
            }

            throw new InvalidOperationException(
                response.StatusCode == HttpStatusCode.NotFound
                    ? "Mother print route was not found. Update Mother POS."
                    : $"Mother print failed with {(int)response.StatusCode}: {json}");
        }

        var payload = JsonSerializer.Deserialize<MotherPrintHttpResponse>(json, JsonOptions);
        return payload is null ? null : ToDto(payload);
    }

    private static HttpClient CreateClient(MotherConnectionSettings settings)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
        return client;
    }

    private static PrintKind MapKind(string printType, bool isReprint)
    {
        if (isReprint)
            return PrintKind.Reprint;

        var normalized = (printType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "kitchen ticket" or "kitchen" or "kitchen_ticket" => PrintKind.KitchenTicket,
            "bill" or "receipt" or "customer receipt" or "customer_receipt" => PrintKind.CustomerReceipt,
            "reprint" => PrintKind.Reprint,
            "cash drawer open" or "cash drawer" or "cash_drawer" => PrintKind.CashDrawer,
            _ => PrintKind.CustomerReceipt
        };
    }

    private static string FormatKind(PrintKind kind) => kind switch
    {
        PrintKind.KitchenTicket => "kitchen ticket",
        PrintKind.CustomerReceipt => "bill",
        PrintKind.Reprint => "reprint",
        PrintKind.CashDrawer => "cash drawer open",
        _ => kind.ToString()
    };

    private static PrintStatus ParseStatus(string? status)
    {
        if (Enum.TryParse<PrintStatus>(status, ignoreCase: true, out var parsed) && parsed != 0)
            return parsed;

        return (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "queued" => PrintStatus.Queued,
            "printing" => PrintStatus.Printing,
            "printed" => PrintStatus.Printed,
            "partial failure" or "partial_failure" => PrintStatus.PartialFailure,
            "failed" => PrintStatus.Failed,
            "printer unavailable" or "printer_unavailable" or "printer offline" => PrintStatus.PrinterUnavailable,
            _ => PrintStatus.Failed
        };
    }

    private static PrintRequestState ToState(PrintResultDto dto) =>
        new(
            dto.RequestId,
            FormatKind(dto.Kind),
            dto.OrderId,
            dto.StatusLabel,
            dto.Message,
            dto.UpdatedAtUtc.ToString("O"),
            dto.UpdatedAtUtc.ToString("O"),
            dto.AuditId,
            dto.JobIds is null ? null : string.Join(",", dto.JobIds),
            dto.FailedRoutes is null
                ? null
                : JsonSerializer.Serialize(dto.FailedRoutes, JsonOptions));

    private static PrintRequestState ToState(MotherPrintHttpResponse payload) =>
        ToState(ToDto(payload));

    private static PrintResultDto ToDto(MotherPrintHttpResponse payload)
    {
        Enum.TryParse<PrintKind>(payload.Kind, ignoreCase: true, out var kind);
        var status = PrintStatus.Failed;
        if (Enum.TryParse<PrintStatus>(payload.Status, ignoreCase: true, out var fromEnum) && fromEnum != 0)
            status = fromEnum;
        else if (!string.IsNullOrWhiteSpace(payload.StatusLabel))
            status = ParseStatus(payload.StatusLabel);

        return new PrintResultDto(
            payload.RequestId ?? Guid.NewGuid().ToString("N"),
            payload.AuditId ?? string.Empty,
            kind == 0 ? PrintKind.CustomerReceipt : kind,
            status == 0 ? PrintStatus.Failed : status,
            payload.Message ?? string.Empty,
            payload.OrderId,
            payload.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : payload.UpdatedAtUtc,
            payload.IsTerminal,
            payload.JobIds,
            payload.FailedRoutes?.Select(f => new PrintRouteFailureDto(f.Route ?? string.Empty, f.Reason ?? string.Empty)).ToList());
    }

    private sealed record MotherPrintHttpResponse(
        [property: JsonPropertyName("requestId")] string? RequestId,
        [property: JsonPropertyName("auditId")] string? AuditId,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("statusLabel")] string? StatusLabel,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("orderId")] string? OrderId,
        [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
        [property: JsonPropertyName("isTerminal")] bool IsTerminal,
        [property: JsonPropertyName("jobIds")] IReadOnlyList<string>? JobIds,
        [property: JsonPropertyName("failedRoutes")] IReadOnlyList<MotherPrintRouteFailure>? FailedRoutes);

    private sealed record MotherPrintRouteFailure(
        [property: JsonPropertyName("route")] string? Route,
        [property: JsonPropertyName("reason")] string? Reason);
}
