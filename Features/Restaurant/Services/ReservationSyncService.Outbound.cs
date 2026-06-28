using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed partial class ReservationSyncService
{
    private CancellationTokenSource? _maintenanceCts;

    public async Task<(bool Success, string Message, CloudReservation? Reservation)> CreatePosReservationAsync(
        CreatePosReservationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return (false, "Customer name is required.", null);
        }

        if (request.Covers <= 0)
        {
            return (false, "Covers must be at least 1.", null);
        }

        await EnsureSchemaAsync();

        var localId = Guid.NewGuid().ToString("N");
        var tempCloudId = $"local:{localId}";

        var reservation = new CloudReservation
        {
            CloudId = tempCloudId,
            LocalId = localId,
            Reference = $"POS-{localId[..8].ToUpperInvariant()}",
            ReservationDate = request.ReservationDate.Date,
            ReservationTime = request.ReservationTime,
            Covers = request.Covers,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone.Trim(),
            Notes = request.Notes.Trim(),
            Allergies = request.Allergies.Trim(),
            TableNumber = request.TableNumber.Trim(),
            Status = "confirmed",
            Source = string.IsNullOrWhiteSpace(request.Channel) ? "pos" : request.Channel.Trim(),
            UploadStatus = "pending",
            LastUpdatedAt = DateTime.UtcNow
        };

        await InsertLocalReservationAsync(reservation);

        var upload = await TryUploadReservationAsync(reservation);
        if (upload.Success && upload.Reservation != null)
        {
            SyncCompleted?.Invoke(this, new ReservationSyncCompletedEventArgs(
                new ReservationSyncResult(true, 1, 0, "Booking uploaded to OrderWeb.")));
            return (true, upload.Message, upload.Reservation);
        }

        SyncCompleted?.Invoke(this, new ReservationSyncCompletedEventArgs(
            new ReservationSyncResult(true, 1, 0, "Booking saved on till. Will upload when online.")));
        return (true, upload.Message ?? "Saved locally. Upload pending.", reservation);
    }

    public async Task<int> UploadPendingReservationsAsync()
    {
        await EnsureSchemaAsync();
        var uploaded = 0;

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            SELECT id, cloud_id, local_id, reference, reservation_date, reservation_time, covers,
                   customer_name, customer_phone, customer_email, notes, allergies, status,
                   source, table_number, deposit_amount_pence, upload_status
            FROM cloud_reservations
            WHERE upload_status IN ('pending', 'failed')
            ORDER BY last_updated_at
            LIMIT 20
            """,
            connection);

        var pending = new List<CloudReservation>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                pending.Add(ReadReservationForUpload(reader));
            }
        }

        foreach (var row in pending)
        {
            var result = await TryUploadReservationAsync(row);
            if (result.Success)
            {
                uploaded++;
            }
        }

        return uploaded;
    }

    public async Task<int> ProcessPendingAcksAsync()
    {
        await EnsureSchemaAsync();
        var sent = 0;

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            SELECT cloud_reservation_id, ack_status, attempts
            FROM reservation_pending_acks
            WHERE attempts < 10
            ORDER BY created_at
            LIMIT 20
            """,
            connection);

        var rows = new List<(string Id, string Status, int Attempts)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString("cloud_reservation_id"), reader.GetString("ack_status"), reader.GetInt32("attempts")));
            }
        }

        foreach (var row in rows)
        {
            if (await AckReservationAsync(row.Id, row.Status))
            {
                await DeletePendingAckAsync(row.Id);
                sent++;
            }
            else
            {
                await IncrementPendingAckAsync(row.Id, "ACK failed");
            }
        }

        return sent;
    }

    private async Task<(bool Success, string? Message, CloudReservation? Reservation)> TryUploadReservationAsync(
        CloudReservation reservation)
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            if (!IsConfigured(config))
            {
                return (false, "Cloud is not configured.", reservation);
            }

            var endpoint = $"{NormalizeApiBaseUrl(config)}/pos/reservations";
            var channel = NormalizeChannel(reservation.Source);
            var phone = string.IsNullOrWhiteSpace(reservation.CustomerPhone)
                ? (channel == "walk_in" ? "WALKIN" : "")
                : reservation.CustomerPhone.Trim();

            var payload = new
            {
                tenant = config.GetValueOrDefault("tenant_slug", ""),
                local_id = reservation.LocalId ?? reservation.CloudId,
                channel,
                reservationDate = reservation.ReservationDate.ToString("yyyy-MM-dd"),
                reservationTime = reservation.ReservationTime.ToString(@"HH\:mm"),
                covers = reservation.Covers,
                customerName = reservation.CustomerName,
                customerPhone = phone,
                notes = reservation.Notes,
                allergies = reservation.Allergies
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            AddAuthHeaders(request, config.GetValueOrDefault("api_key", ""));

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                await MarkUploadFailedAsync(reservation.CloudId, $"HTTP {(int)response.StatusCode}");
                return (false, $"Cloud upload failed: HTTP {(int)response.StatusCode}", reservation);
            }

            var parsed = JsonSerializer.Deserialize<CreateReservationResponse>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed?.Reservation == null || string.IsNullOrWhiteSpace(parsed.Reservation.Id))
            {
                await MarkUploadFailedAsync(reservation.CloudId, "Invalid cloud response");
                return (false, "Cloud did not return a reservation id.", reservation);
            }

            await ApplyCloudUploadResultAsync(
                reservation.CloudId,
                parsed.Reservation.Id,
                parsed.Reservation.Reference ?? reservation.Reference,
                parsed.Reservation.LocalId ?? reservation.LocalId);

            reservation.CloudId = parsed.Reservation.Id;
            reservation.Reference = parsed.Reservation.Reference ?? reservation.Reference;
            reservation.UploadStatus = "synced";

            var message = parsed.Created
                ? $"Uploaded to OrderWeb ({reservation.Reference})."
                : $"Already on OrderWeb ({reservation.Reference}).";
            return (true, message, reservation);
        }
        catch (Exception ex)
        {
            await MarkUploadFailedAsync(reservation.CloudId, ex.Message);
            AppDiagnostics.Log($"Reservation upload failed: {ex.Message}");
            return (false, "Saved on till. Upload will retry automatically.", reservation);
        }
    }

    private async Task InsertLocalReservationAsync(CloudReservation reservation)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            INSERT INTO cloud_reservations (
                cloud_id, local_id, reference, reservation_date, reservation_time, covers,
                customer_name, customer_phone, customer_email, notes, allergies, status,
                source, table_number, deposit_amount_pence, upload_status, last_updated_at
            ) VALUES (
                @cloudId, @localId, @reference, @reservationDate, @reservationTime, @covers,
                @customerName, @customerPhone, @customerEmail, @notes, @allergies, @status,
                @source, @tableNumber, 0, @uploadStatus, UTC_TIMESTAMP()
            )
            """,
            connection);

        command.Parameters.AddWithValue("@cloudId", reservation.CloudId);
        command.Parameters.AddWithValue("@localId", (object?)reservation.LocalId ?? DBNull.Value);
        command.Parameters.AddWithValue("@reference", reservation.Reference);
        command.Parameters.AddWithValue("@reservationDate", reservation.ReservationDate.Date);
        command.Parameters.AddWithValue("@reservationTime", reservation.ReservationTime);
        command.Parameters.AddWithValue("@covers", reservation.Covers);
        command.Parameters.AddWithValue("@customerName", reservation.CustomerName);
        command.Parameters.AddWithValue("@customerPhone", reservation.CustomerPhone);
        command.Parameters.AddWithValue("@customerEmail", reservation.CustomerEmail);
        command.Parameters.AddWithValue("@notes", reservation.Notes);
        command.Parameters.AddWithValue("@allergies", reservation.Allergies);
        command.Parameters.AddWithValue("@status", reservation.Status);
        command.Parameters.AddWithValue("@source", reservation.Source);
        command.Parameters.AddWithValue("@tableNumber", reservation.TableNumber);
        command.Parameters.AddWithValue("@uploadStatus", reservation.UploadStatus);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ApplyCloudUploadResultAsync(string oldCloudId, string newCloudId, string reference, string? localId = null)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            UPDATE cloud_reservations
            SET cloud_id = @newCloudId,
                reference = @reference,
                local_id = COALESCE(@localId, local_id),
                upload_status = 'synced',
                last_updated_at = UTC_TIMESTAMP()
            WHERE cloud_id = @oldCloudId
               OR (@localId IS NOT NULL AND local_id = @localId)
            """,
            connection);
        command.Parameters.AddWithValue("@newCloudId", newCloudId);
        command.Parameters.AddWithValue("@reference", reference);
        command.Parameters.AddWithValue("@oldCloudId", oldCloudId);
        command.Parameters.AddWithValue("@localId", (object?)localId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string NormalizeChannel(string? source)
    {
        var value = (source ?? "walk_in").Trim().ToLowerInvariant();
        return value switch
        {
            "phone" => "phone",
            "walk_in" or "walkin" or "walk-in" or "pos" => "walk_in",
            _ => "walk_in"
        };
    }

    public async Task<(bool Success, string Message)> UpdateReservationStatusAsync(
        string cloudId,
        string status,
        string? localId = null)
    {
        if (string.IsNullOrWhiteSpace(cloudId) && string.IsNullOrWhiteSpace(localId))
        {
            return (false, "Reservation id missing.");
        }

        await EnsureSchemaAsync();

        if (!string.IsNullOrWhiteSpace(cloudId) && !cloudId.StartsWith("local:", StringComparison.Ordinal))
        {
            var ackOk = await AckReservationAsync(cloudId, status);
            if (!ackOk)
            {
                await QueueAckRetryAsync(cloudId, status);
            }
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            UPDATE cloud_reservations
            SET status = @status, last_updated_at = UTC_TIMESTAMP()
            WHERE cloud_id = @cloudId
               OR (@localId IS NOT NULL AND local_id = @localId)
            """,
            connection);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@cloudId", cloudId);
        command.Parameters.AddWithValue("@localId", (object?)localId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();

        SyncCompleted?.Invoke(this, new ReservationSyncCompletedEventArgs(
            new ReservationSyncResult(true, 0, 1, $"Status updated to {status}.")));

        return (true, $"Status updated to {status}.");
    }

    public async Task<(bool Success, string Message)> PatchReservationAsync(
        string cloudId,
        DateTime? reservationDate = null,
        TimeSpan? reservationTime = null,
        int? covers = null,
        string? status = null,
        string? localId = null)
    {
        var config = await _databaseService.GetCloudConfigAsync();
        if (!IsConfigured(config))
        {
            return (false, "Cloud is not configured.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["tenant"] = config.GetValueOrDefault("tenant_slug", "")
        };

        if (!string.IsNullOrWhiteSpace(cloudId) && !cloudId.StartsWith("local:", StringComparison.Ordinal))
        {
            payload["reservation_id"] = cloudId;
        }
        else if (!string.IsNullOrWhiteSpace(localId))
        {
            payload["local_id"] = localId;
        }
        else
        {
            return (false, "Cloud reservation id required for updates.");
        }

        if (reservationDate.HasValue)
        {
            payload["reservationDate"] = reservationDate.Value.ToString("yyyy-MM-dd");
        }

        if (reservationTime.HasValue)
        {
            payload["reservationTime"] = reservationTime.Value.ToString(@"HH\:mm");
        }

        if (covers.HasValue)
        {
            payload["covers"] = covers.Value;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            payload["status"] = status;
        }

        var endpoint = $"{NormalizeApiBaseUrl(config)}/pos/reservations";
        using var request = new HttpRequestMessage(HttpMethod.Patch, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        AddAuthHeaders(request, config.GetValueOrDefault("api_key", ""));

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return (false, $"Cloud update failed: HTTP {(int)response.StatusCode}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                """
                UPDATE cloud_reservations
                SET status = @status, last_updated_at = UTC_TIMESTAMP()
                WHERE cloud_id = @cloudId OR (@localId IS NOT NULL AND local_id = @localId)
                """,
                connection);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@cloudId", cloudId);
            command.Parameters.AddWithValue("@localId", (object?)localId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        return (true, "Reservation updated on OrderWeb.");
    }

    private async Task MarkUploadFailedAsync(string cloudId, string error)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            UPDATE cloud_reservations
            SET upload_status = 'failed',
                notes = CASE
                    WHEN notes IS NULL OR notes = '' THEN @error
                    ELSE CONCAT(notes, ' | Upload: ', @error)
                END,
                last_updated_at = UTC_TIMESTAMP()
            WHERE cloud_id = @cloudId
            """,
            connection);
        command.Parameters.AddWithValue("@cloudId", cloudId);
        command.Parameters.AddWithValue("@error", error.Length > 200 ? error[..200] : error);
        await command.ExecuteNonQueryAsync();
    }

    private async Task QueueAckRetryAsync(string cloudReservationId, string status)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            INSERT INTO reservation_pending_acks (cloud_reservation_id, ack_status, attempts, created_at)
            VALUES (@cloudId, @status, 0, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE ack_status = VALUES(ack_status), last_error = NULL
            """,
            connection);
        command.Parameters.AddWithValue("@cloudId", cloudReservationId);
        command.Parameters.AddWithValue("@status", status);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeletePendingAckAsync(string cloudReservationId)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            "DELETE FROM reservation_pending_acks WHERE cloud_reservation_id = @cloudId",
            connection);
        command.Parameters.AddWithValue("@cloudId", cloudReservationId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task IncrementPendingAckAsync(string cloudReservationId, string error)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            UPDATE reservation_pending_acks
            SET attempts = attempts + 1,
                last_error = @error,
                last_attempt_at = UTC_TIMESTAMP()
            WHERE cloud_reservation_id = @cloudId
            """,
            connection);
        command.Parameters.AddWithValue("@cloudId", cloudReservationId);
        command.Parameters.AddWithValue("@error", error);
        await command.ExecuteNonQueryAsync();
    }

    private void StartMaintenanceTimers()
    {
        StopMaintenanceTimers();
        _maintenanceCts = new CancellationTokenSource();
        _ = Task.Run(() => RunMaintenanceAsync(_maintenanceCts.Token));
    }

    private void StopMaintenanceTimers()
    {
        _maintenanceCts?.Cancel();
        _maintenanceCts?.Dispose();
        _maintenanceCts = null;
    }

    private async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
                await UploadPendingReservationsAsync();
                await ProcessPendingAcksAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log($"Reservation maintenance error: {ex.Message}");
            }
        }
    }

    private static CloudReservation ReadReservationForUpload(MySqlDataReader reader)
    {
        static bool IsNull(MySqlDataReader r, string c) => r.IsDBNull(r.GetOrdinal(c));
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
            CustomerEmail = IsNull(reader, "customer_email") ? "" : reader.GetString("customer_email"),
            Notes = IsNull(reader, "notes") ? "" : reader.GetString("notes"),
            Allergies = IsNull(reader, "allergies") ? "" : reader.GetString("allergies"),
            Status = reader.GetString("status"),
            Source = reader.GetString("source"),
            TableNumber = reader.GetString("table_number"),
            DepositAmountPence = reader.GetInt32("deposit_amount_pence"),
            UploadStatus = reader.GetString("upload_status")
        };
    }

    private sealed record CreateReservationResponse(bool Success, bool Created, CreatedReservationDto? Reservation);

    private sealed record CreatedReservationDto(string Id, string? Reference, string? LocalId);
}
