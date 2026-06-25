using System.Globalization;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public class CustomerDataService
{
    private static bool _tableReady;
    private static bool _legacyMigrationAttempted;

    public const int CacheRetentionDays = 7;
    public const string CsvTemplateHeader = "order_types,name,phone_number,full_address,city,county,postcode";

    private readonly OrderWebCustomerCloudService _cloudService = new();

    public async Task EnsureTableAsync()
    {
        if (_tableReady)
        {
            return;
        }

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        const string createSql = @"
            CREATE TABLE IF NOT EXISTS customer_data (
                id INT AUTO_INCREMENT PRIMARY KEY,
                order_types VARCHAR(20) NOT NULL DEFAULT 'collection',
                name VARCHAR(255) NOT NULL,
                phone_number VARCHAR(50) NOT NULL,
                full_address TEXT NULL,
                city VARCHAR(100) NULL,
                county VARCHAR(100) NULL,
                postcode VARCHAR(20) NULL,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                last_collection_order_date DATETIME NULL,
                last_delivery_order_date DATETIME NULL,
                INDEX idx_customer_phone (phone_number),
                INDEX idx_customer_name (name),
                INDEX idx_customer_postcode (postcode),
                INDEX idx_customer_order_types (order_types)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        using (var createCommand = new MySqlCommand(createSql, connection))
        {
            await createCommand.ExecuteNonQueryAsync();
        }

        await EnsureCacheColumnsAsync(connection);
        await EnsureSyncQueueTableAsync(connection);
        await MigrateLegacyCustomersAsync(connection);
        await BackfillCacheMetadataAsync(connection);
        await PurgeExpiredCacheAsync(connection);
        _tableReady = true;
    }

    public async Task<string> ExportImportTemplateAsync()
    {
        var builder = new StringBuilder();
        builder.AppendLine(CsvTemplateHeader);
        builder.AppendLine("collection,John Smith,07123456789,,,");
        builder.AppendLine("delivery,Jane Doe,07987654321,\"10 High Street\",London,Greater London,SE4 2PJ");
        builder.AppendLine("both,Ali Khan,02071234567,\"Flat 2, 5 Park Road\",Manchester,Greater Manchester,M1 1AE");

        var folder = Path.Combine(FileSystem.AppDataDirectory, "exports");
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, "customer-data-import-template.csv");
        await File.WriteAllTextAsync(filePath, builder.ToString(), Encoding.UTF8);
        return filePath;
    }

    public async Task<List<CustomerDataRecord>> GetAllAsync(string? search = null, string? orderTypeFilter = null, bool recentOnly = true)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        var conditions = new List<string>();
        if (recentOnly)
        {
            conditions.Add("last_used_at >= DATE_SUB(NOW(), INTERVAL @retentionDays DAY)");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            conditions.Add("(name LIKE @search OR phone_number LIKE @search OR phone_normalized LIKE @search OR full_address LIKE @search OR postcode LIKE @search)");
        }

        if (!string.IsNullOrWhiteSpace(orderTypeFilter) && !orderTypeFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (orderTypeFilter.Equals("collection", StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add("(order_types IN ('collection', 'both'))");
            }
            else if (orderTypeFilter.Equals("delivery", StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add("(order_types IN ('delivery', 'both'))");
            }
        }

        var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : string.Empty;
        var query = $@"
            SELECT id, order_types, name, phone_number, full_address, city, county, postcode,
                   created_at, updated_at, last_collection_order_date, last_delivery_order_date,
                   cloud_customer_id, phone_normalized, sync_status, sync_error,
                   cached_at, last_used_at, last_sync_at, points_balance, tier_level
            FROM customer_data
            {whereClause}
            ORDER BY last_used_at DESC, name ASC
            LIMIT 500";

        using var command = new MySqlCommand(query, connection);
        if (recentOnly)
        {
            command.Parameters.AddWithValue("@retentionDays", CacheRetentionDays);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            command.Parameters.AddWithValue("@search", $"%{search.Trim()}%");
        }

        var records = new List<CustomerDataRecord>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public async Task<CustomerSyncSummary> GetSyncSummaryAsync()
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                SUM(CASE WHEN last_used_at >= DATE_SUB(NOW(), INTERVAL @retentionDays DAY) THEN 1 ELSE 0 END) AS total_recent,
                SUM(CASE WHEN last_used_at >= DATE_SUB(NOW(), INTERVAL @retentionDays DAY) AND sync_status = 'synced' THEN 1 ELSE 0 END) AS synced_count,
                SUM(CASE WHEN last_used_at >= DATE_SUB(NOW(), INTERVAL @retentionDays DAY) AND sync_status = 'pending' THEN 1 ELSE 0 END) AS pending_count,
                SUM(CASE WHEN last_used_at >= DATE_SUB(NOW(), INTERVAL @retentionDays DAY) AND sync_status = 'failed' THEN 1 ELSE 0 END) AS failed_count,
                MAX(last_sync_at) AS last_cloud_sync
            FROM customer_data";

        using var summaryCommand = new MySqlCommand(sql, connection);
        summaryCommand.Parameters.AddWithValue("@retentionDays", CacheRetentionDays);

        var summary = new CustomerSyncSummary();
        using (var reader = await summaryCommand.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                summary.TotalRecent = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                summary.SyncedCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                summary.PendingCount = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                summary.FailedCount = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
                summary.LastCloudSyncAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4);
            }
        }

        using var queueCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM pos_customer_sync_queue WHERE status IN ('pending', 'failed')",
            connection);
        summary.QueueCount = Convert.ToInt32(await queueCommand.ExecuteScalarAsync());

        return summary;
    }

    public async Task<(int Synced, int Failed, string? Message)> RetrySyncAsync()
    {
        await EnsureTableAsync();
        await ProcessSyncQueueAsync();

        var failedIds = await GetFailedCustomerIdsAsync();
        var synced = 0;
        var failed = 0;

        foreach (var id in failedIds)
        {
            if (await TrySyncCustomerAsync(id))
            {
                synced++;
            }
            else
            {
                failed++;
            }
        }

        var pendingIds = await GetPendingCustomerIdsAsync();
        foreach (var id in pendingIds)
        {
            if (await TrySyncCustomerAsync(id))
            {
                synced++;
            }
            else
            {
                failed++;
            }
        }

        var message = failed == 0
            ? $"Synced {synced} customer(s) to OrderWeb."
            : $"Synced {synced}, {failed} still failed. Check OrderWeb settings and try again.";

        return (synced, failed, message);
    }

    public async Task<List<CustomerDataRecord>> SearchForCollectionAsync(string? name, string? phone)
    {
        var local = await SearchAsync(name, phone, addressOrPostcode: null, includeCollection: true, includeDelivery: false);
        if (local.Count > 0 || string.IsNullOrWhiteSpace(phone))
        {
            return local;
        }

        var cloud = await _cloudService.SearchByPhoneAsync(phone);
        if (cloud == null)
        {
            return local;
        }

        var cached = await SaveCloudToCacheAsync(cloud);
        return cached == null ? local : new List<CustomerDataRecord> { cached };
    }

    public async Task<List<CustomerDataRecord>> SearchForDeliveryAsync(string? addressOrPostcode, string? name, string? phone)
    {
        var local = await SearchAsync(name, phone, addressOrPostcode, includeCollection: false, includeDelivery: true);
        if (local.Count > 0 || string.IsNullOrWhiteSpace(phone))
        {
            return local;
        }

        var cloud = await _cloudService.SearchByPhoneAsync(phone);
        if (cloud == null)
        {
            return local;
        }

        var cached = await SaveCloudToCacheAsync(cloud);
        return cached == null ? local : new List<CustomerDataRecord> { cached };
    }

    public async Task<List<CustomerDataRecord>> SearchAsync(
        string? name,
        string? phone,
        string? addressOrPostcode,
        bool includeCollection = true,
        bool includeDelivery = true)
    {
        await EnsureTableAsync();

        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            conditions.Add("name LIKE @name");
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            conditions.Add("(phone_number LIKE @phone OR phone_normalized LIKE @phoneNorm)");
        }

        if (!string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            conditions.Add("(full_address LIKE @address OR postcode LIKE @address OR city LIKE @address)");
        }

        if (conditions.Count == 0)
        {
            return new List<CustomerDataRecord>();
        }

        var typeFilter = new List<string>();
        if (includeCollection)
        {
            typeFilter.Add("order_types IN ('collection', 'both')");
        }

        if (includeDelivery)
        {
            typeFilter.Add("order_types IN ('delivery', 'both')");
        }

        var typeClause = typeFilter.Count > 0 ? $" AND ({string.Join(" OR ", typeFilter)})" : string.Empty;

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        var query = $@"
            SELECT id, order_types, name, phone_number, full_address, city, county, postcode,
                   created_at, updated_at, last_collection_order_date, last_delivery_order_date,
                   cloud_customer_id, phone_normalized, sync_status, sync_error,
                   cached_at, last_used_at, last_sync_at, points_balance, tier_level
            FROM customer_data
            WHERE ({string.Join(" OR ", conditions)}){typeClause}
              AND last_used_at >= DATE_SUB(NOW(), INTERVAL @retentionDays DAY)
            ORDER BY last_used_at DESC, name ASC
            LIMIT 50";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@retentionDays", CacheRetentionDays);
        if (!string.IsNullOrWhiteSpace(name))
        {
            command.Parameters.AddWithValue("@name", $"%{name.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            command.Parameters.AddWithValue("@phone", $"%{phone.Trim()}%");
            command.Parameters.AddWithValue("@phoneNorm", $"%{OrderWebCustomerCloudService.NormalizePhone(phone)}%");
        }

        if (!string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            command.Parameters.AddWithValue("@address", $"%{addressOrPostcode.Trim()}%");
        }

        var records = new List<CustomerDataRecord>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public async Task UpsertCollectionCustomerAsync(string name, string phoneNumber)
    {
        var id = await UpsertCollectionCustomerInternalAsync(name, phoneNumber);
        ScheduleCloudSync(id);
    }

    private async Task<int> UpsertCollectionCustomerInternalAsync(string name, string phoneNumber)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        var normalizedPhone = OrderWebCustomerCloudService.NormalizePhone(phoneNumber);
        var existing = await FindByPhoneAsync(connection, phoneNumber);

        if (existing == null)
        {
            const string insertSql = @"
                INSERT INTO customer_data
                (order_types, name, phone_number, phone_normalized, created_at, updated_at, cached_at, last_used_at, sync_status)
                VALUES ('collection', @name, @phone, @phoneNorm, NOW(), NOW(), NOW(), NOW(), 'pending')";

            using var insertCommand = new MySqlCommand(insertSql, connection);
            insertCommand.Parameters.AddWithValue("@name", name.Trim());
            insertCommand.Parameters.AddWithValue("@phone", phoneNumber.Trim());
            insertCommand.Parameters.AddWithValue("@phoneNorm", normalizedPhone);
            await insertCommand.ExecuteNonQueryAsync();
            return (int)insertCommand.LastInsertedId;
        }

        var orderTypes = existing.OrderTypes.Equals("delivery", StringComparison.OrdinalIgnoreCase)
            ? "both"
            : existing.OrderTypes;

        const string updateSql = @"
            UPDATE customer_data
            SET name = @name,
                order_types = @orderTypes,
                phone_normalized = @phoneNorm,
                updated_at = NOW(),
                last_used_at = NOW(),
                sync_status = CASE WHEN sync_status = 'synced' THEN 'pending' ELSE sync_status END
            WHERE id = @id";

        using var updateCommand = new MySqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("@name", name.Trim());
        updateCommand.Parameters.AddWithValue("@orderTypes", orderTypes);
        updateCommand.Parameters.AddWithValue("@phoneNorm", normalizedPhone);
        updateCommand.Parameters.AddWithValue("@id", existing.Id);
        await updateCommand.ExecuteNonQueryAsync();
        return existing.Id;
    }

    public async Task UpsertDeliveryCustomerAsync(
        string name,
        string phoneNumber,
        string fullAddress,
        string? city = null,
        string? county = null,
        string? postcode = null)
    {
        var id = await UpsertDeliveryCustomerInternalAsync(name, phoneNumber, fullAddress, city, county, postcode);
        ScheduleCloudSync(id);
    }

    private async Task<int> UpsertDeliveryCustomerInternalAsync(
        string name,
        string phoneNumber,
        string fullAddress,
        string? city = null,
        string? county = null,
        string? postcode = null)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        var normalizedPostcode = DeliveryZoneService.NormalizePostcode(postcode);
        var normalizedPhone = OrderWebCustomerCloudService.NormalizePhone(phoneNumber);
        var existing = await FindByPhoneAsync(connection, phoneNumber);

        if (existing == null)
        {
            const string insertSql = @"
                INSERT INTO customer_data
                (order_types, name, phone_number, phone_normalized, full_address, city, county, postcode,
                 created_at, updated_at, cached_at, last_used_at, sync_status)
                VALUES ('delivery', @name, @phone, @phoneNorm, @address, @city, @county, @postcode, NOW(), NOW(), NOW(), NOW(), 'pending')";

            using var insertCommand = new MySqlCommand(insertSql, connection);
            insertCommand.Parameters.AddWithValue("@name", name.Trim());
            insertCommand.Parameters.AddWithValue("@phone", phoneNumber.Trim());
            insertCommand.Parameters.AddWithValue("@phoneNorm", normalizedPhone);
            insertCommand.Parameters.AddWithValue("@address", fullAddress.Trim());
            insertCommand.Parameters.AddWithValue("@city", city?.Trim() ?? string.Empty);
            insertCommand.Parameters.AddWithValue("@county", county?.Trim() ?? string.Empty);
            insertCommand.Parameters.AddWithValue("@postcode", normalizedPostcode ?? string.Empty);
            await insertCommand.ExecuteNonQueryAsync();
            return (int)insertCommand.LastInsertedId;
        }

        var orderTypes = existing.OrderTypes.Equals("collection", StringComparison.OrdinalIgnoreCase)
            ? "both"
            : existing.OrderTypes;

        const string updateSql = @"
            UPDATE customer_data
            SET name = @name,
                order_types = @orderTypes,
                phone_normalized = @phoneNorm,
                full_address = @address,
                city = @city,
                county = @county,
                postcode = @postcode,
                updated_at = NOW(),
                last_used_at = NOW(),
                sync_status = CASE WHEN sync_status = 'synced' THEN 'pending' ELSE sync_status END
            WHERE id = @id";

        using var updateCommand = new MySqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("@name", name.Trim());
        updateCommand.Parameters.AddWithValue("@orderTypes", orderTypes);
        updateCommand.Parameters.AddWithValue("@phoneNorm", normalizedPhone);
        updateCommand.Parameters.AddWithValue("@address", fullAddress.Trim());
        updateCommand.Parameters.AddWithValue("@city", city?.Trim() ?? string.Empty);
        updateCommand.Parameters.AddWithValue("@county", county?.Trim() ?? string.Empty);
        updateCommand.Parameters.AddWithValue("@postcode", normalizedPostcode ?? string.Empty);
        updateCommand.Parameters.AddWithValue("@id", existing.Id);
        await updateCommand.ExecuteNonQueryAsync();
        return existing.Id;
    }

    public async Task UpdateLastCollectionOrderDateAsync(string phoneNumber)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE customer_data
            SET last_collection_order_date = NOW(),
                updated_at = NOW(),
                last_used_at = NOW()
            WHERE phone_number = @phone OR phone_normalized = @phoneNorm";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@phone", phoneNumber.Trim());
        command.Parameters.AddWithValue("@phoneNorm", OrderWebCustomerCloudService.NormalizePhone(phoneNumber));
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateLastDeliveryOrderDateAsync(string phoneNumber)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE customer_data
            SET last_delivery_order_date = NOW(),
                updated_at = NOW(),
                last_used_at = NOW()
            WHERE phone_number = @phone OR phone_normalized = @phoneNorm";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@phone", phoneNumber.Trim());
        command.Parameters.AddWithValue("@phoneNorm", OrderWebCustomerCloudService.NormalizePhone(phoneNumber));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteLocalCacheAsync(int id)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        using var command = new MySqlCommand("DELETE FROM customer_data WHERE id = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        var deleted = await command.ExecuteNonQueryAsync() > 0;

        if (deleted)
        {
            using var queueCommand = new MySqlCommand(
                "DELETE FROM pos_customer_sync_queue WHERE local_customer_cache_id = @id",
                connection);
            queueCommand.Parameters.AddWithValue("@id", id);
            await queueCommand.ExecuteNonQueryAsync();
        }

        return deleted;
    }

    public Task<bool> DeleteAsync(int id) => DeleteLocalCacheAsync(id);

    public async Task<int> GetCountAsync()
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        using var command = new MySqlCommand("SELECT COUNT(*) FROM customer_data", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<string> ExportCsvAsync()
    {
        var records = await GetAllAsync();
        var builder = new StringBuilder();
        builder.AppendLine("id,order_types,name,phone_number,full_address,city,county,postcode,created_at,updated_at,last_collection_order_date,last_delivery_order_date");

        foreach (var record in records)
        {
            builder.AppendLine(string.Join(",",
                record.Id.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(record.OrderTypes),
                EscapeCsv(record.Name),
                EscapeCsv(record.PhoneNumber),
                EscapeCsv(record.FullAddress),
                EscapeCsv(record.City),
                EscapeCsv(record.County),
                EscapeCsv(record.Postcode),
                EscapeCsv(record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                EscapeCsv(record.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                EscapeCsv(record.LastCollectionOrderDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                EscapeCsv(record.LastDeliveryOrderDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))));
        }

        var folder = Path.Combine(FileSystem.AppDataDirectory, "exports");
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, $"customer-data-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        await File.WriteAllTextAsync(filePath, builder.ToString(), Encoding.UTF8);
        return filePath;
    }

    public async Task<CustomerDataImportResult> ImportCsvAsync(string filePath)
    {
        await EnsureTableAsync();

        var result = new CustomerDataImportResult();
        var lines = await File.ReadAllLinesAsync(filePath);
        if (lines.Length <= 1)
        {
            result.Errors.Add("CSV file is empty or has no data rows.");
            return result;
        }

        var headerFields = ParseCsvLine(lines[0]);
        var columnMap = BuildColumnMap(headerFields);
        if (!columnMap.ContainsKey("name") || !columnMap.ContainsKey("phone_number"))
        {
            result.Errors.Add("CSV must include name and phone_number columns. Download the import template first.");
            return result;
        }

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var fields = ParseCsvLine(line);
                var name = GetField(fields, columnMap, "name");
                var phone = GetField(fields, columnMap, "phone_number");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone))
                {
                    result.Skipped++;
                    result.Errors.Add($"Row {i + 1}: name and phone_number are required.");
                    continue;
                }

                var orderTypes = NormalizeOrderTypes(GetField(fields, columnMap, "order_types", "collection"));
                var fullAddress = GetField(fields, columnMap, "full_address");
                var city = GetField(fields, columnMap, "city");
                var county = GetField(fields, columnMap, "county");
                var postcodeRaw = GetField(fields, columnMap, "postcode");
                var postcode = DeliveryZoneService.NormalizePostcode(postcodeRaw) ?? postcodeRaw;

                var existing = await FindByPhoneAsync(connection, phone);
                if (existing == null)
                {
                    const string insertSql = @"
                        INSERT INTO customer_data
                        (order_types, name, phone_number, full_address, city, county, postcode, created_at, updated_at)
                        VALUES (@orderTypes, @name, @phone, @address, @city, @county, @postcode, NOW(), NOW())";

                    using var insertCommand = new MySqlCommand(insertSql, connection);
                    insertCommand.Parameters.AddWithValue("@orderTypes", orderTypes);
                    insertCommand.Parameters.AddWithValue("@name", name);
                    insertCommand.Parameters.AddWithValue("@phone", phone);
                    insertCommand.Parameters.AddWithValue("@address", fullAddress);
                    insertCommand.Parameters.AddWithValue("@city", city);
                    insertCommand.Parameters.AddWithValue("@county", county);
                    insertCommand.Parameters.AddWithValue("@postcode", postcode);
                    await insertCommand.ExecuteNonQueryAsync();
                    result.Inserted++;
                }
                else
                {
                    var mergedTypes = MergeOrderTypes(existing.OrderTypes, orderTypes);
                    const string updateSql = @"
                        UPDATE customer_data
                        SET order_types = @orderTypes,
                            name = @name,
                            full_address = @address,
                            city = @city,
                            county = @county,
                            postcode = @postcode,
                            updated_at = NOW()
                        WHERE id = @id";

                    using var updateCommand = new MySqlCommand(updateSql, connection);
                    updateCommand.Parameters.AddWithValue("@orderTypes", mergedTypes);
                    updateCommand.Parameters.AddWithValue("@name", name);
                    updateCommand.Parameters.AddWithValue("@address", string.IsNullOrWhiteSpace(fullAddress) ? existing.FullAddress : fullAddress);
                    updateCommand.Parameters.AddWithValue("@city", string.IsNullOrWhiteSpace(city) ? existing.City : city);
                    updateCommand.Parameters.AddWithValue("@county", string.IsNullOrWhiteSpace(county) ? existing.County : county);
                    updateCommand.Parameters.AddWithValue("@postcode", string.IsNullOrWhiteSpace(postcode) ? existing.Postcode : postcode);
                    updateCommand.Parameters.AddWithValue("@id", existing.Id);
                    await updateCommand.ExecuteNonQueryAsync();
                    result.Updated++;
                }
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Errors.Add($"Row {i + 1}: {ex.Message}");
            }
        }

        return result;
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> headerFields)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerFields.Count; i++)
        {
            var key = headerFields[i].Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static string GetField(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> columnMap, string column, string fallback = "")
    {
        if (!columnMap.TryGetValue(column, out var index) || index >= fields.Count)
        {
            return fallback;
        }

        return fields[index].Trim();
    }

    private static async Task MigrateLegacyCustomersAsync(MySqlConnection connection)
    {
        if (_legacyMigrationAttempted)
        {
            return;
        }

        _legacyMigrationAttempted = true;

        try
        {
            const string collectionSql = @"
                INSERT INTO customer_data
                (order_types, name, phone_number, created_at, updated_at, last_collection_order_date)
                SELECT 'collection', c.name, c.phone_number, c.created_at, COALESCE(c.last_order_date, c.created_at), c.last_order_date
                FROM collection_customers c
                WHERE c.phone_number IS NOT NULL AND c.phone_number <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM customer_data cd
                      WHERE cd.phone_number COLLATE utf8mb4_unicode_ci = c.phone_number COLLATE utf8mb4_unicode_ci
                  )";

            using (var collectionCommand = new MySqlCommand(collectionSql, connection))
            {
                await collectionCommand.ExecuteNonQueryAsync();
            }
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1267)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomerData] Collection migration skipped: {ex.Message}");
        }

        try
        {
            const string deliverySql = @"
                INSERT INTO customer_data
                (order_types, name, phone_number, full_address, postcode, created_at, updated_at, last_delivery_order_date)
                SELECT 'delivery', d.name, d.phone_number, d.address,
                       CASE
                           WHEN LOCATE('\n', d.address) > 0 THEN TRIM(SUBSTRING_INDEX(d.address, '\n', -1))
                           ELSE ''
                       END,
                       d.created_at, COALESCE(d.last_order_date, d.created_at), d.last_order_date
                FROM delivery_customers d
                WHERE d.phone_number IS NOT NULL AND d.phone_number <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM customer_data c
                      WHERE c.phone_number COLLATE utf8mb4_unicode_ci = d.phone_number COLLATE utf8mb4_unicode_ci
                  )";

            using var deliveryCommand = new MySqlCommand(deliverySql, connection);
            await deliveryCommand.ExecuteNonQueryAsync();

            const string mergeSql = @"
                UPDATE customer_data c
                INNER JOIN delivery_customers d
                    ON d.phone_number COLLATE utf8mb4_unicode_ci = c.phone_number COLLATE utf8mb4_unicode_ci
                SET c.order_types = 'both',
                    c.full_address = CASE WHEN d.address <> '' THEN d.address ELSE c.full_address END,
                    c.last_delivery_order_date = COALESCE(d.last_order_date, c.last_delivery_order_date),
                    c.updated_at = NOW()
                WHERE c.order_types = 'collection'";

            using var mergeCommand = new MySqlCommand(mergeSql, connection);
            await mergeCommand.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1267)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomerData] Delivery migration skipped: {ex.Message}");
        }
    }

    private static async Task<CustomerDataRecord?> FindByPhoneAsync(MySqlConnection connection, string phoneNumber)
    {
        var normalized = OrderWebCustomerCloudService.NormalizePhone(phoneNumber);
        const string sql = @"
            SELECT id, order_types, name, phone_number, full_address, city, county, postcode,
                   created_at, updated_at, last_collection_order_date, last_delivery_order_date,
                   cloud_customer_id, phone_normalized, sync_status, sync_error,
                   cached_at, last_used_at, last_sync_at, points_balance, tier_level
            FROM customer_data
            WHERE phone_number = @phone OR phone_normalized = @phoneNorm
            LIMIT 1";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@phone", phoneNumber.Trim());
        command.Parameters.AddWithValue("@phoneNorm", normalized);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadRecord(reader);
    }

    private static CustomerDataRecord ReadRecord(MySqlDataReader reader)
    {
        return new CustomerDataRecord
        {
            Id = reader.GetInt32("id"),
            OrderTypes = reader.GetString("order_types"),
            Name = reader.GetString("name"),
            PhoneNumber = reader.GetString("phone_number"),
            FullAddress = reader.IsDBNull(reader.GetOrdinal("full_address")) ? string.Empty : reader.GetString("full_address"),
            City = reader.IsDBNull(reader.GetOrdinal("city")) ? string.Empty : reader.GetString("city"),
            County = reader.IsDBNull(reader.GetOrdinal("county")) ? string.Empty : reader.GetString("county"),
            Postcode = reader.IsDBNull(reader.GetOrdinal("postcode")) ? string.Empty : reader.GetString("postcode"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
            LastCollectionOrderDate = reader.IsDBNull(reader.GetOrdinal("last_collection_order_date"))
                ? null
                : reader.GetDateTime("last_collection_order_date"),
            LastDeliveryOrderDate = reader.IsDBNull(reader.GetOrdinal("last_delivery_order_date"))
                ? null
                : reader.GetDateTime("last_delivery_order_date"),
            CloudCustomerId = ReadOptionalString(reader, "cloud_customer_id"),
            PhoneNormalized = ReadOptionalString(reader, "phone_normalized"),
            SyncStatus = ReadOptionalString(reader, "sync_status", "pending"),
            SyncError = ReadOptionalString(reader, "sync_error"),
            CachedAt = ReadOptionalDateTime(reader, "cached_at") ?? reader.GetDateTime("created_at"),
            LastUsedAt = ReadOptionalDateTime(reader, "last_used_at") ?? reader.GetDateTime("updated_at"),
            LastSyncAt = ReadOptionalDateTime(reader, "last_sync_at"),
            PointsBalance = ReadOptionalInt(reader, "points_balance"),
            TierLevel = ReadOptionalString(reader, "tier_level")
        };
    }

    private static string ReadOptionalString(MySqlDataReader reader, string column, string fallback = "")
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
    }

    private static int ReadOptionalInt(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private static DateTime? ReadOptionalDateTime(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static async Task EnsureCacheColumnsAsync(MySqlConnection connection)
    {
        const string alterSql = @"
            ALTER TABLE customer_data
            ADD COLUMN IF NOT EXISTS cloud_customer_id VARCHAR(64) DEFAULT '',
            ADD COLUMN IF NOT EXISTS phone_normalized VARCHAR(20) DEFAULT '',
            ADD COLUMN IF NOT EXISTS sync_status VARCHAR(20) NOT NULL DEFAULT 'pending',
            ADD COLUMN IF NOT EXISTS sync_error TEXT NULL,
            ADD COLUMN IF NOT EXISTS cached_at DATETIME NULL,
            ADD COLUMN IF NOT EXISTS last_used_at DATETIME NULL,
            ADD COLUMN IF NOT EXISTS last_sync_at DATETIME NULL,
            ADD COLUMN IF NOT EXISTS points_balance INT DEFAULT 0,
            ADD COLUMN IF NOT EXISTS tier_level VARCHAR(50) DEFAULT ''";

        using var command = new MySqlCommand(alterSql, connection);
        await command.ExecuteNonQueryAsync();

        const string indexSql = @"
            ALTER TABLE customer_data
            ADD INDEX IF NOT EXISTS idx_customer_phone_norm (phone_normalized),
            ADD INDEX IF NOT EXISTS idx_customer_last_used (last_used_at),
            ADD INDEX IF NOT EXISTS idx_customer_sync_status (sync_status)";

        try
        {
            using var indexCommand = new MySqlCommand(indexSql, connection);
            await indexCommand.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomerData] Index ensure skipped: {ex.Message}");
        }
    }

    private static async Task EnsureSyncQueueTableAsync(MySqlConnection connection)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS pos_customer_sync_queue (
                queue_id INT AUTO_INCREMENT PRIMARY KEY,
                local_customer_cache_id INT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                retry_count INT DEFAULT 0,
                next_retry_at DATETIME NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                last_error TEXT NULL,
                INDEX idx_queue_status (status),
                INDEX idx_queue_cache_id (local_customer_cache_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task BackfillCacheMetadataAsync(MySqlConnection connection)
    {
        const string sql = @"
            UPDATE customer_data
            SET cached_at = COALESCE(cached_at, created_at, NOW()),
                last_used_at = COALESCE(last_used_at, updated_at, created_at, NOW()),
                sync_status = COALESCE(NULLIF(sync_status, ''), 'pending')
            WHERE cached_at IS NULL OR last_used_at IS NULL OR sync_status IS NULL OR sync_status = ''";

        using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();

        const string selectSql = @"
            SELECT id, phone_number
            FROM customer_data
            WHERE phone_normalized IS NULL OR phone_normalized = ''";

        var updates = new List<(int Id, string Normalized)>();
        using (var selectCommand = new MySqlCommand(selectSql, connection))
        using (var reader = await selectCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                updates.Add((reader.GetInt32("id"), OrderWebCustomerCloudService.NormalizePhone(reader.GetString("phone_number"))));
            }
        }

        foreach (var (id, normalized) in updates)
        {
            using var updateCommand = new MySqlCommand(
                "UPDATE customer_data SET phone_normalized = @phoneNorm WHERE id = @id",
                connection);
            updateCommand.Parameters.AddWithValue("@phoneNorm", normalized);
            updateCommand.Parameters.AddWithValue("@id", id);
            await updateCommand.ExecuteNonQueryAsync();
        }
    }

    public async Task PurgeExpiredCacheAsync()
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await PurgeExpiredCacheAsync(connection);
    }

    private static async Task PurgeExpiredCacheAsync(MySqlConnection connection)
    {
        const string sql = @"
            DELETE c FROM customer_data c
            LEFT JOIN pos_customer_sync_queue q
                ON q.local_customer_cache_id = c.id AND q.status IN ('pending', 'failed')
            WHERE c.last_used_at < DATE_SUB(NOW(), INTERVAL @retentionDays DAY)
              AND q.queue_id IS NULL";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@retentionDays", CacheRetentionDays);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<CustomerDataRecord?> SaveCloudToCacheAsync(CustomerDataRecord cloudRecord)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        var existing = await FindByPhoneAsync(connection, cloudRecord.PhoneNumber);
        if (existing != null)
        {
            const string updateSql = @"
                UPDATE customer_data
                SET name = @name,
                    order_types = @orderTypes,
                    cloud_customer_id = @cloudId,
                    phone_normalized = @phoneNorm,
                    full_address = CASE WHEN @address <> '' THEN @address ELSE full_address END,
                    city = CASE WHEN @city <> '' THEN @city ELSE city END,
                    county = CASE WHEN @county <> '' THEN @county ELSE county END,
                    postcode = CASE WHEN @postcode <> '' THEN @postcode ELSE postcode END,
                    points_balance = @points,
                    tier_level = @tier,
                    sync_status = 'synced',
                    sync_error = NULL,
                    last_sync_at = NOW(),
                    last_used_at = NOW(),
                    updated_at = NOW()
                WHERE id = @id";

            using var updateCommand = new MySqlCommand(updateSql, connection);
            updateCommand.Parameters.AddWithValue("@name", cloudRecord.Name);
            updateCommand.Parameters.AddWithValue("@orderTypes", cloudRecord.OrderTypes);
            updateCommand.Parameters.AddWithValue("@cloudId", cloudRecord.CloudCustomerId);
            updateCommand.Parameters.AddWithValue("@phoneNorm", cloudRecord.PhoneNormalized);
            updateCommand.Parameters.AddWithValue("@address", cloudRecord.FullAddress);
            updateCommand.Parameters.AddWithValue("@city", cloudRecord.City);
            updateCommand.Parameters.AddWithValue("@county", cloudRecord.County);
            updateCommand.Parameters.AddWithValue("@postcode", cloudRecord.Postcode);
            updateCommand.Parameters.AddWithValue("@points", cloudRecord.PointsBalance);
            updateCommand.Parameters.AddWithValue("@tier", cloudRecord.TierLevel);
            updateCommand.Parameters.AddWithValue("@id", existing.Id);
            await updateCommand.ExecuteNonQueryAsync();

            return await GetByIdAsync(connection, existing.Id);
        }

        const string insertSql = @"
            INSERT INTO customer_data
            (order_types, name, phone_number, phone_normalized, cloud_customer_id, full_address, city, county, postcode,
             points_balance, tier_level, created_at, updated_at, cached_at, last_used_at, sync_status, last_sync_at)
            VALUES (@orderTypes, @name, @phone, @phoneNorm, @cloudId, @address, @city, @county, @postcode,
                    @points, @tier, NOW(), NOW(), NOW(), NOW(), 'synced', NOW())";

        using var insertCommand = new MySqlCommand(insertSql, connection);
        insertCommand.Parameters.AddWithValue("@orderTypes", cloudRecord.OrderTypes);
        insertCommand.Parameters.AddWithValue("@name", cloudRecord.Name);
        insertCommand.Parameters.AddWithValue("@phone", cloudRecord.PhoneNumber);
        insertCommand.Parameters.AddWithValue("@phoneNorm", cloudRecord.PhoneNormalized);
        insertCommand.Parameters.AddWithValue("@cloudId", cloudRecord.CloudCustomerId);
        insertCommand.Parameters.AddWithValue("@address", cloudRecord.FullAddress);
        insertCommand.Parameters.AddWithValue("@city", cloudRecord.City);
        insertCommand.Parameters.AddWithValue("@county", cloudRecord.County);
        insertCommand.Parameters.AddWithValue("@postcode", cloudRecord.Postcode);
        insertCommand.Parameters.AddWithValue("@points", cloudRecord.PointsBalance);
        insertCommand.Parameters.AddWithValue("@tier", cloudRecord.TierLevel);
        await insertCommand.ExecuteNonQueryAsync();

        return await GetByIdAsync(connection, (int)insertCommand.LastInsertedId);
    }

    private static async Task<CustomerDataRecord?> GetByIdAsync(MySqlConnection connection, int id)
    {
        const string sql = @"
            SELECT id, order_types, name, phone_number, full_address, city, county, postcode,
                   created_at, updated_at, last_collection_order_date, last_delivery_order_date,
                   cloud_customer_id, phone_normalized, sync_status, sync_error,
                   cached_at, last_used_at, last_sync_at, points_balance, tier_level
            FROM customer_data
            WHERE id = @id
            LIMIT 1";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadRecord(reader) : null;
    }

    private void ScheduleCloudSync(int localId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await TrySyncCustomerAsync(localId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CustomerData] Background sync failed: {ex.Message}");
            }
        });
    }

    private async Task<bool> TrySyncCustomerAsync(int localId)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        var record = await GetByIdAsync(connection, localId);
        if (record == null)
        {
            return false;
        }

        var payload = BuildUpsertPayload(record);
        var result = await _cloudService.UpsertAsync(payload);

        if (result.Success && result.Customer != null)
        {
            const string successSql = @"
                UPDATE customer_data
                SET cloud_customer_id = @cloudId,
                    sync_status = 'synced',
                    sync_error = NULL,
                    last_sync_at = NOW(),
                    points_balance = @points,
                    tier_level = @tier
                WHERE id = @id";

            using var successCommand = new MySqlCommand(successSql, connection);
            successCommand.Parameters.AddWithValue("@cloudId", result.Customer.CloudCustomerId);
            successCommand.Parameters.AddWithValue("@points", result.Customer.PointsBalance);
            successCommand.Parameters.AddWithValue("@tier", result.Customer.TierLevel);
            successCommand.Parameters.AddWithValue("@id", localId);
            await successCommand.ExecuteNonQueryAsync();

            await RemoveQueueEntriesAsync(connection, localId);
            return true;
        }

        await MarkSyncFailedAsync(connection, localId, result.Error ?? "Cloud sync failed.");
        await EnqueueSyncAsync(connection, localId, payload, result.Error);
        return false;
    }

    private static CustomerCloudUpsertPayload BuildUpsertPayload(CustomerDataRecord record)
    {
        object? address = null;
        if (!string.IsNullOrWhiteSpace(record.FullAddress)
            || !string.IsNullOrWhiteSpace(record.Postcode))
        {
            address = new
            {
                line1 = record.FullAddress,
                city = record.City,
                county = record.County,
                postcode = record.Postcode
            };
        }

        return new CustomerCloudUpsertPayload
        {
            LocalId = record.Id,
            PhoneNormalized = string.IsNullOrWhiteSpace(record.PhoneNormalized)
                ? OrderWebCustomerCloudService.NormalizePhone(record.PhoneNumber)
                : record.PhoneNormalized,
            Name = record.Name,
            OrderType = record.OrderTypes.Equals("both", StringComparison.OrdinalIgnoreCase)
                ? "delivery"
                : record.OrderTypes,
            Address = address
        };
    }

    private static async Task MarkSyncFailedAsync(MySqlConnection connection, int localId, string error)
    {
        const string sql = @"
            UPDATE customer_data
            SET sync_status = 'failed',
                sync_error = @error
            WHERE id = @id";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@error", error);
        command.Parameters.AddWithValue("@id", localId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnqueueSyncAsync(
        MySqlConnection connection,
        int localId,
        CustomerCloudUpsertPayload payload,
        string? error)
    {
        const string deleteSql = "DELETE FROM pos_customer_sync_queue WHERE local_customer_cache_id = @id AND status = 'pending'";
        using (var deleteCommand = new MySqlCommand(deleteSql, connection))
        {
            deleteCommand.Parameters.AddWithValue("@id", localId);
            await deleteCommand.ExecuteNonQueryAsync();
        }

        const string insertSql = @"
            INSERT INTO pos_customer_sync_queue
            (local_customer_cache_id, payload_json, created_at, retry_count, status, last_error)
            VALUES (@id, @payload, NOW(), 0, 'pending', @error)";

        using var insertCommand = new MySqlCommand(insertSql, connection);
        insertCommand.Parameters.AddWithValue("@id", localId);
        insertCommand.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(payload));
        insertCommand.Parameters.AddWithValue("@error", error ?? string.Empty);
        await insertCommand.ExecuteNonQueryAsync();
    }

    private static async Task RemoveQueueEntriesAsync(MySqlConnection connection, int localId)
    {
        using var command = new MySqlCommand(
            "DELETE FROM pos_customer_sync_queue WHERE local_customer_cache_id = @id",
            connection);
        command.Parameters.AddWithValue("@id", localId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ProcessSyncQueueAsync()
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT queue_id, local_customer_cache_id, payload_json
            FROM pos_customer_sync_queue
            WHERE status IN ('pending', 'failed')
            ORDER BY created_at ASC
            LIMIT 25";

        var queueItems = new List<(int QueueId, int LocalId, CustomerCloudUpsertPayload Payload)>();
        using (var command = new MySqlCommand(sql, connection))
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var payloadJson = reader.GetString("payload_json");
                var payload = JsonSerializer.Deserialize<CustomerCloudUpsertPayload>(payloadJson);
                if (payload != null)
                {
                    queueItems.Add((reader.GetInt32("queue_id"), reader.GetInt32("local_customer_cache_id"), payload));
                }
            }
        }

        foreach (var (queueId, localId, payload) in queueItems)
        {
            var result = await _cloudService.UpsertAsync(payload);
            if (result.Success)
            {
                using var successCommand = new MySqlCommand(
                    "UPDATE customer_data SET sync_status = 'synced', sync_error = NULL, last_sync_at = NOW() WHERE id = @id",
                    connection);
                successCommand.Parameters.AddWithValue("@id", localId);
                await successCommand.ExecuteNonQueryAsync();

                using var deleteCommand = new MySqlCommand(
                    "DELETE FROM pos_customer_sync_queue WHERE queue_id = @queueId",
                    connection);
                deleteCommand.Parameters.AddWithValue("@queueId", queueId);
                await deleteCommand.ExecuteNonQueryAsync();
            }
            else
            {
                using var failCommand = new MySqlCommand(@"
                    UPDATE pos_customer_sync_queue
                    SET status = 'failed',
                        retry_count = retry_count + 1,
                        last_error = @error
                    WHERE queue_id = @queueId",
                    connection);
                failCommand.Parameters.AddWithValue("@error", result.Error ?? "Sync failed");
                failCommand.Parameters.AddWithValue("@queueId", queueId);
                await failCommand.ExecuteNonQueryAsync();

                await MarkSyncFailedAsync(connection, localId, result.Error ?? "Sync failed");
            }
        }
    }

    private async Task<List<int>> GetFailedCustomerIdsAsync()
    {
        return await GetCustomerIdsBySyncStatusAsync("failed");
    }

    private async Task<List<int>> GetPendingCustomerIdsAsync()
    {
        return await GetCustomerIdsBySyncStatusAsync("pending");
    }

    private async Task<List<int>> GetCustomerIdsBySyncStatusAsync(string syncStatus)
    {
        await EnsureTableAsync();

        using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT id
            FROM customer_data
            WHERE sync_status = @status
              AND last_used_at >= DATE_SUB(NOW(), INTERVAL @retentionDays DAY)
            ORDER BY last_used_at DESC
            LIMIT 100";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@status", syncStatus);
        command.Parameters.AddWithValue("@retentionDays", CacheRetentionDays);

        var ids = new List<int>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32("id"));
        }

        return ids;
    }

    private static string NormalizeOrderTypes(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "delivery" => "delivery",
            "both" => "both",
            "collection & delivery" => "both",
            "collection and delivery" => "both",
            _ => "collection"
        };
    }

    private static string MergeOrderTypes(string existing, string incoming)
    {
        if (existing.Equals(incoming, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        if (existing.Equals("both", StringComparison.OrdinalIgnoreCase)
            || incoming.Equals("both", StringComparison.OrdinalIgnoreCase))
        {
            return "both";
        }

        return "both";
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        fields.Add(current.ToString());
        return fields;
    }
}
