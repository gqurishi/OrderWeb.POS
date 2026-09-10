using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// In-memory floors/tables snapshot warmed after login so Visual Tables
/// can paint without a fullscreen preloader.
/// </summary>
public static class PosLayoutCache
{
    private static readonly object Gate = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static List<Floor>? _floors;
    private static readonly Dictionary<int, List<RestaurantTable>> TablesByFloor = new();
    private static DateTime _updatedAtUtc = DateTime.MinValue;

    public static bool IsWarm
    {
        get
        {
            lock (Gate)
            {
                return _floors is { Count: > 0 }
                    && DateTime.UtcNow - _updatedAtUtc <= Ttl;
            }
        }
    }

            public static bool HasTables(int floorId)
    {
        lock (Gate)
        {
            return IsWarmUnlocked() && TablesByFloor.ContainsKey(floorId);
        }
    }

    public static IReadOnlyList<Floor>? TryGetFloors()
    {
        lock (Gate)
        {
            if (!IsWarmUnlocked())
            {
                return null;
            }

            return _floors!.ToList();
        }
    }

    public static IReadOnlyList<RestaurantTable>? TryGetTables(int floorId)
    {
        lock (Gate)
        {
            if (!IsWarmUnlocked() || !TablesByFloor.TryGetValue(floorId, out var tables))
            {
                return null;
            }

            return tables.ToList();
        }
    }

    public static void SetFloors(IEnumerable<Floor> floors)
    {
        lock (Gate)
        {
            _floors = floors.ToList();
            TouchUnlocked();
        }
    }

    public static void SetTables(int floorId, IEnumerable<RestaurantTable> tables)
    {
        lock (Gate)
        {
            TablesByFloor[floorId] = tables.ToList();
            TouchUnlocked();
        }
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            _floors = null;
            TablesByFloor.Clear();
            _updatedAtUtc = DateTime.MinValue;
        }
    }

    private static bool IsWarmUnlocked() =>
        _floors is { Count: > 0 }
        && DateTime.UtcNow - _updatedAtUtc <= Ttl;

    private static void TouchUnlocked() => _updatedAtUtc = DateTime.UtcNow;
}
