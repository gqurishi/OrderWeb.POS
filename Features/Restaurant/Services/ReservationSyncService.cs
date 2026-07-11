using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MySqlConnector;
using POS_in_NET.Converters;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed partial class ReservationSyncService : IDisposable
{
    private const string SyncStateKey = "cloud_reservations";
    private readonly DatabaseService _databaseService;
    private readonly OrderWebApiClient? _orderWebApiClient;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _isStarted;

    public event EventHandler<ReservationSyncCompletedEventArgs>? SyncCompleted;

    public ReservationSyncService(DatabaseService databaseService, OrderWebApiClient? orderWebApiClient = null)
    {
        _databaseService = databaseService;
        _orderWebApiClient = orderWebApiClient;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task StartAsync()
    {
        if (_isStarted)
        {
            return;
        }

        var roleCheck = await CanRunReservationCloudAsync();
        if (!roleCheck.Allowed)
        {
            AppDiagnostics.Log($"Reservation sync skipped: {roleCheck.Reason}");
            return;
        }

        var config = await _databaseService.GetCloudConfigAsync();
        if (!IsConfigured(config))
        {
            AppDiagnostics.Log("Reservation sync skipped: cloud configuration incomplete or disabled.");
            return;
        }

        await EnsureSchemaAsync();

        _isStarted = true;

        await SyncTodayAsync(includeCancelled: true);

        AppDiagnostics.Log("Reservation sync started (managed by background sync).");
    }

    public async Task<ReservationSyncResult> SyncTodayAsync(bool includeCancelled = true)
    {
        return await SyncDateAsync(DateTime.Today, useSince: true, includeCancelled);
    }

    public async Task<ReservationSyncResult> SyncDateAsync(DateTime date, bool useSince = false, bool includeCancelled = false)
    {
        var roleCheck = await CanRunReservationCloudAsync();
        if (!roleCheck.Allowed)
        {
            return new ReservationSyncResult(false, 0, 0, roleCheck.Reason);
        }

        if (!await _syncLock.WaitAsync(0))
        {
            return new ReservationSyncResult(false, 0, 0, "Reservation sync already running.");
        }

        try
        {
            await EnsureSchemaAsync();

            var config = await _databaseService.GetCloudConfigAsync();
            if (!IsConfigured(config))
            {
                return new ReservationSyncResult(false, 0, 0, "Cloud API is not configured or disabled.");
            }

            var since = useSince ? await GetLastSyncUtcAsync() : null;
            var response = await PullReservationsAsync(config, date, since, includeCancelled);
            if (!response.Success)
            {
                return new ReservationSyncResult(false, 0, 0, response.ErrorMessage ?? "Reservation pull failed.");
            }

            var newReservations = 0;
            var updatedReservations = 0;

            foreach (var dto in response.Reservations)
            {
                var inbound = await ProcessInboundReservationAsync(dto, fromRealtime: false);
                newReservations += inbound.NewReservations;
                updatedReservations += inbound.UpdatedReservations;
            }

            await SetLastSyncUtcAsync(DateTime.UtcNow);

            var result = new ReservationSyncResult(
                true,
                newReservations,
                updatedReservations,
                response.Reservations.Count == 0
                    ? "No new reservations."
                    : $"Synced {response.Reservations.Count} reservation(s).");

            SyncCompleted?.Invoke(this, new ReservationSyncCompletedEventArgs(result));
            if (newReservations > 0)
            {
                NotifyNewReservations(newReservations);
            }

            return result;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("ReservationSync", ex);
            return new ReservationSyncResult(false, 0, 0, ex.Message);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<IReadOnlyList<CloudReservation>> GetReservationsAsync(DateTime startDate, DateTime endDate)
    {
        await EnsureSchemaAsync();

        var reservations = new List<CloudReservation>();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            SELECT id, cloud_id, local_id, reference, reservation_date, reservation_time, covers,
                   customer_name, customer_phone, customer_email, promo_code, notes, allergies, status,
                   source, table_number, deposit_amount_pence, pos_seen_at, pos_print_status,
                   upload_status, cloud_created_at, cloud_updated_at, last_updated_at
            FROM cloud_reservations
            WHERE reservation_date BETWEEN @startDate AND @endDate
            ORDER BY reservation_date, reservation_time, customer_name
            """,
            connection);

        command.Parameters.AddWithValue("@startDate", startDate.Date);
        command.Parameters.AddWithValue("@endDate", endDate.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            reservations.Add(ReadReservation(reader));
        }

        return reservations;
    }

    public async Task<bool> AckReservationAsync(string cloudReservationId, string status)
    {
        try
        {
            var roleCheck = await CanRunReservationCloudAsync();
            if (!roleCheck.Allowed)
            {
                return false;
            }

            var config = await _databaseService.GetCloudConfigAsync();
            if (!IsConfigured(config) || string.IsNullOrWhiteSpace(cloudReservationId))
            {
                return false;
            }

            var endpoint = $"{NormalizeApiBaseUrl(config)}/pos/reservations/ack";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new
                {
                    tenant = config.GetValueOrDefault("tenant_slug", ""),
                    reservation_id = cloudReservationId,
                    status
                })
            };

            AddAuthHeaders(request, config.GetValueOrDefault("api_key", ""));
            using var response = _orderWebApiClient != null
                ? await _orderWebApiClient.SendAsync(request)
                : await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Reservation ACK failed: {ex.Message}");
            return false;
        }
    }

    private async Task<PullReservationsResult> PullReservationsAsync(
        Dictionary<string, string> config,
        DateTime date,
        DateTime? since,
        bool includeCancelled)
    {
        var tenant = config.GetValueOrDefault("tenant_slug", "");
        var endpoint = $"{NormalizeApiBaseUrl(config)}/pos/pull-reservations" +
                       $"?tenant={Uri.EscapeDataString(tenant)}" +
                       $"&date={date:yyyy-MM-dd}";

        if (since.HasValue)
        {
            endpoint += $"&since={Uri.EscapeDataString(since.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}";
        }

        if (includeCancelled)
        {
            endpoint += "&include_cancelled=true";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddAuthHeaders(request, config.GetValueOrDefault("api_key", ""));

        using var response = _orderWebApiClient != null
            ? await _orderWebApiClient.SendAsync(request)
            : await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return PullReservationsResult.Failed($"HTTP {(int)response.StatusCode}: {body}");
        }

        var payload = JsonSerializer.Deserialize<PullReservationsResponse>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (payload == null)
        {
            return PullReservationsResult.Failed("Reservation API returned an empty response.");
        }

        if (!payload.Success)
        {
            return PullReservationsResult.Failed("Reservation API returned success=false.");
        }

        return PullReservationsResult.Ok(payload.Reservations ?? new List<CloudReservationDto>());
    }

    private async Task<(bool IsNew, bool Updated)> UpsertReservationAsync(CloudReservationDto dto)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        var localId = ResolveDtoString(dto.LocalId, dto.Extra, "localId", "local_id");
        var reference = ResolveDtoString(dto.Reference, dto.Extra, "reference");
        var promoCode = ResolveDtoString(
            dto.PromoCode,
            dto.Extra,
            "promoCode",
            "promo_code",
            "promocode",
            "promotionCode",
            "promotion_code",
            "discountCode",
            "discount_code");
        var tableNumber = ResolveDtoString(
            dto.TableNumber,
            dto.Extra,
            "tableNumber",
            "table_number",
            "tableNo",
            "table_no",
            "table");

        await using var existsCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM cloud_reservations WHERE cloud_id = @cloudId",
            connection);
        existsCommand.Parameters.AddWithValue("@cloudId", dto.Id);
        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync()) > 0;

        if (exists)
        {
            await using var updateCommand = new MySqlCommand(
                """
                UPDATE cloud_reservations
                SET local_id = COALESCE(@localId, local_id),
                    reference = COALESCE(@reference, reference),
                    reservation_date = COALESCE(@reservationDate, reservation_date),
                    reservation_time = COALESCE(@reservationTime, reservation_time),
                    covers = COALESCE(@covers, covers),
                    customer_name = COALESCE(@customerName, customer_name),
                    customer_phone = COALESCE(@customerPhone, customer_phone),
                    customer_email = COALESCE(@customerEmail, customer_email),
                    promo_code = COALESCE(@promoCode, promo_code),
                    notes = COALESCE(@notes, notes),
                    allergies = COALESCE(@allergies, allergies),
                    status = COALESCE(@status, status),
                    source = COALESCE(@source, source),
                    table_number = COALESCE(@tableNumber, table_number),
                    deposit_amount_pence = COALESCE(@depositAmountPence, deposit_amount_pence),
                    pos_print_status = COALESCE(@posPrintStatus, pos_print_status),
                    cloud_created_at = COALESCE(@cloudCreatedAt, cloud_created_at),
                    cloud_updated_at = COALESCE(@cloudUpdatedAt, cloud_updated_at),
                    last_updated_at = UTC_TIMESTAMP()
                WHERE cloud_id = @cloudId
                """,
                connection);

            AddUpdateParameters(updateCommand, dto, localId, reference, promoCode, tableNumber);
            var updated = await updateCommand.ExecuteNonQueryAsync();
            return (false, updated > 0);
        }

        await using var command = new MySqlCommand(
            """
            INSERT INTO cloud_reservations (
                cloud_id, local_id, reference, reservation_date, reservation_time, covers,
                customer_name, customer_phone, customer_email, promo_code, notes, allergies, status,
                source, table_number, deposit_amount_pence, pos_seen_at, pos_print_status,
                cloud_created_at, cloud_updated_at, last_updated_at
            ) VALUES (
                @cloudId, @localId, @reference, @reservationDate, @reservationTime, @covers,
                @customerName, @customerPhone, @customerEmail, @promoCode, @notes, @allergies, @status,
                @source, @tableNumber, @depositAmountPence, @posSeenAt, @posPrintStatus,
                @cloudCreatedAt, @cloudUpdatedAt, UTC_TIMESTAMP()
            )
            """,
            connection);

        command.Parameters.AddWithValue("@cloudId", dto.Id);
        command.Parameters.AddWithValue("@localId", DbText(localId));
        command.Parameters.AddWithValue("@reference", reference ?? "");
        command.Parameters.AddWithValue("@reservationDate", ParseDate(dto.ReservationDate));
        command.Parameters.AddWithValue("@reservationTime", ParseTime(dto.ReservationTime));
        command.Parameters.AddWithValue("@covers", dto.Covers > 0 ? dto.Covers : 1);
        command.Parameters.AddWithValue("@customerName", dto.CustomerName ?? "");
        command.Parameters.AddWithValue("@customerPhone", dto.CustomerPhone ?? "");
        command.Parameters.AddWithValue("@customerEmail", dto.CustomerEmail ?? "");
        command.Parameters.AddWithValue("@promoCode", promoCode ?? "");
        command.Parameters.AddWithValue("@notes", dto.Notes ?? "");
        command.Parameters.AddWithValue("@allergies", dto.Allergies ?? "");
        command.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(dto.Status) ? "confirmed" : dto.Status);
        command.Parameters.AddWithValue("@source", dto.Source ?? "");
        command.Parameters.AddWithValue("@tableNumber", tableNumber ?? "");
        command.Parameters.AddWithValue("@depositAmountPence", dto.DepositAmountPence);
        command.Parameters.AddWithValue("@posSeenAt", ToDbDate(dto.PosSeenAt));
        command.Parameters.AddWithValue("@posPrintStatus", (object?)dto.PosPrintStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("@cloudCreatedAt", ToDbDate(dto.CreatedAt));
        command.Parameters.AddWithValue("@cloudUpdatedAt", ToDbDate(dto.UpdatedAt));

        var affected = await command.ExecuteNonQueryAsync();
        return (true, affected > 0);
    }

    private static void AddUpdateParameters(
        MySqlCommand command,
        CloudReservationDto dto,
        string? localId,
        string? reference,
        string? promoCode,
        string? tableNumber)
    {
        command.Parameters.AddWithValue("@cloudId", dto.Id);
        command.Parameters.AddWithValue("@localId", DbText(localId));
        command.Parameters.AddWithValue("@reference", DbText(reference));
        command.Parameters.AddWithValue("@reservationDate", DbDate(dto.ReservationDate));
        command.Parameters.AddWithValue("@reservationTime", DbTime(dto.ReservationTime));
        command.Parameters.AddWithValue("@covers", dto.Covers > 0 ? dto.Covers : DBNull.Value);
        command.Parameters.AddWithValue("@customerName", DbText(dto.CustomerName));
        command.Parameters.AddWithValue("@customerPhone", DbText(dto.CustomerPhone));
        command.Parameters.AddWithValue("@customerEmail", DbText(dto.CustomerEmail));
        command.Parameters.AddWithValue("@promoCode", DbText(promoCode));
        command.Parameters.AddWithValue("@notes", DbText(dto.Notes));
        command.Parameters.AddWithValue("@allergies", DbText(dto.Allergies));
        command.Parameters.AddWithValue("@status", DbText(dto.Status));
        command.Parameters.AddWithValue("@source", DbText(dto.Source));
        command.Parameters.AddWithValue("@tableNumber", DbText(tableNumber));
        command.Parameters.AddWithValue("@depositAmountPence", dto.DepositAmountPence > 0 ? dto.DepositAmountPence : DBNull.Value);
        command.Parameters.AddWithValue("@posPrintStatus", DbText(dto.PosPrintStatus));
        command.Parameters.AddWithValue("@cloudCreatedAt", ToDbDate(dto.CreatedAt));
        command.Parameters.AddWithValue("@cloudUpdatedAt", ToDbDate(dto.UpdatedAt));
    }

    private static object DbText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static object DbDate(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : ParseDate(value);
    }

    private static object DbTime(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : ParseTime(value);
    }

    private static string? ResolveDtoString(
        string? primary,
        IReadOnlyDictionary<string, JsonElement>? extra,
        params string[] aliases)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (extra == null)
        {
            return null;
        }

        foreach (var alias in aliases)
        {
            if (!extra.TryGetValue(alias, out var element))
            {
                continue;
            }

            var value = JsonElementToString(element);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? JsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private async Task MarkSeenAsync(string cloudReservationId)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            "UPDATE cloud_reservations SET pos_seen_at = COALESCE(pos_seen_at, UTC_TIMESTAMP()) WHERE cloud_id = @cloudId",
            connection);
        command.Parameters.AddWithValue("@cloudId", cloudReservationId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<DateTime?> GetLastSyncUtcAsync()
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            "SELECT last_sync_utc FROM reservation_sync_state WHERE sync_key = @syncKey",
            connection);
        command.Parameters.AddWithValue("@syncKey", SyncStateKey);
        var value = await command.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : Convert.ToDateTime(value, CultureInfo.InvariantCulture);
    }

    private async Task SetLastSyncUtcAsync(DateTime lastSyncUtc)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            INSERT INTO reservation_sync_state (sync_key, last_sync_utc, updated_at)
            VALUES (@syncKey, @lastSyncUtc, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE last_sync_utc = VALUES(last_sync_utc), updated_at = UTC_TIMESTAMP()
            """,
            connection);
        command.Parameters.AddWithValue("@syncKey", SyncStateKey);
        command.Parameters.AddWithValue("@lastSyncUtc", lastSyncUtc);
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureSchemaAsync()
    {
        await using var connection = await _databaseService.GetConnectionAsync();

        await using (var command = new MySqlCommand(
            """
            CREATE TABLE IF NOT EXISTS cloud_reservations (
                id INT AUTO_INCREMENT PRIMARY KEY,
                cloud_id VARCHAR(64) NOT NULL,
                reference VARCHAR(64) NOT NULL DEFAULT '',
                reservation_date DATE NOT NULL,
                reservation_time TIME NOT NULL,
                covers INT NOT NULL DEFAULT 0,
                customer_name VARCHAR(255) NOT NULL DEFAULT '',
                customer_phone VARCHAR(64) NOT NULL DEFAULT '',
                customer_email VARCHAR(255) NOT NULL DEFAULT '',
                promo_code VARCHAR(64) NOT NULL DEFAULT '',
                notes TEXT NULL,
                allergies TEXT NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'confirmed',
                source VARCHAR(32) NOT NULL DEFAULT '',
                table_number VARCHAR(64) NOT NULL DEFAULT '',
                deposit_amount_pence INT NOT NULL DEFAULT 0,
                pos_seen_at DATETIME NULL,
                pos_print_status VARCHAR(32) NULL,
                cloud_created_at DATETIME NULL,
                cloud_updated_at DATETIME NULL,
                last_updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY ux_cloud_reservations_cloud_id (cloud_id),
                INDEX idx_cloud_reservations_date_time (reservation_date, reservation_time),
                INDEX idx_cloud_reservations_status (status)
            )
            """,
            connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new MySqlCommand(
            """
            CREATE TABLE IF NOT EXISTS reservation_sync_state (
                sync_key VARCHAR(64) PRIMARY KEY,
                last_sync_utc DATETIME NULL,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            )
            """,
            connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await ApplyTwoWaySchemaAsync(connection);
    }

    private static async Task ApplyTwoWaySchemaAsync(MySqlConnection connection)
    {
        var alterStatements = new[]
        {
            "ALTER TABLE cloud_reservations ADD COLUMN IF NOT EXISTS local_id VARCHAR(64) NULL AFTER cloud_id",
            "ALTER TABLE cloud_reservations ADD COLUMN IF NOT EXISTS promo_code VARCHAR(64) NOT NULL DEFAULT '' AFTER customer_email",
            "ALTER TABLE cloud_reservations ADD COLUMN IF NOT EXISTS upload_status VARCHAR(20) NOT NULL DEFAULT 'synced' AFTER pos_print_status",
            "CREATE INDEX IF NOT EXISTS idx_cloud_reservations_upload_status ON cloud_reservations (upload_status)",
            "CREATE INDEX IF NOT EXISTS idx_cloud_reservations_local_id ON cloud_reservations (local_id)"
        };

        foreach (var sql in alterStatements)
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new MySqlCommand(
            """
            CREATE TABLE IF NOT EXISTS reservation_pending_acks (
                id INT AUTO_INCREMENT PRIMARY KEY,
                cloud_reservation_id VARCHAR(64) NOT NULL,
                ack_status VARCHAR(32) NOT NULL DEFAULT 'seen',
                attempts INT NOT NULL DEFAULT 0,
                last_error VARCHAR(500) NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_attempt_at DATETIME NULL,
                UNIQUE KEY ux_reservation_pending_acks_cloud (cloud_reservation_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """,
            connection))
        {
            await command.ExecuteNonQueryAsync();
        }
    }

    private static CloudReservation ReadReservation(MySqlDataReader reader)
    {
        static bool IsNull(MySqlDataReader reader, string columnName) => reader.IsDBNull(reader.GetOrdinal(columnName));

        return new CloudReservation
        {
            Id = reader.GetInt32("id"),
            CloudId = reader.GetString("cloud_id"),
            LocalId = IsNull(reader, "local_id") ? null : reader.GetString("local_id"),
            Reference = reader.GetString("reference"),
            ReservationDate = reader.GetDateTime("reservation_date"),
            ReservationTime = reader.GetTimeSpan("reservation_time"),
            Covers = reader.GetInt32("covers"),
            CustomerName = reader.GetString("customer_name"),
            CustomerPhone = reader.GetString("customer_phone"),
            CustomerEmail = reader.GetString("customer_email"),
            PromoCode = IsNull(reader, "promo_code") ? "" : reader.GetString("promo_code"),
            Notes = IsNull(reader, "notes") ? "" : reader.GetString("notes"),
            Allergies = IsNull(reader, "allergies") ? "" : reader.GetString("allergies"),
            Status = reader.GetString("status"),
            Source = reader.GetString("source"),
            TableNumber = reader.GetString("table_number"),
            DepositAmountPence = reader.GetInt32("deposit_amount_pence"),
            PosSeenAt = IsNull(reader, "pos_seen_at") ? null : reader.GetDateTime("pos_seen_at"),
            PosPrintStatus = IsNull(reader, "pos_print_status") ? null : reader.GetString("pos_print_status"),
            UploadStatus = IsNull(reader, "upload_status") ? "synced" : reader.GetString("upload_status"),
            CloudCreatedAt = IsNull(reader, "cloud_created_at") ? null : reader.GetDateTime("cloud_created_at"),
            CloudUpdatedAt = IsNull(reader, "cloud_updated_at") ? null : reader.GetDateTime("cloud_updated_at"),
            LastUpdatedAt = reader.GetDateTime("last_updated_at")
        };
    }

    private static bool IsConfigured(Dictionary<string, string> config)
    {
        return string.Equals(config.GetValueOrDefault("is_enabled"), "True", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(config.GetValueOrDefault("tenant_slug"))
               && !string.IsNullOrWhiteSpace(config.GetValueOrDefault("api_key"));
    }

    private static string NormalizeApiBaseUrl(Dictionary<string, string> config)
    {
        var baseUrl = config.GetValueOrDefault("api_base_url", "");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = config.GetValueOrDefault("cloud_url", "https://orderweb.net/api");
        }

        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/pos/pull-orders", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^"/pos/pull-orders".Length];
        }

        if (baseUrl.EndsWith("/pos/pull-reservations", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^"/pos/pull-reservations".Length];
        }

        if (baseUrl.EndsWith("/pos/reservations", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^"/pos/reservations".Length];
        }

        if (baseUrl.EndsWith("/pos/reservations/ack", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^"/pos/reservations/ack".Length];
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            && string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')))
        {
            baseUrl = $"{baseUrl}/api";
        }

        return OrderWebApiClient.NormalizeApiBaseUrl(baseUrl);
    }

    private static void AddAuthHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
    }

    private async Task<TerminalRoleCheck> CanRunReservationCloudAsync()
    {
        return _orderWebApiClient != null
            ? await _orderWebApiClient.CanRunCloudJobsAsync()
            : await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
    }

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static DateTime ParseDate(string? value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed.Date
            : DateTime.Today;
    }

    private static TimeSpan ParseTime(string? value)
    {
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : TimeSpan.Zero;
    }

    private static object ToDbDate(string? value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : DBNull.Value;
    }

    private static void NotifyNewReservations(int count)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NotificationService.Instance.ShowInfo(
                count == 1
                    ? "A new online reservation has arrived."
                    : $"{count} new online reservations have arrived.",
                "New Reservation");
        });
    }

    public void Dispose()
    {
        StopMaintenanceTimers();
        _httpClient.Dispose();
        _syncLock.Dispose();
    }

    private sealed record PullReservationsResponse(
        bool Success,
        string? Tenant,
        string? Date,
        int Count,
        List<CloudReservationDto>? Reservations);

    private sealed record PullReservationsResult(
        bool Success,
        List<CloudReservationDto> Reservations,
        string? ErrorMessage)
    {
        public static PullReservationsResult Ok(List<CloudReservationDto> reservations) => new(true, reservations, null);
        public static PullReservationsResult Failed(string error) => new(false, new List<CloudReservationDto>(), error);
    }

    internal sealed record CloudReservationDto
    {
        public string Id { get; init; } = string.Empty;
        public string? LocalId { get; init; }
        public string? Reference { get; init; }
        public string? ReservationDate { get; init; }
        public string? ReservationTime { get; init; }
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int Covers { get; init; }
        public string? CustomerName { get; init; }
        public string? CustomerPhone { get; init; }
        public string? CustomerEmail { get; init; }
        public string? Notes { get; init; }
        public string? Allergies { get; init; }
        public string? PromoCode { get; init; }
        public string? Status { get; init; }
        public string? Source { get; init; }
        public string? TableNumber { get; init; }
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int DepositAmountPence { get; init; }
        public string? PosSeenAt { get; init; }
        public string? PosPrintStatus { get; init; }
        public string? CreatedAt { get; init; }
        public string? UpdatedAt { get; init; }
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }
}

public sealed record ReservationSyncResult(bool Success, int NewReservations, int UpdatedReservations, string Message);

public sealed class ReservationSyncCompletedEventArgs : EventArgs
{
    public ReservationSyncCompletedEventArgs(ReservationSyncResult result)
    {
        Result = result;
    }

    public ReservationSyncResult Result { get; }
}
