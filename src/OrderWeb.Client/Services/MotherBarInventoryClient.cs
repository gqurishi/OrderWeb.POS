using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>Client → Mother Bar Inventory. Online-only mutate; Mother owns stock ledger.</summary>
public sealed class MotherBarInventoryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;
    private readonly ClientOfflinePolicy _offlinePolicy;

    public MotherBarInventoryClient()
        : this(new ClientCacheService(), new ClientOfflinePolicy())
    {
    }

    public MotherBarInventoryClient(ClientCacheService cache, ClientOfflinePolicy offlinePolicy)
    {
        _cache = cache;
        _offlinePolicy = offlinePolicy;
    }

    public async Task<BarStockBoardResponseDto> ListBoardAsync(CancellationToken cancellationToken = default)
    {
        var offlineMessage = await EnsureOnlineAsync();
        if (offlineMessage is not null)
        {
            var cached = await TryLoadCachedBoardAsync();
            if (cached is { Success: true, Items.Count: > 0 })
            {
                cached.FromCache = true;
                cached.Message = "Mother offline · showing last saved stock";
                ClientBarInventoryDiagnostics.Record(
                    "board",
                    true,
                    cached.Message,
                    BarInventoryErrorCodes.OfflineMother,
                    fromCache: true);
                return cached;
            }

            ClientBarInventoryDiagnostics.Record(
                "board",
                false,
                offlineMessage,
                BarInventoryErrorCodes.OfflineMother);
            return FailBoard(offlineMessage, BarInventoryErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            var msg = "Sign in and pair with Mother POS before viewing Bar Inventory.";
            ClientBarInventoryDiagnostics.Record("board", false, msg, BarInventoryErrorCodes.AccessDenied);
            return FailBoard(msg, BarInventoryErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            var url = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/bar-inventory";
            using var response = await client.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<BarStockBoardResponseDto>(json, JsonOptions);
            if (dto is not null)
            {
                dto.Success = response.IsSuccessStatusCode && dto.Success;
                dto.Items ??= Array.Empty<BarStockBoardRowDto>();
                if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(dto.ErrorCode))
                {
                    dto.ErrorCode = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                        ? BarInventoryErrorCodes.AccessDenied
                        : BarInventoryErrorCodes.Unknown;
                }

                if (dto.Success)
                {
                    await SaveCachedBoardAsync(dto);
                    ClientBarInventoryDiagnostics.Record(
                        "board",
                        true,
                        $"Loaded {dto.Items.Count} stock row(s).",
                        null);
                }
                else
                {
                    ClientBarInventoryDiagnostics.Record(
                        "board",
                        false,
                        dto.Message ?? "Board load failed.",
                        dto.ErrorCode);
                }

                return dto;
            }

            var fail = FailBoard($"Mother Bar Inventory failed ({(int)response.StatusCode}).", BarInventoryErrorCodes.Unknown);
            ClientBarInventoryDiagnostics.Record("board", false, fail.Message, fail.ErrorCode);
            return fail;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var cached = await TryLoadCachedBoardAsync();
            if (cached is { Success: true, Items.Count: > 0 })
            {
                cached.FromCache = true;
                cached.Message = "Mother offline · showing last saved stock";
                ClientBarInventoryDiagnostics.Record(
                    "board",
                    true,
                    cached.Message,
                    BarInventoryErrorCodes.OfflineMother,
                    fromCache: true);
                return cached;
            }

            var fail = FailBoard($"Could not reach Mother POS: {ex.Message}", BarInventoryErrorCodes.OfflineMother);
            ClientBarInventoryDiagnostics.Record("board", false, fail.Message, fail.ErrorCode);
            return fail;
        }
    }

    public Task<BarStockMovementResponseDto> ReceiveAsync(
        BarStockReceiveRequestDto request,
        CancellationToken cancellationToken = default) =>
        PostMovementAsync("/api/client/bar-inventory/receive", request, cancellationToken);

    public Task<BarStockMovementResponseDto> CountAsync(
        BarStockCountRequestDto request,
        CancellationToken cancellationToken = default) =>
        PostMovementAsync("/api/client/bar-inventory/count", request, cancellationToken);

    public Task<BarStockMovementResponseDto> WasteAsync(
        BarStockWasteRequestDto request,
        CancellationToken cancellationToken = default) =>
        PostMovementAsync("/api/client/bar-inventory/waste", request, cancellationToken);

    public Task<BarStockWeeklyReportResponseDto> GetWeeklyReportAsync(CancellationToken cancellationToken = default) =>
        GetUsageReportAsync(DateTime.Today.AddDays(-6), DateTime.Today, 7, cancellationToken);

    public async Task<BarStockWeeklyReportResponseDto> GetUsageReportAsync(
        DateTime startDate,
        DateTime endDate,
        int? presetDays = null,
        CancellationToken cancellationToken = default)
    {
        var offlineMessage = await EnsureOnlineAsync();
        if (offlineMessage is not null)
        {
            return FailReport(offlineMessage, BarInventoryErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return FailReport(
                "Sign in and pair with Mother POS before viewing Bar Inventory.",
                BarInventoryErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            var qs = BuildReportQuery(startDate, endDate, presetDays);
            var url = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/bar-inventory/report{qs}";
            using var response = await client.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<BarStockWeeklyReportResponseDto>(json, JsonOptions);
            if (dto is not null)
            {
                dto.Success = response.IsSuccessStatusCode && dto.Success;
                dto.Items ??= Array.Empty<BarStockWeeklyReportRowDto>();
                return dto;
            }

            return FailReport($"Mother stock report failed ({(int)response.StatusCode}).", BarInventoryErrorCodes.Unknown);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailReport(ex.Message, BarInventoryErrorCodes.Unknown);
        }
    }

    public async Task<string?> DownloadUsageReportCsvAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var offlineMessage = await EnsureOnlineAsync();
        if (offlineMessage is not null)
        {
            ClientBarInventoryDiagnostics.Record("report_csv", false, offlineMessage, BarInventoryErrorCodes.OfflineMother);
            return null;
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            ClientBarInventoryDiagnostics.Record(
                "report_csv",
                false,
                "Sign in required.",
                BarInventoryErrorCodes.AccessDenied);
            return null;
        }

        try
        {
            using var client = CreateClient(auth);
            var qs = BuildReportQuery(startDate, endDate, null);
            var url = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/bar-inventory/report-csv{qs}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ClientBarInventoryDiagnostics.Record(
                    "report_csv",
                    false,
                    $"Mother CSV failed ({(int)response.StatusCode}).",
                    BarInventoryErrorCodes.Unknown);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var folder = Path.Combine(FileSystem.Current.CacheDirectory, "BarInventory");
            Directory.CreateDirectory(folder);
            var fileName = $"BarUsage_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}_{DateTime.Now:HHmmss}.csv";
            var path = Path.Combine(folder, fileName);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            ClientBarInventoryDiagnostics.Record("report_csv", true, Path.GetFileName(path));
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ClientBarInventoryDiagnostics.Record("report_csv", false, ex.Message, BarInventoryErrorCodes.Unknown);
            return null;
        }
    }

    private static string BuildReportQuery(DateTime startDate, DateTime endDate, int? presetDays)
    {
        if (presetDays is 7 or 15 or 30 or 365)
        {
            return $"?days={presetDays.Value}";
        }

        return $"?from={Uri.EscapeDataString(startDate.ToString("yyyy-MM-dd"))}&to={Uri.EscapeDataString(endDate.ToString("yyyy-MM-dd"))}";
    }

    /// <summary>Downloads Suggested Order PDF from Mother. Returns local file path or null.</summary>
    public async Task<string?> DownloadSuggestPdfAsync(
        string? section = null,
        CancellationToken cancellationToken = default)
    {
        var offlineMessage = await EnsureOnlineAsync();
        if (offlineMessage is not null)
        {
            ClientBarInventoryDiagnostics.Record("suggest_pdf", false, offlineMessage, BarInventoryErrorCodes.OfflineMother);
            return null;
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            ClientBarInventoryDiagnostics.Record(
                "suggest_pdf",
                false,
                "Sign in required.",
                BarInventoryErrorCodes.AccessDenied);
            return null;
        }

        try
        {
            using var client = CreateClient(auth);
            var qs = string.IsNullOrWhiteSpace(section) ? string.Empty : $"?section={Uri.EscapeDataString(section)}";
            var url = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/bar-inventory/suggest-pdf{qs}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ClientBarInventoryDiagnostics.Record(
                    "suggest_pdf",
                    false,
                    $"PDF failed ({(int)response.StatusCode}).",
                    BarInventoryErrorCodes.Unknown);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var folder = Path.Combine(FileSystem.CacheDirectory, "BarInventory");
            Directory.CreateDirectory(folder);
            var name = $"SuggestedOrder_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var path = Path.Combine(folder, name);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            ClientBarInventoryDiagnostics.Record("suggest_pdf", true, $"Saved {name}", null);
            return path;
        }
        catch (Exception ex)
        {
            ClientBarInventoryDiagnostics.Record("suggest_pdf", false, ex.Message, BarInventoryErrorCodes.Unknown);
            return null;
        }
    }

    private async Task<BarStockMovementResponseDto> PostMovementAsync<T>(
        string path,
        T body,
        CancellationToken cancellationToken)
    {
        var op = path.Contains("receive", StringComparison.OrdinalIgnoreCase)
            ? "receive"
            : path.Contains("count", StringComparison.OrdinalIgnoreCase)
                ? "count"
                : path.Contains("waste", StringComparison.OrdinalIgnoreCase)
                    ? "waste"
                    : "mutate";

        var offlineMessage = await EnsureOnlineAsync();
        if (offlineMessage is not null)
        {
            ClientBarInventoryDiagnostics.Record(op, false, offlineMessage, BarInventoryErrorCodes.OfflineMother);
            return FailMovement(offlineMessage, BarInventoryErrorCodes.OfflineMother);
        }

        var auth = await GetAuthAsync();
        if (auth is null)
        {
            var msg = "Sign in and pair with Mother POS before changing stock.";
            ClientBarInventoryDiagnostics.Record(op, false, msg, BarInventoryErrorCodes.AccessDenied);
            return FailMovement(msg, BarInventoryErrorCodes.AccessDenied);
        }

        try
        {
            using var client = CreateClient(auth);
            var url = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}{path}";
            using var content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");
            using var response = await client.PostAsync(url, content, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<BarStockMovementResponseDto>(json, JsonOptions);
            if (dto is not null)
            {
                dto.Success = response.IsSuccessStatusCode && dto.Success;
                if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(dto.ErrorCode))
                {
                    dto.ErrorCode = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                        ? BarInventoryErrorCodes.AccessDenied
                        : BarInventoryErrorCodes.Unknown;
                }

                ClientBarInventoryDiagnostics.Record(
                    op,
                    dto.Success,
                    dto.Message ?? (dto.Success ? "Stock updated." : "Stock update failed."),
                    dto.ErrorCode,
                    dto.Stock?.Id ?? dto.BoardRow?.StockId);
                return dto;
            }

            var fail = FailMovement($"Mother stock update failed ({(int)response.StatusCode}).", BarInventoryErrorCodes.Unknown);
            ClientBarInventoryDiagnostics.Record(op, false, fail.Message, fail.ErrorCode);
            return fail;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var fail = FailMovement($"Could not reach Mother POS: {ex.Message}", BarInventoryErrorCodes.OfflineMother);
            ClientBarInventoryDiagnostics.Record(op, false, fail.Message, fail.ErrorCode);
            return fail;
        }
    }

    private static string BoardCachePath =>
        Path.Combine(FileSystem.AppDataDirectory, "bar_inventory_board_last.json");

    private async Task SaveCachedBoardAsync(BarStockBoardResponseDto dto)
    {
        try
        {
            var copy = new BarStockBoardResponseDto
            {
                Success = true,
                Items = dto.Items ?? Array.Empty<BarStockBoardRowDto>(),
                AdvisoryNote = dto.AdvisoryNote,
                FromCache = false
            };
            var json = JsonSerializer.Serialize(copy, JsonOptions);
            await File.WriteAllTextAsync(BoardCachePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarInventory] Cache save skipped: {ex.Message}");
        }
    }

    private async Task<BarStockBoardResponseDto?> TryLoadCachedBoardAsync()
    {
        try
        {
            if (!File.Exists(BoardCachePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(BoardCachePath);
            var dto = JsonSerializer.Deserialize<BarStockBoardResponseDto>(json, JsonOptions);
            if (dto is null)
            {
                return null;
            }

            dto.Items ??= Array.Empty<BarStockBoardRowDto>();
            dto.Success = true;
            return dto;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> EnsureOnlineAsync()
    {
        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var decision = _offlinePolicy.Evaluate(ClientOperation.BarInventory, online);
        return decision.Allowed ? null : decision.Message;
    }

    private async Task<MotherClientAuth?> GetAuthAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null ||
            session is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken) ||
            string.IsNullOrWhiteSpace(session.SessionToken))
        {
            return null;
        }

        return new MotherClientAuth(settings, session);
    }

    private static HttpClient CreateClient(MotherClientAuth auth)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", auth.Settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", auth.Settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", auth.Session.SessionToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static BarStockBoardResponseDto FailBoard(string message, string code) =>
        new()
        {
            Success = false,
            Message = message,
            ErrorCode = code,
            Items = Array.Empty<BarStockBoardRowDto>()
        };

    private static BarStockMovementResponseDto FailMovement(string message, string code) =>
        new()
        {
            Success = false,
            Message = message,
            ErrorCode = code
        };

    private static BarStockWeeklyReportResponseDto FailReport(string message, string code) =>
        new()
        {
            Success = false,
            Message = message,
            ErrorCode = code,
            Items = Array.Empty<BarStockWeeklyReportRowDto>()
        };

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession Session);
}
