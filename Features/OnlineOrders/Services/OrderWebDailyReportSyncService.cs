using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Uploads in-restaurant end-of-day totals to OrderWeb.net POS API.
/// Auth and base URL match gift card redeem (Bearer token + Cloud Settings).
/// POST https://orderweb.net/api/pos/reports/daily
/// </summary>
public sealed class OrderWebDailyReportSyncService
{
    private const string PendingUploadPreferenceKey = "OrderWebPendingDailyReportDate";
    private const string DailyReportEndpoint = "/pos/reports/daily";
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly DatabaseService _databaseService;
    private readonly ZReportService _zReportService;
    private readonly TimeClockService _timeClockService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private static bool _schemaEnsured;

    private string? _apiKey;
    private string? _baseUrl;
    private string? _tenantId;
    private bool _cloudEnabled;
    private bool _initialized;

    public OrderWebDailyReportSyncService(
        DatabaseService databaseService,
        ZReportService zReportService,
        TimeClockService timeClockService)
    {
        _databaseService = databaseService;
        _zReportService = zReportService;
        _timeClockService = timeClockService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<OrderWebDailyReportSyncResult> UploadAfterZReportAsync(DateTime reportDate)
    {
        MarkPendingUpload(reportDate);
        return await UploadAsync(reportDate, trigger: "z-report", forceReupload: false);
    }

    public void MarkPendingUpload(DateTime reportDate)
    {
        Preferences.Set(PendingUploadPreferenceKey, reportDate.Date.ToString("yyyy-MM-dd"));
        System.Diagnostics.Debug.WriteLine($" [OrderWeb Report] Pending upload marked for {reportDate:yyyy-MM-dd}");
    }

    public DateTime? GetPendingUploadDate()
    {
        var value = Preferences.Get(PendingUploadPreferenceKey, string.Empty);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : null;
    }

    public void ClearPendingUpload()
    {
        Preferences.Remove(PendingUploadPreferenceKey);
    }

    public async Task<bool> IsAlreadyUploadedAsync(DateTime reportDate)
    {
        await EnsureSchemaAsync();
        return await WasSuccessfullyUploadedAsync(reportDate.Date);
    }

    public async Task<OrderWebDailyReportSyncResult> UploadScheduledAsync(DateTime reportDate)
    {
        MarkPendingUpload(reportDate);
        await Task.CompletedTask;
        return new OrderWebDailyReportSyncResult
        {
            Success = true,
            Skipped = true,
            Message = $"Report for {reportDate:yyyy-MM-dd} is ready. Admin can upload from the Report page."
        };
    }

    public async Task<OrderWebDailyReportSyncResult> UploadManualAsync(DateTime reportDate, bool forceReupload = false)
    {
        return await UploadAsync(reportDate, trigger: "manual", forceReupload: forceReupload);
    }

    public async Task<OrderWebDailyReportSyncResult> UploadAsync(
        DateTime reportDate,
        string trigger,
        bool forceReupload)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return new OrderWebDailyReportSyncResult
            {
                Success = false,
                Message = "Daily report upload runs on the mother terminal only."
            };
        }

        await EnsureSchemaAsync();
        await EnsureInitializedAsync();

        if (!_cloudEnabled)
        {
            return new OrderWebDailyReportSyncResult
            {
                Success = false,
                Message = "OrderWeb cloud sync is disabled. Enable it in Cloud Settings."
            };
        }

        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_tenantId))
        {
            return new OrderWebDailyReportSyncResult
            {
                Success = false,
                Message = "OrderWeb API key or Restaurant ID is missing in Cloud Settings."
            };
        }

        var businessDate = reportDate.Date;

        if (!forceReupload && await WasSuccessfullyUploadedAsync(businessDate))
        {
            return new OrderWebDailyReportSyncResult
            {
                Success = true,
                Skipped = true,
                Message = $"Daily report for {businessDate:yyyy-MM-dd} was already uploaded."
            };
        }

        var payload = await _zReportService.BuildInRestaurantDailyUploadAsync(businessDate);
        payload.Tenant = _tenantId!.Trim();
        payload.Labour = await _timeClockService.BuildCloudLabourPayloadAsync(businessDate);

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var url = $"{_baseUrl}{DailyReportEndpoint}";
                var requestBody = new OrderWebDailyReportApiRequest
                {
                    Tenant = payload.Tenant,
                    ReportDate = payload.ReportDateValue,
                    TotalSales = payload.TotalSales,
                    TotalOrders = payload.TotalOrders,
                    CashSales = payload.CashSales,
                    CardSales = payload.CardSales,
                    Labour = payload.Labour
                };

                var json = JsonSerializer.Serialize(requestBody, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($" [OrderWeb Report] POST {url} attempt {attempt}/{MaxAttempts}");
                System.Diagnostics.Debug.WriteLine($" [OrderWeb Report] Body: {json}");

                var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    await _timeClockService.MarkSessionsSyncedForBusinessDateAsync(businessDate);
                    await LogSyncAsync(businessDate, payload, trigger, true, null, responseBody);
                    if (GetPendingUploadDate() == businessDate)
                    {
                        ClearPendingUpload();
                    }

                    return new OrderWebDailyReportSyncResult
                    {
                        Success = true,
                        Message = $"Daily in-restaurant report uploaded for {businessDate:yyyy-MM-dd}.",
                        Payload = payload
                    };
                }

                lastError = new InvalidOperationException($"HTTP {(int)response.StatusCode}: {responseBody}");
                System.Diagnostics.Debug.WriteLine($" [OrderWeb Report] Upload failed: {lastError.Message}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                System.Diagnostics.Debug.WriteLine($" [OrderWeb Report] Upload error: {ex.Message}");
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
        }

        await LogSyncAsync(businessDate, payload, trigger, false, lastError?.Message, null);
        return new OrderWebDailyReportSyncResult
        {
            Success = false,
            Message = lastError?.Message ?? "Daily report upload failed.",
            Payload = payload
        };
    }

    public async Task ReinitializeAsync()
    {
        _initialized = false;
        await EnsureInitializedAsync();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            var config = await _databaseService.GetCloudConfigAsync();
            _apiKey = config.GetValueOrDefault("api_key", "");
            _tenantId = config.GetValueOrDefault("tenant_slug", "");
            _baseUrl = NormalizeApiBaseUrl(
                config.GetValueOrDefault("api_base_url", config.GetValueOrDefault("cloud_url", "")));
            _cloudEnabled = config.GetValueOrDefault("is_enabled", "False") == "True";

            _httpClient.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
        {
            return;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS orderweb_daily_report_sync_log (
                id INT PRIMARY KEY AUTO_INCREMENT,
                report_date DATE NOT NULL,
                tenant VARCHAR(120) NOT NULL,
                total_sales DECIMAL(10,2) NOT NULL DEFAULT 0,
                total_orders INT NOT NULL DEFAULT 0,
                cash_sales DECIMAL(10,2) NOT NULL DEFAULT 0,
                card_sales DECIMAL(10,2) NOT NULL DEFAULT 0,
                trigger_source VARCHAR(30) NOT NULL DEFAULT 'manual',
                success TINYINT(1) NOT NULL DEFAULT 0,
                response_body TEXT NULL,
                error_message VARCHAR(500) NULL,
                uploaded_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY uq_orderweb_daily_report_date (report_date),
                INDEX idx_orderweb_daily_report_success (success, uploaded_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
        await command.ExecuteNonQueryAsync();
        _schemaEnsured = true;
    }

    private async Task<bool> WasSuccessfullyUploadedAsync(DateTime businessDate)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM orderweb_daily_report_sync_log
            WHERE report_date = @reportDate
              AND success = 1";
        command.Parameters.AddWithValue("@reportDate", businessDate.Date);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        return count > 0;
    }

    private async Task LogSyncAsync(
        DateTime businessDate,
        OrderWebDailyReportPayload payload,
        string trigger,
        bool success,
        string? errorMessage,
        string? responseBody)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO orderweb_daily_report_sync_log
                (report_date, tenant, total_sales, total_orders, cash_sales, card_sales,
                 trigger_source, success, response_body, error_message, uploaded_at)
            VALUES
                (@reportDate, @tenant, @totalSales, @totalOrders, @cashSales, @cardSales,
                 @trigger, @success, @responseBody, @errorMessage, NOW())
            ON DUPLICATE KEY UPDATE
                tenant = VALUES(tenant),
                total_sales = VALUES(total_sales),
                total_orders = VALUES(total_orders),
                cash_sales = VALUES(cash_sales),
                card_sales = VALUES(card_sales),
                trigger_source = VALUES(trigger_source),
                success = VALUES(success),
                response_body = VALUES(response_body),
                error_message = VALUES(error_message),
                uploaded_at = NOW()";

        command.Parameters.AddWithValue("@reportDate", businessDate.Date);
        command.Parameters.AddWithValue("@tenant", payload.Tenant);
        command.Parameters.AddWithValue("@totalSales", payload.TotalSales);
        command.Parameters.AddWithValue("@totalOrders", payload.TotalOrders);
        command.Parameters.AddWithValue("@cashSales", payload.CashSales);
        command.Parameters.AddWithValue("@cardSales", payload.CardSales);
        command.Parameters.AddWithValue("@trigger", trigger);
        command.Parameters.AddWithValue("@success", success);
        command.Parameters.AddWithValue("@responseBody", string.IsNullOrWhiteSpace(responseBody) ? DBNull.Value : responseBody);
        command.Parameters.AddWithValue("@errorMessage", string.IsNullOrWhiteSpace(errorMessage) ? DBNull.Value : errorMessage);
        await command.ExecuteNonQueryAsync();
    }

    private static string NormalizeApiBaseUrl(string? configuredUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredUrl)
            ? "https://orderweb.net/api"
            : configuredUrl.Trim();

        baseUrl = baseUrl.TrimEnd('/');

        if (baseUrl.EndsWith("/pos", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^4];
        }

        return string.IsNullOrWhiteSpace(baseUrl)
            ? "https://orderweb.net/api"
            : baseUrl;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class OrderWebDailyReportApiRequest
    {
        [JsonPropertyName("tenant")]
        public string Tenant { get; set; } = string.Empty;

        [JsonPropertyName("reportDate")]
        public string ReportDate { get; set; } = string.Empty;

        [JsonPropertyName("totalSales")]
        public decimal TotalSales { get; set; }

        [JsonPropertyName("totalOrders")]
        public int TotalOrders { get; set; }

        [JsonPropertyName("cashSales")]
        public decimal CashSales { get; set; }

        [JsonPropertyName("cardSales")]
        public decimal CardSales { get; set; }

        [JsonPropertyName("labour")]
        public OrderWebLabourUploadPayload? Labour { get; set; }
    }
}
