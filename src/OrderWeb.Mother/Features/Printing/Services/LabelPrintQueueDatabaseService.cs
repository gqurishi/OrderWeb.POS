using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>Atomic database operations for the Mother-owned durable label queue.</summary>
public sealed class LabelPrintQueueDatabaseService
{
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);
    private readonly DatabaseService _database;

    public LabelPrintQueueDatabaseService(DatabaseService database) => _database = database;

    public async Task RecoverStaleClaimsAsync(string actor)
    {
        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE label_print_jobs
            SET status = CASE WHEN status = 'printing' THEN 'needs_attention' ELSE 'retry_waiting' END,
                attention_at = CASE WHEN status = 'printing' THEN UTC_TIMESTAMP() ELSE attention_at END,
                next_attempt_at = CASE WHEN status = 'claimed' THEN UTC_TIMESTAMP() ELSE NULL END,
                error_message = CASE
                    WHEN status = 'printing' THEN 'Mother stopped during transmission; printing outcome is uncertain.'
                    ELSE 'Stale Mother claim released for retry.'
                END,
                claimed_by_instance = NULL, claim_token = NULL, claimed_at = NULL
            WHERE status IN ('claimed', 'printing') AND claimed_at < @cutoff";
        command.Parameters.AddWithValue("@cutoff", DateTime.UtcNow - StaleClaimAge);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<ClaimedLabelPrintJob?> ClaimNextAsync(string motherInstance)
    {
        using var connection = await _database.GetConnectionAsync();
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        string? id;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = @"
                SELECT j.id
                FROM label_print_jobs j
                JOIN network_printers p ON p.id = j.printer_id
                WHERE j.status IN ('created', 'pending', 'retry_waiting')
                  AND (j.next_attempt_at IS NULL OR j.next_attempt_at <= UTC_TIMESTAMP())
                  AND p.is_enabled = TRUE
                  AND p.printer_type = 'label'
                  AND NOT EXISTS (
                      SELECT 1 FROM label_print_jobs active
                      WHERE active.printer_id = j.printer_id
                        AND active.status IN ('claimed', 'printing'))
                ORDER BY j.created_at, j.id
                LIMIT 1
                FOR UPDATE SKIP LOCKED";
            id = Convert.ToString(await select.ExecuteScalarAsync());
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            await transaction.CommitAsync();
            return null;
        }

        var token = Guid.NewGuid().ToString("D");
        using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = @"
                UPDATE label_print_jobs
                SET status = 'claimed', claimed_by_instance = @instance,
                    claim_token = @token, claimed_at = UTC_TIMESTAMP(), error_message = NULL
                WHERE id = @id AND status IN ('created', 'pending', 'retry_waiting')";
            claim.Parameters.AddWithValue("@instance", motherInstance);
            claim.Parameters.AddWithValue("@token", token);
            claim.Parameters.AddWithValue("@id", id);
            if (await claim.ExecuteNonQueryAsync() != 1)
            {
                await transaction.RollbackAsync();
                return null;
            }
        }
        await InsertEventAsync(connection, transaction, id, null, "claimed", motherInstance, "Claimed by Mother queue worker.");
        await transaction.CommitAsync();
        return new ClaimedLabelPrintJob(await GetJobByClaimAsync(token), token);
    }

    public async Task BeginPrintingAsync(string jobId, string claimToken, string motherInstance, byte[] tpclData, string sha256)
    {
        using var connection = await _database.GetConnectionAsync();
        using var transaction = await connection.BeginTransactionAsync();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            UPDATE label_print_jobs
            SET status = 'printing', generated_tpcl_data = @data,
                generated_payload_sha256 = @hash, attempt_at = UTC_TIMESTAMP()
            WHERE id = @id AND status = 'claimed' AND claim_token = @token
              AND claimed_by_instance = @instance";
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@token", claimToken);
        command.Parameters.AddWithValue("@instance", motherInstance);
        command.Parameters.AddWithValue("@data", tpclData);
        command.Parameters.AddWithValue("@hash", sha256);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("The label claim was lost before printing started.");
        await InsertEventAsync(connection, transaction, jobId, "claimed", "printing", motherInstance, "TPCL transmission started.");
        await transaction.CommitAsync();
    }

    public async Task FinishAttemptAsync(string jobId, string claimToken, string motherInstance, ToshibaNetworkResult result)
    {
        using var connection = await _database.GetConnectionAsync();
        using var transaction = await connection.BeginTransactionAsync();
        int retryCount;
        int maxRetries;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = @"
                SELECT retry_count, max_retries FROM label_print_jobs
                WHERE id = @id AND status = 'printing' AND claim_token = @token
                  AND claimed_by_instance = @instance FOR UPDATE";
            read.Parameters.AddWithValue("@id", jobId);
            read.Parameters.AddWithValue("@token", claimToken);
            read.Parameters.AddWithValue("@instance", motherInstance);
            using var reader = await read.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("The active label claim was lost.");
            retryCount = reader.GetInt32(0);
            maxRetries = reader.GetInt32(1);
        }

        var confirmedDelivery = result.DataAccepted && result.ExpectedProtocolConfirmed;
        var uncertain = result.DataAccepted && !result.ExpectedProtocolConfirmed;
        var exhausted = !result.DataAccepted && retryCount + 1 >= maxRetries;
        var finalStatus = confirmedDelivery ? "completed" : uncertain || exhausted ? "needs_attention" : "retry_waiting";
        var retryDelaySeconds = Math.Min(900, 10 * (1 << Math.Min(retryCount, 6)));
        var understandableError = Explain(result, uncertain, exhausted);

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE label_print_jobs
                SET status = @status,
                    retry_count = retry_count + @incrementRetry,
                    next_attempt_at = @nextAttempt,
                    attention_at = @attention,
                    network_reachable = @network, port_reachable = @port,
                    protocol_confirmed = @protocol, data_accepted = @accepted,
                    printer_status_response = @response, transport_phase = @phase,
                    transport_completed_at = @finished, error_message = @error,
                    completed_at = @completed,
                    claimed_by_instance = NULL, claim_token = NULL, claimed_at = NULL
                WHERE id = @id AND claim_token = @token";
            update.Parameters.AddWithValue("@status", finalStatus);
            update.Parameters.AddWithValue("@incrementRetry", result.DataAccepted ? 0 : 1);
            update.Parameters.AddWithValue("@nextAttempt", finalStatus == "retry_waiting" ? DateTime.UtcNow.AddSeconds(retryDelaySeconds) : (object)DBNull.Value);
            update.Parameters.AddWithValue("@attention", finalStatus == "needs_attention" ? DateTime.UtcNow : (object)DBNull.Value);
            update.Parameters.AddWithValue("@network", result.NetworkReachable);
            update.Parameters.AddWithValue("@port", result.PortReachable);
            update.Parameters.AddWithValue("@protocol", result.ExpectedProtocolConfirmed);
            update.Parameters.AddWithValue("@accepted", result.DataAccepted);
            update.Parameters.AddWithValue("@response", result.PrinterStatusResponse ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@phase", result.Phase.ToString().ToLowerInvariant());
            update.Parameters.AddWithValue("@finished", result.FinishedAt.UtcDateTime);
            update.Parameters.AddWithValue("@error", understandableError ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@completed", confirmedDelivery ? result.FinishedAt.UtcDateTime : (object)DBNull.Value);
            update.Parameters.AddWithValue("@id", jobId);
            update.Parameters.AddWithValue("@token", claimToken);
            if (await update.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("The label result could not be recorded.");
        }

        if (!confirmedDelivery)
            await InsertEventAsync(connection, transaction, jobId, "printing", "failed", motherInstance, understandableError);
        await InsertEventAsync(connection, transaction, jobId, confirmedDelivery ? "printing" : "failed", finalStatus, motherInstance,
            confirmedDelivery ? "Printer returned a valid Toshiba status response after accepting the batch." : understandableError);
        await transaction.CommitAsync();
    }

    public async Task<LabelQueueActionResult> ManualRetryAsync(string jobId, string actor, bool acknowledgePossibleDuplicate)
    {
        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE label_print_jobs
            SET status = 'pending', next_attempt_at = UTC_TIMESTAMP(), attention_at = NULL,
                error_message = NULL, claimed_by_instance = NULL, claim_token = NULL, claimed_at = NULL
            WHERE id = @id AND status IN ('failed', 'retry_waiting', 'needs_attention', 'outcome_unknown')
              AND (COALESCE(data_accepted, FALSE) = FALSE OR @acknowledged = TRUE)";
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@acknowledged", acknowledgePossibleDuplicate);
        var changed = await command.ExecuteNonQueryAsync() == 1;
        return changed
            ? new LabelQueueActionResult(true, "Label job queued for manual retry.", jobId)
            : new LabelQueueActionResult(false, "Retry was refused. If data may already have reached the printer, acknowledge the duplicate-label risk first.", jobId);
    }

    public async Task<LabelQueueActionResult> ManualReprintAsync(string originalJobId, string actor)
    {
        var newId = Guid.NewGuid().ToString("D");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"reprint:{originalJobId}:{newId}"))).ToLowerInvariant();
        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO label_print_jobs
                (id, printer_id, media_profile_id, order_id, order_item_id, source_terminal,
                 client_request_id, idempotency_key, send_revision, job_type, printable_name_snapshot,
                 payload_snapshot, label_template_version, quantity_copies, status, retry_count,
                 max_retries, created_at, reprint_of_job_id)
            SELECT @newId, printer_id, media_profile_id, order_id, order_item_id, @actor,
                   NULL, @key, send_revision, job_type, printable_name_snapshot,
                   payload_snapshot, label_template_version, quantity_copies, 'pending', 0,
                   max_retries, UTC_TIMESTAMP(), id
            FROM label_print_jobs WHERE id = @originalId";
        command.Parameters.AddWithValue("@newId", newId);
        command.Parameters.AddWithValue("@originalId", originalJobId);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@key", key);
        return await command.ExecuteNonQueryAsync() == 1
            ? new LabelQueueActionResult(true, "A new auditable reprint job was created.", newId)
            : new LabelQueueActionResult(false, "The original label job was not found.");
    }

    public async Task<LabelQueueActionResult> CancelStaleAsync(string jobId, string actor, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return new(false, "A cancellation reason is required.", jobId);
        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE label_print_jobs
            SET status = 'cancelled', cancelled_at = UTC_TIMESTAMP(), cancelled_by = @actor,
                cancellation_reason = @reason, next_attempt_at = NULL,
                claimed_by_instance = NULL, claim_token = NULL, claimed_at = NULL
            WHERE id = @id AND status IN ('created', 'pending', 'claimed', 'failed', 'retry_waiting')";
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@reason", reason.Trim());
        return await command.ExecuteNonQueryAsync() == 1
            ? new LabelQueueActionResult(true, "Stale label job cancelled.", jobId)
            : new LabelQueueActionResult(false, "This job is printing, completed, uncertain, or already cancelled and cannot be cancelled safely.", jobId);
    }

    private async Task<LabelPrintJob> GetJobByClaimAsync(string token)
    {
        using var connection = await _database.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM label_print_jobs WHERE claim_token = @token AND status = 'claimed'";
        command.Parameters.AddWithValue("@token", token);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("The claimed label job could not be loaded.");
        return MapJob(reader);
    }

    private static LabelPrintJob MapJob(MySqlDataReader reader)
    {
        var payload = reader.GetString("payload_snapshot");
        return new LabelPrintJob
        {
            Id = reader.GetString("id"), PrinterId = reader.GetInt32("printer_id"),
            MediaProfileId = reader.GetString("media_profile_id"),
            OrderId = reader.IsDBNull("order_id") ? null : reader.GetInt32("order_id"),
            OrderItemId = reader.IsDBNull("order_item_id") ? null : reader.GetInt32("order_item_id"),
            SourceTerminal = reader.GetString("source_terminal"),
            IdempotencyKey = reader.GetString("idempotency_key"), SendRevision = reader.GetInt32("send_revision"),
            JobType = Enum.Parse<LabelJobType>(reader.GetString("job_type"), true),
            Content = JsonSerializer.Deserialize<LabelContentSnapshot>(payload)
                ?? throw new InvalidDataException("The label payload snapshot is invalid."),
            LabelTemplateVersion = reader.GetString("label_template_version"),
            QuantityCopies = reader.GetInt32("quantity_copies"), RetryCount = reader.GetInt32("retry_count"),
            MaxRetries = reader.GetInt32("max_retries"), ClaimedByInstance = reader.GetString("claimed_by_instance"),
            ClaimToken = reader.GetString("claim_token"), ClaimedAt = reader.GetDateTime("claimed_at"),
            CreatedAt = reader.GetDateTime("created_at")
        };
    }

    private static string Explain(ToshibaNetworkResult result, bool uncertain, bool exhausted)
    {
        if (uncertain) return "Label data was sent, but Toshiba confirmation was not received. Check the printer before manually retrying.";
        if (exhausted) return $"Maximum label retries reached. {result.ErrorMessage ?? "The printer could not be reached."}";
        if (!result.NetworkReachable) return "The printer network address is unreachable. Check power, Ethernet cable and IP address.";
        if (!result.PortReachable) return "The printer answered on the network, but TCP port 9100 is unavailable.";
        return result.ErrorMessage ?? "The label could not be sent and will retry automatically.";
    }

    private static async Task InsertEventAsync(MySqlConnection connection, MySqlTransaction transaction, string jobId,
        string? from, string to, string actor, string? message)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO label_print_job_events (job_id, from_status, to_status, actor, message)
            VALUES (@job, @from, @to, @actor, @message)";
        command.Parameters.AddWithValue("@job", jobId);
        command.Parameters.AddWithValue("@from", from ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@to", to);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@message", message ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
