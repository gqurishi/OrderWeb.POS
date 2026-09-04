using System.Collections.Concurrent;

namespace POS_in_NET.Services;

public static class ActiveTableOrderCacheService
{
    private sealed class CacheEntry
    {
        public string OrderId { get; set; } = string.Empty;
        public int? TableSessionId { get; set; }
        public string? TableNumber { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsOpen { get; set; }
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> EntriesByOrderId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan EntryTtl = TimeSpan.FromHours(12);

    public static void Upsert(string orderId, int? tableSessionId, string? tableNumber, DateTime updatedAt, bool isOpen)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        if (!isOpen)
        {
            Remove(orderId, tableSessionId, tableNumber);
            return;
        }

        var normalizedTableNumber = string.IsNullOrWhiteSpace(tableNumber) ? null : tableNumber.Trim();
        var safeUpdatedAt = updatedAt == default ? DateTime.Now : updatedAt;

        EntriesByOrderId.AddOrUpdate(
            orderId.Trim(),
            _ => new CacheEntry
            {
                OrderId = orderId.Trim(),
                TableSessionId = tableSessionId,
                TableNumber = normalizedTableNumber,
                UpdatedAt = safeUpdatedAt,
                IsOpen = true
            },
            (_, existing) =>
            {
                existing.TableSessionId = tableSessionId ?? existing.TableSessionId;
                existing.TableNumber = normalizedTableNumber ?? existing.TableNumber;
                existing.UpdatedAt = safeUpdatedAt > existing.UpdatedAt ? safeUpdatedAt : existing.UpdatedAt;
                existing.IsOpen = true;
                return existing;
            });
    }

    public static bool TryGetOpenOrderBySessionId(int tableSessionId, out string? orderId)
    {
        PruneExpiredEntries();
        var found = EntriesByOrderId.Values
            .Where(entry => entry.IsOpen && entry.TableSessionId == tableSessionId)
            .OrderByDescending(entry => entry.UpdatedAt)
            .FirstOrDefault();

        orderId = found?.OrderId;
        return !string.IsNullOrWhiteSpace(orderId);
    }

    public static bool TryGetOpenOrderByTableNumber(string? tableNumber, out string? orderId)
    {
        PruneExpiredEntries();
        if (string.IsNullOrWhiteSpace(tableNumber))
        {
            orderId = null;
            return false;
        }

        var normalized = tableNumber.Trim();
        var found = EntriesByOrderId.Values
            .Where(entry => entry.IsOpen && string.Equals(entry.TableNumber, normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.UpdatedAt)
            .FirstOrDefault();

        orderId = found?.OrderId;
        return !string.IsNullOrWhiteSpace(orderId);
    }

    public static void Remove(string? orderId, int? tableSessionId = null, string? tableNumber = null)
    {
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            EntriesByOrderId.TryRemove(orderId.Trim(), out _);
        }

        var keysToRemove = EntriesByOrderId
            .Where(pair =>
                (tableSessionId.HasValue && pair.Value.TableSessionId == tableSessionId.Value) ||
                (!string.IsNullOrWhiteSpace(tableNumber) && string.Equals(pair.Value.TableNumber, tableNumber.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            EntriesByOrderId.TryRemove(key, out _);
        }
    }

    private static void PruneExpiredEntries()
    {
        var cutoff = DateTime.Now - EntryTtl;
        var expiredKeys = EntriesByOrderId
            .Where(pair => pair.Value.UpdatedAt < cutoff)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            EntriesByOrderId.TryRemove(key, out _);
        }
    }
}
