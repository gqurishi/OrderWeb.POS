using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class TillExpenseService
{
    private readonly DatabaseService _databaseService;
    private readonly AuthenticationService _authenticationService;
    private static bool _schemaEnsured;

    public TillExpenseService(
        DatabaseService databaseService,
        AuthenticationService authenticationService)
    {
        _databaseService = databaseService;
        _authenticationService = authenticationService;
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
        {
            return;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS till_expenses (
                id INT PRIMARY KEY AUTO_INCREMENT,
                category VARCHAR(30) NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'settled',
                description VARCHAR(255) NULL,
                amount_taken DECIMAL(10,2) NOT NULL DEFAULT 0,
                amount_spent DECIMAL(10,2) NULL,
                amount_returned DECIMAL(10,2) NULL,
                net_amount DECIMAL(10,2) NOT NULL DEFAULT 0,
                counted_cash DECIMAL(10,2) NULL,
                order_id VARCHAR(100) NULL,
                order_number VARCHAR(50) NULL,
                source_area VARCHAR(80) NULL,
                recorded_by_user_id INT NULL,
                recorded_by_name VARCHAR(150) NULL,
                settled_by_user_id INT NULL,
                settled_by_name VARCHAR(150) NULL,
                settled_at DATETIME NULL,
                voided TINYINT(1) NOT NULL DEFAULT 0,
                void_reason VARCHAR(255) NULL,
                notes TEXT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_till_expense_status (status, created_at),
                INDEX idx_till_expense_created (created_at),
                INDEX idx_till_expense_user (recorded_by_user_id, status)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
        await command.ExecuteNonQueryAsync();

        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = @"
                ALTER TABLE cash_drawer_events
                ADD COLUMN IF NOT EXISTS till_expense_id INT NULL";
            await alter.ExecuteNonQueryAsync();
        }
        catch
        {
            // Column may already exist or table missing; cash drawer service creates table.
        }

        _schemaEnsured = true;
    }

    public async Task<TillExpense> CreateShoppingTakeAsync(ShoppingTakeRequest request)
    {
        await EnsureSchemaAsync();
        var user = _authenticationService.CurrentUser;

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO till_expenses
                (category, status, description, amount_taken, net_amount, order_id, order_number,
                 source_area, recorded_by_user_id, recorded_by_name)
            VALUES
                ('shopping', 'pending', @description, @amountTaken, 0, @orderId, @orderNumber,
                 @sourceArea, @userId, @userName);
            SELECT LAST_INSERT_ID();";

        command.Parameters.AddWithValue("@description", request.ItemName.Trim());
        command.Parameters.AddWithValue("@amountTaken", request.AmountTaken);
        command.Parameters.AddWithValue("@orderId", string.IsNullOrWhiteSpace(request.OrderId) ? DBNull.Value : request.OrderId.Trim());
        command.Parameters.AddWithValue("@orderNumber", string.IsNullOrWhiteSpace(request.OrderNumber) ? DBNull.Value : request.OrderNumber.Trim());
        command.Parameters.AddWithValue("@sourceArea", request.SourceArea);
        command.Parameters.AddWithValue("@userId", user?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", GetUserDisplayName(user));

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return (await GetByIdAsync(id))!;
    }

    public async Task<TillExpense> SettleShoppingAsync(ShoppingSettleRequest request)
    {
        await EnsureSchemaAsync();
        var existing = await GetByIdAsync(request.TillExpenseId)
            ?? throw new InvalidOperationException("Shopping trip not found.");

        if (existing.Status != TillExpenseStatus.Pending || existing.Category != TillExpenseCategory.Shopping)
        {
            throw new InvalidOperationException("This trip is not pending settlement.");
        }

        if (request.AmountSpent < 0 || request.AmountSpent > existing.AmountTaken)
        {
            throw new InvalidOperationException("Spent amount must be between £0 and the amount taken.");
        }

        var returned = existing.AmountTaken - request.AmountSpent;
        var user = _authenticationService.CurrentUser;

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE till_expenses
            SET status = 'settled',
                amount_spent = @amountSpent,
                amount_returned = @amountReturned,
                net_amount = @netAmount,
                settled_by_user_id = @userId,
                settled_by_name = @userName,
                settled_at = NOW(),
                updated_at = NOW()
            WHERE id = @id AND status = 'pending'";

        command.Parameters.AddWithValue("@amountSpent", request.AmountSpent);
        command.Parameters.AddWithValue("@amountReturned", returned);
        command.Parameters.AddWithValue("@netAmount", request.AmountSpent);
        command.Parameters.AddWithValue("@userId", user?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", GetUserDisplayName(user));
        command.Parameters.AddWithValue("@id", request.TillExpenseId);

        await command.ExecuteNonQueryAsync();
        return (await GetByIdAsync(request.TillExpenseId))!;
    }

    public async Task<TillExpense> CreateDeliveryPayoutAsync(DeliveryPayoutRequest request)
    {
        await EnsureSchemaAsync();
        var user = _authenticationService.CurrentUser;
        var description = string.IsNullOrWhiteSpace(request.OrderNumber)
            ? "Delivery payout"
            : $"Delivery payout · {request.OrderNumber}";

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO till_expenses
                (category, status, description, amount_taken, amount_spent, amount_returned, net_amount,
                 order_id, order_number, source_area, recorded_by_user_id, recorded_by_name, settled_at)
            VALUES
                ('delivery', 'settled', @description, @amount, @amount, 0, @amount,
                 @orderId, @orderNumber, @sourceArea, @userId, @userName, NOW());
            SELECT LAST_INSERT_ID();";

        command.Parameters.AddWithValue("@description", description);
        command.Parameters.AddWithValue("@amount", request.Amount);
        command.Parameters.AddWithValue("@orderId", string.IsNullOrWhiteSpace(request.OrderId) ? DBNull.Value : request.OrderId.Trim());
        command.Parameters.AddWithValue("@orderNumber", string.IsNullOrWhiteSpace(request.OrderNumber) ? DBNull.Value : request.OrderNumber.Trim());
        command.Parameters.AddWithValue("@sourceArea", request.SourceArea);
        command.Parameters.AddWithValue("@userId", user?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", GetUserDisplayName(user));

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return (await GetByIdAsync(id))!;
    }

    public async Task<TillExpense> CreateCashCountAsync(CashCountRequest request)
    {
        await EnsureSchemaAsync();
        var user = _authenticationService.CurrentUser;

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO till_expenses
                (category, status, description, counted_cash, net_amount, notes, source_area,
                 recorded_by_user_id, recorded_by_name, settled_at)
            VALUES
                ('cash_count', 'settled', @description, @countedCash, 0, @notes, @sourceArea,
                 @userId, @userName, NOW());
            SELECT LAST_INSERT_ID();";

        command.Parameters.AddWithValue("@description", $"Cash count £{request.CountedCash:F2}");
        command.Parameters.AddWithValue("@countedCash", request.CountedCash);
        command.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());
        command.Parameters.AddWithValue("@sourceArea", request.SourceArea);
        command.Parameters.AddWithValue("@userId", user?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", GetUserDisplayName(user));

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return (await GetByIdAsync(id))!;
    }

    public async Task<TillExpense?> CreateOtherExpenseAsync(OtherTillExpenseRequest request)
    {
        await EnsureSchemaAsync();
        if (!request.AmountOut.HasValue || request.AmountOut.Value <= 0)
        {
            return null;
        }

        var user = _authenticationService.CurrentUser;
        var amount = request.AmountOut.Value;

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO till_expenses
                (category, status, description, amount_taken, amount_spent, net_amount, source_area,
                 recorded_by_user_id, recorded_by_name, settled_at)
            VALUES
                ('other', 'settled', @description, @amount, @amount, @amount, @sourceArea,
                 @userId, @userName, NOW());
            SELECT LAST_INSERT_ID();";

        command.Parameters.AddWithValue("@description", request.Reason.Trim());
        command.Parameters.AddWithValue("@amount", amount);
        command.Parameters.AddWithValue("@sourceArea", request.SourceArea);
        command.Parameters.AddWithValue("@userId", user?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", GetUserDisplayName(user));

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return await GetByIdAsync(id);
    }

    public async Task<List<TillExpense>> GetPendingShoppingAsync(int? userId = null)
    {
        await EnsureSchemaAsync();
        var rows = new List<TillExpense>();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, category, status, description, amount_taken, amount_spent, amount_returned,
                   net_amount, counted_cash, order_id, order_number, source_area,
                   recorded_by_user_id, recorded_by_name, settled_by_user_id, settled_by_name,
                   settled_at, voided, void_reason, notes, created_at, updated_at
            FROM till_expenses
            WHERE status = 'pending'
              AND category = 'shopping'
              AND voided = 0
              AND (@userId IS NULL OR recorded_by_user_id = @userId)
            ORDER BY created_at ASC";

        command.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(ReadExpense(reader));
        }

        return rows;
    }

    public async Task<List<TillExpense>> GetExpensesAsync(DateTime startInclusive, DateTime endExclusive)
    {
        await EnsureSchemaAsync();
        var rows = new List<TillExpense>();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, category, status, description, amount_taken, amount_spent, amount_returned,
                   net_amount, counted_cash, order_id, order_number, source_area,
                   recorded_by_user_id, recorded_by_name, settled_by_user_id, settled_by_name,
                   settled_at, voided, void_reason, notes, created_at, updated_at
            FROM till_expenses
            WHERE created_at >= @startAt
              AND created_at < @endAt
              AND voided = 0
            ORDER BY created_at DESC, id DESC";

        command.Parameters.AddWithValue("@startAt", startInclusive);
        command.Parameters.AddWithValue("@endAt", endExclusive);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(ReadExpense(reader));
        }

        return rows;
    }

    public async Task<TillExpenseSummary> GetSummaryAsync(DateTime startInclusive, DateTime endExclusive)
    {
        var expenses = await GetExpensesAsync(startInclusive, endExclusive);
        var pending = await GetPendingShoppingAsync();

        return new TillExpenseSummary
        {
            PendingShoppingCount = pending.Count,
            PendingShoppingTotal = pending.Sum(e => e.AmountTaken),
            ShoppingNet = expenses.Where(e => e.Category == TillExpenseCategory.Shopping && e.Status == TillExpenseStatus.Settled).Sum(e => e.NetAmount),
            DeliveryTotal = expenses.Where(e => e.Category == TillExpenseCategory.Delivery).Sum(e => e.NetAmount),
            OtherTotal = expenses.Where(e => e.Category == TillExpenseCategory.Other).Sum(e => e.NetAmount),
            CashReturnTotal = expenses.Where(e => e.Category == TillExpenseCategory.Shopping && e.Status == TillExpenseStatus.Settled).Sum(e => e.AmountReturned ?? 0),
            TotalNetOut = expenses.Where(e => e.Status == TillExpenseStatus.Settled && e.Category != TillExpenseCategory.CashCount).Sum(e => e.NetAmount)
        };
    }

    public async Task<TillExpense?> GetByIdAsync(int id)
    {
        await EnsureSchemaAsync();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, category, status, description, amount_taken, amount_spent, amount_returned,
                   net_amount, counted_cash, order_id, order_number, source_area,
                   recorded_by_user_id, recorded_by_name, settled_by_user_id, settled_by_name,
                   settled_at, voided, void_reason, notes, created_at, updated_at
            FROM till_expenses
            WHERE id = @id";

        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadExpense(reader) : null;
    }

    private static TillExpense ReadExpense(MySqlDataReader reader)
    {
        return new TillExpense
        {
            Id = reader.GetInt32("id"),
            Category = ParseCategory(reader.GetString("category")),
            Status = ParseStatus(reader.GetString("status")),
            Description = ReadString(reader, "description"),
            AmountTaken = reader.GetDecimal("amount_taken"),
            AmountSpent = reader.IsDBNull(reader.GetOrdinal("amount_spent")) ? null : reader.GetDecimal("amount_spent"),
            AmountReturned = reader.IsDBNull(reader.GetOrdinal("amount_returned")) ? null : reader.GetDecimal("amount_returned"),
            NetAmount = reader.GetDecimal("net_amount"),
            CountedCash = reader.IsDBNull(reader.GetOrdinal("counted_cash")) ? null : reader.GetDecimal("counted_cash"),
            OrderId = ReadNullableString(reader, "order_id"),
            OrderNumber = ReadNullableString(reader, "order_number"),
            SourceArea = ReadString(reader, "source_area"),
            RecordedByUserId = reader.IsDBNull(reader.GetOrdinal("recorded_by_user_id")) ? null : reader.GetInt32("recorded_by_user_id"),
            RecordedByName = ReadString(reader, "recorded_by_name"),
            SettledByUserId = reader.IsDBNull(reader.GetOrdinal("settled_by_user_id")) ? null : reader.GetInt32("settled_by_user_id"),
            SettledByName = ReadNullableString(reader, "settled_by_name"),
            SettledAt = reader.IsDBNull(reader.GetOrdinal("settled_at")) ? null : reader.GetDateTime("settled_at"),
            Voided = reader.GetBoolean("voided"),
            VoidReason = ReadNullableString(reader, "void_reason"),
            Notes = ReadNullableString(reader, "notes"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at")
        };
    }

    private static TillExpenseCategory ParseCategory(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "shopping" => TillExpenseCategory.Shopping,
            "delivery" => TillExpenseCategory.Delivery,
            "cash_count" => TillExpenseCategory.CashCount,
            "other" => TillExpenseCategory.Other,
            _ => TillExpenseCategory.Other
        };

    private static TillExpenseStatus ParseStatus(string value) =>
        Enum.TryParse<TillExpenseStatus>(value, true, out var parsed) ? parsed : TillExpenseStatus.Settled;

    private static string GetUserDisplayName(User? user)
    {
        if (user == null)
        {
            return "Unknown";
        }

        return !string.IsNullOrWhiteSpace(user.Name) ? user.Name.Trim() : user.Username.Trim();
    }

    private static string ReadString(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static string? ReadNullableString(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}

public sealed class TillExpenseSummary
{
    public int PendingShoppingCount { get; set; }
    public decimal PendingShoppingTotal { get; set; }
    public decimal ShoppingNet { get; set; }
    public decimal DeliveryTotal { get; set; }
    public decimal OtherTotal { get; set; }
    public decimal CashReturnTotal { get; set; }
    public decimal TotalNetOut { get; set; }
}
