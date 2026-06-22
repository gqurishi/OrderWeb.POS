using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class DiscountAuditService
{
    private readonly DatabaseService _databaseService;
    private readonly AuthenticationService _authenticationService;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public DiscountAuditService(DatabaseService databaseService, AuthenticationService authenticationService)
    {
        _databaseService = databaseService;
        _authenticationService = authenticationService;
    }

    public async Task<int> LogAsync(DiscountAuditRequest request)
    {
        await EnsureTableExistsAsync();

        var currentUser = _authenticationService.CurrentUser;
        var requestedByName = currentUser == null
            ? "Unknown"
            : !string.IsNullOrWhiteSpace(currentUser.Name)
                ? currentUser.Name
                : currentUser.Username;

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO discount_events
                (event_at,
                 order_id,
                 order_number,
                 table_session_id,
                 table_number,
                 event_action,
                 discount_type,
                 subtotal_amount,
                 previous_discount_amount,
                 discount_amount,
                 discount_percent,
                 total_after_discount,
                 reason,
                 source_area,
                 requested_by_user_id,
                 requested_by_name,
                 requested_by_role,
                 approved_by_user_id,
                 approved_by_name,
                 approved_by_role,
                 approval_required)
            VALUES
                (NOW(),
                 @orderId,
                 @orderNumber,
                 @tableSessionId,
                 @tableNumber,
                 @eventAction,
                 @discountType,
                 @subtotalAmount,
                 @previousDiscountAmount,
                 @discountAmount,
                 @discountPercent,
                 @totalAfterDiscount,
                 @reason,
                 @sourceArea,
                 @requestedByUserId,
                 @requestedByName,
                 @requestedByRole,
                 @approvedByUserId,
                 @approvedByName,
                 @approvedByRole,
                 @approvalRequired);
            SELECT LAST_INSERT_ID();";

        command.Parameters.AddWithValue("@orderId", string.IsNullOrWhiteSpace(request.OrderId) ? DBNull.Value : request.OrderId);
        command.Parameters.AddWithValue("@orderNumber", string.IsNullOrWhiteSpace(request.OrderNumber) ? DBNull.Value : request.OrderNumber);
        command.Parameters.AddWithValue("@tableSessionId", request.TableSessionId.HasValue ? request.TableSessionId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@tableNumber", string.IsNullOrWhiteSpace(request.TableNumber) ? DBNull.Value : request.TableNumber);
        command.Parameters.AddWithValue("@eventAction", request.Action);
        command.Parameters.AddWithValue("@discountType", request.DiscountType);
        command.Parameters.AddWithValue("@subtotalAmount", request.SubtotalAmount);
        command.Parameters.AddWithValue("@previousDiscountAmount", request.PreviousDiscountAmount);
        command.Parameters.AddWithValue("@discountAmount", request.DiscountAmount);
        command.Parameters.AddWithValue("@discountPercent", request.DiscountPercent);
        command.Parameters.AddWithValue("@totalAfterDiscount", request.TotalAfterDiscount);
        command.Parameters.AddWithValue("@reason", string.IsNullOrWhiteSpace(request.Reason) ? DBNull.Value : request.Reason);
        command.Parameters.AddWithValue("@sourceArea", request.SourceArea);
        command.Parameters.AddWithValue("@requestedByUserId", currentUser?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@requestedByName", requestedByName);
        command.Parameters.AddWithValue("@requestedByRole", currentUser?.Role.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@approvedByUserId", request.ApprovedBy?.UserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@approvedByName", string.IsNullOrWhiteSpace(request.ApprovedBy?.Name) ? DBNull.Value : request.ApprovedBy.Name);
        command.Parameters.AddWithValue("@approvedByRole", string.IsNullOrWhiteSpace(request.ApprovedBy?.Role) ? DBNull.Value : request.ApprovedBy.Role);
        command.Parameters.AddWithValue("@approvalRequired", request.ApprovalRequired);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<List<DiscountAuditEntry>> GetAuditEntriesAsync(DateTime startInclusive, DateTime endExclusive)
    {
        await EnsureTableExistsAsync();

        var entries = new List<DiscountAuditEntry>();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id,
                   event_at,
                   order_id,
                   order_number,
                   table_session_id,
                   table_number,
                   event_action,
                   discount_type,
                   subtotal_amount,
                   previous_discount_amount,
                   discount_amount,
                   discount_percent,
                   total_after_discount,
                   reason,
                   source_area,
                   requested_by_user_id,
                   requested_by_name,
                   requested_by_role,
                   approved_by_user_id,
                   approved_by_name,
                   approved_by_role,
                   approval_required
            FROM discount_events
            WHERE event_at >= @startAt
              AND event_at < @endAt
            ORDER BY event_at DESC, id DESC";
        command.Parameters.AddWithValue("@startAt", startInclusive);
        command.Parameters.AddWithValue("@endAt", endExclusive);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new DiscountAuditEntry
            {
                Id = reader.GetInt32("id"),
                EventAt = reader.GetDateTime("event_at"),
                OrderId = ReadNullableString(reader, "order_id"),
                OrderNumber = ReadNullableString(reader, "order_number"),
                TableSessionId = reader.IsDBNull(reader.GetOrdinal("table_session_id"))
                    ? null
                    : reader.GetInt32("table_session_id"),
                TableNumber = ReadNullableString(reader, "table_number"),
                Action = ReadString(reader, "event_action"),
                DiscountType = ReadString(reader, "discount_type"),
                SubtotalAmount = reader.GetDecimal("subtotal_amount"),
                PreviousDiscountAmount = reader.GetDecimal("previous_discount_amount"),
                DiscountAmount = reader.GetDecimal("discount_amount"),
                DiscountPercent = reader.GetDecimal("discount_percent"),
                TotalAfterDiscount = reader.GetDecimal("total_after_discount"),
                Reason = ReadString(reader, "reason"),
                SourceArea = ReadString(reader, "source_area"),
                RequestedByUserId = reader.IsDBNull(reader.GetOrdinal("requested_by_user_id"))
                    ? null
                    : reader.GetInt32("requested_by_user_id"),
                RequestedByName = ReadString(reader, "requested_by_name"),
                RequestedByRole = ReadString(reader, "requested_by_role"),
                ApprovedByUserId = reader.IsDBNull(reader.GetOrdinal("approved_by_user_id"))
                    ? null
                    : reader.GetInt32("approved_by_user_id"),
                ApprovedByName = ReadString(reader, "approved_by_name"),
                ApprovedByRole = ReadString(reader, "approved_by_role"),
                ApprovalRequired = reader.GetBoolean("approval_required")
            });
        }

        return entries;
    }

    public async Task EnsureTableExistsAsync()
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaLock.WaitAsync();
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS discount_events (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    event_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    order_id VARCHAR(100) NULL,
                    order_number VARCHAR(50) NULL,
                    table_session_id INT NULL,
                    table_number VARCHAR(50) NULL,
                    event_action VARCHAR(20) NOT NULL,
                    discount_type VARCHAR(20) NOT NULL,
                    subtotal_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                    previous_discount_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                    discount_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                    discount_percent DECIMAL(6,2) NOT NULL DEFAULT 0.00,
                    total_after_discount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                    reason VARCHAR(255) NULL,
                    source_area VARCHAR(80) NULL,
                    requested_by_user_id INT NULL,
                    requested_by_name VARCHAR(150) NULL,
                    requested_by_role VARCHAR(20) NULL,
                    approved_by_user_id INT NULL,
                    approved_by_name VARCHAR(150) NULL,
                    approved_by_role VARCHAR(20) NULL,
                    approval_required TINYINT(1) NOT NULL DEFAULT 0,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_discount_event_at (event_at),
                    INDEX idx_discount_order_time (order_id, event_at),
                    INDEX idx_discount_user_time (requested_by_user_id, event_at),
                    INDEX idx_discount_action_time (event_action, event_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
            await command.ExecuteNonQueryAsync();

            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private static string ReadString(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static string? ReadNullableString(MySqlDataReader reader, string columnName)
    {
        var value = ReadString(reader, columnName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
