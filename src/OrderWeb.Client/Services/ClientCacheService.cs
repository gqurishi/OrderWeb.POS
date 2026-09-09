using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Contracts.Dtos;
using SQLite;

namespace OrderWeb.Client.Services;

public sealed class ClientCacheService
{
    public const int CurrentSchemaVersion = 1;
    private const string SecureTerminalTokenKey = "orderweb.client.terminal-token";
    private const string SecureSessionTokenKey = "orderweb.client.session-token";

    private readonly SQLiteAsyncConnection _database;

    public ClientCacheService()
    {
        SQLitePCL.Batteries_V2.Init();

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "orderweb_client_cache.db3");
        _database = new SQLiteAsyncConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    public async Task InitializeAsync()
    {
        foreach (var statement in ClientCacheSchema.CreateStatements)
        {
            await _database.ExecuteAsync(statement);
        }

        foreach (var statement in ClientCacheSchema.MigrationStatements)
        {
            try
            {
                await _database.ExecuteAsync(statement);
            }
            catch (SQLiteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        await UpsertDeviceConfigAsync("cache_role", "client_cache_only");
        await UpsertDeviceConfigAsync("schema_version", CurrentSchemaVersion.ToString());
        await UpsertSyncStateAsync("schema_version", CurrentSchemaVersion.ToString());
        await PurgeAllLocalDemoDataAsync();

        // A crash after a sync began but before its transaction committed does
        // not corrupt the prior snapshot. Keep a recovery marker so the next
        // connection performs a new full snapshot rather than trusting partial
        // transport state.
        if (string.Equals(await GetSyncValueAsync("sync_in_progress"), "true", StringComparison.OrdinalIgnoreCase))
        {
            await UpsertSyncStateAsync("forced_full_resync", "true");
            await UpsertSyncStateAsync("sync_in_progress", "false");
        }
    }

    public async Task SaveBootstrapAsync(BootstrapPayload payload)
    {
        await InitializeAsync();
        ValidateSnapshot(payload);

        // Pairing from Mother is credentials-only today. Never wipe menu/floors/tables/orders
        // when the snapshot has no operational data — that caused "everything deleted" on Client.
        var hasOperationalData =
            payload.Categories.Count > 0 ||
            payload.Products.Count > 0 ||
            payload.Floors.Count > 0 ||
            payload.Tables.Count > 0 ||
            payload.OpenOrders.Count > 0;

        if (!hasOperationalData)
        {
            await SavePairingCredentialsOnlyAsync(payload);
            return;
        }

        var checksum = ComputeSnapshotChecksum(payload);
        var startedUtc = DateTimeOffset.UtcNow.ToString("O");
        // This marker is not a data-version update. It lets startup recover
        // safely if the app stops before the following transaction commits.
        await UpsertSyncStateAsync("sync_in_progress", "true");

        await _database.RunInTransactionAsync(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            // Clearing and replacing a full snapshot must be one transaction.
            // Never leave a Client with an empty cache after a failed download.
            foreach (var table in ClientCacheSchema.ResetTables)
            {
                connection.Execute($"DELETE FROM {table}");
            }
            var taxRateIds = payload.TaxRates.Select(taxRate => taxRate.Id).ToHashSet();
            var categoryIds = payload.Categories.Select(category => category.Id).ToHashSet();
            var productIds = payload.Products
                .Where(product => categoryIds.Contains(product.CategoryId))
                .Select(product => product.Id)
                .ToHashSet();
            var modifierGroupIds = payload.ModifierGroups.Select(group => group.Id).ToHashSet();
            var floorIds = payload.Floors.Select(floor => floor.Id).ToHashSet();
            var tableIds = payload.Tables
                .Where(table => floorIds.Contains(table.FloorId))
                .Select(table => table.Id)
                .ToHashSet();
            // Customer search data is not part of a Child bootstrap. Mother is
            // the source of truth and searches are performed online with an
            // authenticated staff session. This prevents a whole customer
            // directory being copied to a terminal.
            var customerIds = new HashSet<int>();
            var openOrderIds = payload.OpenOrders.Select(order => order.Id).ToHashSet();

            connection.Execute(
                "INSERT OR REPLACE INTO restaurant_info (id, mother_id, name, description, currency, time_zone, updated_utc) VALUES (1, ?, ?, ?, ?, ?, ?)",
                payload.Restaurant.MotherId,
                payload.Restaurant.Name,
                payload.Restaurant.Description,
                payload.Restaurant.Currency,
                payload.Restaurant.TimeZone,
                now);

            connection.Execute(
                "INSERT OR REPLACE INTO mother_connection (id, mode, status, display_name, api_base_url, websocket_url, pairing_code_hint, terminal_id, last_seen_utc, created_utc, updated_utc) VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "paired",
                "bootstrapped",
                payload.Terminal.TerminalName,
                payload.Terminal.ApiBaseUrl,
                payload.Terminal.WebSocketUrl,
                payload.Terminal.PairingCodeHint,
                payload.Terminal.TerminalId,
                now,
                now,
                now);

            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('terminal_id', ?, ?)", payload.Terminal.TerminalId, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('api_base_url', ?, ?)", payload.Terminal.ApiBaseUrl, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('websocket_url', ?, ?)", payload.Terminal.WebSocketUrl, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('terminal_name', ?, ?)", payload.Terminal.TerminalName, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('device_role', ?, ?)", payload.Terminal.DeviceRole, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('restaurant_name', ?, ?)", payload.Restaurant.Name, now);

            foreach (var taxRate in payload.TaxRates)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO tax_rates (id, mother_id, name, rate_percent, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?)",
                    taxRate.Id,
                    taxRate.MotherId,
                    taxRate.Name,
                    taxRate.RatePercent,
                    taxRate.IsActive ? 1 : 0,
                    now);
            }

            foreach (var category in payload.Categories)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO categories (id, mother_id, name, color, sort_order, is_active, parent_id, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    category.Id,
                    category.MotherId,
                    category.Name,
                    category.Color,
                    category.SortOrder,
                    category.IsActive ? 1 : 0,
                    category.ParentId,
                    now);
            }

            foreach (var product in payload.Products)
            {
                if (!categoryIds.Contains(product.CategoryId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO products (id, mother_id, category_id, name, description, sku, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    product.Id,
                    product.MotherId,
                    product.CategoryId,
                    product.Name,
                    product.Description,
                    product.Sku,
                    product.IsActive ? 1 : 0,
                    now);
            }

            foreach (var price in payload.Prices)
            {
                if (!productIds.Contains(price.ProductId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO prices (id, product_id, price_type, amount, currency, tax_rate_id, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    price.Id,
                    price.ProductId,
                    price.PriceType,
                    price.Amount,
                    price.Currency,
                    price.TaxRateId.HasValue && taxRateIds.Contains(price.TaxRateId.Value) ? price.TaxRateId : null,
                    now);
            }

            foreach (var group in payload.ModifierGroups)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO modifier_groups (id, mother_id, name, min_select, max_select, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    group.Id,
                    group.MotherId,
                    group.Name,
                    group.MinSelect,
                    group.MaxSelect,
                    group.IsActive ? 1 : 0,
                    now);
            }

            foreach (var modifier in payload.Modifiers)
            {
                if (!modifierGroupIds.Contains(modifier.ModifierGroupId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO modifiers (id, mother_id, modifier_group_id, name, price_delta, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    modifier.Id,
                    modifier.MotherId,
                    modifier.ModifierGroupId,
                    modifier.Name,
                    modifier.PriceDelta,
                    modifier.IsActive ? 1 : 0,
                    now);
            }

            foreach (var productModifier in payload.ProductModifiers)
            {
                if (!productIds.Contains(productModifier.ProductId) || !modifierGroupIds.Contains(productModifier.ModifierGroupId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO product_modifiers (product_id, modifier_group_id, sort_order, updated_utc) VALUES (?, ?, ?, ?)",
                    productModifier.ProductId,
                    productModifier.ModifierGroupId,
                    productModifier.SortOrder,
                    now);
            }

            foreach (var floor in payload.Floors)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO floors (id, mother_id, name, sort_order, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?)",
                    floor.Id,
                    floor.MotherId,
                    floor.Name,
                    floor.SortOrder,
                    floor.IsActive ? 1 : 0,
                    now);
            }

            foreach (var table in payload.Tables)
            {
                if (!floorIds.Contains(table.FloorId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO tables (id, mother_id, floor_id, table_number, seats, status, current_total, current_order_id, covers, server_name, session_status, minutes_occupied, version, position_x, position_y, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, NULL, 0, NULL, NULL, 0, 1, ?, ?, ?)",
                    table.Id,
                    table.MotherId,
                    table.FloorId,
                    table.TableNumber,
                    table.Seats,
                    table.Status,
                    table.CurrentTotal,
                    table.PositionX,
                    table.PositionY,
                    now);
            }

            foreach (var order in payload.OpenOrders)
            {
                var tableId = order.TableId.HasValue && tableIds.Contains(order.TableId.Value)
                    ? order.TableId
                    : null;
                var customerId = order.CustomerId.HasValue && customerIds.Contains(order.CustomerId.Value)
                    ? order.CustomerId
                    : null;

                connection.Execute(
                    "INSERT OR REPLACE INTO open_orders (id, mother_id, order_number, order_type, table_id, customer_id, guests, status, subtotal, tax, total, version, opened_utc, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)",
                    order.Id,
                    order.MotherId,
                    order.OrderNumber,
                    order.OrderType,
                    tableId,
                    customerId,
                    order.Guests,
                    order.Status,
                    order.Total,
                    Math.Round(order.Total * 0.2m, 2),
                    order.Total,
                    order.OpenedUtc,
                    now);
            }

            foreach (var item in payload.OrderItems)
            {
                if (!openOrderIds.Contains(item.OrderId))
                {
                    continue;
                }

                var productId = item.ProductId.HasValue && productIds.Contains(item.ProductId.Value)
                    ? item.ProductId
                    : null;

                connection.Execute(
                    "INSERT OR REPLACE INTO order_items (id, order_id, product_id, name, quantity, unit_price, notes, modifier_json, status, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    item.Id,
                    item.OrderId,
                    productId,
                    item.Name,
                    item.Quantity,
                    item.UnitPrice,
                    item.Notes,
                    "[]",
                    item.Status,
                    now);
            }

            // Website orders are Mother-only. Keep the legacy schema for
            // migration compatibility, but never hydrate it on a Client.

            foreach (var reservation in payload.Reservations)
            {
                var tableId = reservation.TableId.HasValue && tableIds.Contains(reservation.TableId.Value)
                    ? reservation.TableId
                    : null;

                connection.Execute(
                    "INSERT OR REPLACE INTO reservations_cache (id, mother_id, customer_name, phone, table_id, party_size, reservation_utc, status, payload_json, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    reservation.Id,
                    reservation.MotherId,
                    reservation.CustomerName,
                    reservation.Phone,
                    tableId,
                    reservation.PartySize,
                    reservation.ReservationUtc,
                    reservation.Status,
                    JsonSerializer.Serialize(reservation),
                    now);
            }

            var permissionId = 0;
            foreach (var permission in payload.Permissions)
            {
                permissionId++;
                connection.Execute(
                    "INSERT OR REPLACE INTO permissions_cache (id, user_id, permission_key, is_allowed, updated_utc) VALUES (?, ?, ?, ?, ?)",
                    permissionId,
                    permission.UserId,
                    permission.PermissionKey,
                    permission.IsAllowed ? 1 : 0,
                    now);
            }

            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('schema_version', ?, ?)", payload.Sync.SchemaVersion.ToString(), now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('last_bootstrap_time', ?, ?)", payload.Sync.GeneratedUtc, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('last_sync_time', ?, ?)", payload.Sync.GeneratedUtc, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('last_event_id', ?, ?)", payload.Sync.LastEventId, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('last_full_refresh_time', ?, ?)", payload.Sync.GeneratedUtc, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('bootstrap_id', ?, ?)", payload.Sync.BootstrapId, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('menu_version', ?, ?)", payload.Sync.MenuVersion, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('table_version', ?, ?)", payload.Sync.TableVersion, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('order_version', ?, ?)", payload.Sync.OrderVersion, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('permission_version', ?, ?)", payload.Sync.PermissionVersion, now);
            // The transaction above has now replaced the complete validated
            // snapshot. Clear pending group flags only in this same commit.
            foreach (var section in new[] { "menu", "categories", "products", "availability", "branding", "floors", "tables", "permissions", "features", "settings", "images" })
            {
                connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES (?, 'false', ?)", $"pending_{section}_sync", now);
            }
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('payload_version', '1', ?)", now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('payload_checksum', ?, ?)", checksum, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('forced_full_resync', 'false', ?)", now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('sync_in_progress', 'false', ?)", now);
            connection.Execute("INSERT OR REPLACE INTO event_checkpoint (id, stream_name, last_event_id, last_event_utc, updated_utc) VALUES (1, 'mother', ?, ?, ?)", payload.Sync.LastEventId, payload.Sync.GeneratedUtc, now);
            connection.Execute(
                "INSERT INTO sync_history (sync_kind, status, bootstrap_id, payload_version, checksum, message, started_utc, completed_utc) VALUES ('full_snapshot', 'completed', ?, 1, ?, 'Snapshot validated and applied atomically.', ?, ?)",
                payload.Sync.BootstrapId,
                checksum,
                startedUtc,
                now);
        });

        await SecureStorage.Default.SetAsync(SecureTerminalTokenKey, payload.Terminal.TerminalToken);
        await _database.ExecuteAsync("DELETE FROM device_config WHERE key = 'terminal_token'");

        await PurgeAllLocalDemoDataAsync();
    }

    /// <summary>
    /// Pairing-only bootstrap: update Mother connection credentials without clearing
    /// menu, floors, tables, or open orders already cached on this Client.
    /// </summary>
    private async Task SavePairingCredentialsOnlyAsync(BootstrapPayload payload)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        await _database.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "INSERT OR REPLACE INTO restaurant_info (id, mother_id, name, description, currency, time_zone, updated_utc) VALUES (1, ?, ?, ?, ?, ?, ?)",
                payload.Restaurant.MotherId,
                payload.Restaurant.Name,
                payload.Restaurant.Description,
                payload.Restaurant.Currency,
                payload.Restaurant.TimeZone,
                now);

            connection.Execute(
                "INSERT OR REPLACE INTO mother_connection (id, mode, status, display_name, api_base_url, websocket_url, pairing_code_hint, terminal_id, last_seen_utc, created_utc, updated_utc) VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, COALESCE((SELECT created_utc FROM mother_connection WHERE id = 1), ?), ?)",
                "paired",
                "bootstrapped",
                payload.Terminal.TerminalName,
                payload.Terminal.ApiBaseUrl,
                payload.Terminal.WebSocketUrl,
                payload.Terminal.PairingCodeHint,
                payload.Terminal.TerminalId,
                now,
                now,
                now);

            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('terminal_id', ?, ?)", payload.Terminal.TerminalId, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('api_base_url', ?, ?)", payload.Terminal.ApiBaseUrl, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('websocket_url', ?, ?)", payload.Terminal.WebSocketUrl, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('terminal_name', ?, ?)", payload.Terminal.TerminalName, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('device_role', ?, ?)", payload.Terminal.DeviceRole, now);
            connection.Execute("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('restaurant_name', ?, ?)", payload.Restaurant.Name, now);

            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('schema_version', ?, ?)", payload.Sync.SchemaVersion.ToString(), now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('last_bootstrap_time', ?, ?)", payload.Sync.GeneratedUtc, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('bootstrap_id', ?, ?)", payload.Sync.BootstrapId, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('sync_in_progress', 'false', ?)", now);
            connection.Execute(
                "INSERT INTO sync_history (sync_kind, status, bootstrap_id, payload_version, checksum, message, started_utc, completed_utc) VALUES ('pairing_credentials', 'completed', ?, 1, '', 'Pairing credentials saved; operational cache preserved.', ?, ?)",
                payload.Sync.BootstrapId,
                now,
                now);
        });

        await SecureStorage.Default.SetAsync(SecureTerminalTokenKey, payload.Terminal.TerminalToken);
        await _database.ExecuteAsync("DELETE FROM device_config WHERE key = 'terminal_token'");
    }

    public async Task<CacheStatus> GetStatusAsync()
    {
        await InitializeAsync();

        return new CacheStatus(
            await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories"),
            await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products"),
            await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tables"),
            await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM open_orders"),
            await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM online_orders_cache"),
            await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pending_actions"),
            await GetSyncValueAsync("schema_version"),
            await GetSyncValueAsync("last_bootstrap_time"),
            await GetSyncValueAsync("last_sync_time"),
            await GetSyncValueAsync("last_event_id"),
            await GetSyncValueAsync("last_full_refresh_time"));
    }

    public async Task QueuePendingActionAsync(string actionType, object payload)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        await _database.ExecuteAsync(
            "INSERT INTO pending_actions (action_type, payload_json, status, retry_count, created_utc, updated_utc) VALUES (?, ?, 'pending', 0, ?, ?)",
            actionType,
            JsonSerializer.Serialize(payload),
            now,
            now);
    }

    public async Task<IReadOnlyList<CachedFloor>> GetFloorsWithTablesAsync()
    {
        await InitializeAsync();

        var floors = await _database.QueryAsync<CachedFloorRow>("SELECT id, name, sort_order, background_image_id FROM floors WHERE is_active = 1 ORDER BY sort_order, name");
        var tables = await _database.QueryAsync<CachedTableRow>("SELECT id, floor_id, table_number, seats, status, current_total, current_order_id, covers, server_name, session_status, minutes_occupied, version, position_x, position_y, design_icon FROM tables ORDER BY table_number");

        return floors
            .Select(floor => new CachedFloor(
                floor.Id,
                floor.Name,
                floor.SortOrder,
                tables
                    .Where(table => table.FloorId == floor.Id)
                    .Select(table => new CachedTable(
                        table.Id,
                        table.FloorId,
                        table.TableNumber,
                        table.Seats,
                        table.Status,
                        table.CurrentTotal,
                        table.CurrentOrderId,
                        table.Covers,
                        table.ServerName,
                        table.SessionStatus,
                        table.MinutesOccupied,
                        table.Version,
                        table.PositionX,
                        table.PositionY,
                        table.DesignIcon))
                    .ToList(),
                string.IsNullOrWhiteSpace(floor.BackgroundImageId) ? null : floor.BackgroundImageId))
            .ToList();
    }

    /// <summary>
    /// Replaces cached floors/tables only after the snapshot validates.
    /// Returns Kept when empty/invalid would destroy a good cache (Phase 1/2 guards).
    /// </summary>
    public async Task<CacheReplaceResult> ReplaceLayoutAsync(FloorSnapshotDto floors, TableSnapshotDto tables)
    {
        await InitializeAsync();
        var existingTables = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tables");

        var validFloors = new List<(int Id, string MotherId, string Name, int SortOrder, string? BackgroundImageId)>();
        var floorIds = new HashSet<int>();
        foreach (var floor in floors.Floors)
        {
            if (!int.TryParse(floor.Id, out var floorId) || floorId <= 0 || string.IsNullOrWhiteSpace(floor.Name))
            {
                continue;
            }

            if (!floorIds.Add(floorId))
            {
                continue;
            }

            validFloors.Add((
                floorId,
                floor.Id,
                floor.Name.Trim(),
                floor.SortOrder,
                string.IsNullOrWhiteSpace(floor.BackgroundImageId) ? null : floor.BackgroundImageId.Trim()));
        }

        foreach (var table in tables.Tables)
        {
            if (!int.TryParse(table.FloorId, out var floorId) || floorId <= 0 || floorIds.Contains(floorId))
            {
                continue;
            }

            floorIds.Add(floorId);
            validFloors.Add((floorId, floorId.ToString(), $"Floor {floorId}", floorId, null));
        }

        var validTables = new List<(int Id, string MotherId, int FloorId, RestaurantTableDto Table)>();
        foreach (var table in tables.Tables)
        {
            if (!int.TryParse(table.Id, out var tableId) ||
                !int.TryParse(table.FloorId, out var floorId) ||
                tableId <= 0 ||
                !floorIds.Contains(floorId) ||
                string.IsNullOrWhiteSpace(table.Name))
            {
                continue;
            }

            validTables.Add((tableId, table.Id, floorId, table));
        }

        if (tables.Tables.Count > 0 && validTables.Count == 0)
        {
            return CacheReplaceResult.Kept(
                "Layout snapshot had tables but none were valid to commit — kept last good layout.");
        }

        if (validTables.Count == 0 && existingTables > 0)
        {
            return CacheReplaceResult.Kept("Mother returned no tables — kept the last good restaurant layout.");
        }

        if (tables.Tables.Count >= 3 && validTables.Count < tables.Tables.Count / 2)
        {
            return CacheReplaceResult.Kept(
                $"Layout replace rejected — {validTables.Count}/{tables.Tables.Count} tables valid (too many orphans).");
        }

        await _database.RunInTransactionAsync(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            connection.Execute("UPDATE open_orders SET table_id = NULL");
            connection.Execute("UPDATE reservations_cache SET table_id = NULL");
            connection.Execute("DELETE FROM tables");
            connection.Execute("DELETE FROM floors");

            foreach (var floor in validFloors)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO floors (id, mother_id, name, sort_order, is_active, background_image_id, updated_utc) VALUES (?, ?, ?, ?, 1, ?, ?)",
                    floor.Id,
                    floor.MotherId,
                    floor.Name,
                    floor.SortOrder,
                    floor.BackgroundImageId,
                    now);
            }

            foreach (var entry in validTables)
            {
                var table = entry.Table;
                connection.Execute(
                    """
                    INSERT OR REPLACE INTO tables
                        (id, mother_id, floor_id, table_number, seats, status, current_total, current_order_id, covers, server_name, session_status, minutes_occupied, version, position_x, position_y, design_icon, updated_utc)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?, 0, ?, ?, ?, ?, ?)
                    """,
                    entry.Id,
                    entry.MotherId,
                    entry.FloorId,
                    table.Name,
                    table.Capacity,
                    table.Status,
                    table.CurrentTotal,
                    table.OpenOrderId,
                    table.GuestCount,
                    table.SessionStatus,
                    Math.Max(1, (int)table.Revision),
                    (int)Math.Round(table.X),
                    (int)Math.Round(table.Y),
                    string.IsNullOrWhiteSpace(table.Icon) ? "table_1.png" : table.Icon,
                    now);
            }

            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('table_version', ?, ?)", tables.Version, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('last_sync_time', ?, ?)", now, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('section_layout_ok', 'true', ?)", now);
            connection.Execute(
                "INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('section_layout_detail', ?, ?)",
                $"{validFloors.Count} floors, {validTables.Count} tables",
                now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('section_layout_utc', ?, ?)", now, now);
        });
        return CacheReplaceResult.Ok($"{validFloors.Count} floors, {validTables.Count} tables");
    }

    public async Task ReplaceReservationsAsync(IReadOnlyList<CachedReservation> reservations)
    {
        await InitializeAsync();
        await _database.RunInTransactionAsync(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            connection.Execute("DELETE FROM reservations_cache");
            foreach (var reservation in reservations)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO reservations_cache (id, mother_id, customer_name, phone, table_id, party_size, reservation_utc, status, payload_json, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    reservation.Id,
                    reservation.MotherId,
                    reservation.CustomerName,
                    reservation.Phone,
                    reservation.TableId,
                    reservation.PartySize,
                    reservation.ReservationUtc,
                    reservation.Status,
                    reservation.PayloadJson,
                    string.IsNullOrWhiteSpace(reservation.UpdatedUtc) ? now : reservation.UpdatedUtc);
            }
        });
    }

    /// <summary>
    /// Replaces cached menu only after stabilize + validate. Empty/invalid snapshots keep last-good cache.
    /// Local int ids are derived from MotherId so they stay stable across refreshes.
    /// </summary>
    public async Task<CacheReplaceResult> ReplaceMenuAsync(MenuSnapshotDto snapshot)
    {
        await InitializeAsync();
        var existingCategories = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories");
        var stabilized = MenuSnapshotStabilizer.Stabilize(snapshot);
        var validationError = MenuSnapshotStabilizer.ValidateForCommit(snapshot, stabilized);
        if (validationError is not null)
        {
            return CacheReplaceResult.Kept(validationError);
        }

        // Never commit an empty menu — even when local cache is already empty.
        // Applying [] marks sync "Applied" then fails the login gate with a confusing error.
        if (stabilized.Categories.Count == 0)
        {
            return existingCategories > 0
                ? CacheReplaceResult.Kept("Mother returned an empty menu — kept the last good menu cache.")
                : CacheReplaceResult.Kept(
                    "Mother Food Menu has no categories for Client. Open Mother → Food Menu, add Active categories/items, then Retry sync.");
        }

        await _database.RunInTransactionAsync(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            connection.Execute("DELETE FROM tasting_menu_choices");
            connection.Execute("DELETE FROM tasting_menu_courses");
            connection.Execute("DELETE FROM tasting_menu_options");
            connection.Execute("DELETE FROM tasting_menus");
            connection.Execute("DELETE FROM meal_deal_category_rules");
            connection.Execute("DELETE FROM meal_deal_choices");
            connection.Execute("DELETE FROM meal_deals");
            connection.Execute("DELETE FROM product_quick_notes");
            connection.Execute("DELETE FROM product_variants");
            connection.Execute("DELETE FROM product_modifiers");
            connection.Execute("DELETE FROM modifiers");
            connection.Execute("DELETE FROM prices");
            connection.Execute("DELETE FROM products");
            connection.Execute("DELETE FROM modifier_groups");
            connection.Execute("DELETE FROM categories");

            var categoryIds = stabilized.Categories.Select(category => category.Id).ToHashSet();
            var productIds = stabilized.Products.Select(product => product.Id).ToHashSet();
            var modifierGroupIds = stabilized.ModifierGroups.Select(group => group.Id).ToHashSet();
            var mealDealIds = stabilized.MealDeals.Select(deal => deal.Id).ToHashSet();
            var tastingMenuIds = stabilized.TastingMenus.Select(menu => menu.Id).ToHashSet();
            var tastingCourseIds = stabilized.TastingMenuCourses.Select(course => course.Id).ToHashSet();

            foreach (var category in stabilized.Categories)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO categories (id, mother_id, name, color, sort_order, is_active, parent_id, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    category.Id,
                    category.MotherId,
                    category.Name,
                    category.Color,
                    category.SortOrder,
                    category.IsActive ? 1 : 0,
                    category.ParentId,
                    now);
            }

            foreach (var product in stabilized.Products)
            {
                if (!categoryIds.Contains(product.CategoryId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO products (id, mother_id, category_id, name, description, sku, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    product.Id,
                    product.MotherId,
                    product.CategoryId,
                    product.Name,
                    product.Description,
                    product.Sku,
                    product.IsActive ? 1 : 0,
                    now);
            }

            foreach (var price in stabilized.Prices)
            {
                if (!productIds.Contains(price.ProductId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO prices (id, product_id, price_type, amount, currency, tax_rate_id, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    price.Id,
                    price.ProductId,
                    price.PriceType,
                    price.Amount,
                    price.Currency,
                    price.TaxRateId,
                    now);
            }

            foreach (var group in stabilized.ModifierGroups)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO modifier_groups (id, mother_id, name, min_select, max_select, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    group.Id,
                    group.MotherId,
                    group.Name,
                    group.MinSelect,
                    group.MaxSelect,
                    group.IsActive ? 1 : 0,
                    now);
            }

            foreach (var modifier in stabilized.Modifiers)
            {
                if (!modifierGroupIds.Contains(modifier.ModifierGroupId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO modifiers (id, mother_id, modifier_group_id, name, price_delta, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    modifier.Id,
                    modifier.MotherId,
                    modifier.ModifierGroupId,
                    modifier.Name,
                    modifier.PriceDelta,
                    modifier.IsActive ? 1 : 0,
                    now);
            }

            foreach (var productModifier in stabilized.ProductModifiers)
            {
                if (!productIds.Contains(productModifier.ProductId) || !modifierGroupIds.Contains(productModifier.ModifierGroupId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO product_modifiers (product_id, modifier_group_id, sort_order, updated_utc) VALUES (?, ?, ?, ?)",
                    productModifier.ProductId,
                    productModifier.ModifierGroupId,
                    productModifier.SortOrder,
                    now);
            }

            foreach (var variant in stabilized.Variants)
            {
                if (!productIds.Contains(variant.ProductId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO product_variants (id, mother_id, product_id, name, description, takeaway_price, dine_in_price, sort_order, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    variant.Id,
                    variant.MotherId,
                    variant.ProductId,
                    variant.Name,
                    variant.Description,
                    variant.TakeawayPrice,
                    variant.DineInPrice,
                    variant.SortOrder,
                    variant.IsActive ? 1 : 0,
                    now);
            }

            foreach (var note in stabilized.QuickNotes)
            {
                if (!productIds.Contains(note.ProductId) || string.IsNullOrWhiteSpace(note.NoteText))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO product_quick_notes (id, mother_id, product_id, note_text, sort_order, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    note.Id,
                    note.MotherId,
                    note.ProductId,
                    note.NoteText.Trim(),
                    note.SortOrder,
                    note.IsActive ? 1 : 0,
                    now);
            }

            foreach (var deal in stabilized.MealDeals)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO meal_deals (id, mother_id, name, description, price, color, pick_count, vat_category, sort_order, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    deal.Id,
                    deal.MotherId,
                    deal.Name,
                    deal.Description,
                    deal.Price,
                    deal.Color,
                    deal.PickCount,
                    deal.VatCategory,
                    deal.SortOrder,
                    deal.IsActive ? 1 : 0,
                    now);
            }

            foreach (var choice in stabilized.MealDealChoices)
            {
                if (!mealDealIds.Contains(choice.MealDealId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO meal_deal_choices (id, mother_id, meal_deal_id, name, sort_order, updated_utc) VALUES (?, ?, ?, ?, ?, ?)",
                    choice.Id,
                    choice.MotherId,
                    choice.MealDealId,
                    choice.Name,
                    choice.SortOrder,
                    now);
            }

            foreach (var rule in stabilized.MealDealCategoryRules)
            {
                if (!mealDealIds.Contains(rule.MealDealId))
                {
                    continue;
                }

                var idsJson = System.Text.Json.JsonSerializer.Serialize(rule.MenuItemMotherIds ?? Array.Empty<string>());
                connection.Execute(
                    "INSERT OR REPLACE INTO meal_deal_category_rules (id, mother_id, meal_deal_id, name, is_required, min_selections, max_selections, menu_item_mother_ids_json, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    rule.Id,
                    rule.MotherId,
                    rule.MealDealId,
                    rule.Name,
                    rule.IsRequired ? 1 : 0,
                    rule.MinSelections,
                    rule.MaxSelections,
                    idsJson,
                    now);
            }

            foreach (var tasting in stabilized.TastingMenus)
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO tasting_menus (id, mother_id, name, description, color, sort_order, is_active, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    tasting.Id,
                    tasting.MotherId,
                    tasting.Name,
                    tasting.Description,
                    tasting.Color,
                    tasting.SortOrder,
                    tasting.IsActive ? 1 : 0,
                    now);
            }

            foreach (var option in stabilized.TastingMenuOptions)
            {
                if (!tastingMenuIds.Contains(option.TastingMenuId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO tasting_menu_options (id, mother_id, tasting_menu_id, name, price, includes_wine, course_count, sort_order, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    option.Id,
                    option.MotherId,
                    option.TastingMenuId,
                    option.Name,
                    option.Price,
                    option.IncludesWine ? 1 : 0,
                    option.CourseCount,
                    option.SortOrder,
                    now);
            }

            foreach (var course in stabilized.TastingMenuCourses)
            {
                if (!tastingMenuIds.Contains(course.TastingMenuId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO tasting_menu_courses (id, mother_id, tasting_menu_id, name, wine_name, course_number, required, vat_category, sort_order, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    course.Id,
                    course.MotherId,
                    course.TastingMenuId,
                    course.Name,
                    course.WineName,
                    course.CourseNumber,
                    course.Required ? 1 : 0,
                    course.VatCategory,
                    course.SortOrder,
                    now);
            }

            foreach (var choice in stabilized.TastingMenuChoices)
            {
                if (!tastingCourseIds.Contains(choice.CourseId))
                {
                    continue;
                }

                connection.Execute(
                    "INSERT OR REPLACE INTO tasting_menu_choices (id, mother_id, course_id, name, print_group_id, sort_order, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    choice.Id,
                    choice.MotherId,
                    choice.CourseId,
                    choice.Name,
                    choice.PrintGroupId,
                    choice.SortOrder,
                    now);
            }

            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('menu_version', ?, ?)", snapshot.Version, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('last_sync_time', ?, ?)", now, now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('section_menu_ok', 'true', ?)", now);
            connection.Execute(
                "INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('section_menu_detail', ?, ?)",
                $"{stabilized.Categories.Count} categories, {stabilized.Products.Count} products, {stabilized.Variants.Count} variants, {stabilized.MealDeals.Count} meal deals, {stabilized.TastingMenus.Count} tasting menus",
                now);
            connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES ('section_menu_utc', ?, ?)", now, now);
        });
        return CacheReplaceResult.Ok($"{stabilized.Categories.Count} categories, {stabilized.Products.Count} products, {stabilized.Variants.Count} variants, {stabilized.MealDeals.Count} meal deals");
    }

    public async Task RecordOperationalSectionAsync(string section, bool ok, string detail)
    {
        await InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var key = section.Trim().ToLowerInvariant();
        await UpsertSyncStateAsync($"section_{key}_ok", ok ? "true" : "false");
        await UpsertSyncStateAsync($"section_{key}_detail", detail ?? string.Empty);
        await UpsertSyncStateAsync($"section_{key}_utc", now);
        await UpsertSyncStateAsync("last_operational_sync_utc", now);
    }

    public async Task<OperationalSectionSyncStatus> GetOperationalSectionSyncStatusAsync()
    {
        await InitializeAsync();
        return new OperationalSectionSyncStatus(
            ParseSectionOk(await GetSyncValueAsync("section_menu_ok")),
            await GetSyncValueAsync("section_menu_detail"),
            await GetSyncValueAsync("section_menu_utc"),
            ParseSectionOk(await GetSyncValueAsync("section_layout_ok")),
            await GetSyncValueAsync("section_layout_detail"),
            await GetSyncValueAsync("section_layout_utc"),
            ParseSectionOk(await GetSyncValueAsync("section_orders_ok")),
            await GetSyncValueAsync("section_orders_detail"),
            await GetSyncValueAsync("section_orders_utc"),
            await GetSyncValueAsync("last_operational_sync_utc"));
    }

    private static bool? ParseSectionOk(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task ReplaceOperationalOrdersAsync(IReadOnlyList<MotherOrderState> orders)
    {
        await InitializeAsync();
        var keepIds = orders.Select(order => order.OrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = await GetOpenOrderStatesAsync();
        foreach (var order in existing)
        {
            var type = (order.OrderType ?? string.Empty).Trim().ToLowerInvariant();
            var isCustomerOrder = type is "collection" or "pickup" or "col" or "takeaway" or "delivery" or "del";
            if (isCustomerOrder && !keepIds.Contains(order.OrderId))
            {
                await _database.ExecuteAsync("DELETE FROM order_items WHERE order_id = ?", order.OrderId);
                await _database.ExecuteAsync("DELETE FROM open_orders WHERE id = ?", order.OrderId);
            }
        }

        foreach (var order in orders)
        {
            await SaveOrderStateAsync(order);
        }
    }

    public async Task<IReadOnlyList<CachedMenuCategory>> GetMenuCategoriesAsync()
    {
        await InitializeAsync();
        var rows = await _database.QueryAsync<CachedCategoryRow>(
            "SELECT id, name, color, sort_order, parent_id FROM categories WHERE is_active = 1 ORDER BY sort_order, name");
        return rows
            .Select(row => new CachedMenuCategory(row.Id, row.Name, row.Color ?? "#3B82F6", row.SortOrder, row.ParentId))
            .ToList();
    }

    public async Task<HashSet<int>> GetCategoryIdsWithProductsAsync()
    {
        await InitializeAsync();
        var rows = await _database.QueryAsync<CachedCategoryIdRow>(
            "SELECT DISTINCT category_id AS id FROM products WHERE is_active = 1");
        return rows.Select(row => row.Id).ToHashSet();
    }

    public async Task<IReadOnlyList<CachedProduct>> GetProductsByCategoryAsync(int categoryId)
    {
        await InitializeAsync();
        var rows = await _database.QueryAsync<CachedProductRow>(@"
            SELECT p.id, p.category_id, p.name, p.mother_id,
                   COALESCE((
                       SELECT amount FROM prices WHERE product_id = p.id AND price_type = 'takeaway' LIMIT 1
                   ), (
                       SELECT amount FROM prices WHERE product_id = p.id AND price_type = 'dine_in' LIMIT 1
                   ), (
                       SELECT amount FROM prices WHERE product_id = p.id LIMIT 1
                   ), 0) AS price,
                   COALESCE((
                       SELECT currency FROM prices WHERE product_id = p.id AND price_type = 'takeaway' LIMIT 1
                   ), (
                       SELECT currency FROM prices WHERE product_id = p.id LIMIT 1
                   ), 'GBP') AS currency
            FROM products p
            WHERE p.category_id = ? AND p.is_active = 1
            ORDER BY p.name", categoryId);

        var result = new List<CachedProduct>();
        foreach (var row in rows)
        {
            result.Add(new CachedProduct(
                row.Id,
                row.CategoryId,
                row.Name,
                row.Price,
                row.Currency,
                await GetModifierGroupsForProductAsync(row.Id),
                row.MotherId,
                await GetVariantsForProductAsync(row.Id),
                await GetQuickNotesForProductAsync(row.Id)));
        }

        return result;
    }

    private async Task<IReadOnlyList<CachedProductVariant>> GetVariantsForProductAsync(int productId)
    {
        var rows = await _database.QueryAsync<CachedVariantRow>(@"
            SELECT mother_id, name, description, takeaway_price, dine_in_price, sort_order
            FROM product_variants
            WHERE product_id = ? AND is_active = 1
            ORDER BY sort_order, name", productId);

        return rows
            .Select(row => new CachedProductVariant(
                row.MotherId ?? string.Empty,
                row.Name,
                row.Description,
                row.TakeawayPrice,
                row.DineInPrice,
                row.SortOrder))
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetQuickNotesForProductAsync(int productId)
    {
        var rows = await _database.QueryAsync<CachedQuickNoteRow>(@"
            SELECT note_text
            FROM product_quick_notes
            WHERE product_id = ? AND is_active = 1
            ORDER BY sort_order
            LIMIT 6", productId);

        return rows
            .Select(row => row.NoteText?.Trim() ?? string.Empty)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
    }

    public async Task<MotherOrderState?> GetOrderStateAsync(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        await InitializeAsync();
        var orders = await _database.QueryAsync<CachedOrderRow>("SELECT id, order_number, order_type, table_id, guests, status, subtotal, tax, total, version, updated_utc FROM open_orders WHERE id = ? LIMIT 1", orderId);
        var order = orders.FirstOrDefault();
        if (order == null)
        {
            return null;
        }

        var table = order.TableId.HasValue
            ? (await _database.QueryAsync<CachedTableRow>("SELECT id, floor_id, table_number, seats, status, current_total, current_order_id, covers, server_name, session_status, minutes_occupied, version, position_x, position_y FROM tables WHERE id = ? LIMIT 1", order.TableId.Value)).FirstOrDefault()
            : null;

        var lines = await _database.QueryAsync<CachedOrderItemRow>(OrderItemSelectSql, order.Id);

        return new MotherOrderState(
            order.Id,
            order.OrderNumber,
            order.OrderType,
            order.TableId,
            table?.TableNumber,
            order.Guests,
            order.Status,
            lines.Select(ToMotherOrderLine).ToList(),
            order.Subtotal,
            order.Tax,
            order.Total,
            order.Version,
            order.UpdatedUtc,
            table?.ServerName);
    }

    public async Task<IReadOnlyList<MotherOrderState>> GetOpenOrderStatesAsync()
    {
        await InitializeAsync();
        var orders = await _database.QueryAsync<CachedOrderRow>("SELECT id, order_number, order_type, table_id, guests, status, subtotal, tax, total, version, updated_utc FROM open_orders ORDER BY updated_utc DESC");
        var states = new List<MotherOrderState>();

        foreach (var order in orders)
        {
            var table = order.TableId.HasValue
                ? (await _database.QueryAsync<CachedTableRow>("SELECT id, floor_id, table_number, seats, status, current_total, current_order_id, covers, server_name, session_status, minutes_occupied, version, position_x, position_y FROM tables WHERE id = ? LIMIT 1", order.TableId.Value)).FirstOrDefault()
                : null;

            var lines = await _database.QueryAsync<CachedOrderItemRow>(OrderItemSelectSql, order.Id);

            states.Add(new MotherOrderState(
                order.Id,
                order.OrderNumber,
                order.OrderType,
                order.TableId,
                table?.TableNumber,
                order.Guests,
                order.Status,
                lines.Select(ToMotherOrderLine).ToList(),
                order.Subtotal,
                order.Tax,
                order.Total,
                order.Version,
                order.UpdatedUtc,
                table?.ServerName));
        }

        return states;
    }

    public async Task SaveOrderStateAsync(MotherOrderState state)
    {
        await InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var tableId = state.TableId.HasValue && await RowExistsByIdAsync("tables", state.TableId.Value)
            ? state.TableId
            : null;

        await _database.ExecuteAsync(@"
            INSERT OR REPLACE INTO open_orders (id, mother_id, order_number, order_type, table_id, guests, status, subtotal, tax, total, version, opened_utc, updated_utc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, COALESCE((SELECT opened_utc FROM open_orders WHERE id = ?), ?), ?)",
            state.OrderId,
            $"mother-order-{state.OrderId}",
            state.OrderNumber,
            state.OrderType,
            tableId,
            state.Guests,
            state.Status,
            state.Subtotal,
            state.Tax,
            state.Total,
            state.Version,
            state.OrderId,
            now,
            now);

        await _database.ExecuteAsync("DELETE FROM order_items WHERE order_id = ?", state.OrderId);
        foreach (var line in state.Lines)
        {
            var productId = line.ProductId.HasValue && await RowExistsByIdAsync("products", line.ProductId.Value)
                ? line.ProductId
                : null;

            await _database.ExecuteAsync(
                """
                INSERT OR REPLACE INTO order_items
                    (id, order_id, product_id, product_mother_id, name, quantity, unit_price, notes, modifier_json,
                     variant_id, variant_name, variant_price, meal_deal_id, meal_deal_choices_json, tasting_menu_id, status, updated_utc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'open', ?)
                """,
                line.Id,
                state.OrderId,
                productId,
                line.ProductMotherId,
                line.Name,
                line.Quantity,
                line.UnitPrice,
                line.Notes,
                JsonSerializer.Serialize(line.Modifiers),
                line.VariantId,
                line.VariantName,
                line.VariantPrice,
                line.MealDealId,
                line.MealDealChoices is null ? null : JsonSerializer.Serialize(line.MealDealChoices),
                line.TastingMenuId,
                now);
        }

        if (tableId.HasValue)
        {
            await _database.ExecuteAsync(@"
                UPDATE tables
                SET status = 'Occupied',
                    current_total = ?,
                    current_order_id = ?,
                    covers = ?,
                    server_name = ?,
                    session_status = ?,
                    minutes_occupied = CASE WHEN minutes_occupied = 0 THEN 1 ELSE minutes_occupied END,
                    version = ?,
                    updated_utc = ?
                WHERE id = ?",
                state.Total,
                state.OrderId,
                state.Guests,
                state.ServerName,
                state.Status == "sent_to_kitchen" ? "FoodServed" : "Ordering",
                state.Version,
                now,
                tableId.Value);
        }
    }

    public async Task<IReadOnlyList<CachedCustomer>> SearchCachedCustomersAsync(Models.CustomerSearchRequest request)
    {
        await InitializeAsync();
        var term = $"%{request.Name ?? request.Phone ?? request.AddressOrPostcode ?? string.Empty}%";
        var rows = await _database.QueryAsync<CachedCustomerRow>(@"
            SELECT id, mother_id, name, phone, email, address, postcode, loyalty_points
            FROM customers_cache
            WHERE name LIKE ? OR phone LIKE ? OR address LIKE ? OR postcode LIKE ?
            ORDER BY updated_utc DESC
            LIMIT 8", term, term, term, term);

        return rows.Select(row => new CachedCustomer(row.Id, row.MotherId, row.Name, row.Phone, row.Email, row.Address, row.Postcode, row.LoyaltyPoints)).ToList();
    }

    public async Task CacheCustomersAsync(IReadOnlyList<CachedCustomer> customers)
    {
        // Kept as a compatibility no-op while callers move to the explicit
        // active-order method. A search result must never become a local
        // customer directory on a Child device.
        await Task.CompletedTask;
    }

    public async Task CacheCustomerForActiveOrderAsync(CachedCustomer customer, bool isDelivery)
    {
        await InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToString("O");
        await _database.ExecuteAsync(
            "INSERT OR REPLACE INTO customers_cache (id, mother_id, name, phone, email, address, postcode, loyalty_points, updated_utc) VALUES (?, ?, ?, ?, NULL, ?, ?, 0, ?)",
            customer.Id,
            customer.MotherId,
            customer.Name,
            customer.Phone,
            // A collection order has no need to retain an address. Email and
            // loyalty data are never copied to the Client cache.
            isDelivery ? customer.Address : string.Empty,
            isDelivery ? customer.Postcode : null,
            now);
    }

    public async Task SavePrintRequestAsync(PrintRequestState request)
    {
        await InitializeAsync();
        await _database.ExecuteAsync(
            "INSERT OR REPLACE INTO print_requests (id, print_type, order_id, status, message, created_utc, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
            request.Id,
            request.PrintType,
            request.OrderId,
            request.Status,
            request.Message,
            request.CreatedUtc,
            request.UpdatedUtc);
    }

    public async Task<IReadOnlyList<PrintRequestState>> GetRecentPrintRequestsAsync()
    {
        await InitializeAsync();
        var rows = await _database.QueryAsync<PrintRequestRow>("SELECT id, print_type, order_id, status, message, created_utc, updated_utc FROM print_requests ORDER BY updated_utc DESC LIMIT 8");
        return rows.Select(row => new PrintRequestState(row.Id, row.PrintType, row.OrderId, row.Status, row.Message, row.CreatedUtc, row.UpdatedUtc)).ToList();
    }

    public async Task<CachedImageMetadata?> GetImageMetadataAsync(string imageId)
    {
        await InitializeAsync();
        var rows = await _database.QueryAsync<ImageCacheRow>(
            "SELECT image_id, remote_path, content_hash, local_path, mime_type, last_synchronized_utc FROM image_cache WHERE image_id = ? LIMIT 1", imageId);
        var row = rows.FirstOrDefault();
        return row == null ? null : new CachedImageMetadata(row.ImageId, row.RemotePath, row.ContentHash, row.LocalPath, row.MimeType, row.LastSynchronizedUtc);
    }

    public async Task SaveImageMetadataAsync(CachedImageMetadata image)
    {
        await InitializeAsync();
        await _database.ExecuteAsync(
            "INSERT OR REPLACE INTO image_cache (image_id, remote_path, content_hash, local_path, mime_type, last_synchronized_utc) VALUES (?, ?, ?, ?, ?, ?)",
            image.ImageId, image.RemotePath, image.ContentHash, image.LocalPath, image.MimeType, image.LastSynchronizedUtc);
    }

    /// <returns>false when this is a duplicate or stale event already applied.</returns>
    public async Task<bool> RecordAuthoritativeEventAsync(long eventId, string eventType, string version, DateTimeOffset occurredAt)
    {
        await InitializeAsync();
        var previous = await GetSyncValueAsync("last_event_id");
        if (long.TryParse(previous, out var appliedId) && eventId <= appliedId)
        {
            return false;
        }
        if (long.TryParse(previous, out var previousId) && eventId > previousId + 1)
        {
            await UpsertSyncStateAsync("forced_full_resync", "true");
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        await _database.ExecuteAsync(
            "INSERT OR REPLACE INTO event_checkpoint (id, stream_name, last_event_id, last_event_utc, updated_utc) VALUES (1, 'mother', ?, ?, ?)",
            eventId.ToString(), occurredAt.ToString("O"), now);
        await UpsertSyncStateAsync("last_event_id", eventId.ToString());
        await UpsertSyncStateAsync("last_event_type", eventType);
        await UpsertSyncStateAsync("last_event_version", version);

        var section = ResolveConfigurationSection(eventType);
        if (section != null)
        {
            // This is deliberately a *server* version.  The matching local
            // version changes only inside SaveBootstrapAsync after the entire
            // validated snapshot transaction commits.
            await UpsertSyncStateAsync($"server_{section}_version", version);
            await UpsertSyncStateAsync($"pending_{section}_sync", "true");
            await UpsertSyncStateAsync("forced_full_resync", "true");
        }
        return true;
    }

    /// <summary>
    /// Records Mother versions as pending. This intentionally never updates a
    /// local section version: only SaveBootstrapAsync can do that after its
    /// full SQLite transaction has committed.
    /// </summary>
    public async Task RecordMotherConfigurationVersionsAsync(IReadOnlyDictionary<string, string> versions)
    {
        await InitializeAsync();
        await _database.RunInTransactionAsync(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            foreach (var pair in versions)
            {
                connection.Execute("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES (?, ?, ?)", $"server_{pair.Key}_version", pair.Value, now);
            }
        });
    }

    public async Task MarkWebSocketReconnectedAsync()
    {
        await InitializeAsync();
        // WebSocket is a notification transport. It cannot prove no message
        // was missed while disconnected, so require Mother version comparison.
        await UpsertSyncStateAsync("forced_full_resync", "true");
        await UpsertSyncStateAsync("websocket_reconnected_utc", DateTimeOffset.UtcNow.ToString("O"));
    }

    public async Task<IReadOnlyList<CachedOnlineOrder>> GetOnlineOrdersAsync()
    {
        await InitializeAsync();
        var rows = await _database.QueryAsync<OnlineOrderRow>("SELECT id, mother_id, order_number, customer_name, order_type, due_time, status, total, payload_json, updated_utc FROM online_orders_cache ORDER BY updated_utc DESC");
        return rows.Select(row => new CachedOnlineOrder(row.Id, row.MotherId, row.OrderNumber, row.CustomerName, row.OrderType, row.DueTime, row.Status, row.Total, row.PayloadJson, row.UpdatedUtc)).ToList();
    }

    public async Task<IReadOnlyList<CachedReservation>> GetCachedReservationsAsync(DateTime startInclusive, DateTime endExclusive)
    {
        await InitializeAsync();
        var startKey = startInclusive.Date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var endKey = endExclusive.Date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var rows = await _database.QueryAsync<ReservationCacheRow>(@"
            SELECT r.id, r.mother_id, r.customer_name, r.phone, r.table_id, t.table_number, r.party_size, r.reservation_utc, r.status, r.payload_json, r.updated_utc
            FROM reservations_cache r
            LEFT JOIN tables t ON t.id = r.table_id
            WHERE r.reservation_utc >= ? AND r.reservation_utc < ?
            ORDER BY r.reservation_utc", startKey, endKey);

        return rows.Select(row => new CachedReservation(
            row.Id,
            row.MotherId,
            row.CustomerName,
            row.Phone,
            row.TableId,
            row.TableNumber,
            row.PartySize,
            row.ReservationUtc,
            row.Status,
            row.PayloadJson,
            row.UpdatedUtc)).ToList();
    }

    public async Task SaveOnlineOrderAsync(CachedOnlineOrder order)
    {
        await InitializeAsync();
        await _database.ExecuteAsync(
            "INSERT OR REPLACE INTO online_orders_cache (id, mother_id, order_number, customer_name, order_type, due_time, status, total, payload_json, updated_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            order.Id,
            order.MotherId,
            order.OrderNumber,
            order.CustomerName,
            order.OrderType,
            order.DueTime,
            order.Status,
            order.Total,
            order.PayloadJson,
            order.UpdatedUtc);
    }

    /// <summary>
    /// Stores the last successful day-level order-history page for offline viewing only.
    /// One snapshot row per date+orderType (search cleared, page 1).
    /// </summary>
    public async Task SaveOrderHistoryDaySnapshotAsync(
        DateTime historyDate,
        string orderType,
        ClientOrderHistoryResponseDto response)
    {
        await InitializeAsync();
        var dateKey = historyDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var typeKey = string.IsNullOrWhiteSpace(orderType) ? "ALL" : orderType.Trim().ToUpperInvariant();
        var cacheKey = $"{dateKey}|{typeKey}";
        var payload = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await _database.ExecuteAsync(
            """
            INSERT OR REPLACE INTO order_history_day_snapshot
            (cache_key, history_date, order_type, search, page, page_size, has_next_page, message, payload_json, cached_utc)
            VALUES (?, ?, ?, '', 1, ?, ?, ?, ?, ?)
            """,
            cacheKey,
            dateKey,
            typeKey,
            response.PageSize <= 0 ? 50 : response.PageSize,
            response.HasNextPage ? 1 : 0,
            response.Message ?? string.Empty,
            payload,
            DateTimeOffset.UtcNow.ToString("O"));
    }

    public async Task<ClientOrderHistoryResponseDto?> GetOrderHistoryDaySnapshotAsync(
        DateTime historyDate,
        string orderType)
    {
        await InitializeAsync();
        var dateKey = historyDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var typeKey = string.IsNullOrWhiteSpace(orderType) ? "ALL" : orderType.Trim().ToUpperInvariant();
        var cacheKey = $"{dateKey}|{typeKey}";
        var rows = await _database.QueryAsync<OrderHistorySnapshotRow>(
            "SELECT payload_json, message, cached_utc, has_next_page, page_size FROM order_history_day_snapshot WHERE cache_key = ? LIMIT 1",
            cacheKey);
        var row = rows.FirstOrDefault();
        if (row is null || string.IsNullOrWhiteSpace(row.PayloadJson))
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<ClientOrderHistoryResponseDto>(
                row.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (dto is null)
            {
                return null;
            }

            var cachedLabel = DateTimeOffset.TryParse(row.CachedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cachedAt)
                ? cachedAt.ToLocalTime().ToString("g")
                : row.CachedUtc;
            return dto with
            {
                Success = true,
                FromCache = true,
                ErrorCode = OrderHistoryErrorCodes.CachedSnapshot,
                Message = string.IsNullOrWhiteSpace(dto.Message)
                    ? $"Offline snapshot from {cachedLabel}"
                    : $"{dto.Message} (offline snapshot from {cachedLabel})",
                HasNextPage = row.HasNextPage != 0,
                Page = 1,
                PageSize = row.PageSize > 0 ? row.PageSize : dto.PageSize
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class OrderHistorySnapshotRow
    {
        [Column("payload_json")]
        public string PayloadJson { get; set; } = string.Empty;

        [Column("message")]
        public string Message { get; set; } = string.Empty;

        [Column("cached_utc")]
        public string CachedUtc { get; set; } = string.Empty;

        [Column("has_next_page")]
        public int HasNextPage { get; set; }

        [Column("page_size")]
        public int PageSize { get; set; }
    }

    private async Task<IReadOnlyList<CachedModifierGroup>> GetModifierGroupsForProductAsync(int productId)
    {
        var groups = await _database.QueryAsync<CachedModifierGroupRow>(@"
            SELECT mg.id, mg.name, mg.min_select, mg.max_select
            FROM product_modifiers pm
            JOIN modifier_groups mg ON mg.id = pm.modifier_group_id
            WHERE pm.product_id = ? AND mg.is_active = 1
            ORDER BY pm.sort_order, mg.name", productId);

        var result = new List<CachedModifierGroup>();
        foreach (var group in groups)
        {
            var modifiers = await _database.QueryAsync<CachedModifierRow>("SELECT id, name, price_delta FROM modifiers WHERE modifier_group_id = ? AND is_active = 1 ORDER BY name", group.Id);
            result.Add(new CachedModifierGroup(group.Id, group.Name, group.MinSelect, group.MaxSelect, modifiers.Select(modifier => new CachedModifier(modifier.Id, modifier.Name, modifier.PriceDelta)).ToList()));
        }

        return result;
    }

    public async Task SaveLoginSessionAsync(LoginSession session)
    {
        await InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToString("O");

        await _database.ExecuteAsync(
            "INSERT OR REPLACE INTO current_session (id, user_id, user_name, role, session_token, expires_utc, updated_utc) VALUES (1, ?, ?, ?, '', ?, ?)",
            session.UserId,
            session.UserName,
            session.Role,
            session.ExpiresAtUtc.ToString("O"),
            now);
        await SecureStorage.Default.SetAsync(SecureSessionTokenKey, session.SessionToken);

        await _database.ExecuteAsync("DELETE FROM permissions_cache WHERE user_id = ?", session.UserId);

        var permissionId = 1000;
        foreach (var permission in session.Permissions)
        {
            permissionId++;
            await _database.ExecuteAsync(
                "INSERT OR REPLACE INTO permissions_cache (id, user_id, permission_key, is_allowed, updated_utc) VALUES (?, ?, ?, 1, ?)",
                permissionId,
                session.UserId,
                permission,
                now);
        }

        if (session.Features is null)
        {
            await _database.ExecuteAsync("DELETE FROM device_config WHERE key IN ('login_features', 'login_routes')");
        }
        else
        {
            await UpsertDeviceConfigAsync("login_features", JsonSerializer.Serialize(session.Features));
            await UpsertDeviceConfigAsync("login_routes", JsonSerializer.Serialize(session.Routes ?? Array.Empty<string>()));
        }

        ClientHostAccess.ApplyFromSession(session);
    }

    public async Task<LoginSession?> GetCurrentLoginSessionAsync()
    {
        await InitializeAsync();
        var rows = await _database.QueryAsync<CurrentSessionRow>(
            "SELECT user_id, user_name, role, session_token, expires_utc FROM current_session WHERE id = 1 LIMIT 1");
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(row.ExpiresUtc, out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow)
        {
            await ClearLoginSessionAsync();
            return null;
        }

        var sessionToken = await SecureStorage.Default.GetAsync(SecureSessionTokenKey);
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            sessionToken = row.SessionToken;
            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                await SecureStorage.Default.SetAsync(SecureSessionTokenKey, sessionToken);
                await _database.ExecuteAsync("UPDATE current_session SET session_token = '' WHERE id = 1");
            }
        }
        if (string.IsNullOrWhiteSpace(sessionToken)) return null;

        var permissions = await _database.QueryAsync<PermissionRow>(
            "SELECT permission_key FROM permissions_cache WHERE user_id = ? AND is_allowed = 1",
            row.UserId);

        var features = ParseStoredStringList(await GetDeviceConfigValueAsync("login_features"));
        var routes = ParseStoredStringList(await GetDeviceConfigValueAsync("login_routes"));
        var session = new LoginSession(
            row.UserId,
            row.UserName,
            row.Role,
            permissions.Select(permission => permission.PermissionKey).ToList(),
            sessionToken,
            expiresAt,
            features,
            routes);
        ClientHostAccess.ApplyFromSession(session);
        return session;
    }

    public async Task ClearLoginSessionAsync()
    {
        await InitializeAsync();
        await _database.ExecuteAsync("DELETE FROM current_session");
        await _database.ExecuteAsync("DELETE FROM device_config WHERE key IN ('login_features', 'login_routes')");
        SecureStorage.Default.Remove(SecureSessionTokenKey);
        ClientHostAccess.Clear();
    }

    public async Task MarkTerminalDisabledAsync(string reason)
    {
        await InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToString("O");
        await UpsertDeviceConfigAsync("terminal_disabled", "true");
        await UpsertDeviceConfigAsync("terminal_disabled_reason", string.IsNullOrWhiteSpace(reason) ? "Disabled by Mother POS" : reason);
        await _database.ExecuteAsync(
            "UPDATE mother_connection SET status = ?, updated_utc = ? WHERE id = 1",
            "disabled",
            now);
    }

    public async Task ClearTerminalDisabledAsync()
    {
        await InitializeAsync();
        await UpsertDeviceConfigAsync("terminal_disabled", "false");
        await UpsertDeviceConfigAsync("terminal_disabled_reason", string.Empty);
    }

    public async Task<bool> IsTerminalDisabledAsync()
    {
        await InitializeAsync();
        var value = await GetDeviceConfigValueAsync("terminal_disabled");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> GetTerminalDisabledReasonAsync()
    {
        await InitializeAsync();
        return await GetDeviceConfigValueAsync("terminal_disabled_reason") ?? "This Client POS has been disabled by the Mother POS.";
    }

    public async Task<MotherConnectionSettings?> GetMotherConnectionAsync()
    {
        await InitializeAsync();
        var rows = await _database.QueryAsync<MotherConnectionRow>(
            "SELECT api_base_url, websocket_url, terminal_id, auth_token_hint FROM mother_connection WHERE id = 1 LIMIT 1");
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var terminalToken = await SecureStorage.Default.GetAsync(SecureTerminalTokenKey) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(terminalToken))
        {
            // One-time migration from older Client SQLite caches.
            terminalToken = await GetDeviceConfigValueAsync("terminal_token") ?? row.AuthTokenHint ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(terminalToken))
            {
                await SecureStorage.Default.SetAsync(SecureTerminalTokenKey, terminalToken);
                await _database.ExecuteAsync("DELETE FROM device_config WHERE key = 'terminal_token'");
            }
        }
        return new MotherConnectionSettings(
            row.ApiBaseUrl ?? string.Empty,
            row.WebSocketUrl ?? string.Empty,
            row.TerminalId ?? string.Empty,
            terminalToken);
    }

    public async Task UpdateMotherEndpointsAsync(string apiBaseUrl, string webSocketUrl)
    {
        await InitializeAsync();
        if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(webSocketUrl))
        {
            throw new ArgumentException("Mother API and WebSocket URLs are required.");
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        await _database.ExecuteAsync(
            "UPDATE mother_connection SET api_base_url = ?, websocket_url = ?, status = 'connected', last_seen_utc = ?, updated_utc = ? WHERE id = 1",
            apiBaseUrl.Trim(),
            webSocketUrl.Trim(),
            now,
            now);
        await _database.ExecuteAsync(
            "INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('api_base_url', ?, ?)",
            apiBaseUrl.Trim(),
            now);
        await _database.ExecuteAsync(
            "INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES ('websocket_url', ?, ?)",
            webSocketUrl.Trim(),
            now);
    }

    private async Task ClearCacheTablesAsync()
    {
        foreach (var table in ClientCacheSchema.ResetTables)
        {
            await _database.ExecuteAsync($"DELETE FROM {table}");
        }
    }

    public async Task ForceFullResyncAsync(string reason = "Requested by Client")
    {
        await InitializeAsync();
        await UpsertSyncStateAsync("forced_full_resync", "true");
        await _database.ExecuteAsync(
            "INSERT INTO sync_history (sync_kind, status, payload_version, message, started_utc, completed_utc) VALUES ('full_snapshot', 'requested', 1, ?, ?, ?)",
            reason,
            DateTimeOffset.UtcNow.ToString("O"),
            DateTimeOffset.UtcNow.ToString("O"));
    }

    private static void ValidateSnapshot(BootstrapPayload payload)
    {
        if (payload.Sync.SchemaVersion <= 0 || string.IsNullOrWhiteSpace(payload.Sync.BootstrapId))
        {
            throw new InvalidOperationException("Mother POS returned a snapshot without a valid schema version and bootstrap ID.");
        }

        if (payload.Products.Any(product => product.Id <= 0 || product.CategoryId <= 0) ||
            payload.Tables.Any(table => table.Id <= 0 || table.FloorId <= 0) ||
            payload.OpenOrders.Any(order => string.IsNullOrWhiteSpace(order.Id)))
        {
            throw new InvalidOperationException("Mother POS returned an invalid snapshot identity.");
        }
    }

    private static string ComputeSnapshotChecksum(BootstrapPayload payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Removes leftover Client-only demo rows (mother_id LIKE demo-%).
    /// Do not delete blank mother_id menu/layout rows — that wiped real cache after partial syncs.
    /// </summary>
    public async Task PurgeAllLocalDemoDataAsync()
    {
        await InitializeAsyncForPurge();

        await _database.ExecuteAsync("DELETE FROM current_session WHERE user_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM permissions_cache WHERE user_id LIKE 'demo-%'");

        await _database.ExecuteAsync(
            """
            DELETE FROM product_modifiers
            WHERE product_id IN (
                SELECT id FROM products
                WHERE mother_id LIKE 'demo-%')
            """);
        await _database.ExecuteAsync(
            """
            DELETE FROM prices
            WHERE product_id IN (
                SELECT id FROM products
                WHERE mother_id LIKE 'demo-%')
            """);
        await _database.ExecuteAsync("DELETE FROM products WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM categories WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM modifiers WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM modifier_groups WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM tax_rates WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM tables WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM floors WHERE mother_id LIKE 'demo-%'");

        await _database.ExecuteAsync(
            """
            DELETE FROM order_items
            WHERE order_id IN (SELECT id FROM open_orders WHERE mother_id LIKE 'demo-%')
            """);
        await _database.ExecuteAsync("DELETE FROM open_orders WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM online_orders_cache WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM reservations_cache WHERE mother_id LIKE 'demo-%'");
        await _database.ExecuteAsync("DELETE FROM customers_cache WHERE mother_id LIKE 'demo-%'");
    }

    private async Task InitializeAsyncForPurge()
    {
        foreach (var statement in ClientCacheSchema.CreateStatements)
        {
            await _database.ExecuteAsync(statement);
        }
    }

    private async Task UpdateSyncMarkersAsync(bool fullRefresh)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        await UpsertSyncStateAsync("last_sync_time", now);
        if (fullRefresh)
        {
            await UpsertSyncStateAsync("last_full_refresh_time", now);
        }
    }

    private async Task UpsertDeviceConfigAsync(string key, string value)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        await _database.ExecuteAsync("INSERT OR REPLACE INTO device_config (key, value, updated_utc) VALUES (?, ?, ?)", key, value, now);
    }

    private async Task UpsertSyncStateAsync(string key, string value)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        await _database.ExecuteAsync("INSERT OR REPLACE INTO sync_state (key, value, updated_utc) VALUES (?, ?, ?)", key, value, now);
    }

    private async Task<string?> GetSyncValueAsync(string key)
    {
        var rows = await _database.QueryAsync<SyncStateRow>("SELECT value FROM sync_state WHERE key = ? LIMIT 1", key);
        return rows.FirstOrDefault()?.Value;
    }

    private static string? ResolveConfigurationSection(string eventType)
    {
        var normalized = eventType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "menu.updated" => "menu",
            "category.updated" or "categories.updated" => "categories",
            "product.updated" or "products.updated" => "products",
            "availability.updated" => "availability",
            "branding.updated" => "branding",
            "floor.updated" or "floors.updated" => "floors",
            "table.updated" or "tables.updated" => "tables",
            "permissions.updated" => "permissions",
            "features.updated" => "features",
            "settings.updated" => "settings",
            "images.updated" => "images",
            _ => null
        };
    }

    private static IReadOnlyList<string>? ParseStoredStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> GetDeviceConfigValueAsync(string key)
    {
        var rows = await _database.QueryAsync<DeviceConfigRow>("SELECT value FROM device_config WHERE key = ? LIMIT 1", key);
        return rows.FirstOrDefault()?.Value;
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, params object[] args)
    {
        var result = await _database.ExecuteScalarAsync<T>(sql, args);
        return result;
    }

    private async Task<bool> RowExistsByIdAsync(string tableName, object id)
    {
        var table = tableName switch
        {
            "tables" => "tables",
            "products" => "products",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "Unsupported cache table.")
        };

        return await ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE id = ?", id) > 0;
    }

    private const string OrderItemSelectSql =
        """
        SELECT id, product_id, product_mother_id, name, quantity, unit_price, notes, modifier_json,
               variant_id, variant_name, variant_price, meal_deal_id, meal_deal_choices_json, tasting_menu_id
        FROM order_items
        WHERE order_id = ?
        ORDER BY id
        """;

    private static MotherOrderLine ToMotherOrderLine(CachedOrderItemRow line) =>
        new(
            line.Id,
            line.ProductId,
            line.Name,
            line.Quantity,
            line.UnitPrice,
            line.Notes,
            DeserializeModifiers(line.ModifierJson),
            line.ProductMotherId,
            line.VariantId,
            line.VariantName,
            line.VariantPrice,
            line.MealDealId,
            DeserializeModifiers(line.MealDealChoicesJson),
            line.TastingMenuId);

    private static IReadOnlyList<string> DeserializeModifiers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(value) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private sealed class SyncStateRow
    {
        [Column("value")]
        public string? Value { get; set; }
    }

    private sealed class CurrentSessionRow
    {
        [Column("user_id")]
        public string UserId { get; set; } = string.Empty;

        [Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Column("role")]
        public string Role { get; set; } = string.Empty;

        [Column("session_token")]
        public string SessionToken { get; set; } = string.Empty;

        [Column("expires_utc")]
        public string ExpiresUtc { get; set; } = string.Empty;
    }

    private sealed class PermissionRow
    {
        [Column("permission_key")]
        public string PermissionKey { get; set; } = string.Empty;
    }

    private sealed class CachedFloorRow
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("background_image_id")]
        public string? BackgroundImageId { get; set; }
    }

    private sealed class CachedTableRow
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("floor_id")]
        public int FloorId { get; set; }

        [Column("table_number")]
        public string TableNumber { get; set; } = string.Empty;

        [Column("seats")]
        public int Seats { get; set; }

        [Column("status")]
        public string Status { get; set; } = string.Empty;

        [Column("current_total")]
        public decimal CurrentTotal { get; set; }

        [Column("current_order_id")]
        public string? CurrentOrderId { get; set; }

        [Column("covers")]
        public int Covers { get; set; }

        [Column("server_name")]
        public string? ServerName { get; set; }

        [Column("session_status")]
        public string? SessionStatus { get; set; }

        [Column("minutes_occupied")]
        public int MinutesOccupied { get; set; }

        [Column("version")]
        public int Version { get; set; }

        [Column("position_x")]
        public int PositionX { get; set; }

        [Column("position_y")]
        public int PositionY { get; set; }

        [Column("design_icon")]
        public string? DesignIcon { get; set; }
    }

    private sealed class CachedCategoryRow
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("color")]
        public string? Color { get; set; }

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("parent_id")]
        public int? ParentId { get; set; }
    }

    private sealed class CachedCategoryIdRow
    {
        [Column("id")]
        public int Id { get; set; }
    }

    private sealed class CachedProductRow
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("category_id")]
        public int CategoryId { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("price")]
        public decimal Price { get; set; }

        [Column("currency")]
        public string Currency { get; set; } = "GBP";

        [Column("mother_id")]
        public string? MotherId { get; set; }
    }

    private sealed class CachedVariantRow
    {
        [Column("mother_id")]
        public string? MotherId { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("description")]
        public string? Description { get; set; }

        [Column("takeaway_price")]
        public decimal TakeawayPrice { get; set; }

        [Column("dine_in_price")]
        public decimal DineInPrice { get; set; }

        [Column("sort_order")]
        public int SortOrder { get; set; }
    }

    private sealed class CachedQuickNoteRow
    {
        [Column("note_text")]
        public string? NoteText { get; set; }
    }

    private sealed class CachedModifierGroupRow
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("min_select")]
        public int MinSelect { get; set; }

        [Column("max_select")]
        public int MaxSelect { get; set; }
    }

    private sealed class CachedModifierRow
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("price_delta")]
        public decimal PriceDelta { get; set; }
    }

    private sealed class CachedOrderRow
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("order_number")]
        public string OrderNumber { get; set; } = string.Empty;

        [Column("order_type")]
        public string OrderType { get; set; } = string.Empty;

        [Column("table_id")]
        public int? TableId { get; set; }

        [Column("guests")]
        public int Guests { get; set; }

        [Column("status")]
        public string Status { get; set; } = string.Empty;

        [Column("subtotal")]
        public decimal Subtotal { get; set; }

        [Column("tax")]
        public decimal Tax { get; set; }

        [Column("total")]
        public decimal Total { get; set; }

        [Column("version")]
        public int Version { get; set; }

        [Column("updated_utc")]
        public string UpdatedUtc { get; set; } = string.Empty;
    }

    private sealed class CachedOrderItemRow
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("product_id")]
        public int? ProductId { get; set; }

        [Column("product_mother_id")]
        public string? ProductMotherId { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("unit_price")]
        public decimal UnitPrice { get; set; }

        [Column("notes")]
        public string? Notes { get; set; }

        [Column("modifier_json")]
        public string? ModifierJson { get; set; }

        [Column("variant_id")]
        public string? VariantId { get; set; }

        [Column("variant_name")]
        public string? VariantName { get; set; }

        [Column("variant_price")]
        public decimal? VariantPrice { get; set; }

        [Column("meal_deal_id")]
        public string? MealDealId { get; set; }

        [Column("meal_deal_choices_json")]
        public string? MealDealChoicesJson { get; set; }

        [Column("tasting_menu_id")]
        public string? TastingMenuId { get; set; }
    }

    private sealed class CachedCustomerRow
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("mother_id")]
        public string MotherId { get; set; } = string.Empty;

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("phone")]
        public string Phone { get; set; } = string.Empty;

        [Column("email")]
        public string? Email { get; set; }

        [Column("address")]
        public string Address { get; set; } = string.Empty;

        [Column("postcode")]
        public string? Postcode { get; set; }

        [Column("loyalty_points")]
        public int LoyaltyPoints { get; set; }
    }

    private sealed class PrintRequestRow
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("print_type")]
        public string PrintType { get; set; } = string.Empty;

        [Column("order_id")]
        public string? OrderId { get; set; }

        [Column("status")]
        public string Status { get; set; } = string.Empty;

        [Column("message")]
        public string Message { get; set; } = string.Empty;

        [Column("created_utc")]
        public string CreatedUtc { get; set; } = string.Empty;

        [Column("updated_utc")]
        public string UpdatedUtc { get; set; } = string.Empty;
    }

    private sealed class ImageCacheRow
    {
        [Column("image_id")] public string ImageId { get; set; } = string.Empty;
        [Column("remote_path")] public string RemotePath { get; set; } = string.Empty;
        [Column("content_hash")] public string ContentHash { get; set; } = string.Empty;
        [Column("local_path")] public string LocalPath { get; set; } = string.Empty;
        [Column("mime_type")] public string? MimeType { get; set; }
        [Column("last_synchronized_utc")] public string LastSynchronizedUtc { get; set; } = string.Empty;
    }

    private sealed class OnlineOrderRow
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("mother_id")]
        public string MotherId { get; set; } = string.Empty;

        [Column("order_number")]
        public string OrderNumber { get; set; } = string.Empty;

        [Column("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [Column("order_type")]
        public string OrderType { get; set; } = string.Empty;

        [Column("due_time")]
        public string DueTime { get; set; } = string.Empty;

        [Column("status")]
        public string Status { get; set; } = string.Empty;

        [Column("total")]
        public decimal Total { get; set; }

        [Column("payload_json")]
        public string PayloadJson { get; set; } = string.Empty;

        [Column("updated_utc")]
        public string UpdatedUtc { get; set; } = string.Empty;
    }

    private sealed class ReservationCacheRow
    {
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("mother_id")]
        public string MotherId { get; set; } = string.Empty;

        [Column("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [Column("phone")]
        public string Phone { get; set; } = string.Empty;

        [Column("table_id")]
        public int? TableId { get; set; }

        [Column("table_number")]
        public string? TableNumber { get; set; }

        [Column("party_size")]
        public int PartySize { get; set; }

        [Column("reservation_utc")]
        public string ReservationUtc { get; set; } = string.Empty;

        [Column("status")]
        public string Status { get; set; } = string.Empty;

        [Column("payload_json")]
        public string? PayloadJson { get; set; }

        [Column("updated_utc")]
        public string UpdatedUtc { get; set; } = string.Empty;
    }

    private sealed class MotherConnectionRow
    {
        [Column("api_base_url")]
        public string? ApiBaseUrl { get; set; }

        [Column("websocket_url")]
        public string? WebSocketUrl { get; set; }

        [Column("terminal_id")]
        public string? TerminalId { get; set; }

        [Column("auth_token_hint")]
        public string? AuthTokenHint { get; set; }
    }

    private sealed class DeviceConfigRow
    {
        [Column("value")]
        public string Value { get; set; } = string.Empty;
    }
}

public sealed record MotherConnectionSettings(string ApiBaseUrl, string WebSocketUrl, string TerminalId, string TerminalToken);
public sealed record CachedImageMetadata(string ImageId, string RemotePath, string ContentHash, string LocalPath, string? MimeType, string LastSynchronizedUtc);

public sealed record CacheStatus(
    int Categories,
    int Products,
    int Tables,
    int OpenOrders,
    int OnlineOrders,
    int PendingActions,
    string? SchemaVersion,
    string? LastBootstrapTime,
    string? LastSyncTime,
    string? LastEventId,
    string? LastFullRefreshTime);

/// <summary>Last known honesty for menu / layout / orders sync sections.</summary>
public sealed record OperationalSectionSyncStatus(
    bool? MenuOk,
    string? MenuDetail,
    string? MenuUtc,
    bool? LayoutOk,
    string? LayoutDetail,
    string? LayoutUtc,
    bool? OrdersOk,
    string? OrdersDetail,
    string? OrdersUtc,
    string? LastOperationalSyncUtc);

