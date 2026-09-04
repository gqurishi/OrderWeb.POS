using System.Globalization;
using System.Text.Json;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed partial class ReservationSyncService
{
    public async Task<(bool Success, string Message)> ProcessWebhookPayloadAsync(string body, string? eventType)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (false, "Empty webhook body.");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (string.IsNullOrWhiteSpace(eventType))
            {
                if (root.TryGetProperty("event", out var eventElement))
                {
                    eventType = eventElement.GetString();
                }
                else if (root.TryGetProperty("type", out var typeElement))
                {
                    eventType = typeElement.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(eventType))
            {
                return (false, "Missing event type.");
            }

            if (IsReservationEvent(eventType))
            {
                var dto = ParseReservationPayload(root);
                if (dto == null)
                {
                    return (false, "Invalid reservation payload.");
                }

                if (eventType.Equals("reservation_cancelled", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(dto.Status))
                {
                    dto = dto with { Status = "cancelled" };
                }

                var result = await ProcessInboundReservationAsync(dto, fromRealtime: true);
                return (result.Success, result.Message);
            }

            return (true, $"Ignored event {eventType}.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("ReservationWebhook", ex);
            return (false, ex.Message);
        }
    }

    internal async Task<ReservationSyncResult> ProcessInboundReservationAsync(
        CloudReservationDto dto,
        bool fromRealtime = false)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            return new ReservationSyncResult(false, 0, 0, "Reservation id missing.");
        }

        if (!string.IsNullOrWhiteSpace(dto.LocalId))
        {
            var ownUpload = await FindRowByLocalIdAsync(dto.LocalId);
            if (ownUpload.HasValue)
            {
                await ApplyCloudUploadResultAsync(
                    ownUpload.Value.CloudId,
                    dto.Id,
                    dto.Reference ?? ownUpload.Value.Reference,
                    dto.LocalId);
            }
        }

        var existsById = await ReservationExistsAsync(dto.Id);
        if (!existsById && !string.IsNullOrWhiteSpace(dto.Reference))
        {
            var existingCloudId = await FindCloudIdByReferenceAsync(dto.Reference);
            if (!string.IsNullOrWhiteSpace(existingCloudId))
            {
                if (existingCloudId.StartsWith("local:", StringComparison.Ordinal))
                {
                    await ApplyCloudUploadResultAsync(existingCloudId, dto.Id, dto.Reference, dto.LocalId);
                    existsById = true;
                }

                if (!existingCloudId.StartsWith("local:", StringComparison.Ordinal)
                    && !existingCloudId.Equals(dto.Id, StringComparison.Ordinal))
                {
                    dto = dto with { Id = existingCloudId };
                    existsById = true;
                }
            }
        }

        var upsert = await UpsertReservationAsync(dto);
        var isNew = upsert.IsNew;

        if (isNew)
        {
            var ackOk = await AckReservationAsync(dto.Id, "seen");
            if (!ackOk)
            {
                await QueueAckRetryAsync(dto.Id, "seen");
            }

            await MarkSeenAsync(dto.Id);
            NotifyNewReservations(1);
        }

        if (fromRealtime)
        {
            SyncCompleted?.Invoke(this, new ReservationSyncCompletedEventArgs(
                new ReservationSyncResult(true, isNew ? 1 : 0, upsert.Updated ? 1 : 0, "Live reservation received.")));
        }

        return new ReservationSyncResult(
            true,
            isNew ? 1 : 0,
            upsert.Updated ? 1 : 0,
            isNew ? "New reservation saved." : "Reservation updated.");
    }

    internal CloudReservationDto? ParseReservationFromWebSocket(JsonElement root)
    {
        return ParseReservationPayload(root);
    }

    private static CloudReservationDto? ParseReservationPayload(JsonElement root)
    {
        var eventType = GetRootString(root, "event") ?? GetRootString(root, "type");
        JsonElement data = root;
        if (root.TryGetProperty("reservation", out var reservationElement))
        {
            data = reservationElement;
        }
        else if (root.TryGetProperty("data", out var dataElement))
        {
            data = dataElement;
        }

        string? GetString(string name)
        {
            if (!data.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        int GetInt(string name, int fallback = 0)
        {
            if (!data.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            {
                return fallback;
            }

            if (element.TryGetInt32(out var value))
            {
                return value;
            }

            if (element.TryGetDecimal(out var decimalValue))
            {
                return (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        var id = GetString("id") ?? GetString("reservation_id") ?? GetString("reservationId");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return new CloudReservationDto
        {
            Id = id,
            LocalId = GetString("localId") ?? GetString("local_id"),
            Reference = GetString("reference"),
            ReservationDate = GetString("date") ?? GetString("reservationDate"),
            ReservationTime = GetString("time") ?? GetString("reservationTime"),
            Covers = GetInt("covers"),
            CustomerName = GetString("name") ?? GetString("customerName"),
            CustomerPhone = GetString("phone") ?? GetString("customerPhone"),
            CustomerEmail = GetString("email") ?? GetString("customerEmail"),
            Notes = GetString("notes"),
            Allergies = GetString("allergies"),
            PromoCode = GetString("promoCode")
                ?? GetString("promo_code")
                ?? GetString("promocode")
                ?? GetString("promotionCode")
                ?? GetString("promotion_code")
                ?? GetString("discountCode")
                ?? GetString("discount_code"),
            Status = GetString("status")
                ?? (eventType?.Equals("reservation_cancelled", StringComparison.OrdinalIgnoreCase) == true ? "cancelled" : null),
            Source = GetString("source") ?? GetString("channel"),
            TableNumber = GetString("tableNumber")
                ?? GetString("table_number")
                ?? GetString("tableNo")
                ?? GetString("table_no")
                ?? GetString("table"),
            DepositAmountPence = GetInt("depositAmountPence"),
            CreatedAt = GetString("createdAt"),
            UpdatedAt = GetString("updatedAt")
        };
    }

    private static bool IsReservationEvent(string eventType)
    {
        return eventType.Equals("new_reservation", StringComparison.OrdinalIgnoreCase)
               || eventType.Equals("reservation_created", StringComparison.OrdinalIgnoreCase)
               || eventType.Equals("reservation_updated", StringComparison.OrdinalIgnoreCase)
               || eventType.Equals("reservation_cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetRootString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static bool IsPosOriginatedSource(string? source)
    {
        var value = (source ?? string.Empty).Trim().ToLowerInvariant();
        return value is "pos" or "walk_in" or "walkin" or "walk-in" or "phone";
    }

    private async Task<(string CloudId, string Reference)?> FindRowByLocalIdAsync(string localId)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            "SELECT cloud_id, reference FROM cloud_reservations WHERE local_id = @localId LIMIT 1",
            connection);
        command.Parameters.AddWithValue("@localId", localId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.GetString("cloud_id"), reader.GetString("reference"));
    }

    private async Task<bool> ReservationExistsAsync(string cloudId)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM cloud_reservations WHERE cloud_id = @cloudId",
            connection);
        command.Parameters.AddWithValue("@cloudId", cloudId);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private async Task<string?> FindCloudIdByReferenceAsync(string reference)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            "SELECT cloud_id FROM cloud_reservations WHERE reference = @reference LIMIT 1",
            connection);
        command.Parameters.AddWithValue("@reference", reference);
        var value = await command.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : Convert.ToString(value);
    }
}
