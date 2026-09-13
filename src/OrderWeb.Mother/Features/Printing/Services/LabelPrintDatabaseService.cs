using System.Text.Json;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Persists label profiles and immutable label jobs. It stores no printer
/// drivers or Toshiba executable software.
/// </summary>
public sealed class LabelPrintDatabaseService
{
    private readonly DatabaseService _database;

    public LabelPrintDatabaseService(DatabaseService database) => _database = database;

    public async Task<IReadOnlyList<LabelMediaProfile>> GetActiveMediaProfilesAsync()
    {
        var result = new List<LabelMediaProfile>();
        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, profile_name, manufacturer, model_code, width_mm, height_mm,
                   gap_mm, sensor_type, darkness, speed_ips, horizontal_offset_mm,
                   vertical_offset_mm, finishing_mode, is_active
            FROM label_media_profiles
            WHERE is_active = TRUE
            ORDER BY width_mm, height_mm, profile_name";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new LabelMediaProfile
            {
                Id = reader.GetString("id"),
                ProfileName = reader.GetString("profile_name"),
                Manufacturer = reader.GetString("manufacturer"),
                ModelCode = reader.GetString("model_code"),
                WidthMm = reader.GetDecimal("width_mm"),
                HeightMm = reader.GetDecimal("height_mm"),
                GapMm = reader.GetDecimal("gap_mm"),
                SensorType = ParseSensor(reader.GetString("sensor_type")),
                Darkness = reader.GetInt32("darkness"),
                SpeedIps = reader.GetInt32("speed_ips"),
                HorizontalOffsetMm = reader.GetDecimal("horizontal_offset_mm"),
                VerticalOffsetMm = reader.GetDecimal("vertical_offset_mm"),
                FinishingMode = reader.GetString("finishing_mode") == "cut" ? LabelFinishingMode.Cutter : LabelFinishingMode.TearOff,
                IsActive = reader.GetBoolean("is_active")
            });
        }
        return result;
    }

    public async Task EnsureBuiltInProfilesAsync()
    {
        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO label_media_profiles
                (id, profile_name, manufacturer, model_code, width_mm, height_mm, gap_mm, sensor_type, darkness, speed_ips,
                 horizontal_offset_mm, vertical_offset_mm, finishing_mode, is_active)
            VALUES
                ('toshiba-60x40-container', 'Toshiba 60 × 40 mm Container', 'toshiba', 'b-fv4d-gs14', 60, 40, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
                ('toshiba-51x30-compact', 'Toshiba 51 × 30 mm Compact', 'toshiba', 'b-fv4d-gs14', 51, 30, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
                ('toshiba-80x50-delivery', 'Toshiba 80 × 50 mm Delivery', 'toshiba', 'b-fv4d-gs14', 80, 50, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
                ('xprinter-60x40-container', 'Xprinter 60 × 40 mm Container', 'xprinter', 'xp-421b', 60, 40, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
                ('xprinter-51x30-compact', 'Xprinter 51 × 30 mm Compact', 'xprinter', 'xp-421b', 51, 30, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
                ('xprinter-80x50-delivery', 'Xprinter 80 × 50 mm Delivery', 'xprinter', 'xp-421b', 80, 50, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
                ('brother-60x40-container', 'Brother 60 × 40 mm Container', 'brother', 'td-4420dn', 60, 40, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
                ('brother-51x30-compact', 'Brother 51 × 30 mm Compact', 'brother', 'td-4420dn', 51, 30, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
                ('brother-80x50-delivery', 'Brother 80 × 50 mm Delivery', 'brother', 'td-4420dn', 80, 50, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1)
            ON DUPLICATE KEY UPDATE updated_at = CURRENT_TIMESTAMP";
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.Number == 1146)
        {
            // Label tables are created by the label migration. Do not block printer setup if they are not applied yet.
        }
    }

    public async Task<bool> HasJobsForSourceRequestPrefixAsync(string sourceTerminal, string requestPrefix)
    {
        if (string.IsNullOrWhiteSpace(sourceTerminal) || string.IsNullOrWhiteSpace(requestPrefix))
            return false;

        var escaped = requestPrefix.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        try
        {
            using var connection = await _database.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM label_print_jobs
                WHERE source_terminal = @source
                  AND client_request_id LIKE @prefix ESCAPE '\\'";
            command.Parameters.AddWithValue("@source", sourceTerminal.Trim());
            command.Parameters.AddWithValue("@prefix", escaped + "%");
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        catch (MySqlException ex) when (ex.Number == 1146)
        {
            return false;
        }
    }

    public async Task<string> EnqueueAsync(LabelPrintJob job)
    {
        ValidateNewJob(job);
        var payloadSnapshot = JsonSerializer.Serialize(job.Content);

        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO label_print_jobs
                (id, printer_id, media_profile_id, order_id, order_item_id, source_terminal,
                 client_request_id, idempotency_key, send_revision, job_type, printable_name_snapshot, payload_snapshot,
                 label_template_version, quantity_copies, generated_tpcl_data,
                 generated_payload_sha256, status, retry_count, max_retries, error_message,
                 attempt_at, completed_at, reprint_of_job_id)
            VALUES
                (@id, @printerId, @mediaProfileId, @orderId, @orderItemId, @sourceTerminal,
                 @clientRequestId, @idempotencyKey, @sendRevision, @jobType, @printableName, @payloadSnapshot,
                 @templateVersion, @copies, @tpclData, @payloadHash, @status,
                 @retryCount, @maxRetries, @errorMessage, @attemptAt, @completedAt, @reprintOf)";
        command.Parameters.AddWithValue("@id", job.Id);
        command.Parameters.AddWithValue("@printerId", job.PrinterId);
        command.Parameters.AddWithValue("@mediaProfileId", job.MediaProfileId);
        command.Parameters.AddWithValue("@orderId", job.OrderId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@orderItemId", job.OrderItemId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@sourceTerminal", job.SourceTerminal.Trim());
        command.Parameters.AddWithValue("@clientRequestId", job.ClientRequestId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@idempotencyKey", job.IdempotencyKey);
        command.Parameters.AddWithValue("@sendRevision", job.SendRevision);
        command.Parameters.AddWithValue("@jobType", ToDb(job.JobType));
        command.Parameters.AddWithValue("@printableName", job.Content.Name);
        command.Parameters.AddWithValue("@payloadSnapshot", payloadSnapshot);
        command.Parameters.AddWithValue("@templateVersion", job.LabelTemplateVersion);
        command.Parameters.AddWithValue("@copies", job.QuantityCopies);
        command.Parameters.AddWithValue("@tpclData", job.GeneratedTpclData ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@payloadHash", job.GeneratedPayloadSha256 ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@status", ToDb(job.Status));
        command.Parameters.AddWithValue("@retryCount", job.RetryCount);
        command.Parameters.AddWithValue("@maxRetries", job.MaxRetries);
        command.Parameters.AddWithValue("@errorMessage", job.ErrorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@attemptAt", job.AttemptAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@completedAt", job.CompletedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@reprintOf", job.ReprintOfJobId ?? (object)DBNull.Value);

        try
        {
            await command.ExecuteNonQueryAsync();
            return job.Id;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            using var existing = connection.CreateCommand();
            existing.CommandText = @"
                SELECT id FROM label_print_jobs
                WHERE idempotency_key = @idempotencyKey
                   OR (source_terminal = @sourceTerminal AND client_request_id = @clientRequestId)
                LIMIT 1";
            existing.Parameters.AddWithValue("@sourceTerminal", job.SourceTerminal.Trim());
            existing.Parameters.AddWithValue("@clientRequestId", job.ClientRequestId ?? (object)DBNull.Value);
            existing.Parameters.AddWithValue("@idempotencyKey", job.IdempotencyKey);
            return Convert.ToString(await existing.ExecuteScalarAsync())
                ?? throw new InvalidOperationException("The duplicate label request could not be resolved.");
        }
    }

    /// <summary>
    /// Records transport evidence. Data accepted without physical confirmation is
    /// outcome_unknown and must not be automatically retried as a definite failure.
    /// </summary>
    public async Task RecordNetworkResultAsync(string jobId, ToshibaNetworkResult result)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("A label job ID is required.", nameof(jobId));
        ArgumentNullException.ThrowIfNull(result);

        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE label_print_jobs
            SET network_reachable = @network, port_reachable = @port,
                protocol_confirmed = @protocol, data_accepted = @accepted,
                physical_label_confirmed = @physical,
                printer_status_response = @response, transport_phase = @phase,
                transport_completed_at = @finished, error_message = @error,
                status = CASE
                    WHEN @physical = TRUE THEN 'completed'
                    WHEN @accepted = TRUE THEN 'outcome_unknown'
                    ELSE 'failed'
                END,
                completed_at = CASE WHEN @physical = TRUE THEN @finished ELSE completed_at END
            WHERE id = @id";
        command.Parameters.AddWithValue("@id", jobId.Trim());
        command.Parameters.AddWithValue("@network", result.NetworkReachable);
        command.Parameters.AddWithValue("@port", result.PortReachable);
        command.Parameters.AddWithValue("@protocol", result.ExpectedProtocolConfirmed);
        command.Parameters.AddWithValue("@accepted", result.DataAccepted);
        command.Parameters.AddWithValue("@physical", result.PhysicalLabelConfirmed);
        command.Parameters.AddWithValue("@response", result.PrinterStatusResponse ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@phase", result.Phase.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("@finished", result.FinishedAt.UtcDateTime);
        command.Parameters.AddWithValue("@error", result.ErrorMessage ?? (object)DBNull.Value);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("The label job was not found.");
    }

    public async Task PrepareNetworkAttemptAsync(string jobId, byte[] tpclData, string sha256)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("A label job ID is required.", nameof(jobId));
        if (tpclData is not { Length: > 0 }) throw new ArgumentException("Generated TPCL data is required.", nameof(tpclData));
        if (sha256?.Length != 64) throw new ArgumentException("A SHA-256 payload hash is required.", nameof(sha256));

        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE label_print_jobs
            SET generated_tpcl_data = @data, generated_payload_sha256 = @hash,
                retry_count = retry_count + IF(attempt_at IS NULL, 0, 1),
                status = 'printing', attempt_at = UTC_TIMESTAMP(),
                error_message = NULL
            WHERE id = @id AND status IN ('pending', 'failed')";
        command.Parameters.AddWithValue("@id", jobId.Trim());
        command.Parameters.AddWithValue("@data", tpclData);
        command.Parameters.AddWithValue("@hash", sha256);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("The label job is not pending or retryable.");
    }

    public async Task ConfirmPhysicalLabelAsync(string jobId, int printerId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("A label job ID is required.", nameof(jobId));
        if (printerId <= 0) throw new ArgumentOutOfRangeException(nameof(printerId));

        using var connection = await _database.GetConnectionAsync();
        using var transaction = await connection.BeginTransactionAsync();
        var confirmedAt = DateTime.UtcNow;
        using var job = connection.CreateCommand();
        job.Transaction = transaction;
        job.CommandText = @"
            UPDATE label_print_jobs
            SET physical_label_confirmed = TRUE, physical_confirmed_at = @confirmed,
                completed_at = @confirmed, status = 'completed', error_message = NULL
            WHERE id = @id AND printer_id = @printerId";
        job.Parameters.AddWithValue("@id", jobId.Trim());
        job.Parameters.AddWithValue("@printerId", printerId);
        job.Parameters.AddWithValue("@confirmed", confirmedAt);
        if (await job.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("The label job was not found for this printer.");

        using var printer = connection.CreateCommand();
        printer.Transaction = transaction;
        printer.CommandText = "UPDATE network_printers SET last_successful_physical_test = @confirmed WHERE id = @printerId";
        printer.Parameters.AddWithValue("@printerId", printerId);
        printer.Parameters.AddWithValue("@confirmed", confirmedAt);
        await printer.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static void ValidateNewJob(LabelPrintJob job)
    {
        if (job.PrinterId <= 0) throw new ArgumentOutOfRangeException(nameof(job.PrinterId));
        if (string.IsNullOrWhiteSpace(job.MediaProfileId)) throw new ArgumentException("A media profile is required.");
        if (string.IsNullOrWhiteSpace(job.SourceTerminal)) throw new ArgumentException("A source terminal is required.");
        if (job.QuantityCopies is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(job.QuantityCopies));
        if (job.IdempotencyKey.Length != 64 || job.IdempotencyKey.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A 64-character hexadecimal idempotency key is required.");
        if (job.SendRevision < 1) throw new ArgumentOutOfRangeException(nameof(job.SendRevision));
        if (job.Content is null || string.IsNullOrWhiteSpace(job.Content.Name))
            throw new ArgumentException("A printable item or component name is required.");
        if (job.Content.ContentType != ToDb(job.JobType) && job.JobType != LabelJobType.Test)
            throw new ArgumentException("Label job type and name snapshot type do not match.");
        if (job.Content.Name.Length > 300)
            throw new ArgumentOutOfRangeException(nameof(job.Content), "A printable label name cannot exceed 300 characters.");
    }

    private static LabelSensorType ParseSensor(string value) => value switch
    {
        "blackmark" => LabelSensorType.BlackMark,
        "continuous" => LabelSensorType.Continuous,
        _ => LabelSensorType.Gap
    };

    private static string ToDb(LabelJobType value) => value.ToString().ToLowerInvariant();
    private static string ToDb(LabelJobStatus value) => value switch
    {
        LabelJobStatus.RetryWaiting => "retry_waiting",
        LabelJobStatus.NeedsAttention => "needs_attention",
        LabelJobStatus.OutcomeUnknown => "outcome_unknown",
        _ => value.ToString().ToLowerInvariant()
    };
}
