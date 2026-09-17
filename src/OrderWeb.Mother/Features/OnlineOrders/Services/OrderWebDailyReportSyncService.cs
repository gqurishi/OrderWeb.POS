using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Uploads in-restaurant end-of-day totals and labour to OrderWeb.net POS API.
/// Fully automatic: every night around 3 AM (and on catch-up while Mother is online)
/// Mother posts each completed local business day in the last 7 days that is still missing.
/// Same reportDate again is a cloud restatement — not a second POS day.
/// POST https://orderweb.net/api/pos/reports/daily
/// </summary>
public sealed class OrderWebDailyReportSyncService
{
    private const string PendingUploadPreferenceKey = "OrderWebPendingDailyReportDate";
    private const string DailyReportEndpoint = "/pos/reports/daily";
    private const int MaxAttempts = 3;
    /// <summary>Rolling window of completed days to retry after power/internet outage.</summary>
    private const int AutoCatchUpDays = 7;

    private readonly HttpClient _httpClient;
    private readonly DatabaseService _databaseService;
    private readonly ZReportService _zReportService;
    private readonly TimeClockService _timeClockService;
    private readonly CustomerDataService? _customerDataService;
    private readonly ReservationSyncService? _reservationSyncService;
    private readonly OrderWebApiClient? _orderWebApiClient;
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
        TimeClockService timeClockService,
        CustomerDataService? customerDataService = null,
        ReservationSyncService? reservationSyncService = null,
        OrderWebApiClient? orderWebApiClient = null)
    {
        _databaseService = databaseService;
        _zReportService = zReportService;
        _timeClockService = timeClockService;
        _customerDataService = customerDataService;
        _reservationSyncService = reservationSyncService;
        _orderWebApiClient = orderWebApiClient;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
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
        return await WasVatFinalUploadedAsync(reportDate.Date);
    }

    /// <summary>
    /// Phase 4 — Cashier report date = current trading day (rolls at 3:00 AM local).
    /// Same calendar day as live Z / Cashier dashboard; typically tapped after till close (~22:00).
    /// </summary>
    public static DateTime ResolveCashierReportDate(DateTime? localNow = null) =>
        TradingDayHelper.GetBusinessDate(localNow);

    /// <summary>
    /// Cashier Quick action — uploads today's trading day as <c>operations</c>
    /// (cloud Business Reports). Does not finalise VAT. Blocks if open orders remain.
    /// </summary>
    public async Task<OrderWebDailyReportSyncResult> UploadCashierOperationsAsync(DateTime? reportDate = null)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return new OrderWebDailyReportSyncResult
            {
                Success = false,
                Message = "Upload Report runs on the mother terminal only."
            };
        }

        // Phase 4 day selection: locked trading day (or explicit date for tests).
        var businessDate = reportDate?.Date ?? ResolveCashierReportDate();

        await EnsureSchemaAsync();
        await EnsureInitializedAsync();
        if (!_cloudEnabled || string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_tenantId))
        {
            return new OrderWebDailyReportSyncResult
            {
                Success = false,
                Message = "OrderWeb is offline or not configured. Check Cloud Settings / internet, then try again."
            };
        }

        // Phase 4 safety: same gate as auto VAT defer — do not send a half-open day.
        if (await HasUnfinalizedLocalOrdersAsync(businessDate))
        {
            var openCount = await CountUnfinalizedLocalOrdersAsync(businessDate);
            return new OrderWebDailyReportSyncResult
            {
                Success = false,
                Message =
                    $"Cannot upload {businessDate:dd MMM yyyy} yet — {openCount} open or unfinished order(s) remain. " +
                    "Close / pay / void them first, then Upload Report again."
            };
        }

        var result = await UploadAsync(
            businessDate,
            purpose: OrderWebDailyReportUploadRules.PurposeOperations,
            trigger: OrderWebDailyReportUploadRules.TriggerCashier);

        // Phase 4 local log trail (DB row written in UploadAsync; diagnostics for till support).
        AppDiagnostics.Log(
            $"[OrderWeb Report] Phase4 Cashier operations date={businessDate:yyyy-MM-dd} " +
            $"purpose={OrderWebDailyReportUploadRules.PurposeOperations} " +
            $"trigger={OrderWebDailyReportUploadRules.TriggerCashier} " +
            $"success={result.Success} skipped={result.Skipped} — VAT final still due at 3:00 AM");

        if (result.Payload != null)
        {
            LogV1SpotCheck(result.Payload, result.Success, result.Skipped, "cashier_ops — cloud VAT should stay empty until 3 AM");
        }

        return result;
    }

    /// <summary>
    /// Automatic end-of-day upload (3 AM scheduler + online catch-up). Sends a completed
    /// trading day as <c>vat</c> final when local orders for that day are finalized.
    /// After Cashier operations for the same date, this restates the day as VAT final.
    /// </summary>
    public async Task<OrderWebDailyReportSyncResult> UploadScheduledAsync(DateTime reportDate)
    {
        MarkPendingUpload(reportDate);

        var syncNotes = new List<string>();

        if (_customerDataService != null)
        {
            try
            {
                var customerSync = await _customerDataService.RetrySyncAsync();
                syncNotes.Add(
                    customerSync.Failed == 0
                        ? $"{customerSync.Synced} customer(s) synced."
                        : $"{customerSync.Synced} customer(s) synced, {customerSync.Failed} failed.");
            }
            catch (Exception ex)
            {
                syncNotes.Add($"Customer sync failed: {ex.Message}");
                AppDiagnostics.Log($"Scheduled customer sync failed: {ex.Message}");
            }
        }

        if (_reservationSyncService != null)
        {
            try
            {
                var uploadedReservations = await _reservationSyncService.UploadPendingReservationsAsync();
                if (uploadedReservations > 0)
                {
                    syncNotes.Add($"{uploadedReservations} pending reservation(s) uploaded.");
                }
            }
            catch (Exception ex)
            {
                syncNotes.Add($"Reservation upload failed: {ex.Message}");
                AppDiagnostics.Log($"Scheduled reservation upload failed: {ex.Message}");
            }
        }

        if (await HasUnfinalizedLocalOrdersAsync(reportDate.Date))
        {
            var deferred = new OrderWebDailyReportSyncResult
            {
                Success = true,
                Skipped = true,
                Message = $"Automatic VAT upload for {reportDate:yyyy-MM-dd} was deferred because the day still has open or unfinished orders."
            };
            AppDiagnostics.Log($"Scheduled OrderWeb report deferred for {reportDate:yyyy-MM-dd}: {deferred.Message}");
            return deferred;
        }

        var result = await UploadAsync(
            reportDate.Date,
            purpose: OrderWebDailyReportUploadRules.PurposeVat,
            trigger: OrderWebDailyReportUploadRules.TriggerAutomatic3Am);
        if (syncNotes.Count > 0)
        {
            result.Message = $"{result.Message} {string.Join(" ", syncNotes)}".Trim();
        }

        AppDiagnostics.Log(
            $"[OrderWeb Report] Phase4 VAT final date={reportDate:yyyy-MM-dd} " +
            $"purpose={OrderWebDailyReportUploadRules.PurposeVat} " +
            $"trigger={OrderWebDailyReportUploadRules.TriggerAutomatic3Am} " +
            $"success={result.Success} skipped={result.Skipped}");

        if (result.Payload != null)
        {
            LogV1SpotCheck(result.Payload, result.Success, result.Skipped, "vat_final — cloud VAT → POS / Return should fill");
        }

        return result;
    }

    /// <summary>
    /// Completed local days in the last <see cref="AutoCatchUpDays"/> that do not yet have a
    /// successful <c>vat</c> / 3 AM upload (Cashier operations alone does not clear the backlog).
    /// </summary>
    public async Task<IReadOnlyList<DateTime>> GetMissingCompletedReportDatesAsync(DateTime localNow)
    {
        await EnsureSchemaAsync();

        var latestEligibleBusinessDate = localNow.TimeOfDay >= TimeSpan.FromHours(3)
            ? localNow.Date.AddDays(-1)
            : localNow.Date.AddDays(-2);
        var earliestBusinessDate = latestEligibleBusinessDate.AddDays(-(AutoCatchUpDays - 1));

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT DISTINCT DATE(DATE_SUB(o.created_at, INTERVAL 3 HOUR)) AS business_date
            FROM orders o
            WHERE COALESCE(o.source_channel, 'local') = 'local'
              AND o.created_at >= @earliestStart
              AND o.created_at < @eligibleEnd
              AND NOT EXISTS (
                    SELECT 1
                    FROM orderweb_daily_report_sync_log sync_log
                    WHERE sync_log.report_date = DATE(DATE_SUB(o.created_at, INTERVAL 3 HOUR))
                      AND sync_log.success = 1
                      AND (
                            sync_log.trigger_source = @vatTrigger
                            OR LOWER(COALESCE(sync_log.purpose, '')) = @vatPurpose
                      )
              )
            ORDER BY business_date";
        command.Parameters.AddWithValue("@earliestStart", TradingDayHelper.GetBusinessDayStart(earliestBusinessDate));
        command.Parameters.AddWithValue("@eligibleEnd", TradingDayHelper.GetBusinessDayEnd(latestEligibleBusinessDate));
        command.Parameters.AddWithValue("@vatTrigger", OrderWebDailyReportUploadRules.TriggerAutomatic3Am);
        command.Parameters.AddWithValue("@vatPurpose", OrderWebDailyReportUploadRules.PurposeVat);

        var dates = new List<DateTime>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            dates.Add(reader.GetDateTime("business_date").Date);
        }

        return dates;
    }

    /// <summary>Status for Report banner — auto VAT upload (no manual VAT button).</summary>
    public async Task<OrderWebDailyUploadStatus> GetAutoUploadStatusAsync(DateTime? localNow = null)
    {
        var now = localNow ?? DateTime.Now;
        var missing = await GetMissingCompletedReportDatesAsync(now);
        var last = await GetLastSuccessfulVatUploadAsync();

        return new OrderWebDailyUploadStatus
        {
            PendingDayCount = missing.Count,
            OldestPendingDate = missing.Count > 0 ? missing[0] : null,
            NewestPendingDate = missing.Count > 0 ? missing[^1] : null,
            LastSuccessfulUploadDate = last?.ReportDate,
            LastSuccessfulUploadedAt = last?.UploadedAt
        };
    }

    /// <summary>Legacy entry — routes to Cashier operations upload.</summary>
    public async Task<OrderWebDailyReportSyncResult> UploadManualAsync(DateTime reportDate)
    {
        return await UploadCashierOperationsAsync(reportDate);
    }

    private async Task<(DateTime ReportDate, DateTime UploadedAt)?> GetLastSuccessfulVatUploadAsync()
    {
        await EnsureSchemaAsync();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT report_date, uploaded_at
            FROM orderweb_daily_report_sync_log
            WHERE success = 1
              AND (
                    trigger_source = @vatTrigger
                    OR LOWER(COALESCE(purpose, '')) = @vatPurpose
              )
            ORDER BY report_date DESC, uploaded_at DESC
            LIMIT 1";
        command.Parameters.AddWithValue("@vatTrigger", OrderWebDailyReportUploadRules.TriggerAutomatic3Am);
        command.Parameters.AddWithValue("@vatPurpose", OrderWebDailyReportUploadRules.PurposeVat);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.GetDateTime("report_date").Date, reader.GetDateTime("uploaded_at"));
    }

    private async Task<bool> HasUnfinalizedLocalOrdersAsync(DateTime businessDate) =>
        await CountUnfinalizedLocalOrdersAsync(businessDate) > 0;

    private async Task<int> CountUnfinalizedLocalOrdersAsync(DateTime businessDate)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM orders
            WHERE COALESCE(source_channel, 'local') = 'local'
              AND created_at >= @dayStart
              AND created_at < @dayEnd
              AND (
                    COALESCE(is_open, 1) = 1
                    OR NOT (
                        COALESCE(LOWER(local_lifecycle_state), '') IN ('paid', 'voided')
                        OR LOWER(COALESCE(status, '')) IN ('completed', 'closed', 'paid', 'cancelled', 'voided')
                    )
              )";
        command.Parameters.AddWithValue("@dayStart", TradingDayHelper.GetBusinessDayStart(businessDate));
        command.Parameters.AddWithValue("@dayEnd", TradingDayHelper.GetBusinessDayEnd(businessDate));
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<OrderWebDailyReportSyncResult> UploadAsync(
        DateTime reportDate,
        string purpose,
        string trigger)
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
        var isVatFinal = OrderWebDailyReportUploadRules.IsVatFinalPurpose(purpose)
            || OrderWebDailyReportUploadRules.IsVatFinalTrigger(trigger);

        // Cashier operations may restate Reports; VAT final only skips if already vat-finalised.
        if (isVatFinal && await WasVatFinalUploadedAsync(businessDate))
        {
            return new OrderWebDailyReportSyncResult
            {
                Success = true,
                Skipped = true,
                Message = $"VAT final for {businessDate:yyyy-MM-dd} was already uploaded."
            };
        }

        var autoClosedSessions = await _timeClockService.CloseOpenSessionsForDailyUploadAsync(businessDate);
        if (autoClosedSessions > 0)
        {
            System.Diagnostics.Debug.WriteLine($" [OrderWeb Report] Auto-closed {autoClosedSessions} staff clock session(s) before daily upload.");
        }

        var payload = await _zReportService.BuildInRestaurantDailyUploadAsync(businessDate);
        payload.Tenant = _tenantId!.Trim();
        // Phase 1: shared body from builder; only purpose/trigger differ per caller.
        OrderWebDailyReportUploadRules.ApplyFlags(payload, purpose, trigger);
        payload.Labour = await _timeClockService.BuildCloudLabourPayloadAsync(businessDate);

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var url = $"{_baseUrl}{DailyReportEndpoint}";
                var requestBody = MapToApiRequest(payload);

                var json = JsonSerializer.Serialize(requestBody, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey(
                    "daily-report",
                    payload.Tenant,
                    payload.ReportDateValue,
                    payload.Purpose);

                // Done-when log: purpose + trigger visible for Postman / Output window.
                AppDiagnostics.Log(
                    $"[OrderWeb Report] POST daily-report date={payload.ReportDateValue} " +
                    $"purpose={payload.Purpose} trigger={payload.Trigger} " +
                    $"idempotency={idempotencyKey} attempt={attempt}/{MaxAttempts}");
                LogV1SpotCheck(payload, success: false, skipped: false, $"post_attempt={attempt}/{MaxAttempts}");
                System.Diagnostics.Debug.WriteLine(
                    $"[OrderWeb Report] purpose={payload.Purpose} trigger={payload.Trigger} body={json}");

                _httpClient.DefaultRequestHeaders.Remove("Idempotency-Key");
                _httpClient.DefaultRequestHeaders.Remove("X-Idempotency-Key");
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
                request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);
                _orderWebApiClient?.ApplyAuthHeaders(request, _apiKey.Trim(), idempotencyKey);

                var response = _orderWebApiClient != null
                    ? await _orderWebApiClient.SendAsync(request)
                    : await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    await _timeClockService.MarkSessionsSyncedForBusinessDateAsync(businessDate);
                    await LogSyncAsync(businessDate, payload, payload.Trigger, true, null, responseBody);
                    if (GetPendingUploadDate() == businessDate)
                    {
                        ClearPendingUpload();
                    }

                    var message = isVatFinal
                        ? $"VAT final uploaded for {businessDate:yyyy-MM-dd}."
                        : "Sent to OrderWeb Reports. VAT finalises automatically at 3:00 AM.";

                    LogV1SpotCheck(payload, success: true, skipped: false, "http_ok");

                    return new OrderWebDailyReportSyncResult
                    {
                        Success = true,
                        Message = message,
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

        await QueueFailedReportUploadAsync(payload);
        await LogSyncAsync(businessDate, payload, payload.Trigger, false, lastError?.Message, null);
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
            _baseUrl = OrderWebApiClient.NormalizeApiBaseUrl(
                config.GetValueOrDefault("api_base_url", config.GetValueOrDefault("cloud_url", "")));
            _cloudEnabled = config.GetValueOrDefault("is_enabled", "False") == "True";

            _httpClient.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _apiKey.Trim());
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

        if (!RuntimeSchemaPolicy.IsMigrationManaged)
        {
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
                    purpose VARCHAR(30) NOT NULL DEFAULT 'operations',
                    trigger_source VARCHAR(30) NOT NULL DEFAULT 'manual',
                    success TINYINT(1) NOT NULL DEFAULT 0,
                    response_body TEXT NULL,
                    error_message VARCHAR(500) NULL,
                    uploaded_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE KEY uq_orderweb_daily_report_date (report_date),
                    INDEX idx_orderweb_daily_report_success (success, uploaded_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = @"
                ALTER TABLE orderweb_daily_report_sync_log
                ADD COLUMN purpose VARCHAR(30) NOT NULL DEFAULT 'operations' AFTER card_sales";
            await alter.ExecuteNonQueryAsync();
        }
        catch
        {
            // Column already exists on upgraded DBs.
        }

        _schemaEnsured = true;
    }

    private async Task QueueFailedReportUploadAsync(OrderWebDailyReportPayload payload)
    {
        if (_orderWebApiClient == null || string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_baseUrl))
        {
            return;
        }

        var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey(
            "daily-report",
            payload.Tenant,
            payload.ReportDateValue,
            payload.Purpose);
        var requestBody = MapToApiRequest(payload);

        await _orderWebApiClient.EnqueueAsync(
            "daily_report",
            $"{_baseUrl}{DailyReportEndpoint}",
            requestBody,
            _apiKey,
            idempotencyKey,
            priority: 3);
    }

    private async Task<bool> WasVatFinalUploadedAsync(DateTime businessDate)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM orderweb_daily_report_sync_log
            WHERE report_date = @reportDate
              AND success = 1
              AND (
                    trigger_source = @vatTrigger
                    OR LOWER(COALESCE(purpose, '')) = @vatPurpose
              )";
        command.Parameters.AddWithValue("@reportDate", businessDate.Date);
        command.Parameters.AddWithValue("@vatTrigger", OrderWebDailyReportUploadRules.TriggerAutomatic3Am);
        command.Parameters.AddWithValue("@vatPurpose", OrderWebDailyReportUploadRules.PurposeVat);
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
                 purpose, trigger_source, success, response_body, error_message, uploaded_at)
            VALUES
                (@reportDate, @tenant, @totalSales, @totalOrders, @cashSales, @cardSales,
                 @purpose, @trigger, @success, @responseBody, @errorMessage, NOW())
            ON DUPLICATE KEY UPDATE
                tenant = VALUES(tenant),
                total_sales = VALUES(total_sales),
                total_orders = VALUES(total_orders),
                cash_sales = VALUES(cash_sales),
                card_sales = VALUES(card_sales),
                purpose = IF(purpose = 'vat' AND success = 1 AND VALUES(purpose) = 'operations', purpose, VALUES(purpose)),
                trigger_source = IF(purpose = 'vat' AND success = 1 AND VALUES(purpose) = 'operations', trigger_source, VALUES(trigger_source)),
                success = IF(purpose = 'vat' AND success = 1 AND VALUES(purpose) = 'operations', 1, VALUES(success)),
                response_body = VALUES(response_body),
                error_message = VALUES(error_message),
                uploaded_at = NOW()";

        command.Parameters.AddWithValue("@reportDate", businessDate.Date);
        command.Parameters.AddWithValue("@tenant", payload.Tenant);
        command.Parameters.AddWithValue("@totalSales", payload.TotalSales);
        command.Parameters.AddWithValue("@totalOrders", payload.TotalOrders);
        command.Parameters.AddWithValue("@cashSales", payload.CashSales);
        command.Parameters.AddWithValue("@cardSales", payload.CardSales);
        command.Parameters.AddWithValue("@purpose", payload.Purpose);
        command.Parameters.AddWithValue("@trigger", trigger);
        command.Parameters.AddWithValue("@success", success);
        command.Parameters.AddWithValue("@responseBody", string.IsNullOrWhiteSpace(responseBody) ? DBNull.Value : responseBody);
        command.Parameters.AddWithValue("@errorMessage", string.IsNullOrWhiteSpace(errorMessage) ? DBNull.Value : errorMessage);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Phase V1 ops spot-check: one searchable line with purpose/trigger + filing fields.
    /// Always appends to AppData <c>orderweb-daily-report.log</c> (Release builds included).
    /// </summary>
    private static void LogV1SpotCheck(
        OrderWebDailyReportPayload payload,
        bool success,
        bool skipped,
        string? note = null)
    {
        var bands = payload.VatByRate == null || payload.VatByRate.Count == 0
            ? "none"
            : string.Join(
                "|",
                payload.VatByRate.Select(r =>
                    $"{r.VatRate:0.##}%:net={r.NetAmount:0.00}/vat={r.VatAmount:0.00}/gross={r.GrossAmount:0.00}"));

        var line =
            $"[OrderWeb Report] V1 spot-check date={payload.ReportDateValue} " +
            $"purpose={payload.Purpose} trigger={payload.Trigger} " +
            $"success={success} skipped={skipped} " +
            $"netSales={payload.NetSales:0.00} vatAmount={payload.VatAmount:0.00} " +
            $"grossSales={payload.GrossSales:0.00} vatByRate=[{bands}]" +
            (string.IsNullOrWhiteSpace(note) ? string.Empty : $" note={note}");

        AppDiagnostics.Log(line);
        System.Diagnostics.Debug.WriteLine(line);

        try
        {
            string directory;
            try
            {
                directory = FileSystem.AppDataDirectory;
            }
            catch
            {
                directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OrderWebPOS",
                    "logs");
            }

            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "orderweb-daily-report.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch
        {
            // Never block till on log I/O.
        }
    }

    private static OrderWebDailyReportApiRequest MapToApiRequest(OrderWebDailyReportPayload payload)
    {
        return new OrderWebDailyReportApiRequest
        {
            ContractVersion = payload.ContractVersion,
            Tenant = payload.Tenant,
            ReportDate = payload.ReportDateValue,
            TotalSales = payload.TotalSales,
            TotalOrders = payload.TotalOrders,
            CashSales = payload.CashSales,
            CardSales = payload.CardSales,
            GiftCardSales = payload.GiftCardSales,
            ItemSales = payload.ItemSales,
            Discounts = payload.Discounts,
            ServiceCharges = payload.ServiceCharges,
            RemovedServiceChargeCount = payload.RemovedServiceChargeCount,
            RemovedServiceChargeValue = payload.RemovedServiceChargeValue,
            CashTips = payload.CashTips,
            CardTips = payload.CardTips,
            DeliveryFees = payload.DeliveryFees,
            Refunds = payload.Refunds,
            Vat = payload.Vat,
            NetSales = payload.NetSales,
            VatAmount = payload.VatAmount,
            GrossSales = payload.GrossSales,
            VatByRate = payload.VatByRate
                .Select(r => new OrderWebDailyVatByRateApiRow
                {
                    VatRate = r.VatRate,
                    NetAmount = r.NetAmount,
                    VatAmount = r.VatAmount,
                    GrossAmount = r.GrossAmount
                })
                .ToList(),
            FinalMoneyCollected = payload.FinalMoneyCollected,
            Purpose = payload.Purpose,
            Trigger = payload.Trigger,
            Labour = payload.Labour
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class OrderWebDailyReportApiRequest
    {
        [JsonPropertyName("contractVersion")]
        public int ContractVersion { get; set; } = 2;
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

        [JsonPropertyName("giftCardSales")]
        public decimal GiftCardSales { get; set; }

        [JsonPropertyName("itemSales")] public decimal ItemSales { get; set; }
        [JsonPropertyName("discounts")] public decimal Discounts { get; set; }
        [JsonPropertyName("serviceCharges")] public decimal ServiceCharges { get; set; }
        [JsonPropertyName("removedServiceChargeCount")] public int RemovedServiceChargeCount { get; set; }
        [JsonPropertyName("removedServiceChargeValue")] public decimal RemovedServiceChargeValue { get; set; }
        [JsonPropertyName("cashTips")] public decimal CashTips { get; set; }
        [JsonPropertyName("cardTips")] public decimal CardTips { get; set; }
        [JsonPropertyName("deliveryFees")] public decimal DeliveryFees { get; set; }
        [JsonPropertyName("refunds")] public decimal Refunds { get; set; }
        [JsonPropertyName("vat")] public decimal Vat { get; set; }
        [JsonPropertyName("netSales")] public decimal NetSales { get; set; }
        [JsonPropertyName("vatAmount")] public decimal VatAmount { get; set; }
        [JsonPropertyName("grossSales")] public decimal GrossSales { get; set; }
        [JsonPropertyName("vatByRate")] public List<OrderWebDailyVatByRateApiRow> VatByRate { get; set; } = new();
        [JsonPropertyName("finalMoneyCollected")] public decimal FinalMoneyCollected { get; set; }

        [JsonPropertyName("purpose")]
        public string Purpose { get; set; } = OrderWebDailyReportUploadRules.PurposeOperations;

        [JsonPropertyName("trigger")]
        public string Trigger { get; set; } = OrderWebDailyReportUploadRules.TriggerCashier;

        [JsonPropertyName("labour")]
        public OrderWebLabourUploadPayload? Labour { get; set; }
    }

    private sealed class OrderWebDailyVatByRateApiRow
    {
        [JsonPropertyName("vatRate")]
        public decimal VatRate { get; set; }

        [JsonPropertyName("netAmount")]
        public decimal NetAmount { get; set; }

        [JsonPropertyName("vatAmount")]
        public decimal VatAmount { get; set; }

        [JsonPropertyName("grossAmount")]
        public decimal GrossAmount { get; set; }
    }
}
