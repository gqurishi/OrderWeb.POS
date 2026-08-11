using MySqlConnector;
using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Automatically cleans up rolling order data every night.
/// </summary>
public class DatabaseCleanupService
{
    private const int WebOrderRetentionDays = 7;
    private const int LocalOrderRetentionYears = 2;
    private const int LocalCleanupBatchSize = 250;
    private const int LocalCleanupMaxBatchesPerRun = 4;

    /// <summary>
    /// Purge finalized OrderWeb cache rows after seven days and finalized local
    /// orders after a rolling two years, but only when OrderWeb has confirmed the
    /// financial report for the order's trading day.
    /// </summary>
    public async Task<CleanupResult> RunCleanupAsync()
    {
        var result = new CleanupResult();
        var startTime = DateTime.Now;

        try
        {
            using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            Debug.WriteLine(" Starting database cleanup...");

            result.WebOrdersDeleted = await CountOrdersForCleanupAsync(connection, "web", WebOrderRetentionDays, requireClosed: true);
            var localOrdersEligible = await CountLocalOrdersForRetentionCleanupAsync(connection);

            if (result.WebOrdersDeleted == 0 && localOrdersEligible == 0)
            {
                Debug.WriteLine(" No old orders eligible for cleanup");
                result.Success = true;
                Preferences.Set("LastCleanupDate", DateTime.Now.ToString("O"));
                return result;
            }

            Debug.WriteLine($" Found {result.WebOrdersDeleted} cached web order(s) older than {WebOrderRetentionDays} days");
            if (result.WebOrdersDeleted > 0)
            {
                await LogSampleOrdersAsync(connection, "web", WebOrderRetentionDays, requireClosed: true);
            }

            if (localOrdersEligible > 0)
            {
                Debug.WriteLine($" Found {localOrdersEligible} local POS order(s) older than {LocalOrderRetentionYears} years with confirmed cloud reports");
            }

            await DeleteOrdersForCleanupAsync(connection, "web", WebOrderRetentionDays, requireClosed: true, result);
            for (var batch = 0; batch < LocalCleanupMaxBatchesPerRun; batch++)
            {
                var deleted = await DeleteExpiredLocalOrdersBatchAsync(connection, result);
                if (deleted < LocalCleanupBatchSize)
                {
                    break;
                }
            }

            result.Success = true;
            result.Duration = (DateTime.Now - startTime).TotalMilliseconds;

            Debug.WriteLine($" Cleanup completed in {result.Duration:F0}ms");
            Debug.WriteLine($" Summary: {result.WebOrdersDeleted} finalized web cache order(s), {result.LocalOrdersDeleted} expired local order(s), {result.ItemsDeleted} item(s), {result.AddonsDeleted} addon(s) deleted");

            // Save cleanup timestamp
            Preferences.Set("LastCleanupDate", DateTime.Now.ToString("O"));

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Debug.WriteLine($" Cleanup error: {ex.Message}");
            return result;
        }
    }

    private static DateTime GetLocalRetentionCutoff() =>
        TradingDayHelper.GetBusinessDayStart(TradingDayHelper.GetBusinessDate().AddYears(-LocalOrderRetentionYears));

    private static string BuildLocalRetentionPredicate(string alias)
    {
        var prefix = string.IsNullOrWhiteSpace(alias) ? "" : $"{alias}.";
        return $@"
            COALESCE({prefix}source_channel, 'local') = 'local'
            AND {prefix}created_at < @localCutoff
            AND COALESCE(LOWER({prefix}sync_status), '') = 'synced'
            AND COALESCE({prefix}is_open, 0) = 0
            AND (
                COALESCE(LOWER({prefix}local_lifecycle_state), '') IN ('paid', 'voided')
                OR LOWER(COALESCE({prefix}status, '')) IN ('completed', 'closed', 'paid', 'cancelled', 'voided')
            )
            AND EXISTS (
                SELECT 1
                FROM orderweb_daily_report_sync_log report_sync
                WHERE report_sync.report_date = DATE(DATE_SUB({prefix}created_at, INTERVAL 1 HOUR))
                  AND report_sync.success = 1
            )";
    }

    private static async Task<int> CountLocalOrdersForRetentionCleanupAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM orders o
            WHERE {BuildLocalRetentionPredicate("o")}", connection);
        command.Parameters.AddWithValue("@localCutoff", GetLocalRetentionCutoff());
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> DeleteExpiredLocalOrdersBatchAsync(
        MySqlConnection connection,
        CleanupResult result)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            using (var createCandidates = new MySqlCommand(@"
                CREATE TEMPORARY TABLE IF NOT EXISTS local_cleanup_candidates (
                    id INT PRIMARY KEY
                ) ENGINE=MEMORY", connection, transaction))
            {
                await createCandidates.ExecuteNonQueryAsync();
            }

            // DELETE keeps the surrounding transaction intact; TRUNCATE would
            // implicitly commit in MySQL.
            using (var clearCandidates = new MySqlCommand(
                       "DELETE FROM local_cleanup_candidates",
                       connection,
                       transaction))
            {
                await clearCandidates.ExecuteNonQueryAsync();
            }

            using (var selectCandidates = new MySqlCommand($@"
                INSERT INTO local_cleanup_candidates (id)
                SELECT o.id
                FROM orders o
                WHERE {BuildLocalRetentionPredicate("o")}
                ORDER BY o.created_at, o.id
                LIMIT {LocalCleanupBatchSize}", connection, transaction))
            {
                selectCandidates.Parameters.AddWithValue("@localCutoff", GetLocalRetentionCutoff());
                await selectCandidates.ExecuteNonQueryAsync();
            }

            int candidateCount;
            using (var count = new MySqlCommand("SELECT COUNT(*) FROM local_cleanup_candidates", connection, transaction))
            {
                candidateCount = Convert.ToInt32(await count.ExecuteScalarAsync());
            }

            if (candidateCount == 0)
            {
                await transaction.CommitAsync();
                return 0;
            }

            const string auditSql = @"
                INSERT IGNORE INTO local_order_deletion_audit
                    (action_type, original_order_db_id, order_id, order_number, cloud_order_id,
                     source_channel, order_status, lifecycle_state, total_amount, payment_method,
                     business_date, deletion_reason, deleted_by_name, terminal_name)
                SELECT
                    'local_retention_purge', o.id, o.order_id, o.order_number, o.cloud_order_id,
                    COALESCE(o.source_channel, 'local'), o.status, o.local_lifecycle_state,
                    COALESCE(o.total_amount, 0.00), o.payment_method,
                    DATE(DATE_SUB(o.created_at, INTERVAL 1 HOUR)),
                    'Local order exceeded rolling two-year retention; daily financial report confirmed in OrderWeb.net.',
                    'System retention cleanup', @terminalName
                FROM orders o
                INNER JOIN local_cleanup_candidates candidates ON candidates.id = o.id";
            using (var audit = new MySqlCommand(auditSql, connection, transaction))
            {
                var terminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
                audit.Parameters.AddWithValue("@terminalName", string.IsNullOrWhiteSpace(terminalName) ? Environment.MachineName : terminalName);
                await audit.ExecuteNonQueryAsync();
            }

            result.AddonsDeleted += await CountBatchRowsAsync(connection, transaction, @"
                SELECT COUNT(*) FROM order_item_addons addons
                INNER JOIN order_items items ON items.id = addons.order_item_id
                INNER JOIN local_cleanup_candidates candidates ON candidates.id = items.order_id");
            result.ItemsDeleted += await CountBatchRowsAsync(connection, transaction, @"
                SELECT COUNT(*) FROM order_items items
                INNER JOIN local_cleanup_candidates candidates ON candidates.id = items.order_id");
            result.PaymentsDeleted += await CountBatchRowsAsync(connection, transaction, @"
                SELECT COUNT(*) FROM order_payments payments
                INNER JOIN local_cleanup_candidates candidates ON candidates.id = payments.order_id");
            result.EventsDeleted += await CountBatchRowsAsync(connection, transaction, @"
                SELECT COUNT(*) FROM order_events events
                INNER JOIN local_cleanup_candidates candidates ON candidates.id = events.order_id");
            result.RefundsDeleted += await ExecuteBatchDeleteAsync(connection, transaction, @"
                DELETE refunds FROM order_refunds refunds
                INNER JOIN local_cleanup_candidates candidates ON candidates.id = refunds.order_id");

            var deleted = await ExecuteBatchDeleteAsync(connection, transaction, @"
                DELETE orders FROM orders
                INNER JOIN local_cleanup_candidates candidates ON candidates.id = orders.id");

            await transaction.CommitAsync();
            result.LocalOrdersDeleted += deleted;
            Debug.WriteLine($"  Deleted {deleted} expired local order(s) in a verified retention batch");
            return deleted;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<int> CountBatchRowsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql)
    {
        using var command = new MySqlCommand(sql, connection, transaction);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ExecuteBatchDeleteAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql)
    {
        using var command = new MySqlCommand(sql, connection, transaction);
        return await command.ExecuteNonQueryAsync();
    }

    private static string BuildCleanupPredicate(string tableAlias, bool requireClosed)
    {
        var prefix = string.IsNullOrWhiteSpace(tableAlias) ? "" : $"{tableAlias}.";
        var predicate = $@"
                {prefix}created_at < DATE_SUB(CURDATE(), INTERVAL @days DAY)
                AND COALESCE({prefix}source_channel, 'local') = @sourceChannel";

        predicate += $@"
                AND COALESCE(LOWER({prefix}sync_status), '') = 'synced'";

        if (requireClosed)
        {
            predicate += $@"
                AND COALESCE({prefix}is_open, 0) = 0
                AND (
                    COALESCE(LOWER({prefix}local_lifecycle_state), '') IN ('paid', 'voided')
                    OR LOWER(COALESCE({prefix}status, '')) IN ('completed', 'closed', 'paid', 'cancelled', 'voided')
                )";
        }

        return predicate;
    }

    private static void AddCleanupParameters(MySqlCommand command, string sourceChannel, int retentionDays)
    {
        command.Parameters.AddWithValue("@sourceChannel", sourceChannel);
        command.Parameters.AddWithValue("@days", retentionDays);
    }

    private static async Task<int> CountOrdersForCleanupAsync(
        MySqlConnection connection,
        string sourceChannel,
        int retentionDays,
        bool requireClosed)
    {
        var countQuery = $@"
            SELECT COUNT(*)
            FROM orders o
            WHERE {BuildCleanupPredicate("o", requireClosed)}";

        using var countCmd = new MySqlCommand(countQuery, connection);
        AddCleanupParameters(countCmd, sourceChannel, retentionDays);
        return Convert.ToInt32(await countCmd.ExecuteScalarAsync());
    }

    private static async Task LogSampleOrdersAsync(
        MySqlConnection connection,
        string sourceChannel,
        int retentionDays,
        bool requireClosed)
    {
        var idsQuery = $@"
            SELECT id, COALESCE(NULLIF(order_number, ''), order_id) AS order_ref, created_at
            FROM orders o
            WHERE {BuildCleanupPredicate("o", requireClosed)}
            ORDER BY created_at
            LIMIT 10";

        using var idsCmd = new MySqlCommand(idsQuery, connection);
        AddCleanupParameters(idsCmd, sourceChannel, retentionDays);
        using var reader = await idsCmd.ExecuteReaderAsync();

        Debug.WriteLine($" Sample {sourceChannel} orders to be deleted:");
        while (await reader.ReadAsync())
        {
            var orderRef = reader.IsDBNull(reader.GetOrdinal("order_ref"))
                ? $"#{reader.GetInt32("id")}"
                : reader.GetString("order_ref");
            var createdAt = reader.GetDateTime("created_at");
            Debug.WriteLine($"   {orderRef} - {createdAt:yyyy-MM-dd}");
        }
    }

    private static async Task DeleteOrdersForCleanupAsync(
        MySqlConnection connection,
        string sourceChannel,
        int retentionDays,
        bool requireClosed,
        CleanupResult result)
    {
        var orderPredicate = BuildCleanupPredicate("o", requireClosed);
        var plainOrderPredicate = BuildCleanupPredicate("", requireClosed);

        await ArchiveOrdersForCleanupAsync(connection, orderPredicate, sourceChannel, retentionDays);

        result.AddonsDeleted += await ExecuteCleanupDeleteAsync(connection, $@"
            DELETE oia FROM order_item_addons oia
            INNER JOIN order_items oi ON oia.order_item_id = oi.id
            INNER JOIN orders o ON oi.order_id = o.id
            WHERE {orderPredicate}", sourceChannel, retentionDays);

        result.SendTrackingDeleted += await ExecuteCleanupDeleteAsync(connection, $@"
            DELETE oist FROM order_item_send_tracking oist
            INNER JOIN orders o ON oist.order_id = o.id
            WHERE {orderPredicate}", sourceChannel, retentionDays);

        result.PaymentsDeleted += await ExecuteCleanupDeleteAsync(connection, $@"
            DELETE op FROM order_payments op
            INNER JOIN orders o ON op.order_id = o.id
            WHERE {orderPredicate}", sourceChannel, retentionDays);

        result.EventsDeleted += await ExecuteCleanupDeleteAsync(connection, $@"
            DELETE oe FROM order_events oe
            INNER JOIN orders o ON oe.order_id = o.id
            WHERE {orderPredicate}", sourceChannel, retentionDays);

        result.RefundsDeleted += await ExecuteCleanupDeleteAsync(connection, $@"
            DELETE ore FROM order_refunds ore
            INNER JOIN orders o ON ore.order_id = o.id
            WHERE {orderPredicate}", sourceChannel, retentionDays);

        result.ItemsDeleted += await ExecuteCleanupDeleteAsync(connection, $@"
            DELETE oi FROM order_items oi
            INNER JOIN orders o ON oi.order_id = o.id
            WHERE {orderPredicate}", sourceChannel, retentionDays);

        var deletedOrders = await ExecuteCleanupDeleteAsync(connection, $@"
            DELETE FROM orders
            WHERE {plainOrderPredicate}", sourceChannel, retentionDays);

        if (sourceChannel == "web")
        {
            result.WebOrdersDeleted = deletedOrders;
        }
        else
        {
            result.LocalOrdersDeleted = deletedOrders;
        }

        Debug.WriteLine($"  Deleted {deletedOrders} {sourceChannel} order(s)");
    }

    private static async Task ArchiveOrdersForCleanupAsync(
        MySqlConnection connection,
        string orderPredicate,
        string sourceChannel,
        int retentionDays)
    {
        const string reason = "Finalized OrderWeb cache expired after seven days; authoritative order remains in OrderWeb.net.";
        var terminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
        var sql = $@"
            INSERT IGNORE INTO local_order_deletion_audit
                (action_type, original_order_db_id, order_id, order_number, cloud_order_id,
                 source_channel, order_status, lifecycle_state, total_amount, payment_method,
                 business_date, deletion_reason, deleted_by_name, terminal_name)
            SELECT
                'web_cache_purge', o.id, o.order_id, o.order_number, o.cloud_order_id,
                COALESCE(o.source_channel, 'web'), o.status, o.local_lifecycle_state,
                COALESCE(o.total_amount, 0.00), o.payment_method, DATE(o.created_at),
                @deletionReason, 'System cache cleanup', @terminalName
            FROM orders o
            WHERE {orderPredicate}";

        using var command = new MySqlCommand(sql, connection);
        AddCleanupParameters(command, sourceChannel, retentionDays);
        command.Parameters.AddWithValue("@deletionReason", reason);
        command.Parameters.AddWithValue("@terminalName", string.IsNullOrWhiteSpace(terminalName) ? Environment.MachineName : terminalName);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecuteCleanupDeleteAsync(
        MySqlConnection connection,
        string sql,
        string sourceChannel,
        int retentionDays)
    {
        using var command = new MySqlCommand(sql, connection);
        AddCleanupParameters(command, sourceChannel, retentionDays);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Create database indexes for better performance
    /// </summary>
    public async Task CreateIndexesAsync()
    {
        try
        {
            using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            Debug.WriteLine(" Creating database indexes for performance...");

            // Index for today's orders query (most common)
            await CreateIndexIfNotExistsAsync(connection, 
                "idx_orders_created_sync", 
                "orders", 
                "(created_at DESC, sync_status)");

            // Index for order number lookup
            await CreateIndexIfNotExistsAsync(connection, 
                "idx_orders_number", 
                "orders", 
                "(order_number)");

            // Index for customer search
            await CreateIndexIfNotExistsAsync(connection, 
                "idx_orders_customer", 
                "orders", 
                "(customer_name, customer_phone)");

            // Index for order items
            await CreateIndexIfNotExistsAsync(connection, 
                "idx_order_items_order_id", 
                "order_items", 
                "(order_id)");

            Debug.WriteLine(" Database indexes created successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error creating indexes: {ex.Message}");
        }
    }

    private async Task CreateIndexIfNotExistsAsync(MySqlConnection connection, string indexName, string tableName, string columns)
    {
        try
        {
            // Check if index exists
            var schemaName = TerminalConfigurationService.GetActiveDatabaseName();
            var checkQuery = $@"
                SELECT COUNT(*) 
                FROM information_schema.STATISTICS 
                WHERE TABLE_SCHEMA = @schemaName 
                AND TABLE_NAME = '{tableName}' 
                AND INDEX_NAME = '{indexName}'";

            using var checkCmd = new MySqlCommand(checkQuery, connection);
            checkCmd.Parameters.AddWithValue("@schemaName", schemaName);
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

            if (!exists)
            {
                var createQuery = $"CREATE INDEX {indexName} ON {tableName} {columns}";
                using var createCmd = new MySqlCommand(createQuery, connection);
                await createCmd.ExecuteNonQueryAsync();
                Debug.WriteLine($"    Created index: {indexName}");
            }
            else
            {
                Debug.WriteLine($"Info: Index exists: {indexName}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"    Could not create index {indexName}: {ex.Message}");
        }
    }

    private async Task OptimizeTablesAsync(MySqlConnection connection)
    {
        try
        {
            Debug.WriteLine(" Optimizing tables...");

            var tables = new[] { "orders", "order_items", "order_item_addons", "order_payments", "order_events", "order_refunds", "order_item_send_tracking" };
            foreach (var table in tables)
            {
                using var cmd = new MySqlCommand($"OPTIMIZE TABLE {table}", connection);
                await cmd.ExecuteNonQueryAsync();
            }

            Debug.WriteLine(" Tables optimized");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Table optimization warning: {ex.Message}");
        }
    }

    /// <summary>
    /// Get cleanup statistics
    /// </summary>
    public async Task<CleanupStats> GetCleanupStatsAsync()
    {
        var stats = new CleanupStats();

        try
        {
            using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            // Count cached web orders that are eligible for daily cleanup
            var countQuery = $@"
                SELECT COUNT(*)
                FROM orders
                WHERE {BuildCleanupPredicate("", requireClosed: true)}";

            using var countCmd = new MySqlCommand(countQuery, connection);
            AddCleanupParameters(countCmd, "web", WebOrderRetentionDays);
            stats.OldWebOrdersCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            // Count total local POS orders
            var localQuery = @"
                SELECT COUNT(*) 
                FROM orders 
                WHERE COALESCE(source_channel, 'local') = 'local'";

            using var localCmd = new MySqlCommand(localQuery, connection);
            stats.LocalPosOrdersCount = Convert.ToInt32(await localCmd.ExecuteScalarAsync());

            stats.OldLocalOrdersCount = await CountLocalOrdersForRetentionCleanupAsync(connection);

            // Get last cleanup date
            var lastCleanup = Preferences.Get("LastCleanupDate", "");
            if (!string.IsNullOrEmpty(lastCleanup))
            {
                stats.LastCleanupDate = DateTime.Parse(lastCleanup);
            }

            stats.WebRetentionDays = WebOrderRetentionDays;
            stats.LocalRetentionDays = (int)(TradingDayHelper.GetBusinessDate() - TradingDayHelper.GetBusinessDate().AddYears(-LocalOrderRetentionYears)).TotalDays;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting cleanup stats: {ex.Message}");
        }

        return stats;
    }
}

public class CleanupResult
{
    public bool Success { get; set; }
    public int OrdersDeleted
    {
        get => TotalOrdersDeleted;
        set => WebOrdersDeleted = value;
    }
    public int WebOrdersDeleted { get; set; }
    public int LocalOrdersDeleted { get; set; }
    public int TotalOrdersDeleted => WebOrdersDeleted + LocalOrdersDeleted;
    public int ItemsDeleted { get; set; }
    public int AddonsDeleted { get; set; }
    public int SendTrackingDeleted { get; set; }
    public int PaymentsDeleted { get; set; }
    public int EventsDeleted { get; set; }
    public int RefundsDeleted { get; set; }
    public double Duration { get; set; }
    public string ErrorMessage { get; set; } = "";
}

public class CleanupStats
{
    public int OldWebOrdersCount { get; set; }
    public int OldLocalOrdersCount { get; set; }
    public int LocalPosOrdersCount { get; set; }
    public DateTime? LastCleanupDate { get; set; }
    public int RetentionDays
    {
        get => WebRetentionDays;
        set => WebRetentionDays = value;
    }
    public int WebRetentionDays { get; set; }
    public int LocalRetentionDays { get; set; }
}
