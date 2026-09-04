using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

public sealed class MotherOrderClient
{
    private int _commandCount;

    public async Task<MotherCommandResult> OpenOrCreateTableOrderAsync(CachedTable table, int covers, LoginSession? session)
    {
        await Task.Delay(180);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var orderId = string.IsNullOrWhiteSpace(table.CurrentOrderId)
            ? $"T{table.TableNumber}-{DateTimeOffset.UtcNow:HHmmss}"
            : table.CurrentOrderId;

        var state = new MotherOrderState(
            orderId,
            string.IsNullOrWhiteSpace(table.CurrentOrderId) ? $"Table {table.TableNumber}" : table.CurrentOrderId,
            "Table",
            table.Id,
            table.TableNumber,
            Math.Max(covers, table.Covers > 0 ? table.Covers : 1),
            "open",
            Array.Empty<MotherOrderLine>(),
            0m,
            0m,
            0m,
            table.Version + 1,
            now,
            session?.UserName ?? "Client User");

        return new MotherCommandResult(state, false, string.IsNullOrWhiteSpace(table.CurrentOrderId) ? "Order created by Mother POS." : "Existing order opened from Mother POS.");
    }

    public async Task<MotherCommandResult> CreateCustomerOrderAsync(CustomerOrderDraft draft, LoginSession? session)
    {
        await Task.Delay(180);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var prefix = draft.OrderType.Equals("Delivery", StringComparison.OrdinalIgnoreCase) ? "DEL" : "COL";
        var deliveryLine = draft.DeliveryFee > 0
            ? new[] { new MotherOrderLine(Guid.NewGuid().ToString("N"), null, "Delivery Fee", 1, draft.DeliveryFee, draft.DeliveryZone, Array.Empty<string>()) }
            : Array.Empty<MotherOrderLine>();
        var subtotal = deliveryLine.Sum(line => line.UnitPrice * line.Quantity);
        var tax = Math.Round(subtotal * 0.2m, 2);

        var state = new MotherOrderState(
            $"{prefix}-{DateTimeOffset.UtcNow:HHmmss}",
            $"{prefix} {DateTime.Now:HH:mm}",
            draft.OrderType,
            null,
            null,
            0,
            "open",
            deliveryLine,
            subtotal,
            tax,
            subtotal + tax,
            1,
            now,
            session?.UserName ?? "Client User",
            $"{draft.Customer.Name} · {draft.Customer.Phone} · {draft.PickupTime ?? draft.ScheduledTime ?? "ASAP"}");

        return new MotherCommandResult(state, false, $"{draft.OrderType} order created by Mother POS.");
    }

    public async Task<MotherCommandResult> AddItemAsync(MotherOrderState state, CachedProduct product, IReadOnlyList<string> modifiers)
    {
        await Task.Delay(140);
        var lines = state.Lines.ToList();
        lines.Add(new MotherOrderLine(Guid.NewGuid().ToString("N"), product.Id, product.Name, 1, product.Price + ModifierTotal(product, modifiers), null, modifiers));
        return ResultWithTotals(state, lines, "Item added by Mother POS.");
    }

    public async Task<MotherCommandResult> UpdateQuantityAsync(MotherOrderState state, MotherOrderLine line, int quantity)
    {
        await Task.Delay(120);
        var lines = state.Lines
            .Select(existing => existing.Id == line.Id ? existing with { Quantity = Math.Max(quantity, 1) } : existing)
            .ToList();
        return ResultWithTotals(state, lines, "Quantity updated by Mother POS.");
    }

    public async Task<MotherCommandResult> RemoveItemAsync(MotherOrderState state, MotherOrderLine line)
    {
        await Task.Delay(120);
        var lines = state.Lines.Where(existing => existing.Id != line.Id).ToList();
        return ResultWithTotals(state, lines, "Item removed by Mother POS.");
    }

    public async Task<MotherCommandResult> AddNoteAsync(MotherOrderState state, MotherOrderLine line, string note)
    {
        await Task.Delay(120);
        var lines = state.Lines
            .Select(existing => existing.Id == line.Id ? existing with { Notes = note } : existing)
            .ToList();
        return ResultWithTotals(state, lines, "Note saved by Mother POS.");
    }

    public async Task<MotherCommandResult> SendToKitchenAsync(MotherOrderState state)
    {
        await Task.Delay(220);
        var updated = state with
        {
            Status = "sent_to_kitchen",
            Version = state.Version + 1,
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("O")
        };

        return new MotherCommandResult(updated, false, "Mother queued kitchen print/send request.");
    }

    public async Task<MotherCommandResult> RefreshLatestAsync(MotherOrderState state)
    {
        await Task.Delay(160);
        var conflictLine = new MotherOrderLine(Guid.NewGuid().ToString("N"), null, "Mother Sync Notice", 1, 0m, "Another terminal updated this order.", Array.Empty<string>());
        var refreshed = ResultWithTotals(state, state.Lines.Append(conflictLine).ToList(), "Order refreshed with latest Mother state.");
        return refreshed with { ConflictDetected = true };
    }

    private MotherCommandResult ResultWithTotals(MotherOrderState state, IReadOnlyList<MotherOrderLine> lines, string message)
    {
        _commandCount++;
        var subtotal = lines.Sum(line => line.Quantity * line.UnitPrice);
        var tax = Math.Round(subtotal * 0.2m, 2);
        var conflict = _commandCount % 9 == 0;
        var updated = state with
        {
            Lines = lines,
            Subtotal = subtotal,
            Tax = tax,
            Total = subtotal + tax,
            Version = state.Version + 1,
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("O"),
            ConflictMessage = conflict ? "Another Client changed this order. Showing latest Mother state." : null
        };

        return new MotherCommandResult(updated, conflict, conflict ? "Conflict detected. Mother returned latest order." : message);
    }

    private static decimal ModifierTotal(CachedProduct product, IReadOnlyList<string> selectedModifiers)
    {
        return product.ModifierGroups
            .SelectMany(group => group.Modifiers)
            .Where(modifier => selectedModifiers.Contains(modifier.Name))
            .Sum(modifier => modifier.PriceDelta);
    }
}
