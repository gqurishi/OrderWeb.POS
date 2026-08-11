using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class TableServiceChargeOrderAuditService
{
    private readonly DatabaseService _databaseService;

    public TableServiceChargeOrderAuditService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task RecordAsync(
        string externalOrderId,
        string eventType,
        decimal percentage,
        decimal basis,
        decimal amount,
        ServiceChargeClassification? classification,
        string? reason,
        User performedBy,
        User? approvedBy,
        CancellationToken cancellationToken = default)
    {
        var normalizedEvent = eventType.Trim().ToLowerInvariant();
        if (normalizedEvent is not ("applied" or "recalculated" or "removed" or "restored"))
        {
            throw new ArgumentOutOfRangeException(nameof(eventType));
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string findSql = "SELECT id FROM orders WHERE order_id = @externalOrderId FOR UPDATE";
            await using var find = new MySqlCommand(findSql, connection, transaction);
            find.Parameters.AddWithValue("@externalOrderId", externalOrderId);
            var idValue = await find.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Save the order before changing its service charge.");
            var orderDbId = Convert.ToInt32(idValue);

            var isRemoved = normalizedEvent == "removed";
            const string updateSql = """
                UPDATE orders
                SET service_charge_percentage = @percentage,
                    service_charge_basis = @basis,
                    service_charge_amount = @amount,
                    service_charge_status = @status,
                    service_charge_classification = @classification,
                    service_charge_removal_reason = @reason,
                    service_charge_removed_by_user_id = @removedByUserId,
                    service_charge_removed_by_name = @removedByName,
                    service_charge_approved_by_user_id = @approvedByUserId,
                    service_charge_approved_by_name = @approvedByName,
                    service_charge_removed_at = @removedAt
                WHERE id = @orderId
                """;
            await using (var update = new MySqlCommand(updateSql, connection, transaction))
            {
                update.Parameters.AddWithValue("@percentage", percentage);
                update.Parameters.AddWithValue("@basis", basis);
                update.Parameters.AddWithValue("@amount", isRemoved ? 0m : amount);
                update.Parameters.AddWithValue("@status", isRemoved ? "removed" : "applied");
                update.Parameters.AddWithValue("@classification", ToDb(classification) ?? (object)DBNull.Value);
                update.Parameters.AddWithValue("@reason", isRemoved ? reason ?? (object)DBNull.Value : DBNull.Value);
                update.Parameters.AddWithValue("@removedByUserId", isRemoved ? performedBy.Id : DBNull.Value);
                update.Parameters.AddWithValue("@removedByName", isRemoved ? DisplayName(performedBy) : DBNull.Value);
                update.Parameters.AddWithValue("@approvedByUserId", isRemoved && approvedBy != null ? approvedBy.Id : DBNull.Value);
                update.Parameters.AddWithValue("@approvedByName", isRemoved && approvedBy != null ? DisplayName(approvedBy) : DBNull.Value);
                update.Parameters.AddWithValue("@removedAt", isRemoved ? DateTime.Now : DBNull.Value);
                update.Parameters.AddWithValue("@orderId", orderDbId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            const string eventSql = """
                INSERT INTO order_service_charge_events
                    (order_id, event_type, service_charge_percentage, service_charge_basis,
                     service_charge_amount, service_charge_classification, reason,
                     performed_by_user_id, performed_by_name,
                     approved_by_user_id, approved_by_name, event_at)
                VALUES
                    (@orderId, @eventType, @percentage, @basis,
                     @amount, @classification, @reason,
                     @performedByUserId, @performedByName,
                     @approvedByUserId, @approvedByName, CURRENT_TIMESTAMP)
                """;
            await using (var audit = new MySqlCommand(eventSql, connection, transaction))
            {
                audit.Parameters.AddWithValue("@orderId", orderDbId);
                audit.Parameters.AddWithValue("@eventType", normalizedEvent);
                audit.Parameters.AddWithValue("@percentage", percentage);
                audit.Parameters.AddWithValue("@basis", basis);
                audit.Parameters.AddWithValue("@amount", amount);
                audit.Parameters.AddWithValue("@classification", ToDb(classification) ?? (object)DBNull.Value);
                audit.Parameters.AddWithValue("@reason", reason ?? (object)DBNull.Value);
                audit.Parameters.AddWithValue("@performedByUserId", performedBy.Id > 0 ? performedBy.Id : DBNull.Value);
                audit.Parameters.AddWithValue("@performedByName", DisplayName(performedBy));
                audit.Parameters.AddWithValue("@approvedByUserId", approvedBy is { Id: > 0 } ? approvedBy.Id : DBNull.Value);
                audit.Parameters.AddWithValue("@approvedByName", approvedBy == null ? DBNull.Value : DisplayName(approvedBy));
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string DisplayName(User user) =>
        !string.IsNullOrWhiteSpace(user.Name) ? user.Name : user.Username;

    private static string? ToDb(ServiceChargeClassification? classification) => classification switch
    {
        ServiceChargeClassification.Optional => "optional",
        ServiceChargeClassification.Compulsory => "compulsory",
        _ => null
    };
}
