using MySqlConnector;
using OrderWeb.Contracts.Dtos;
using POS_in_NET.Services;

namespace POS_in_NET.Services;

/// <summary>
/// Bar Inventory data layer — stock master, menu track/link, and Phase 4 movements.
/// </summary>
public sealed class BarStockService
{
    private static bool _schemaReady;

    public async Task EnsureSchemaAsync(MySqlConnection? existing = null)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged)
        {
            return;
        }

        if (_schemaReady && existing is null)
        {
            return;
        }

        if (existing is not null)
        {
            await EnsureSchemaCoreAsync(existing);
            _schemaReady = true;
            return;
        }

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaCoreAsync(connection);
        _schemaReady = true;
    }

    public async Task<IReadOnlyList<BarStockBoardRowDto>> ListTrackedBoardAsync()
    {
        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        const string sql = @"
            SELECT s.id, s.name, s.sku, s.bar_section, s.stock_unit, s.pack_size, s.on_hand,
                   s.par_level, s.low_level, s.max_level,
                   COUNT(m.Id) AS linked_count
            FROM bar_stock_items s
            LEFT JOIN FoodMenuItems m
                ON m.bar_stock_item_id = s.id
               AND m.track_bar_inventory = 1
            WHERE s.is_active = 1
            GROUP BY s.id, s.name, s.sku, s.bar_section, s.stock_unit, s.pack_size, s.on_hand,
                     s.par_level, s.low_level, s.max_level
            ORDER BY s.bar_section ASC, s.name ASC";

        await using var command = new MySqlCommand(sql, connection);
        var rows = new List<BarStockBoardRowDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var onHand = reader.GetDecimal("on_hand");
            var (low, max) = ReadLevels(reader);
            var pack = reader.GetDecimal("pack_size");
            var unit = BarStockUnits.Normalize(reader.GetString("stock_unit"));
            var section = BarStockSections.Normalize(reader.IsDBNull(reader.GetOrdinal("bar_section"))
                ? null
                : reader.GetString("bar_section"));
            rows.Add(BuildBoardRowDto(
                reader.GetString("id"),
                reader.GetString("name"),
                reader.IsDBNull(reader.GetOrdinal("sku")) ? null : reader.GetString("sku"),
                section,
                unit,
                pack,
                onHand,
                low,
                max,
                reader.GetInt32("linked_count")));
        }

        return rows;
    }

    /// <summary>
    /// Soft advisory: top Track Off Drink/Alcohol items sold in the last 7 paid days.
    /// </summary>
    public async Task<string?> GetUntrackedDrinkAdvisoryAsync(int topN = 5)
    {
        topN = Math.Clamp(topN, 1, 10);
        try
        {
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            var since = DateTime.Today.AddDays(-6);
            const string sql = @"
                SELECT COALESCE(NULLIF(TRIM(f.Name), ''), oi.item_name) AS item_name,
                       SUM(oi.quantity) AS sold_qty
                FROM order_items oi
                INNER JOIN orders o ON o.id = oi.order_id
                INNER JOIN FoodMenuItems f ON f.Id = oi.menu_item_id
                WHERE COALESCE(o.paid_at, o.updated_at) >= @since
                  AND LOWER(COALESCE(o.local_lifecycle_state, '')) = 'paid'
                  AND COALESCE(f.track_bar_inventory, 0) = 0
                  AND LOWER(COALESCE(f.ItemType, '')) IN ('drink', 'alcohol')
                  AND oi.quantity > 0
                GROUP BY COALESCE(NULLIF(TRIM(f.Name), ''), oi.item_name)
                HAVING sold_qty >= 3
                ORDER BY sold_qty DESC
                LIMIT @take";

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@since", since);
            command.Parameters.AddWithValue("@take", topN);
            var names = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.IsDBNull(0) ? null : reader.GetString(0);
                var qty = reader.GetDecimal(1);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                names.Add($"{name.Trim()} ({qty:0})");
            }

            if (names.Count == 0)
            {
                return null;
            }

            return "Consider Track On for high-volume drinks: " + string.Join(", ", names);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStock] Untracked advisory skipped: {ex.Message}");
            return null;
        }
    }

    /// <summary>Admin creates shelf stock from Bar Inventory (SKU + ml/bottle calculator).</summary>
    public async Task<BarStockCreateResponseDto> CreateAdminStockAsync(BarStockCreateRequestDto request)
    {
        if (request is null)
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "Stock details are required.");
        }

        var sku = (request.Sku ?? string.Empty).Trim().ToUpperInvariant();
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sku))
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "SKU / inventory number is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "Stock name is required.");
        }

        if (request.MlPerBottle <= 0)
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "ml per bottle must be greater than zero (e.g. 750).");
        }

        if (request.OpeningBottles < 0)
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "Opening bottles cannot be negative.");
        }

        var levels = NormalizeAndValidateLevels(request.LowLevel, ResolveMax(request.MaxLevel, request.ParLevel));
        if (!levels.Ok)
        {
            return CreateFail(BarInventoryErrorCodes.Validation, levels.Error!);
        }

        try
        {
            var existing = await FindBySkuAsync(sku);
            if (existing is not null)
            {
                return CreateFail(BarInventoryErrorCodes.Validation, $"SKU {sku} already exists ({existing.Name}).");
            }

            var stock = await UpsertStockAsync(new BarStockItemDto
            {
                Id = string.Empty,
                Name = name,
                Sku = sku,
                Section = BarStockSections.Normalize(request.Section),
                StockUnit = BarStockUnits.Bottle,
                PackSize = request.MlPerBottle,
                LowLevel = levels.Low,
                MaxLevel = levels.Max,
                ParLevel = levels.Max,
                OnHand = request.OpeningBottles,
                IsActive = true
            });

            return new BarStockCreateResponseDto
            {
                Success = true,
                Message = $"Added {stock.Name} · {stock.OnHand:0.###} bottle(s) · {stock.PackSize:0.###} ml each.",
                Stock = stock,
                BoardRow = ToBoardRow(stock, linkedCount: 0)
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockService] CreateAdminStock failed: {ex.Message}");
            return CreateFail(BarInventoryErrorCodes.Unknown, "Could not create stock item.");
        }
    }

    /// <summary>Admin edits name / SKU / section / ml size — does not change on-hand.</summary>
    public async Task<BarStockCreateResponseDto> UpdateAdminStockAsync(BarStockUpdateRequestDto request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.StockId))
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "Stock item is required.");
        }

        var name = (request.Name ?? string.Empty).Trim();
        var sku = (request.Sku ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name))
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "Stock name is required.");
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "SKU / inventory number is required.");
        }

        if (request.MlPerBottle <= 0)
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "ml per bottle must be greater than zero.");
        }

        var levels = NormalizeAndValidateLevels(request.LowLevel, request.MaxLevel);
        if (!levels.Ok)
        {
            return CreateFail(BarInventoryErrorCodes.Validation, levels.Error!);
        }

        try
        {
            var existing = await GetByIdAsync(request.StockId.Trim());
            if (existing is null || !existing.IsActive)
            {
                return CreateFail(BarInventoryErrorCodes.NotFound, "Stock item not found.");
            }

            var skuOwner = await FindBySkuAsync(sku);
            if (skuOwner is not null &&
                !string.Equals(skuOwner.Id, existing.Id, StringComparison.OrdinalIgnoreCase))
            {
                return CreateFail(BarInventoryErrorCodes.Validation, $"SKU {sku} already exists ({skuOwner.Name}).");
            }

            existing.Name = name;
            existing.Sku = sku;
            existing.Section = BarStockSections.Normalize(request.Section);
            existing.PackSize = request.MlPerBottle;
            existing.StockUnit = BarStockUnits.Bottle;
            existing.LowLevel = levels.Low;
            existing.MaxLevel = levels.Max;
            existing.ParLevel = levels.Max;
            var stock = await UpsertStockAsync(existing);
            return new BarStockCreateResponseDto
            {
                Success = true,
                Message = $"Updated {stock.Name}.",
                Stock = stock,
                BoardRow = ToBoardRow(stock, linkedCount: 0)
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockService] UpdateAdminStock failed: {ex.Message}");
            return CreateFail(BarInventoryErrorCodes.Unknown, "Could not update stock item.");
        }
    }

    /// <summary>Admin soft-deletes stock (is_active = 0) — disappears from Bar Inventory board.</summary>
    public async Task<BarStockCreateResponseDto> DeleteAdminStockAsync(string stockId)
    {
        if (string.IsNullOrWhiteSpace(stockId))
        {
            return CreateFail(BarInventoryErrorCodes.Validation, "Stock item is required.");
        }

        try
        {
            var existing = await GetByIdAsync(stockId.Trim());
            if (existing is null)
            {
                return CreateFail(BarInventoryErrorCodes.NotFound, "Stock item not found.");
            }

            if (!existing.IsActive)
            {
                return new BarStockCreateResponseDto
                {
                    Success = true,
                    Message = "Stock already removed.",
                    Stock = existing
                };
            }

            existing.IsActive = false;
            var stock = await UpsertStockAsync(existing);
            return new BarStockCreateResponseDto
            {
                Success = true,
                Message = $"Deleted {stock.Name}.",
                Stock = stock
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockService] DeleteAdminStock failed: {ex.Message}");
            return CreateFail(BarInventoryErrorCodes.Unknown, "Could not delete stock item.");
        }
    }

    public async Task<BarStockItemDto?> FindBySkuAsync(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        const string sql = @"
            SELECT id, name, sku, bar_section, stock_unit, pack_size, par_level, low_level, max_level, on_hand, is_active, created_at, updated_at
            FROM bar_stock_items
            WHERE UPPER(TRIM(sku)) = @sku
            LIMIT 1";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@sku", sku.Trim().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadStock(reader) : null;
    }

    private static BarStockCreateResponseDto CreateFail(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            Message = message
        };

    public async Task<BarStockMovementResponseDto> ReceiveAsync(
        BarStockReceiveRequestDto request,
        BarStockMovementActorDto? actor = null)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.StockId))
        {
            return MovementFail(BarInventoryErrorCodes.Validation, "Stock item is required.");
        }

        if (request.Qty <= 0)
        {
            return MovementFail(BarInventoryErrorCodes.Validation, "Receive quantity must be greater than zero.");
        }

        return await ApplyMovementCoreAsync(
            request.StockId.Trim(),
            BarStockMovementTypes.Receive,
            request.Qty,
            request.InputUnit,
            isAbsoluteCount: false,
            note: request.Note,
            idempotencyKey: request.IdempotencyKey,
            actor: actor);
    }

    public async Task<BarStockMovementResponseDto> CountAsync(
        BarStockCountRequestDto request,
        BarStockMovementActorDto? actor = null)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.StockId))
        {
            return MovementFail(BarInventoryErrorCodes.Validation, "Stock item is required.");
        }

        if (request.CountedQty < 0)
        {
            return MovementFail(BarInventoryErrorCodes.Validation, "Counted quantity cannot be negative.");
        }

        return await ApplyMovementCoreAsync(
            request.StockId.Trim(),
            BarStockMovementTypes.Count,
            request.CountedQty,
            inputUnit: null,
            isAbsoluteCount: true,
            note: request.Note,
            idempotencyKey: request.IdempotencyKey,
            actor: actor);
    }

    public async Task<BarStockMovementResponseDto> WasteAsync(
        BarStockWasteRequestDto request,
        BarStockMovementActorDto? actor = null)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.StockId))
        {
            return MovementFail(BarInventoryErrorCodes.Validation, "Stock item is required.");
        }

        if (request.Qty <= 0)
        {
            return MovementFail(BarInventoryErrorCodes.Validation, "Waste quantity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return MovementFail(BarInventoryErrorCodes.Validation, "Waste reason is required.");
        }

        return await ApplyMovementCoreAsync(
            request.StockId.Trim(),
            BarStockMovementTypes.Waste,
            request.Qty,
            inputUnit: null,
            isAbsoluteCount: false,
            note: request.Reason.Trim(),
            idempotencyKey: request.IdempotencyKey,
            actor: actor,
            reasonRequiredAlreadyValidated: true);
    }

    /// <summary>
    /// Phase 5 — deduct tracked SKU × ml × line qty (idempotent per line+stock). Never blocks the till.
    /// </summary>
    public async Task<BarSaleDeductionResultDto> DeductSalesForOrderAsync(
        string orderId,
        IReadOnlyList<BarSaleOrderLineDto> lines,
        BarStockMovementActorDto? actor = null)
    {
        return await ApplySaleBatchAsync(orderId, lines, actor, reverse: false);
    }

    /// <summary>
    /// Phase 5 — reverse prior sale movements for voided lines (idempotent sale_void).
    /// </summary>
    public async Task<BarSaleDeductionResultDto> ReverseSalesForOrderAsync(
        string orderId,
        IReadOnlyList<BarSaleOrderLineDto> lines,
        BarStockMovementActorDto? actor = null)
    {
        return await ApplySaleBatchAsync(orderId, lines, actor, reverse: true);
    }

    private async Task<BarSaleDeductionResultDto> ApplySaleBatchAsync(
        string orderId,
        IReadOnlyList<BarSaleOrderLineDto> lines,
        BarStockMovementActorDto? actor,
        bool reverse)
    {
        var result = new BarSaleDeductionResultDto { Success = true };
        if (string.IsNullOrWhiteSpace(orderId) || lines is null || lines.Count == 0)
        {
            return result;
        }

        try
        {
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.LineKey) ||
                    string.IsNullOrWhiteSpace(line.MenuItemId) ||
                    line.Quantity <= 0)
                {
                    result.SkippedCount++;
                    continue;
                }

                var setup = await GetMenuItemSetupAsync(line.MenuItemId.Trim(), connection);
                if (setup is null || !setup.TrackBarInventory || setup.Components.Count == 0)
                {
                    result.SkippedCount++;
                    continue;
                }

                foreach (var component in setup.Components)
                {
                    if (string.IsNullOrWhiteSpace(component.BarStockItemId) || component.SellPortionMl <= 0)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    var stock = await GetByIdAsync(component.BarStockItemId);
                    if (stock is null || !stock.IsActive)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[BarStockSale] Skip missing/inactive stock {component.BarStockItemId} for menu {line.MenuItemId}");
                        result.SkippedCount++;
                        continue;
                    }

                    if (!TryComputeSaleDeltaInStockUnit(stock, component.SellPortionMl, line.Quantity, out var absDelta))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[BarStockSale] Skip portion convert stock={stock.Id} unit={stock.StockUnit} pack={stock.PackSize} ml={component.SellPortionMl}");
                        result.SkippedCount++;
                        continue;
                    }

                    var lineKey = TruncateIdem(line.LineKey.Trim(), 40);
                    var saleKey = TruncateIdem($"sale:{lineKey}:{stock.Id}", 80);
                    var voidKey = TruncateIdem($"sale_void:{lineKey}:{stock.Id}", 80);

                    if (reverse)
                    {
                        var priorSale = await TryGetByIdempotencyAsync(connection, saleKey);
                        if (priorSale is null || !priorSale.Success)
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        var voided = await ApplySignedStockDeltaAsync(
                            connection,
                            stock.Id,
                            BarStockMovementTypes.SaleVoid,
                            absDelta,
                            note: $"void order {orderId.Trim()} line {lineKey}",
                            idempotencyKey: voidKey,
                            actor: actor,
                            orderId: orderId.Trim(),
                            orderLineKey: lineKey);
                        if (voided.Success)
                        {
                            result.AppliedCount++;
                            if (voided.QtyAfter < 0)
                            {
                                result.OverdrawCount++;
                            }
                        }
                        else
                        {
                            result.SkippedCount++;
                        }
                    }
                    else
                    {
                        var applied = await ApplySignedStockDeltaAsync(
                            connection,
                            stock.Id,
                            BarStockMovementTypes.Sale,
                            -absDelta,
                            note: $"sale order {orderId.Trim()} line {lineKey}",
                            idempotencyKey: saleKey,
                            actor: actor,
                            orderId: orderId.Trim(),
                            orderLineKey: lineKey);
                        if (applied.Success)
                        {
                            result.AppliedCount++;
                            if (applied.QtyAfter < 0)
                            {
                                result.OverdrawCount++;
                            }
                        }
                        else
                        {
                            result.SkippedCount++;
                        }
                    }
                }
            }

            result.Message = reverse
                ? $"Sale void · applied {result.AppliedCount}, skipped {result.SkippedCount}."
                : $"Sale deduct · applied {result.AppliedCount}, skipped {result.SkippedCount}.";
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockSale] Batch failed: {ex.Message}");
            return new BarSaleDeductionResultDto
            {
                Success = false,
                Message = "Bar sale batch failed (till not blocked)."
            };
        }
    }

    /// <summary>
    /// Convert sell portion (ml) × qty into stock_unit delta. False when pack/unit cannot convert safely.
    /// </summary>
    internal static bool TryComputeSaleDeltaInStockUnit(
        BarStockItemDto stock,
        decimal portionMl,
        decimal lineQty,
        out decimal absDeltaStockUnit)
    {
        absDeltaStockUnit = 0m;
        if (portionMl <= 0 || lineQty <= 0)
        {
            return false;
        }

        var totalMl = portionMl * lineQty;
        var unit = BarStockUnits.Normalize(stock.StockUnit);
        if (unit == BarStockUnits.Ml)
        {
            absDeltaStockUnit = totalMl;
            return true;
        }

        // Bottle / case shelf: pack_size must be ml-per-bottle (≥ 100) to convert portion ml.
        var pack = stock.PackSize;
        if (pack < 100m)
        {
            return false;
        }

        absDeltaStockUnit = totalMl / pack;
        return absDeltaStockUnit > 0;
    }

    private async Task<BarStockMovementResponseDto> ApplySignedStockDeltaAsync(
        MySqlConnection connection,
        string stockId,
        string movementType,
        decimal signedDelta,
        string? note,
        string idempotencyKey,
        BarStockMovementActorDto? actor,
        string? orderId,
        string? orderLineKey)
    {
        var existing = await TryGetByIdempotencyAsync(connection, idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            const string lockSql = @"
                SELECT id, name, sku, bar_section, stock_unit, pack_size, par_level, low_level, max_level, on_hand, is_active, created_at, updated_at
                FROM bar_stock_items
                WHERE id = @id
                LIMIT 1
                FOR UPDATE";

            BarStockItemDto stock;
            await using (var lockCmd = new MySqlCommand(lockSql, connection, tx))
            {
                lockCmd.Parameters.AddWithValue("@id", stockId);
                await using var reader = await lockCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    await tx.RollbackAsync();
                    return MovementFail(BarInventoryErrorCodes.NotFound, "Stock item was not found.");
                }

                stock = ReadStock(reader);
            }

            if (!stock.IsActive)
            {
                await tx.RollbackAsync();
                return MovementFail(BarInventoryErrorCodes.Validation, "Stock item is inactive.");
            }

            var before = stock.OnHand;
            var after = before + signedDelta;
            var overdraw = after < 0;
            var now = DateTime.UtcNow;
            await using (var update = new MySqlCommand(
                "UPDATE bar_stock_items SET on_hand = @onHand, updated_at = @updated WHERE id = @id",
                connection,
                tx))
            {
                update.Parameters.AddWithValue("@onHand", after);
                update.Parameters.AddWithValue("@updated", now);
                update.Parameters.AddWithValue("@id", stockId);
                await update.ExecuteNonQueryAsync();
            }

            var movementId = Guid.NewGuid().ToString("N");
            const string insertSql = @"
                INSERT INTO bar_stock_movements
                    (id, stock_item_id, movement_type, qty_delta, qty_before, qty_after, reason,
                     staff_user_id, staff_display_name, terminal_id, terminal_label, source,
                     idempotency_key, order_id, order_line_key, overdraw, created_at)
                VALUES
                    (@id, @stockId, @type, @delta, @before, @after, @reason,
                     @staffId, @staffName, @terminalId, @terminalLabel, @source,
                     @idem, @orderId, @lineKey, @overdraw, @created)";
            await using (var insert = new MySqlCommand(insertSql, connection, tx))
            {
                insert.Parameters.AddWithValue("@id", movementId);
                insert.Parameters.AddWithValue("@stockId", stockId);
                insert.Parameters.AddWithValue("@type", movementType);
                insert.Parameters.AddWithValue("@delta", signedDelta);
                insert.Parameters.AddWithValue("@before", before);
                insert.Parameters.AddWithValue("@after", after);
                insert.Parameters.AddWithValue("@reason",
                    string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim());
                insert.Parameters.AddWithValue("@staffId",
                    string.IsNullOrWhiteSpace(actor?.StaffUserId) ? DBNull.Value : actor!.StaffUserId!.Trim());
                insert.Parameters.AddWithValue("@staffName",
                    string.IsNullOrWhiteSpace(actor?.StaffDisplayName) ? DBNull.Value : actor!.StaffDisplayName!.Trim());
                insert.Parameters.AddWithValue("@terminalId",
                    string.IsNullOrWhiteSpace(actor?.TerminalId) ? DBNull.Value : actor!.TerminalId!.Trim());
                insert.Parameters.AddWithValue("@terminalLabel",
                    string.IsNullOrWhiteSpace(actor?.TerminalLabel) ? DBNull.Value : actor!.TerminalLabel!.Trim());
                insert.Parameters.AddWithValue("@source",
                    string.IsNullOrWhiteSpace(actor?.Source) ? "mother" : actor!.Source.Trim().ToLowerInvariant());
                insert.Parameters.AddWithValue("@idem", idempotencyKey);
                insert.Parameters.AddWithValue("@orderId",
                    string.IsNullOrWhiteSpace(orderId) ? DBNull.Value : orderId.Trim());
                insert.Parameters.AddWithValue("@lineKey",
                    string.IsNullOrWhiteSpace(orderLineKey) ? DBNull.Value : orderLineKey.Trim());
                insert.Parameters.AddWithValue("@overdraw", overdraw);
                insert.Parameters.AddWithValue("@created", now);
                await insert.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            stock.OnHand = after;
            stock.UpdatedAtUtc = new DateTimeOffset(now, TimeSpan.Zero);
            var board = await BuildBoardRowAsync(connection, stock);
            return new BarStockMovementResponseDto
            {
                Success = true,
                Message = overdraw
                    ? $"Sale updated · on-hand {after:0.###} {stock.StockUnit} (overdraw)."
                    : $"Sale updated · on-hand {after:0.###} {stock.StockUnit}.",
                MovementId = movementId,
                MovementType = movementType,
                QtyBefore = before,
                QtyAfter = after,
                QtyDelta = signedDelta,
                Stock = stock,
                BoardRow = board
            };
        }
        catch (MySqlException ex) when (ex.Number is 1062)
        {
            try { await tx.RollbackAsync(); } catch { /* ignore */ }
            var replay = await TryGetByIdempotencyAsync(connection, idempotencyKey);
            return replay ?? MovementFail(BarInventoryErrorCodes.Unknown, "Duplicate sale movement.");
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* ignore */ }
            throw;
        }
    }

    private static string TruncateIdem(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max];
    }

    public async Task<BarStockWeeklyReportResponseDto> GetWeeklyReportAsync(int days = 7)
    {
        days = days switch
        {
            15 => 15,
            30 => 30,
            365 => 365,
            _ => Math.Clamp(days, 1, 365)
        };
        var end = DateTime.Today;
        var start = end.AddDays(-(days - 1));
        return await GetUsageReportAsync(start, end);
    }

    /// <summary>Have / Use / Waste report for an inclusive local date range.</summary>
    public async Task<BarStockWeeklyReportResponseDto> GetUsageReportAsync(DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        if (end < start)
        {
            return new BarStockWeeklyReportResponseDto
            {
                Success = false,
                ErrorCode = BarInventoryErrorCodes.Validation,
                Message = "End date cannot be before start date.",
                StartDate = start,
                EndDate = end
            };
        }

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        var localStart = start;
        var localEndExclusive = end.AddDays(1);
        var dayCount = Math.Max(1, (end - start).Days + 1);
        var periodLabel = $"{start:d MMM yyyy} – {end:d MMM yyyy} · {dayCount} days";

        const string sql = @"
            SELECT s.id, s.name, s.sku, s.bar_section, s.stock_unit, s.pack_size, s.on_hand,
                   s.par_level, s.low_level, s.max_level,
                   COALESCE(SUM(CASE WHEN m.movement_type = 'receive' THEN m.qty_delta ELSE 0 END), 0) AS receive_total,
                   COALESCE(SUM(CASE WHEN m.movement_type = 'waste' THEN ABS(m.qty_delta) ELSE 0 END), 0) AS waste_total,
                   COALESCE(SUM(CASE WHEN m.movement_type = 'count' THEN m.qty_delta ELSE 0 END), 0) AS count_variance,
                   COALESCE(SUM(CASE WHEN m.movement_type = 'sale' THEN ABS(m.qty_delta) ELSE 0 END), 0) AS sale_total
            FROM bar_stock_items s
            LEFT JOIN bar_stock_movements m
                ON m.stock_item_id = s.id
               AND m.created_at >= @from
               AND m.created_at < @to
            WHERE s.is_active = 1
            GROUP BY s.id, s.name, s.sku, s.bar_section, s.stock_unit, s.pack_size, s.on_hand,
                     s.par_level, s.low_level, s.max_level
            ORDER BY s.bar_section ASC, s.name ASC";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", localStart);
        command.Parameters.AddWithValue("@to", localEndExclusive);

        var items = new List<BarStockWeeklyReportRowDto>();
        decimal receiveTotal = 0, wasteTotal = 0, countVarianceTotal = 0, usedTotal = 0, haveTotal = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var onHand = reader.GetDecimal("on_hand");
            var (low, max) = ReadLevels(reader);
            var pack = reader.GetDecimal("pack_size");
            var unit = BarStockUnits.Normalize(reader.GetString("stock_unit"));
            var waste = reader.GetDecimal("waste_total");
            var used = reader.GetDecimal("sale_total");
            var isLow = BarStockOrderSuggest.IsBelowLow(onHand, low);
            var row = new BarStockWeeklyReportRowDto
            {
                StockId = reader.GetString("id"),
                Name = reader.GetString("name"),
                Sku = reader.IsDBNull(reader.GetOrdinal("sku")) ? null : reader.GetString("sku"),
                Section = BarStockSections.Normalize(reader.IsDBNull(reader.GetOrdinal("bar_section"))
                    ? null
                    : reader.GetString("bar_section")),
                StockUnit = unit,
                PackSize = pack,
                ReceiveTotal = reader.GetDecimal("receive_total"),
                WasteTotal = waste,
                CountVarianceTotal = reader.GetDecimal("count_variance"),
                UsedTotal = used,
                EndingOnHand = onHand,
                ParLevel = max,
                SuggestOrderQty = BarStockOrderSuggest.QtyToReachMax(onHand, low, max, pack, unit),
                SuggestOrderDisplay = BarStockOrderSuggest.FormatSuggest(onHand, low, max, pack, unit),
                Status = isLow ? "Low" : "OK",
                HaveDisplay = BarStockQtyDisplay.Format(onHand, pack, unit),
                UsedDisplay = BarStockQtyDisplay.Format(used, pack, unit),
                WasteDisplay = BarStockQtyDisplay.Format(waste, pack, unit)
            };
            items.Add(row);
            receiveTotal += row.ReceiveTotal;
            wasteTotal += row.WasteTotal;
            countVarianceTotal += row.CountVarianceTotal;
            usedTotal += row.UsedTotal;
            haveTotal += row.EndingOnHand;
        }

        var hasActivity = items.Any(i => i.UsedTotal != 0 || i.WasteTotal != 0 || i.ReceiveTotal != 0);

        return new BarStockWeeklyReportResponseDto
        {
            Success = true,
            PeriodLabel = periodLabel,
            StartDate = start,
            EndDate = end,
            ReceiveTotal = receiveTotal,
            WasteTotal = wasteTotal,
            CountVarianceTotal = countVarianceTotal,
            UsedTotal = usedTotal,
            HaveTotal = haveTotal,
            HaveTotalDisplay = $"{BarStockQtyDisplay.FormatQty(haveTotal)} bottle{(Math.Abs(haveTotal) == 1m ? "" : "s")}",
            UsedTotalDisplay = $"{BarStockQtyDisplay.FormatQty(usedTotal)} bottle{(Math.Abs(usedTotal) == 1m ? "" : "s")}",
            WasteTotalDisplay = $"{BarStockQtyDisplay.FormatQty(wasteTotal)} bottle{(Math.Abs(wasteTotal) == 1m ? "" : "s")}",
            Items = items,
            Message = hasActivity ? null : "No use or waste in this period"
        };
    }

    public Task<string> ExportUsageReportCsvAsync(DateTime startDate, DateTime endDate) =>
        ExportUsageReportCsvCoreAsync(startDate, endDate);

    private async Task<string> ExportUsageReportCsvCoreAsync(DateTime startDate, DateTime endDate)
    {
        var report = await GetUsageReportAsync(startDate, endDate);
        if (!report.Success)
        {
            throw new InvalidOperationException(report.Message ?? "Could not build report.");
        }

        var restaurantName = await ResolveRestaurantNameAsync();
        var generatedAt = DateTime.Now;
        var folder = Path.Combine(FileSystem.Current.AppDataDirectory, "BarInventory");
        Directory.CreateDirectory(folder);
        var fileName = $"BarUsage_{report.StartDate:yyyyMMdd}_{report.EndDate:yyyyMMdd}_{generatedAt:HHmmss}.csv";
        var filePath = Path.Combine(folder, fileName);
        await File.WriteAllTextAsync(
            filePath,
            BarStockUsageReportCsv.Build(report, restaurantName, generatedAt));
        return filePath;
    }

    /// <summary>Have / Use / Waste period report PDF (Admin).</summary>
    public async Task<string> ExportUsageReportPdfAsync(
        DateTime startDate,
        DateTime endDate,
        string? businessName = null)
    {
        var report = await GetUsageReportAsync(startDate, endDate);
        if (!report.Success)
        {
            throw new InvalidOperationException(report.Message ?? "Could not build report.");
        }

        var restaurantName = string.IsNullOrWhiteSpace(businessName)
            ? await ResolveRestaurantNameAsync()
            : businessName.Trim();
        var generatedAt = DateTime.Now;

        var folder = Path.Combine(FileSystem.Current.AppDataDirectory, "BarInventory");
        Directory.CreateDirectory(folder);
        var fileName = $"BarUsage_{report.StartDate:yyyyMMdd}_{report.EndDate:yyyyMMdd}_{generatedAt:HHmmss}.pdf";
        var filePath = Path.Combine(folder, fileName);

        using var document = new Syncfusion.Pdf.PdfDocument();
        document.PageSettings.Size = Syncfusion.Pdf.PdfPageSize.A4;
        var page = document.Pages.Add();
        var graphics = page.Graphics;
        var titleFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 18, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold);
        var headingFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 12, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold);
        var normalFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 10);
        var metaFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 9);

        float y = 24f;
        graphics.DrawString(
            restaurantName,
            titleFont,
            Syncfusion.Pdf.Graphics.PdfBrushes.Black,
            new Syncfusion.Drawing.PointF(20, y));
        y += 26;
        graphics.DrawString(
            $"Stock report generated - {generatedAt:dd MMM yyyy HH:mm}",
            headingFont,
            Syncfusion.Pdf.Graphics.PdfBrushes.Black,
            new Syncfusion.Drawing.PointF(20, y));
        y += 18;
        graphics.DrawString(
            string.IsNullOrWhiteSpace(report.PeriodLabel)
                ? $"{report.StartDate:dd MMM yyyy} – {report.EndDate:dd MMM yyyy}"
                : report.PeriodLabel,
            metaFont,
            Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray,
            new Syncfusion.Drawing.PointF(20, y));
        y += 14;
        graphics.DrawString(
            $"Have {report.HaveTotalDisplay}  ·  Use {report.UsedTotalDisplay}  ·  Waste {report.WasteTotalDisplay}",
            metaFont,
            Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray,
            new Syncfusion.Drawing.PointF(20, y));
        y += 20;

        var rows = (report.Items ?? Array.Empty<BarStockWeeklyReportRowDto>())
            .OrderBy(r => BarStockSections.DisplayName(r.Section), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (rows.Count == 0)
        {
            graphics.DrawString(
                "No stock rows for this period.",
                normalFont,
                Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                new Syncfusion.Drawing.PointF(20, y));
        }
        else
        {
            string? lastSection = null;
            foreach (var row in rows)
            {
                if (y > 760)
                {
                    page = document.Pages.Add();
                    graphics = page.Graphics;
                    y = 24f;
                    lastSection = null;
                }

                var sectionName = BarStockSections.DisplayName(row.Section);
                if (!string.Equals(lastSection, sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    lastSection = sectionName;
                    graphics.DrawString(
                        sectionName,
                        headingFont,
                        Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray,
                        new Syncfusion.Drawing.PointF(20, y));
                    y += 16;
                    graphics.DrawString("Item", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(20, y));
                    graphics.DrawString("SKU", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(150, y));
                    graphics.DrawString("Have", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(240, y));
                    graphics.DrawString("Use", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(350, y));
                    graphics.DrawString("Waste", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(450, y));
                    y += 14;
                }

                graphics.DrawString(
                    Truncate(row.Name, 22),
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(20, y));
                graphics.DrawString(
                    Truncate(row.Sku ?? "—", 12),
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(150, y));
                graphics.DrawString(
                    Truncate(string.IsNullOrWhiteSpace(row.HaveDisplay) ? "—" : row.HaveDisplay, 16),
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(240, y));
                graphics.DrawString(
                    Truncate(string.IsNullOrWhiteSpace(row.UsedDisplay) ? "—" : row.UsedDisplay, 16),
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(350, y));
                graphics.DrawString(
                    Truncate(string.IsNullOrWhiteSpace(row.WasteDisplay) ? "—" : row.WasteDisplay, 16),
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(450, y));
                y += 15;
            }

            y += 10;
            graphics.DrawString(
                $"{rows.Count} item(s)",
                metaFont,
                Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray,
                new Syncfusion.Drawing.PointF(20, y));
        }

        await using (var stream = File.Create(filePath))
        {
            document.Save(stream);
        }

        return filePath;
    }

    private static async Task<string> ResolveRestaurantNameAsync()
    {
        try
        {
            var info = await new BusinessSettingsService().GetBusinessInfoAsync();
            if (!string.IsNullOrWhiteSpace(info?.RestaurantName))
            {
                return info.RestaurantName.Trim();
            }
        }
        catch
        {
            // Fall through to default label.
        }

        return "POS-in-NET";
    }

    private async Task<BarStockMovementResponseDto> ApplyMovementCoreAsync(
        string stockId,
        string movementType,
        decimal qtyOrCounted,
        string? inputUnit,
        bool isAbsoluteCount,
        string? note,
        string? idempotencyKey,
        BarStockMovementActorDto? actor,
        bool reasonRequiredAlreadyValidated = false)
    {
        _ = reasonRequiredAlreadyValidated;
        try
        {
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            var key = string.IsNullOrWhiteSpace(idempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : idempotencyKey.Trim();

            var existing = await TryGetByIdempotencyAsync(connection, key);
            if (existing is not null)
            {
                return existing;
            }

            await using var tx = await connection.BeginTransactionAsync();
            try
            {
                const string lockSql = @"
                    SELECT id, name, sku, bar_section, stock_unit, pack_size, par_level, low_level, max_level, on_hand, is_active, created_at, updated_at
                    FROM bar_stock_items
                    WHERE id = @id
                    LIMIT 1
                    FOR UPDATE";

                BarStockItemDto? stock;
                await using (var lockCmd = new MySqlCommand(lockSql, connection, tx))
                {
                    lockCmd.Parameters.AddWithValue("@id", stockId);
                    await using var reader = await lockCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return MovementFail(BarInventoryErrorCodes.NotFound, "Stock item was not found.");
                    }

                    stock = ReadStock(reader);
                }

                if (!stock.IsActive)
                {
                    await tx.RollbackAsync();
                    return MovementFail(BarInventoryErrorCodes.Validation, "Stock item is inactive.");
                }

                var before = stock.OnHand;
                decimal delta;
                decimal after;

                if (isAbsoluteCount)
                {
                    after = qtyOrCounted;
                    delta = after - before;
                }
                else if (string.Equals(movementType, BarStockMovementTypes.Waste, StringComparison.OrdinalIgnoreCase))
                {
                    delta = -qtyOrCounted;
                    after = before + delta;
                    if (after < 0)
                    {
                        await tx.RollbackAsync();
                        return MovementFail(
                            BarInventoryErrorCodes.Validation,
                            $"Waste would leave on-hand below zero (have {before:0.###} {stock.StockUnit}).");
                    }
                }
                else
                {
                    // Receive — convert cases via pack_size when needed.
                    var unit = string.IsNullOrWhiteSpace(inputUnit)
                        ? stock.StockUnit
                        : BarStockUnits.Normalize(inputUnit);
                    if (string.Equals(unit, BarStockUnits.Case, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(stock.StockUnit, BarStockUnits.Case, StringComparison.OrdinalIgnoreCase))
                    {
                        if (stock.PackSize <= 1m)
                        {
                            await tx.RollbackAsync();
                            return MovementFail(
                                BarInventoryErrorCodes.Validation,
                                "Pack size must be greater than 1 to receive cases into this stock unit.");
                        }

                        delta = qtyOrCounted * stock.PackSize;
                    }
                    else if (!string.Equals(unit, stock.StockUnit, StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(unit, BarStockUnits.Case, StringComparison.OrdinalIgnoreCase))
                    {
                        await tx.RollbackAsync();
                        return MovementFail(
                            BarInventoryErrorCodes.Validation,
                            $"Receive unit must be {stock.StockUnit} or case.");
                    }
                    else
                    {
                        delta = qtyOrCounted;
                    }

                    after = before + delta;
                }

                var now = DateTime.UtcNow;
                const string updateSql = @"
                    UPDATE bar_stock_items
                    SET on_hand = @onHand, updated_at = @updated
                    WHERE id = @id";
                await using (var update = new MySqlCommand(updateSql, connection, tx))
                {
                    update.Parameters.AddWithValue("@onHand", after);
                    update.Parameters.AddWithValue("@updated", now);
                    update.Parameters.AddWithValue("@id", stockId);
                    await update.ExecuteNonQueryAsync();
                }

                var movementId = Guid.NewGuid().ToString("N");
                const string insertSql = @"
                    INSERT INTO bar_stock_movements
                        (id, stock_item_id, movement_type, qty_delta, qty_before, qty_after, reason,
                         staff_user_id, staff_display_name, terminal_id, terminal_label, source,
                         idempotency_key, created_at)
                    VALUES
                        (@id, @stockId, @type, @delta, @before, @after, @reason,
                         @staffId, @staffName, @terminalId, @terminalLabel, @source,
                         @idem, @created)";
                await using (var insert = new MySqlCommand(insertSql, connection, tx))
                {
                    insert.Parameters.AddWithValue("@id", movementId);
                    insert.Parameters.AddWithValue("@stockId", stockId);
                    insert.Parameters.AddWithValue("@type", movementType);
                    insert.Parameters.AddWithValue("@delta", delta);
                    insert.Parameters.AddWithValue("@before", before);
                    insert.Parameters.AddWithValue("@after", after);
                    insert.Parameters.AddWithValue("@reason",
                        string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim());
                    insert.Parameters.AddWithValue("@staffId",
                        string.IsNullOrWhiteSpace(actor?.StaffUserId) ? DBNull.Value : actor!.StaffUserId!.Trim());
                    insert.Parameters.AddWithValue("@staffName",
                        string.IsNullOrWhiteSpace(actor?.StaffDisplayName) ? DBNull.Value : actor!.StaffDisplayName!.Trim());
                    insert.Parameters.AddWithValue("@terminalId",
                        string.IsNullOrWhiteSpace(actor?.TerminalId) ? DBNull.Value : actor!.TerminalId!.Trim());
                    insert.Parameters.AddWithValue("@terminalLabel",
                        string.IsNullOrWhiteSpace(actor?.TerminalLabel) ? DBNull.Value : actor!.TerminalLabel!.Trim());
                    insert.Parameters.AddWithValue("@source",
                        string.IsNullOrWhiteSpace(actor?.Source) ? "mother" : actor!.Source.Trim().ToLowerInvariant());
                    insert.Parameters.AddWithValue("@idem", key);
                    insert.Parameters.AddWithValue("@created", now);
                    await insert.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                stock.OnHand = after;
                stock.UpdatedAtUtc = new DateTimeOffset(now, TimeSpan.Zero);
                var board = await BuildBoardRowAsync(connection, stock);

                return new BarStockMovementResponseDto
                {
                    Success = true,
                    Message = movementType switch
                    {
                        BarStockMovementTypes.Receive => $"Received · on-hand now {after:0.###} {stock.StockUnit}.",
                        BarStockMovementTypes.Count => $"Count saved · variance {delta:+0.###;-0.###;0} · on-hand {after:0.###} {stock.StockUnit}.",
                        BarStockMovementTypes.Waste => $"Waste logged · on-hand now {after:0.###} {stock.StockUnit}.",
                        _ => "Stock updated."
                    },
                    MovementId = movementId,
                    MovementType = movementType,
                    QtyBefore = before,
                    QtyAfter = after,
                    QtyDelta = delta,
                    Stock = stock,
                    BoardRow = board
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (MySqlException ex) when (ex.Number is 1062)
        {
            // Race on idempotency — return the winner's result.
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            var key = string.IsNullOrWhiteSpace(idempotencyKey)
                ? null
                : idempotencyKey.Trim();
            if (key is not null)
            {
                var replay = await TryGetByIdempotencyAsync(connection, key);
                if (replay is not null)
                {
                    return replay;
                }
            }

            return MovementFail(BarInventoryErrorCodes.Unknown, "Could not apply stock movement (duplicate).");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockService] Movement failed: {ex.Message}");
            return MovementFail(BarInventoryErrorCodes.Unknown, "Could not apply stock movement.");
        }
    }

    private static async Task<BarStockMovementResponseDto?> TryGetByIdempotencyAsync(
        MySqlConnection connection,
        string idempotencyKey)
    {
        const string sql = @"
            SELECT m.id, m.movement_type, m.qty_delta, m.qty_before, m.qty_after,
                   s.id AS stock_id, s.name, s.sku, s.bar_section, s.stock_unit, s.pack_size,
                   s.par_level, s.low_level, s.max_level,
                   s.on_hand, s.is_active, s.created_at, s.updated_at
            FROM bar_stock_movements m
            INNER JOIN bar_stock_items s ON s.id = m.stock_item_id
            WHERE m.idempotency_key = @key
            LIMIT 1";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var levels = ReadLevels(reader);
        var stock = new BarStockItemDto
        {
            Id = reader.GetString("stock_id"),
            Name = reader.GetString("name"),
            Sku = reader.IsDBNull(reader.GetOrdinal("sku")) ? null : reader.GetString("sku"),
            Section = BarStockSections.Normalize(reader.IsDBNull(reader.GetOrdinal("bar_section"))
                ? null
                : reader.GetString("bar_section")),
            StockUnit = BarStockUnits.Normalize(reader.GetString("stock_unit")),
            PackSize = reader.GetDecimal("pack_size"),
            LowLevel = levels.Low,
            MaxLevel = levels.Max,
            ParLevel = levels.Max,
            OnHand = reader.GetDecimal("on_hand"),
            IsActive = ReadBool(reader, "is_active"),
            CreatedAtUtc = ToUtc(reader.GetDateTime("created_at")),
            UpdatedAtUtc = ToUtc(reader.GetDateTime("updated_at"))
        };

        return new BarStockMovementResponseDto
        {
            Success = true,
            Message = "Already applied (same request).",
            MovementId = reader.GetString("id"),
            MovementType = reader.GetString("movement_type"),
            QtyDelta = reader.GetDecimal("qty_delta"),
            QtyBefore = reader.GetDecimal("qty_before"),
            QtyAfter = reader.GetDecimal("qty_after"),
            Stock = stock,
            BoardRow = ToBoardRow(stock, linkedCount: 0)
        };
    }

    private static async Task<BarStockBoardRowDto> BuildBoardRowAsync(MySqlConnection connection, BarStockItemDto stock)
    {
        const string sql = @"
            SELECT COUNT(*) FROM FoodMenuItems
            WHERE bar_stock_item_id = @id AND track_bar_inventory = 1";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", stock.Id);
        var linked = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        return ToBoardRow(stock, linked);
    }

    private static BarStockBoardRowDto ToBoardRow(BarStockItemDto stock, int linkedCount) =>
        BuildBoardRowDto(
            stock.Id,
            stock.Name,
            stock.Sku,
            BarStockSections.Normalize(stock.Section),
            stock.StockUnit,
            stock.PackSize,
            stock.OnHand,
            stock.LowLevel > 0 ? stock.LowLevel : stock.ParLevel,
            stock.MaxLevel > 0 ? stock.MaxLevel : stock.ParLevel,
            linkedCount);

    private static BarStockBoardRowDto BuildBoardRowDto(
        string stockId,
        string name,
        string? sku,
        string section,
        string unit,
        decimal pack,
        decimal onHand,
        decimal lowLevel,
        decimal maxLevel,
        int linkedCount)
    {
        var (low, max) = NormalizeLevelsForRead(lowLevel, maxLevel, maxLevel);
        var isLow = BarStockOrderSuggest.IsBelowLow(onHand, low);
        return new BarStockBoardRowDto
        {
            StockId = stockId,
            Name = name,
            Sku = sku,
            Section = section,
            StockUnit = unit,
            PackSize = pack,
            OnHand = onHand,
            LowLevel = low,
            MaxLevel = max,
            ParLevel = max,
            Status = isLow ? "Low" : "OK",
            LinkedMenuItemCount = linkedCount,
            SuggestOrderQty = BarStockOrderSuggest.QtyToReachMax(onHand, low, max, pack, unit),
            SuggestOrderDisplay = BarStockOrderSuggest.FormatSuggest(onHand, low, max, pack, unit)
        };
    }

    private static decimal ResolveMax(decimal maxLevel, decimal parLevel) =>
        maxLevel > 0m ? maxLevel : Math.Max(0m, parLevel);

    private static (bool Ok, decimal Low, decimal Max, string? Error) NormalizeAndValidateLevels(decimal low, decimal max)
    {
        if (low <= 0m)
        {
            return (false, 0m, 0m, "Low limit is required and must be greater than zero.");
        }

        if (max <= 0m)
        {
            return (false, 0m, 0m, "Max limit is required and must be greater than zero.");
        }

        if (low > max)
        {
            return (false, 0m, 0m, "Low limit cannot be greater than Max limit.");
        }

        return (true, low, max, null);
    }

    /// <summary>Fill missing Low/Max from legacy par (migration / old rows).</summary>
    private static (decimal Low, decimal Max) NormalizeLevelsForRead(decimal low, decimal max, decimal par)
    {
        var resolvedMax = max > 0m ? max : Math.Max(0m, par);
        var resolvedLow = low > 0m
            ? low
            : resolvedMax > 0m
                ? Math.Min(resolvedMax, Math.Max(1m, Math.Floor(resolvedMax / 3m)))
                : 0m;
        if (resolvedLow > resolvedMax && resolvedMax > 0m)
        {
            resolvedLow = resolvedMax;
        }

        return (resolvedLow, resolvedMax);
    }

    private static (decimal Low, decimal Max) ReadLevels(MySqlDataReader reader)
    {
        var par = reader.GetDecimal("par_level");
        var low = HasColumn(reader, "low_level") ? reader.GetDecimal("low_level") : 0m;
        var max = HasColumn(reader, "max_level") ? reader.GetDecimal("max_level") : 0m;
        return NormalizeLevelsForRead(low, max, par);
    }

    private static bool HasColumn(MySqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static BarStockMovementResponseDto MovementFail(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            Message = message
        };

    public async Task<IReadOnlyList<BarStockItemDto>> ListAsync(bool activeOnly = true)
    {
        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        var sql = @"
            SELECT id, name, sku, bar_section, stock_unit, pack_size, par_level, low_level, max_level,
                   on_hand, is_active, created_at, updated_at
            FROM bar_stock_items";
        if (activeOnly)
        {
            sql += " WHERE is_active = 1";
        }

        sql += " ORDER BY name ASC";

        await using var command = new MySqlCommand(sql, connection);
        var items = new List<BarStockItemDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(ReadStock(reader));
        }

        return items;
    }

    public async Task<BarStockItemDto?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        const string sql = @"
            SELECT id, name, sku, bar_section, stock_unit, pack_size, par_level, low_level, max_level, on_hand, is_active, created_at, updated_at
            FROM bar_stock_items
            WHERE id = @id
            LIMIT 1";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadStock(reader) : null;
    }

    public async Task<BarStockItemDto> UpsertStockAsync(BarStockItemDto stock)
    {
        ArgumentNullException.ThrowIfNull(stock);

        var name = (stock.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Stock name is required.", nameof(stock));
        }

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        var id = string.IsNullOrWhiteSpace(stock.Id) ? Guid.NewGuid().ToString() : stock.Id.Trim();
        var unit = BarStockUnits.Normalize(stock.StockUnit);
        var section = BarStockSections.Normalize(stock.Section);
        var packSize = stock.PackSize <= 0 ? 1m : stock.PackSize;
        var now = DateTime.UtcNow;

        const string sql = @"
            INSERT INTO bar_stock_items
                (id, name, sku, bar_section, stock_unit, pack_size, par_level, low_level, max_level,
                 on_hand, is_active, created_at, updated_at)
            VALUES
                (@id, @name, @sku, @section, @unit, @pack, @par, @low, @max,
                 @onHand, @active, @created, @updated)
            ON DUPLICATE KEY UPDATE
                name = VALUES(name),
                sku = VALUES(sku),
                bar_section = VALUES(bar_section),
                stock_unit = VALUES(stock_unit),
                pack_size = VALUES(pack_size),
                par_level = VALUES(par_level),
                low_level = VALUES(low_level),
                max_level = VALUES(max_level),
                is_active = VALUES(is_active),
                updated_at = VALUES(updated_at)";
        // on_hand is set on insert only — daily qty via Receive/Waste/sale.

        await using var command = new MySqlCommand(sql, connection);
        var maxLevel = stock.MaxLevel > 0m ? stock.MaxLevel : Math.Max(0m, stock.ParLevel);
        var lowLevel = stock.LowLevel > 0m ? stock.LowLevel : 0m;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@sku", string.IsNullOrWhiteSpace(stock.Sku) ? DBNull.Value : stock.Sku.Trim());
        command.Parameters.AddWithValue("@section", section);
        command.Parameters.AddWithValue("@unit", unit);
        command.Parameters.AddWithValue("@pack", packSize);
        command.Parameters.AddWithValue("@par", maxLevel);
        command.Parameters.AddWithValue("@low", lowLevel);
        command.Parameters.AddWithValue("@max", maxLevel);
        command.Parameters.AddWithValue("@onHand", stock.OnHand);
        command.Parameters.AddWithValue("@active", stock.IsActive);
        command.Parameters.AddWithValue("@created", now);
        command.Parameters.AddWithValue("@updated", now);
        await command.ExecuteNonQueryAsync();

        return (await GetByIdAsync(id))!;
    }

    /// <summary>
    /// Phase 1 save path: persist Track + stock link + sell portion on a menu item.
    /// Creates/updates the stock master row when <see cref="MenuItemBarStockSetupDto.Stock"/> is provided.
    /// </summary>
    public async Task<MenuItemBarStockSetupResponseDto> SaveMenuItemSetupAsync(MenuItemBarStockSetupDto setup)
    {
        if (setup is null || string.IsNullOrWhiteSpace(setup.MenuItemId))
        {
            return new MenuItemBarStockSetupResponseDto
            {
                Success = false,
                ErrorCode = BarInventoryErrorCodes.Validation,
                Message = "Menu item id is required."
            };
        }

        try
        {
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            var menuItemId = setup.MenuItemId.Trim();
            var components = NormalizeComponents(setup);

            if (setup.TrackBarInventory)
            {
                // Legacy create-stock path still supported if Components empty but Stock nested.
                if (components.Count == 0 && setup.Stock is not null)
                {
                    if (string.IsNullOrWhiteSpace(setup.Stock.Name))
                    {
                        setup.Stock.Name = "Stock item";
                    }

                    var created = await UpsertStockAsync(setup.Stock);
                    var ml = ConvertLegacyPortionToMl(setup.SellPortionQty, setup.SellPortionUnit, created);
                    components =
                    [
                        new MenuItemBarStockComponentDto
                        {
                            BarStockItemId = created.Id,
                            Sku = created.Sku,
                            StockName = created.Name,
                            SellPortionMl = ml,
                            SortOrder = 0
                        }
                    ];
                }

                if (components.Count == 0)
                {
                    return new MenuItemBarStockSetupResponseDto
                    {
                        Success = false,
                        ErrorCode = BarInventoryErrorCodes.Validation,
                        Message = "Track On needs at least one SKU + ml row. Create stock on Bar Inventory first."
                    };
                }

                foreach (var row in components)
                {
                    var stock = await GetByIdAsync(row.BarStockItemId);
                    if (stock is null || !stock.IsActive)
                    {
                        return new MenuItemBarStockSetupResponseDto
                        {
                            Success = false,
                            ErrorCode = BarInventoryErrorCodes.NotFound,
                            Message = $"Stock SKU not found ({row.Sku ?? row.BarStockItemId})."
                        };
                    }

                    if (row.SellPortionMl <= 0)
                    {
                        return new MenuItemBarStockSetupResponseDto
                        {
                            Success = false,
                            ErrorCode = BarInventoryErrorCodes.Validation,
                            Message = "Each SKU row needs ml greater than zero (e.g. 175 or 330)."
                        };
                    }
                }
            }
            else if (components.Count == 0)
            {
                // Track Off with no new rows — keep existing component links for later.
                components = await ListComponentsAsync(connection, menuItemId);
            }

            var primary = components.FirstOrDefault();
            const string updateSql = @"
                UPDATE FoodMenuItems
                SET track_bar_inventory = @track,
                    bar_stock_item_id = @stockId,
                    sell_portion_qty = @portionQty,
                    sell_portion_unit = @portionUnit,
                    UpdatedAt = @updated
                WHERE Id = @menuItemId";

            await using (var update = new MySqlCommand(updateSql, connection))
            {
                update.Parameters.AddWithValue("@track", setup.TrackBarInventory);
                update.Parameters.AddWithValue("@stockId",
                    primary is not null ? primary.BarStockItemId : DBNull.Value);
                update.Parameters.AddWithValue("@portionQty",
                    primary is not null ? primary.SellPortionMl : DBNull.Value);
                update.Parameters.AddWithValue("@portionUnit",
                    primary is not null ? BarStockUnits.Ml : DBNull.Value);
                update.Parameters.AddWithValue("@updated", DateTime.Now);
                update.Parameters.AddWithValue("@menuItemId", menuItemId);
                var rows = await update.ExecuteNonQueryAsync();
                if (rows <= 0)
                {
                    return new MenuItemBarStockSetupResponseDto
                    {
                        Success = false,
                        ErrorCode = BarInventoryErrorCodes.NotFound,
                        Message = "Menu item was not found."
                    };
                }
            }

            // Always persist current component list when provided; Track Off keeps links.
            if (setup.TrackBarInventory || NormalizeComponents(setup).Count > 0)
            {
                await ReplaceComponentsAsync(connection, menuItemId, components);
            }

            var saved = await GetMenuItemSetupAsync(menuItemId, connection);
            BarStockItemDto? stockDto = null;
            if (saved?.BarStockItemId is { Length: > 0 } id)
            {
                stockDto = await GetByIdAsync(id);
            }

            return new MenuItemBarStockSetupResponseDto
            {
                Success = true,
                Message = setup.TrackBarInventory
                    ? $"Bar stock setup saved ({saved?.Components.Count ?? 0} SKU link(s))."
                    : "Bar stock tracking turned off.",
                Setup = saved,
                Stock = stockDto
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockService] SaveMenuItemSetup failed: {ex.Message}");
            return new MenuItemBarStockSetupResponseDto
            {
                Success = false,
                ErrorCode = BarInventoryErrorCodes.Unknown,
                Message = "Could not save bar stock setup."
            };
        }
    }

    private static List<MenuItemBarStockComponentDto> NormalizeComponents(MenuItemBarStockSetupDto setup)
    {
        var list = new List<MenuItemBarStockComponentDto>();
        if (setup.Components is { Count: > 0 })
        {
            var order = 0;
            foreach (var row in setup.Components)
            {
                if (string.IsNullOrWhiteSpace(row.BarStockItemId))
                {
                    continue;
                }

                list.Add(new MenuItemBarStockComponentDto
                {
                    BarStockItemId = row.BarStockItemId.Trim(),
                    Sku = row.Sku,
                    StockName = row.StockName,
                    SellPortionMl = row.SellPortionMl,
                    SortOrder = order++
                });
            }

            return list;
        }

        // Legacy single-link payload.
        if (!string.IsNullOrWhiteSpace(setup.BarStockItemId) && setup.SellPortionQty is > 0)
        {
            list.Add(new MenuItemBarStockComponentDto
            {
                BarStockItemId = setup.BarStockItemId.Trim(),
                SellPortionMl = setup.SellPortionQty.Value,
                SortOrder = 0
            });
        }

        return list;
    }

    private static decimal ConvertLegacyPortionToMl(
        decimal? qty,
        string? unit,
        BarStockItemDto stock)
    {
        var amount = qty is > 0 ? qty.Value : 1m;
        var u = BarStockUnits.Normalize(unit ?? stock.StockUnit);
        if (u == BarStockUnits.Ml)
        {
            return amount;
        }

        var pack = stock.PackSize > 0 ? stock.PackSize : 750m;
        // pack >= 100 → ml per bottle; else bottles-per-case (assume 750 ml bottles).
        if (pack >= 100m)
        {
            return amount * pack;
        }

        return amount * pack * 750m;
    }

    private static async Task ReplaceComponentsAsync(
        MySqlConnection connection,
        string menuItemId,
        IReadOnlyList<MenuItemBarStockComponentDto> components)
    {
        await using (var clear = new MySqlCommand(
            "DELETE FROM menu_item_bar_stock_components WHERE menu_item_id = @menuId",
            connection))
        {
            clear.Parameters.AddWithValue("@menuId", menuItemId);
            await clear.ExecuteNonQueryAsync();
        }

        foreach (var row in components)
        {
            const string insert = @"
                INSERT INTO menu_item_bar_stock_components
                    (id, menu_item_id, bar_stock_item_id, sell_portion_ml, sort_order)
                VALUES
                    (@id, @menuId, @stockId, @ml, @sort)";
            await using var cmd = new MySqlCommand(insert, connection);
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@menuId", menuItemId);
            cmd.Parameters.AddWithValue("@stockId", row.BarStockItemId);
            cmd.Parameters.AddWithValue("@ml", row.SellPortionMl);
            cmd.Parameters.AddWithValue("@sort", row.SortOrder);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<MenuItemBarStockSetupDto?> GetMenuItemSetupAsync(string menuItemId, MySqlConnection? existing = null)
    {
        if (string.IsNullOrWhiteSpace(menuItemId))
        {
            return null;
        }

        if (existing is not null)
        {
            return await ReadMenuItemSetupAsync(existing, menuItemId);
        }

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        return await ReadMenuItemSetupAsync(connection, menuItemId);
    }

    private async Task<MenuItemBarStockSetupDto?> ReadMenuItemSetupAsync(MySqlConnection connection, string menuItemId)
    {
        const string sql = @"
            SELECT Id, track_bar_inventory, bar_stock_item_id, sell_portion_qty, sell_portion_unit
            FROM FoodMenuItems
            WHERE Id = @id
            LIMIT 1";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", menuItemId.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var setup = new MenuItemBarStockSetupDto
        {
            MenuItemId = reader.GetString("Id"),
            TrackBarInventory = ReadBool(reader, "track_bar_inventory"),
            BarStockItemId = reader.IsDBNull(reader.GetOrdinal("bar_stock_item_id"))
                ? null
                : reader.GetString("bar_stock_item_id"),
            SellPortionQty = reader.IsDBNull(reader.GetOrdinal("sell_portion_qty"))
                ? null
                : reader.GetDecimal("sell_portion_qty"),
            SellPortionUnit = reader.IsDBNull(reader.GetOrdinal("sell_portion_unit"))
                ? null
                : reader.GetString("sell_portion_unit")
        };
        await reader.CloseAsync();

        var components = await ListComponentsAsync(connection, menuItemId.Trim());
        if (components.Count == 0 &&
            !string.IsNullOrWhiteSpace(setup.BarStockItemId) &&
            setup.SellPortionQty is > 0)
        {
            // Migrate legacy single link into components table on read/save path.
            var stock = await GetByIdAsync(setup.BarStockItemId);
            var ml = stock is null
                ? setup.SellPortionQty.Value
                : ConvertLegacyPortionToMl(setup.SellPortionQty, setup.SellPortionUnit, stock);
            components =
            [
                new MenuItemBarStockComponentDto
                {
                    BarStockItemId = setup.BarStockItemId!,
                    Sku = stock?.Sku,
                    StockName = stock?.Name,
                    SellPortionMl = ml,
                    SortOrder = 0
                }
            ];
            await ReplaceComponentsAsync(connection, menuItemId.Trim(), components);
            // Normalize legacy columns to ml.
            const string normalize = @"
                UPDATE FoodMenuItems
                SET sell_portion_qty = @ml, sell_portion_unit = 'ml'
                WHERE Id = @id";
            await using var norm = new MySqlCommand(normalize, connection);
            norm.Parameters.AddWithValue("@ml", ml);
            norm.Parameters.AddWithValue("@id", menuItemId.Trim());
            await norm.ExecuteNonQueryAsync();
            setup.SellPortionQty = ml;
            setup.SellPortionUnit = BarStockUnits.Ml;
        }

        setup.Components = components;
        if (components.Count > 0)
        {
            setup.BarStockItemId = components[0].BarStockItemId;
            setup.SellPortionQty = components[0].SellPortionMl;
            setup.SellPortionUnit = BarStockUnits.Ml;
        }

        return setup;
    }

    private static async Task<List<MenuItemBarStockComponentDto>> ListComponentsAsync(
        MySqlConnection connection,
        string menuItemId)
    {
        const string sql = @"
            SELECT c.bar_stock_item_id, c.sell_portion_ml, c.sort_order, s.sku, s.name
            FROM menu_item_bar_stock_components c
            LEFT JOIN bar_stock_items s ON s.id = c.bar_stock_item_id
            WHERE c.menu_item_id = @menuId
            ORDER BY c.sort_order ASC, c.id ASC";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@menuId", menuItemId);
        var rows = new List<MenuItemBarStockComponentDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new MenuItemBarStockComponentDto
            {
                BarStockItemId = reader.GetString("bar_stock_item_id"),
                SellPortionMl = reader.GetDecimal("sell_portion_ml"),
                SortOrder = reader.GetInt32("sort_order"),
                Sku = reader.IsDBNull(reader.GetOrdinal("sku")) ? null : reader.GetString("sku"),
                StockName = reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString("name")
            });
        }

        return rows;
    }

    private static async Task EnsureSchemaCoreAsync(MySqlConnection connection)
    {
        const string stockTable = @"
            CREATE TABLE IF NOT EXISTS bar_stock_items (
                id VARCHAR(36) NOT NULL PRIMARY KEY,
                name VARCHAR(150) NOT NULL,
                sku VARCHAR(50) NULL,
                stock_unit VARCHAR(20) NOT NULL DEFAULT 'bottle',
                pack_size DECIMAL(12,3) NOT NULL DEFAULT 1.000,
                par_level DECIMAL(12,3) NOT NULL DEFAULT 0.000,
                on_hand DECIMAL(12,3) NOT NULL DEFAULT 0.000,
                is_active TINYINT(1) NOT NULL DEFAULT 1,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_bar_stock_name (name),
                INDEX idx_bar_stock_sku (sku),
                INDEX idx_bar_stock_active (is_active)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        await using (var create = new MySqlCommand(stockTable, connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using (var alterSection = new MySqlCommand(
            "ALTER TABLE bar_stock_items ADD COLUMN IF NOT EXISTS bar_section VARCHAR(30) NOT NULL DEFAULT 'other'",
            connection))
        {
            await alterSection.ExecuteNonQueryAsync();
        }

        await using (var alterLow = new MySqlCommand(
            "ALTER TABLE bar_stock_items ADD COLUMN IF NOT EXISTS low_level DECIMAL(12,3) NOT NULL DEFAULT 0.000",
            connection))
        {
            await alterLow.ExecuteNonQueryAsync();
        }

        await using (var alterMax = new MySqlCommand(
            "ALTER TABLE bar_stock_items ADD COLUMN IF NOT EXISTS max_level DECIMAL(12,3) NOT NULL DEFAULT 0.000",
            connection))
        {
            await alterMax.ExecuteNonQueryAsync();
        }

        // Migrate legacy single par → Max + Low (~30% of max, min 1).
        await using (var migrateLevels = new MySqlCommand(
            @"UPDATE bar_stock_items
              SET max_level = par_level,
                  low_level = CASE
                      WHEN par_level <= 0 THEN 0
                      WHEN par_level < 3 THEN 1
                      ELSE GREATEST(1, FLOOR(par_level / 3))
                  END
              WHERE (max_level IS NULL OR max_level = 0)
                AND par_level > 0",
            connection))
        {
            await migrateLevels.ExecuteNonQueryAsync();
        }

        try
        {
            await using var sectionIndex = new MySqlCommand(
                "ALTER TABLE bar_stock_items ADD INDEX IF NOT EXISTS idx_bar_stock_section (bar_section)",
                connection);
            await sectionIndex.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockService] Section index ensure skipped: {ex.Message}");
        }

        const string movementsTable = @"
            CREATE TABLE IF NOT EXISTS bar_stock_movements (
                id VARCHAR(36) NOT NULL PRIMARY KEY,
                stock_item_id VARCHAR(36) NOT NULL,
                movement_type VARCHAR(20) NOT NULL,
                qty_delta DECIMAL(12,3) NOT NULL,
                qty_before DECIMAL(12,3) NOT NULL,
                qty_after DECIMAL(12,3) NOT NULL,
                reason VARCHAR(255) NULL,
                staff_user_id VARCHAR(36) NULL,
                staff_display_name VARCHAR(120) NULL,
                terminal_id VARCHAR(80) NULL,
                terminal_label VARCHAR(120) NULL,
                source VARCHAR(20) NOT NULL DEFAULT 'mother',
                idempotency_key VARCHAR(80) NOT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY uq_bar_stock_mov_idem (idempotency_key),
                INDEX idx_bar_stock_mov_stock (stock_item_id, created_at),
                INDEX idx_bar_stock_mov_type (movement_type, created_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        await using (var createMovements = new MySqlCommand(movementsTable, connection))
        {
            await createMovements.ExecuteNonQueryAsync();
        }

        await using (var alterSaleCols = new MySqlCommand(
            @"ALTER TABLE bar_stock_movements
              ADD COLUMN IF NOT EXISTS order_id VARCHAR(64) NULL,
              ADD COLUMN IF NOT EXISTS order_line_key VARCHAR(80) NULL,
              ADD COLUMN IF NOT EXISTS overdraw TINYINT(1) NOT NULL DEFAULT 0",
            connection))
        {
            await alterSaleCols.ExecuteNonQueryAsync();
        }

        try
        {
            await using var saleIndex = new MySqlCommand(
                "ALTER TABLE bar_stock_movements ADD INDEX IF NOT EXISTS idx_bar_stock_mov_order (order_id, order_line_key)",
                connection);
            await saleIndex.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockService] Sale order index ensure skipped: {ex.Message}");
        }

        const string menuColumns = @"
            ALTER TABLE FoodMenuItems
            ADD COLUMN IF NOT EXISTS track_bar_inventory TINYINT(1) NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS bar_stock_item_id VARCHAR(36) NULL,
            ADD COLUMN IF NOT EXISTS sell_portion_qty DECIMAL(12,3) NULL,
            ADD COLUMN IF NOT EXISTS sell_portion_unit VARCHAR(20) NULL";

        await using (var alter = new MySqlCommand(menuColumns, connection))
        {
            await alter.ExecuteNonQueryAsync();
        }

        try
        {
            const string indexSql = @"
                ALTER TABLE FoodMenuItems
                ADD INDEX IF NOT EXISTS idx_foodmenu_track_bar (track_bar_inventory),
                ADD INDEX IF NOT EXISTS idx_foodmenu_bar_stock (bar_stock_item_id)";
            await using var indexCommand = new MySqlCommand(indexSql, connection);
            await indexCommand.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BarStockService] Index ensure skipped: {ex.Message}");
        }

        const string componentsTable = @"
            CREATE TABLE IF NOT EXISTS menu_item_bar_stock_components (
                id VARCHAR(36) NOT NULL PRIMARY KEY,
                menu_item_id VARCHAR(36) NOT NULL,
                bar_stock_item_id VARCHAR(36) NOT NULL,
                sell_portion_ml DECIMAL(12,3) NOT NULL,
                sort_order INT NOT NULL DEFAULT 0,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                INDEX idx_mibsc_menu (menu_item_id),
                INDEX idx_mibsc_stock (bar_stock_item_id),
                UNIQUE KEY uq_mibsc_menu_stock (menu_item_id, bar_stock_item_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        await using (var createComponents = new MySqlCommand(componentsTable, connection))
        {
            await createComponents.ExecuteNonQueryAsync();
        }
    }

    private static BarStockItemDto ReadStock(MySqlDataReader reader)
    {
        var sectionOrdinal = reader.GetOrdinal("bar_section");
        var (low, max) = ReadLevels(reader);
        return new()
        {
            Id = reader.GetString("id"),
            Name = reader.GetString("name"),
            Sku = reader.IsDBNull(reader.GetOrdinal("sku")) ? null : reader.GetString("sku"),
            Section = BarStockSections.Normalize(reader.IsDBNull(sectionOrdinal) ? null : reader.GetString(sectionOrdinal)),
            StockUnit = BarStockUnits.Normalize(reader.GetString("stock_unit")),
            PackSize = reader.GetDecimal("pack_size"),
            LowLevel = low,
            MaxLevel = max,
            ParLevel = max,
            OnHand = reader.GetDecimal("on_hand"),
            IsActive = ReadBool(reader, "is_active"),
            CreatedAtUtc = ToUtc(reader.GetDateTime("created_at")),
            UpdatedAtUtc = ToUtc(reader.GetDateTime("updated_at"))
        };
    }

    private static bool ReadBool(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            bool b => b,
            sbyte sb => sb != 0,
            byte by => by != 0,
            short s => s != 0,
            int i => i != 0,
            long l => l != 0,
            _ => Convert.ToBoolean(value)
        };
    }

    private static DateTimeOffset ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : new DateTimeOffset(value.ToUniversalTime());

    /// <summary>Suggested Order PDF — items at/under Low with qty to reach Max.</summary>
    public async Task<string> ExportSuggestedOrderPdfAsync(
        string? section = null,
        string businessName = "POS-in-NET")
    {
        var board = await ListTrackedBoardAsync();
        var filter = BarStockSections.NormalizeFilter(section);
        var rows = board
            .Where(r =>
                string.Equals(r.Status, "Low", StringComparison.OrdinalIgnoreCase) &&
                r.SuggestOrderQty > 0m &&
                (filter == BarStockSections.All ||
                 string.Equals(BarStockSections.Normalize(r.Section), filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => BarStockSections.DisplayName(r.Section), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var folder = Path.Combine(FileSystem.Current.AppDataDirectory, "BarInventory");
        Directory.CreateDirectory(folder);
        var sectionLabel = filter == BarStockSections.All
            ? "All"
            : BarStockSections.DisplayName(filter);
        var fileName = $"SuggestedOrder_{sectionLabel.Replace(' ', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Path.Combine(folder, fileName);

        using var document = new Syncfusion.Pdf.PdfDocument();
        document.PageSettings.Size = Syncfusion.Pdf.PdfPageSize.A4;
        var page = document.Pages.Add();
        var graphics = page.Graphics;
        var titleFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 18, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold);
        var headingFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 12, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold);
        var normalFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 11);
        var metaFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 9);

        float y = 24f;
        graphics.DrawString(
            businessName,
            titleFont,
            Syncfusion.Pdf.Graphics.PdfBrushes.Black,
            new Syncfusion.Drawing.PointF(20, y));
        y += 26;
        graphics.DrawString(
            "Suggested Order",
            headingFont,
            Syncfusion.Pdf.Graphics.PdfBrushes.Black,
            new Syncfusion.Drawing.PointF(20, y));
        y += 18;
        graphics.DrawString(
            $"Section: {sectionLabel}  ·  Generated: {DateTime.Now:dd MMM yyyy HH:mm}",
            metaFont,
            Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray,
            new Syncfusion.Drawing.PointF(20, y));
        y += 14;
        graphics.DrawString(
            "Order when on-hand ≤ Low · qty = Max − on-hand",
            metaFont,
            Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray,
            new Syncfusion.Drawing.PointF(20, y));
        y += 22;

        if (rows.Count == 0)
        {
            graphics.DrawString(
                "Nothing to order in this section.",
                normalFont,
                Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                new Syncfusion.Drawing.PointF(20, y));
        }
        else
        {
            graphics.DrawString("Item", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(20, y));
            graphics.DrawString("SKU", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(200, y));
            graphics.DrawString("Now", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(300, y));
            graphics.DrawString("Order", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(380, y));
            graphics.DrawString("Max", metaFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray, new Syncfusion.Drawing.PointF(470, y));
            y += 14;

            foreach (var row in rows)
            {
                if (y > 760)
                {
                    page = document.Pages.Add();
                    graphics = page.Graphics;
                    y = 24f;
                }

                graphics.DrawString(
                    Truncate(row.Name, 28),
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(20, y));
                graphics.DrawString(
                    Truncate(row.Sku ?? "—", 14),
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(200, y));
                graphics.DrawString(
                    $"{row.OnHand:0.###} {row.StockUnit}",
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(300, y));
                graphics.DrawString(
                    row.SuggestOrderDisplay,
                    headingFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(380, y));
                graphics.DrawString(
                    $"{row.MaxLevel:0.###}",
                    normalFont,
                    Syncfusion.Pdf.Graphics.PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(470, y));
                y += 16;
            }

            y += 10;
            graphics.DrawString(
                $"{rows.Count} item(s) to order",
                metaFont,
                Syncfusion.Pdf.Graphics.PdfBrushes.DarkSlateGray,
                new Syncfusion.Drawing.PointF(20, y));
        }

        await using (var stream = File.Create(filePath))
        {
            document.Save(stream);
        }

        return filePath;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
