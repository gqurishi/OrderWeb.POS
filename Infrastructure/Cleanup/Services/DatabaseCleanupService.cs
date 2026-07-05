using MySqlConnector;
using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Automatically cleans up rolling order data every night.
/// </summary>
public class DatabaseCleanupService
{
    private readonly string _connectionString;
    private const int WebOrderRetentionDays = 7;
    private const int LocalOrderRetentionDays = 365;

    public DatabaseCleanupService()
    {
        _connectionString = TerminalConfigurationService.GetPosConnectionString();
    }

    /// <summary>
    /// Run cleanup for cached web orders and closed local POS orders.
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

            result.WebOrdersDeleted = await CountOrdersForCleanupAsync(connection, "web", WebOrderRetentionDays, requireClosed: false);
            result.LocalOrdersDeleted = await CountOrdersForCleanupAsync(connection, "local", LocalOrderRetentionDays, requireClosed: true);

            if (result.TotalOrdersDeleted == 0)
            {
                Debug.WriteLine(" No old orders eligible for cleanup");
                result.Success = true;
                return result;
            }

            Debug.WriteLine($" Found {result.WebOrdersDeleted} cached web order(s) older than {WebOrderRetentionDays} days");
            Debug.WriteLine($" Found {result.LocalOrdersDeleted} closed local POS order(s) older than {LocalOrderRetentionDays} days");

            if (result.WebOrdersDeleted > 0)
            {
                await LogSampleOrdersAsync(connection, "web", WebOrderRetentionDays, requireClosed: false);
            }

            if (result.LocalOrdersDeleted > 0)
            {
                await LogSampleOrdersAsync(connection, "local", LocalOrderRetentionDays, requireClosed: true);
            }

            await DeleteOrdersForCleanupAsync(connection, "web", WebOrderRetentionDays, requireClosed: false, result);
            await DeleteOrdersForCleanupAsync(connection, "local", LocalOrderRetentionDays, requireClosed: true, result);

            // Optimize tables to reclaim space after the rolling cleanup.
            await OptimizeTablesAsync(connection);

            result.Success = true;
            result.Duration = (DateTime.Now - startTime).TotalMilliseconds;

            Debug.WriteLine($" Cleanup completed in {result.Duration:F0}ms");
            Debug.WriteLine($" Summary: {result.WebOrdersDeleted} web order(s), {result.LocalOrdersDeleted} local order(s), {result.ItemsDeleted} item(s), {result.AddonsDeleted} addon(s) deleted");

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

    private static string BuildCleanupPredicate(string tableAlias, bool requireClosed)
    {
        var prefix = string.IsNullOrWhiteSpace(tableAlias) ? "" : $"{tableAlias}.";
        var predicate = $@"
                {prefix}created_at < DATE_SUB(CURDATE(), INTERVAL @days DAY)
                AND COALESCE({prefix}source_channel, 'local') = @sourceChannel";

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
            var countQuery = @"
                SELECT COUNT(*) 
                FROM orders 
                WHERE created_at < DATE_SUB(CURDATE(), INTERVAL @days DAY)
                AND COALESCE(source_channel, 'local') = 'web'";

            using var countCmd = new MySqlCommand(countQuery, connection);
            countCmd.Parameters.AddWithValue("@days", WebOrderRetentionDays);
            stats.OldWebOrdersCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            stats.OldLocalOrdersCount = await CountOrdersForCleanupAsync(connection, "local", LocalOrderRetentionDays, requireClosed: true);

            // Count total local POS orders
            var localQuery = @"
                SELECT COUNT(*) 
                FROM orders 
                WHERE COALESCE(source_channel, 'local') = 'local'";

            using var localCmd = new MySqlCommand(localQuery, connection);
            stats.LocalPosOrdersCount = Convert.ToInt32(await localCmd.ExecuteScalarAsync());

            // Get last cleanup date
            var lastCleanup = Preferences.Get("LastCleanupDate", "");
            if (!string.IsNullOrEmpty(lastCleanup))
            {
                stats.LastCleanupDate = DateTime.Parse(lastCleanup);
            }

            stats.WebRetentionDays = WebOrderRetentionDays;
            stats.LocalRetentionDays = LocalOrderRetentionDays;
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
