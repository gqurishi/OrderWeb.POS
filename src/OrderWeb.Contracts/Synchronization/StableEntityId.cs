using System.Text;

namespace OrderWeb.Contracts.Synchronization;

/// <summary>
/// Deterministic positive int ids from Mother string keys so Client menu/layout
/// FKs stay stable across refreshes (unlike sequential snapshot counters).
/// </summary>
public static class StableEntityId
{
    public static int FromKey(string? key, ISet<int> used)
    {
        var seed = string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();
        var id = HashPositive(seed);
        while (id <= 0 || !used.Add(id))
        {
            id = id >= int.MaxValue - 1 ? 1 : id + 1;
        }

        return id;
    }

    public static int HashPositive(string key)
    {
        // FNV-1a 32-bit, forced positive and non-zero.
        unchecked
        {
            uint hash = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(key.Trim().ToUpperInvariant()))
            {
                hash ^= b;
                hash *= 16777619;
            }

            var value = (int)(hash & 0x7FFFFFFF);
            return value == 0 ? 1 : value;
        }
    }
}
