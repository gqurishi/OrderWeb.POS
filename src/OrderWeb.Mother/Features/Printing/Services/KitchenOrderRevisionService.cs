using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class KitchenOrderRevision
{
    public long DatabaseId { get; init; }
    public string RevisionId { get; init; } = string.Empty;
    public int RevisionNumber { get; init; }
    public string TicketType { get; init; } = "initial";
    public string? Reason { get; init; }
    public List<KitchenOrderRevisionLine> Lines { get; init; } = new();
}

public sealed class KitchenOrderRevisionLine
{
    public string LineId { get; init; } = string.Empty;
    public string ClientItemId { get; init; } = string.Empty;
    public KitchenChangeAction Action { get; init; }
    public int Quantity { get; init; }
    public int PreviousQuantity { get; init; }
    public int CurrentQuantity { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? MenuItemId { get; init; }
    public string? VariantId { get; init; }
    public string? PrintGroupId { get; init; }
    public string? Notes { get; init; }
    public string? PreviousNotes { get; init; }
    public string? Modifiers { get; init; }
    public string AddonsJson { get; init; } = "[]";
}

public sealed class KitchenOrderRevisionService
{
    private readonly DatabaseService _databaseService;

    public KitchenOrderRevisionService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task EnsureBaselineAsync(int orderDbId, TableOrder order)
    {
        var sentItems = order.Items
            .Where(item => item.Quantity > 0
                && item.SendStatus is not ItemSendStatus.NotSent
                && !IsTastingCourseChild(item))
            .ToList();
        if (sentItems.Count == 0)
        {
            return;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockOrderAsync(connection, transaction, orderDbId);
        foreach (var item in sentItems)
        {
            await InsertStateIfMissingAsync(connection, transaction, orderDbId, Snapshot(item));
        }
        await transaction.CommitAsync();
    }

    public Task<KitchenOrderRevision?> CreateRevisionAsync(
        int orderDbId,
        TableOrder order,
        string? createdBy) =>
        CreateRevisionInternalAsync(orderDbId, order, createdBy, fullVoid: false, reason: null);

    public Task<KitchenOrderRevision?> CreateFullVoidRevisionAsync(
        int orderDbId,
        TableOrder order,
        string? createdBy,
        string reason) =>
        CreateRevisionInternalAsync(orderDbId, order, createdBy, fullVoid: true, reason);

    public async Task<List<KitchenOrderRevision>> GetRetryableRevisionsAsync(int orderDbId)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        var revisions = new List<KitchenOrderRevision>();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT r.id, r.revision_id, r.revision_number, r.ticket_type, r.reason,
                   i.line_id, i.client_item_id, i.action_type, i.quantity,
                   i.previous_quantity, i.current_quantity, i.item_name, i.display_name,
                   i.menu_item_id, i.variant_id, i.print_group_id, i.notes, i.previous_notes,
                   i.modifiers, i.addons_json
            FROM order_kitchen_revisions r
            JOIN order_kitchen_revision_items i ON i.revision_id = r.id
            WHERE r.order_id = @orderId
              AND r.status IN ('queued', 'partial', 'failed')
              AND i.print_status <> 'printed'
            ORDER BY r.revision_number, i.id";
        command.Parameters.AddWithValue("@orderId", orderDbId);
        await using var reader = await command.ExecuteReaderAsync();
        KitchenOrderRevision? currentRevision = null;
        long currentRevisionId = 0;
        while (await reader.ReadAsync())
        {
            var revisionDbId = reader.GetInt64(0);
            if (currentRevision == null || revisionDbId != currentRevisionId)
            {
                currentRevisionId = revisionDbId;
                currentRevision = new KitchenOrderRevision
                {
                    DatabaseId = revisionDbId,
                    RevisionId = reader.GetString(1),
                    RevisionNumber = reader.GetInt32(2),
                    TicketType = reader.GetString(3),
                    Reason = reader.IsDBNull(4) ? null : reader.GetString(4)
                };
                revisions.Add(currentRevision);
            }

            currentRevision.Lines.Add(new KitchenOrderRevisionLine
            {
                LineId = reader.GetString(5),
                ClientItemId = reader.GetString(6),
                Action = ParseAction(reader.GetString(7)),
                Quantity = reader.GetInt32(8),
                PreviousQuantity = reader.GetInt32(9),
                CurrentQuantity = reader.GetInt32(10),
                ItemName = reader.GetString(11),
                DisplayName = reader.IsDBNull(12) ? null : reader.GetString(12),
                MenuItemId = reader.IsDBNull(13) ? null : reader.GetString(13),
                VariantId = reader.IsDBNull(14) ? null : reader.GetString(14),
                PrintGroupId = reader.IsDBNull(15) ? null : reader.GetString(15),
                Notes = reader.IsDBNull(16) ? null : reader.GetString(16),
                PreviousNotes = reader.IsDBNull(17) ? null : reader.GetString(17),
                Modifiers = reader.IsDBNull(18) ? null : reader.GetString(18),
                AddonsJson = reader.IsDBNull(19) ? "[]" : reader.GetString(19)
            });
        }
        return revisions;
    }

    public TableOrder BuildPrintOrder(TableOrder source, KitchenOrderRevision revision)
    {
        var printOrder = new TableOrder
        {
            Id = source.Id,
            OrderNumber = source.OrderNumber,
            TableNumber = source.TableNumber,
            StaffName = source.StaffName,
            StaffId = source.StaffId,
            CoverCount = source.CoverCount,
            StartTime = source.StartTime,
            CreatedAt = source.CreatedAt,
            UpdatedAt = DateTime.Now,
            Notes = source.Notes,
            OrderMode = source.OrderMode,
            Status = source.Status,
            KitchenRevisionNumber = revision.RevisionNumber,
            KitchenTicketType = revision.TicketType,
            KitchenRevisionReason = revision.Reason
        };

        foreach (var line in revision.Lines)
        {
            var addons = DeserializeAddons(line.AddonsJson);
            var sourceItem = source.Items.FirstOrDefault(item =>
                string.Equals(item.Id, line.ClientItemId, StringComparison.OrdinalIgnoreCase));
            printOrder.Items.Add(new TableOrderItem
            {
                Id = line.LineId,
                SourceItemId = line.ClientItemId,
                OrderId = source.Id,
                MenuItemId = line.MenuItemId ?? string.Empty,
                VariantId = line.VariantId,
                DisplayName = line.DisplayName,
                Name = line.ItemName,
                Quantity = line.Quantity,
                PreviousQuantity = line.PreviousQuantity,
                UnitPrice = 0m,
                PrintGroupId = line.PrintGroupId,
                PrintInRed = sourceItem?.PrintInRed ?? false,
                Notes = line.Notes,
                PreviousNotes = line.PreviousNotes,
                Modifiers = line.Modifiers,
                KitchenAction = line.Action,
                SendStatus = ItemSendStatus.NotSent,
                CreatedAt = DateTime.Now,
                SelectedAddons = new ObservableCollection<SelectedAddon>(addons)
            });
        }

        return printOrder;
    }

    public async Task MarkPrintResultAsync(KitchenOrderRevision revision, OrderRoutingPrintResult result)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var now = DateTime.Now;
        var printedCount = 0;

        foreach (var line in revision.Lines)
        {
            var printed = result.PrintedItemIds.Contains(line.LineId);
            if (printed)
            {
                printedCount++;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                UPDATE order_kitchen_revision_items
                SET print_status = @status,
                    failure_reason = @failure,
                    printed_at = @printedAt
                WHERE line_id = @lineId";
            command.Parameters.AddWithValue("@status", printed ? "printed" : "failed");
            command.Parameters.AddWithValue("@failure", printed
                ? DBNull.Value
                : result.FailedRoutes.FirstOrDefault() ?? "No printer accepted this revision line");
            command.Parameters.AddWithValue("@printedAt", printed ? now : DBNull.Value);
            command.Parameters.AddWithValue("@lineId", line.LineId);
            await command.ExecuteNonQueryAsync();
        }

        var status = printedCount == revision.Lines.Count
            ? "printed"
            : printedCount == 0 ? "failed" : "partial";
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = @"
                UPDATE order_kitchen_revisions
                SET status = @status,
                    completed_at = @completedAt,
                    failure_reason = @failure
                WHERE id = @id";
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@completedAt", status == "printed" ? now : DBNull.Value);
            command.Parameters.AddWithValue("@failure", status == "printed"
                ? DBNull.Value
                : result.FailedRoutes.FirstOrDefault() ?? "One or more revision lines failed");
            command.Parameters.AddWithValue("@id", revision.DatabaseId);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private async Task<KitchenOrderRevision?> CreateRevisionInternalAsync(
        int orderDbId,
        TableOrder order,
        string? createdBy,
        bool fullVoid,
        string? reason)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockOrderAsync(connection, transaction, orderDbId);
        var states = await LoadStatesForUpdateAsync(connection, transaction, orderDbId);
        var current = order.Items
            .Where(item => !item.IsVoided
                && item.Quantity > 0
                && !string.IsNullOrWhiteSpace(item.Id)
                && !IsTastingCourseChild(item)
                && (!fullVoid || item.SendStatus is not ItemSendStatus.NotSent))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Snapshot(group.First()), StringComparer.OrdinalIgnoreCase);

        var revisionNumber = await GetNextRevisionNumberAsync(connection, transaction, orderDbId);
        var lines = fullVoid
            ? BuildFullVoidLines(states, current)
            : BuildDeltaLines(states, current, revisionNumber);

        if (lines.Count == 0)
        {
            await transaction.RollbackAsync();
            return null;
        }

        var ticketType = ResolveTicketType(lines, revisionNumber, fullVoid);
        var revisionId = Guid.NewGuid().ToString();
        long revisionDbId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO order_kitchen_revisions
                    (revision_id, order_id, revision_number, ticket_type, status, created_by, reason)
                VALUES (@revisionId, @orderId, @revisionNumber, @ticketType, 'queued', @createdBy, @reason);
                SELECT LAST_INSERT_ID();";
            command.Parameters.AddWithValue("@revisionId", revisionId);
            command.Parameters.AddWithValue("@orderId", orderDbId);
            command.Parameters.AddWithValue("@revisionNumber", revisionNumber);
            command.Parameters.AddWithValue("@ticketType", ticketType);
            command.Parameters.AddWithValue("@createdBy", createdBy ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@reason", reason ?? (object)DBNull.Value);
            revisionDbId = Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        foreach (var line in lines)
        {
            await InsertRevisionLineAsync(connection, transaction, revisionDbId, line);
        }

        if (fullVoid)
        {
            foreach (var state in states.Values.Where(state => state.Quantity > 0))
            {
                state.Quantity = 0;
                await UpsertStateAsync(connection, transaction, orderDbId, state);
            }
        }
        else
        {
            foreach (var snapshot in current.Values)
            {
                await UpsertStateAsync(connection, transaction, orderDbId, snapshot);
            }

            foreach (var removed in states.Values.Where(state => state.Quantity > 0 && !current.ContainsKey(state.ClientItemId)))
            {
                removed.Quantity = 0;
                await UpsertStateAsync(connection, transaction, orderDbId, removed);
            }
        }

        await transaction.CommitAsync();
        return new KitchenOrderRevision
        {
            DatabaseId = revisionDbId,
            RevisionId = revisionId,
            RevisionNumber = revisionNumber,
            TicketType = ticketType,
            Reason = reason,
            Lines = lines
        };
    }

    private static bool IsTastingCourseChild(TableOrderItem item) =>
        item.MenuItemId.StartsWith("tasting-course:", StringComparison.OrdinalIgnoreCase);

    private static List<KitchenOrderRevisionLine> BuildDeltaLines(
        IReadOnlyDictionary<string, KitchenItemState> states,
        IReadOnlyDictionary<string, KitchenItemState> current,
        int revisionNumber)
    {
        var lines = new List<KitchenOrderRevisionLine>();
        foreach (var item in current.Values)
        {
            states.TryGetValue(item.ClientItemId, out var previous);
            if (previous == null || previous.Quantity == 0)
            {
                lines.Add(ToLine(item, revisionNumber == 1 ? KitchenChangeAction.New : KitchenChangeAction.Add, item.Quantity, previous?.Quantity ?? 0, item.Quantity, previous));
                continue;
            }

            if (HasKitchenContentChanged(previous, item))
            {
                // A preparation change is deliberately represented as two simple kitchen
                // instructions: cancel the old preparation, then make the updated one.
                lines.Add(ToLine(previous, KitchenChangeAction.Void, previous.Quantity, previous.Quantity, 0, previous));
                lines.Add(ToLine(item, KitchenChangeAction.Add, item.Quantity, 0, item.Quantity, previous));
                continue;
            }

            if (item.Quantity > previous.Quantity)
            {
                lines.Add(ToLine(item, KitchenChangeAction.Add, item.Quantity - previous.Quantity, previous.Quantity, item.Quantity, previous));
            }
            else if (item.Quantity < previous.Quantity)
            {
                lines.Add(ToLine(previous, KitchenChangeAction.Void, previous.Quantity - item.Quantity, previous.Quantity, item.Quantity, previous));
            }
        }

        foreach (var removed in states.Values.Where(state => state.Quantity > 0 && !current.ContainsKey(state.ClientItemId)))
        {
            lines.Add(ToLine(removed, KitchenChangeAction.Void, removed.Quantity, removed.Quantity, 0, removed));
        }

        return lines.Where(line => line.Quantity > 0).ToList();
    }

    private static bool HasKitchenContentChanged(KitchenItemState previous, KitchenItemState current)
    {
        // Print-group changes affect routing only. They must never make an already-sent
        // item appear as a new kitchen change when an order is reopened.
        // Database hydration can represent the same value as null, empty text, or via
        // the display-name fallback. Normalize those values before calculating a delta.
        return !KitchenTextEquals(GetKitchenItemName(previous), GetKitchenItemName(current))
            || !KitchenTextEquals(previous.MenuItemId, current.MenuItemId)
            || !KitchenTextEquals(previous.VariantId, current.VariantId)
            || !KitchenTextEquals(previous.Notes, current.Notes)
            || !KitchenTextEquals(previous.Modifiers, current.Modifiers)
            || !AddonsEqual(previous.AddonsJson, current.AddonsJson);
    }

    private static string GetKitchenItemName(KitchenItemState item) =>
        string.IsNullOrWhiteSpace(item.DisplayName) ? item.ItemName : item.DisplayName;

    private static bool KitchenTextEquals(string? previous, string? current) =>
        string.Equals(
            previous?.Trim() ?? string.Empty,
            current?.Trim() ?? string.Empty,
            StringComparison.Ordinal);

    private static bool AddonsEqual(string previousJson, string currentJson)
    {
        var previous = DeserializeAddons(previousJson)
            .OrderBy(addon => addon.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var current = DeserializeAddons(currentJson)
            .OrderBy(addon => addon.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return previous.Count == current.Count
            && previous.Zip(current).All(pair =>
                string.Equals(pair.First.Id?.Trim(), pair.Second.Id?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(pair.First.Name?.Trim(), pair.Second.Name?.Trim(), StringComparison.OrdinalIgnoreCase)
                && pair.First.Price == pair.Second.Price);
    }

    private static List<KitchenOrderRevisionLine> BuildFullVoidLines(
        IReadOnlyDictionary<string, KitchenItemState> states,
        IReadOnlyDictionary<string, KitchenItemState> current)
    {
        var source = states.Values.Any(state => state.Quantity > 0)
            ? states.Values.Where(state => state.Quantity > 0)
            : current.Values.Where(state => state.Quantity > 0);
        return source
            .Select(item => ToLine(item, KitchenChangeAction.Void, item.Quantity, item.Quantity, 0, item))
            .ToList();
    }

    private static KitchenOrderRevisionLine ToLine(
        KitchenItemState item,
        KitchenChangeAction action,
        int quantity,
        int previousQuantity,
        int currentQuantity,
        KitchenItemState? previous)
    {
        return new KitchenOrderRevisionLine
        {
            LineId = Guid.NewGuid().ToString(),
            ClientItemId = item.ClientItemId,
            Action = action,
            Quantity = quantity,
            PreviousQuantity = previousQuantity,
            CurrentQuantity = currentQuantity,
            ItemName = item.ItemName,
            DisplayName = item.DisplayName,
            MenuItemId = item.MenuItemId,
            VariantId = item.VariantId,
            PrintGroupId = item.PrintGroupId,
            Notes = item.Notes,
            PreviousNotes = previous?.Notes,
            Modifiers = item.Modifiers,
            AddonsJson = item.AddonsJson
        };
    }

    private static string ResolveTicketType(IReadOnlyCollection<KitchenOrderRevisionLine> lines, int revisionNumber, bool fullVoid)
    {
        if (fullVoid || lines.All(line => line.Action == KitchenChangeAction.Void)) return "void";
        if (revisionNumber == 1 && lines.All(line => line.Action == KitchenChangeAction.New)) return "initial";
        if (lines.All(line => line.Action is KitchenChangeAction.New or KitchenChangeAction.Add)) return "addition";
        return "mixed";
    }

    private static KitchenItemState Snapshot(TableOrderItem item)
    {
        var addonsJson = JsonSerializer.Serialize(item.SelectedAddons.Select(addon => new SelectedAddon
        {
            Id = addon.Id,
            Name = addon.Name,
            Price = addon.Price
        }));
        var state = new KitchenItemState
        {
            ClientItemId = item.Id,
            ItemName = item.Name,
            DisplayName = item.DisplayName,
            MenuItemId = item.MenuItemId,
            VariantId = item.VariantId,
            PrintGroupId = item.PrintGroupId,
            Quantity = item.Quantity,
            Notes = item.Notes,
            Modifiers = item.Modifiers,
            AddonsJson = addonsJson
        };
        state.Fingerprint = Fingerprint(state);
        return state;
    }

    private static string Fingerprint(KitchenItemState state)
    {
        var value = string.Join("\n",
            state.ItemName.Trim(),
            state.DisplayName?.Trim() ?? string.Empty,
            state.MenuItemId?.Trim() ?? string.Empty,
            state.VariantId?.Trim() ?? string.Empty,
            state.PrintGroupId?.Trim() ?? string.Empty,
            state.Notes?.Trim() ?? string.Empty,
            state.Modifiers?.Trim() ?? string.Empty,
            state.AddonsJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static List<SelectedAddon> DeserializeAddons(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<SelectedAddon>>(json) ?? new List<SelectedAddon>();
        }
        catch
        {
            return new List<SelectedAddon>();
        }
    }

    private static KitchenChangeAction ParseAction(string value) => value.ToLowerInvariant() switch
    {
        "add" => KitchenChangeAction.Add,
        "void" => KitchenChangeAction.Void,
        "change" => KitchenChangeAction.Change,
        _ => KitchenChangeAction.New
    };

    private static async Task<Dictionary<string, KitchenItemState>> LoadStatesForUpdateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int orderDbId)
    {
        var states = new Dictionary<string, KitchenItemState>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT client_item_id, item_name, display_name, menu_item_id, variant_id, print_group_id,
                   committed_quantity, notes, modifiers, addons_json, item_fingerprint
            FROM order_kitchen_item_state
            WHERE order_id = @orderId
            FOR UPDATE";
        command.Parameters.AddWithValue("@orderId", orderDbId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var state = new KitchenItemState
            {
                ClientItemId = reader.GetString(0),
                ItemName = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                MenuItemId = reader.IsDBNull(3) ? null : reader.GetString(3),
                VariantId = reader.IsDBNull(4) ? null : reader.GetString(4),
                PrintGroupId = reader.IsDBNull(5) ? null : reader.GetString(5),
                Quantity = reader.GetInt32(6),
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                Modifiers = reader.IsDBNull(8) ? null : reader.GetString(8),
                AddonsJson = reader.IsDBNull(9) ? "[]" : reader.GetString(9),
                Fingerprint = reader.GetString(10)
            };
            states[state.ClientItemId] = state;
        }
        return states;
    }

    private static async Task<int> GetNextRevisionNumberAsync(MySqlConnection connection, MySqlTransaction transaction, int orderDbId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(revision_number), 0) + 1 FROM order_kitchen_revisions WHERE order_id = @orderId";
        command.Parameters.AddWithValue("@orderId", orderDbId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task LockOrderAsync(MySqlConnection connection, MySqlTransaction transaction, int orderDbId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM orders WHERE id = @orderId FOR UPDATE";
        command.Parameters.AddWithValue("@orderId", orderDbId);
        if (await command.ExecuteScalarAsync() == null)
        {
            throw new InvalidOperationException($"Order {orderDbId} no longer exists.");
        }
    }

    private static async Task InsertRevisionLineAsync(MySqlConnection connection, MySqlTransaction transaction, long revisionDbId, KitchenOrderRevisionLine line)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO order_kitchen_revision_items
                (line_id, revision_id, client_item_id, action_type, quantity, previous_quantity, current_quantity,
                 item_name, display_name, menu_item_id, variant_id, print_group_id, notes, previous_notes, modifiers, addons_json)
            VALUES
                (@lineId, @revisionId, @clientItemId, @action, @quantity, @previousQuantity, @currentQuantity,
                 @itemName, @displayName, @menuItemId, @variantId, @printGroupId, @notes, @previousNotes, @modifiers, @addonsJson)";
        command.Parameters.AddWithValue("@lineId", line.LineId);
        command.Parameters.AddWithValue("@revisionId", revisionDbId);
        command.Parameters.AddWithValue("@clientItemId", line.ClientItemId);
        command.Parameters.AddWithValue("@action", line.Action.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("@quantity", line.Quantity);
        command.Parameters.AddWithValue("@previousQuantity", line.PreviousQuantity);
        command.Parameters.AddWithValue("@currentQuantity", line.CurrentQuantity);
        command.Parameters.AddWithValue("@itemName", line.ItemName);
        command.Parameters.AddWithValue("@displayName", line.DisplayName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@menuItemId", line.MenuItemId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@variantId", line.VariantId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@printGroupId", line.PrintGroupId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@notes", line.Notes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@previousNotes", line.PreviousNotes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@modifiers", line.Modifiers ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@addonsJson", line.AddonsJson);
        await command.ExecuteNonQueryAsync();
    }

    private static Task InsertStateIfMissingAsync(MySqlConnection connection, MySqlTransaction transaction, int orderDbId, KitchenItemState state) =>
        UpsertStateAsync(connection, transaction, orderDbId, state, insertOnly: true);

    private static async Task UpsertStateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int orderDbId,
        KitchenItemState state,
        bool insertOnly = false)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO order_kitchen_item_state
                (order_id, client_item_id, item_name, display_name, menu_item_id, variant_id, print_group_id,
                 committed_quantity, notes, modifiers, addons_json, item_fingerprint)
            VALUES
                (@orderId, @clientItemId, @itemName, @displayName, @menuItemId, @variantId, @printGroupId,
                 @quantity, @notes, @modifiers, @addonsJson, @fingerprint)" +
            (insertOnly ? " ON DUPLICATE KEY UPDATE client_item_id = client_item_id" : @"
            ON DUPLICATE KEY UPDATE
                item_name = VALUES(item_name), display_name = VALUES(display_name), menu_item_id = VALUES(menu_item_id),
                variant_id = VALUES(variant_id), print_group_id = VALUES(print_group_id),
                committed_quantity = VALUES(committed_quantity), notes = VALUES(notes), modifiers = VALUES(modifiers),
                addons_json = VALUES(addons_json), item_fingerprint = VALUES(item_fingerprint)");
        command.Parameters.AddWithValue("@orderId", orderDbId);
        command.Parameters.AddWithValue("@clientItemId", state.ClientItemId);
        command.Parameters.AddWithValue("@itemName", state.ItemName);
        command.Parameters.AddWithValue("@displayName", state.DisplayName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@menuItemId", state.MenuItemId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@variantId", state.VariantId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@printGroupId", state.PrintGroupId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@quantity", state.Quantity);
        command.Parameters.AddWithValue("@notes", state.Notes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@modifiers", state.Modifiers ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@addonsJson", state.AddonsJson);
        command.Parameters.AddWithValue("@fingerprint", state.Fingerprint);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class KitchenItemState
    {
        public string ClientItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? MenuItemId { get; set; }
        public string? VariantId { get; set; }
        public string? PrintGroupId { get; set; }
        public int Quantity { get; set; }
        public string? Notes { get; set; }
        public string? Modifiers { get; set; }
        public string AddonsJson { get; set; } = "[]";
        public string Fingerprint { get; set; } = string.Empty;
    }
}
