using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Services;

/// <summary>Client-side gateway for Cashier data. It has no database authority.</summary>
public sealed class MotherCashierClient
{
    private readonly ClientCacheService _cache;
    private CashierDashboardSummary? _lastSummary;

    public MotherCashierClient(ClientCacheService cache) => _cache = cache;

    public async Task<CashierDashboardResult> GetDashboardAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null || session is null || string.IsNullOrWhiteSpace(session.SessionToken))
            return CashierDashboardResult.Unavailable("Sign in to Mother POS to view live Cashier data.", _lastSummary);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            using var response = await client.GetAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/cashier/dashboard");
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return CashierDashboardResult.Unavailable(response.StatusCode == HttpStatusCode.Forbidden ? "Cashier permission was denied by Mother POS." : "Mother POS did not provide the dashboard.", _lastSummary);

            var envelope = JsonSerializer.Deserialize<CashierDashboardEnvelope>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (envelope is null || !envelope.Success) return CashierDashboardResult.Unavailable("Mother POS returned an invalid dashboard response.", _lastSummary);
            _lastSummary = new CashierDashboardSummary(
                envelope.BusinessDate,
                envelope.TotalOrders,
                envelope.TotalSales,
                envelope.CashTotal,
                envelope.CardTotal,
                envelope.OtherPaymentTotal,
                envelope.VoidCount,
                envelope.VoidAmount,
                envelope.DiscountTotal,
                envelope.ExpectedCash,
                envelope.CountedCash,
                envelope.Variance,
                envelope.TerminalName,
                envelope.GeneratedUtc,
                envelope.Version,
                envelope.DateLine,
                envelope.UpdatedText,
                envelope.Gross,
                envelope.Net,
                envelope.Vat,
                envelope.Tips,
                envelope.PosSales,
                envelope.OnlineSales,
                envelope.PettyCashOut,
                envelope.WebOrders,
                envelope.VsYesterday,
                envelope.ExpectedCashDisplay,
                envelope.CashDisplay,
                envelope.CardDisplay);
            return new CashierDashboardResult(true, false, string.Empty, _lastSummary);
        }
        catch
        {
            return CashierDashboardResult.Unavailable("Mother POS is unavailable. Displayed data may be stale.", _lastSummary);
        }
    }

    public async Task<(bool Success, string Message, CashierZReportPreview? Preview)> GetZReportPreviewAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null || session is null) return (false, "Sign in to Mother POS first.", null);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            using var response = await client.GetAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/cashier/z-report/preview");
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return (false, "Mother POS could not provide the Z Report preview.", null);
            var preview = JsonSerializer.Deserialize<CashierZReportPreview>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return preview is { Success: true } ? (true, string.Empty, preview) : (false, "Mother returned an invalid Z Report preview.", null);
        }
        catch { return (false, "Mother POS is unavailable. Z Report preview requires a live connection.", null); }
    }

    public Task<CashierActionResult> PrintZReportAsync() => SendActionAsync("/api/client/cashier/z-report/print");
    public Task<CashierActionResult> OpenCashDrawerAsync(string? reason, decimal? amount = null, string? details = null) => SendActionAsync("/api/client/cashier/cash-drawer/open", reason, amount, details);

    public async Task<IReadOnlyList<CashDrawerPendingTrip>> GetPendingShoppingAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null || session is null)
        {
            return Array.Empty<CashDrawerPendingTrip>();
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            using var response = await client.PostAsJsonAsync(
                $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/cashier/cash-drawer/pending-shopping",
                new { requestId = Guid.NewGuid().ToString("N"), sessionToken = session.SessionToken });
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<CashDrawerPendingTrip>();
            }

            var envelope = await response.Content.ReadFromJsonAsync<PendingShoppingEnvelope>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return envelope?.Trips?.Select(trip => new CashDrawerPendingTrip(
                trip.Id,
                trip.PickerLabel ?? $"{trip.Description} · £{trip.AmountTaken:F2} out",
                trip.Summary ?? $"{trip.Description} · £{trip.AmountTaken:F2} taken · by {trip.RecordedByName}",
                trip.AmountTaken,
                trip.Description ?? "Shopping")).ToList()
                ?? (IReadOnlyList<CashDrawerPendingTrip>)Array.Empty<CashDrawerPendingTrip>();
        }
        catch
        {
            return Array.Empty<CashDrawerPendingTrip>();
        }
    }

    public async Task<CashierActionResult> SettleShoppingAsync(int tillExpenseId, decimal amountSpent)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null || session is null)
        {
            return new(false, "Sign in to Mother POS first.", null, null);
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            using var response = await client.PostAsJsonAsync(
                $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/cashier/cash-drawer/settle-shopping",
                new
                {
                    requestId = Guid.NewGuid().ToString("N"),
                    sessionToken = session.SessionToken,
                    tillExpenseId,
                    amountSpent
                });
            var result = await response.Content.ReadFromJsonAsync<CashierActionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result ?? new(false, "Mother returned an invalid action response.", null, null);
        }
        catch
        {
            return new(false, "Mother POS is unavailable. This action requires a live connection.", null, null);
        }
    }

    private async Task<CashierActionResult> SendActionAsync(string path, string? reason = null, decimal? amount = null, string? details = null)
    {
        var settings = await _cache.GetMotherConnectionAsync(); var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null || session is null) return new(false, "Sign in to Mother POS first.", null, null);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId); client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken); client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            using var response = await client.PostAsJsonAsync($"{settings.ApiBaseUrl.TrimEnd('/')}{path}", new { requestId = Guid.NewGuid().ToString("N"), sessionToken = session.SessionToken, reason, amount, details });
            var result = await response.Content.ReadFromJsonAsync<CashierActionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result ?? new(false, "Mother returned an invalid action response.", null, null);
        }
        catch { return new(false, "Mother POS is unavailable. This action requires a live connection.", null, null); }
    }

    private sealed record CashierDashboardEnvelope(
        bool Success,
        DateTime BusinessDate,
        int TotalOrders,
        decimal TotalSales,
        decimal CashTotal,
        decimal CardTotal,
        decimal OtherPaymentTotal,
        int VoidCount,
        decimal VoidAmount,
        decimal DiscountTotal,
        decimal ExpectedCash,
        decimal? CountedCash,
        decimal? Variance,
        string TerminalName,
        DateTimeOffset GeneratedUtc,
        string Version,
        string DateLine = "",
        string UpdatedText = "",
        string Gross = "",
        string Net = "",
        string Vat = "",
        string Tips = "",
        string PosSales = "",
        string OnlineSales = "",
        string PettyCashOut = "",
        string WebOrders = "",
        string VsYesterday = "",
        string ExpectedCashDisplay = "",
        string CashDisplay = "",
        string CardDisplay = "");
    private sealed record PendingShoppingEnvelope(bool Success, List<PendingShoppingTrip>? Trips);
    private sealed record PendingShoppingTrip(int Id, string? Description, decimal AmountTaken, string? RecordedByName, string? PickerLabel, string? Summary);
}

public sealed record CashierDashboardSummary(
    DateTime BusinessDate,
    int TotalOrders,
    decimal TotalSales,
    decimal CashTotal,
    decimal CardTotal,
    decimal OtherPaymentTotal,
    int VoidCount,
    decimal VoidAmount,
    decimal DiscountTotal,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? Variance,
    string TerminalName,
    DateTimeOffset GeneratedUtc,
    string Version,
    string DateLine = "",
    string UpdatedText = "",
    string Gross = "",
    string Net = "",
    string Vat = "",
    string Tips = "",
    string PosSales = "",
    string OnlineSales = "",
    string PettyCashOut = "",
    string WebOrders = "",
    string VsYesterday = "",
    string ExpectedCashDisplay = "",
    string CashDisplay = "",
    string CardDisplay = "");
public sealed record CashierDashboardResult(bool Success, bool IsStale, string Message, CashierDashboardSummary? Summary)
{
    public static CashierDashboardResult Unavailable(string message, CashierDashboardSummary? cached) => new(false, cached is not null, message, cached);
}
public sealed record CashierZReportPreview(bool Success, bool IsPreview, DateTime BusinessDate, string TerminalName, DateTimeOffset GeneratedUtc, int TotalOrders, decimal GrossSales, decimal CashTotal, decimal CardTotal, decimal OtherPaymentTotal, int VoidCount, decimal DiscountTotal, decimal ExpectedCash, decimal? CountedCash, decimal? Variance, string ReportReference);
public sealed record CashierActionResult(bool Success, string Message, string? PrinterName, string? ReportReference);
