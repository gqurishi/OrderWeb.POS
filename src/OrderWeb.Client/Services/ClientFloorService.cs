using OrderWeb.Client.Models;
using OrderWeb.Contracts.Floors;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using Microsoft.Maui.Networking;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client floor-plan host over SQLite cache. Marks cached data stale while offline.
/// </summary>
public sealed class ClientFloorService : IFloorPlanService
{
    private readonly ClientCacheService _cache;
    private int? _selectedFloorId;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public ClientFloorService(ClientCacheService? cache = null)
    {
        _cache = cache ?? new ClientCacheService();
    }

    public async Task<OperationResult<FloorPlanDto>> GetFloorPlanAsync(
        int? selectedFloorId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.InitializeAsync();
            var floors = await _cache.GetFloorsWithTablesAsync();
            var status = await _cache.GetStatusAsync();
            var (online, stale, sync) = BuildSyncStatus(status);

            if (floors.Count == 0)
            {
                return OperationResult<FloorPlanDto>.Ok(new FloorPlanDto(
                    null,
                    null,
                    Array.Empty<FloorTabDto>(),
                    Array.Empty<FloorTableDto>(),
                    sync,
                    online ? FloorPlanCapabilities.ClientOnline : FloorPlanCapabilities.ClientOffline,
                    StatusBanner: "No floor layout cached. Sync from Mother POS."));
            }

            var floorId = selectedFloorId ?? _selectedFloorId ?? floors[0].Id;
            var selected = floors.FirstOrDefault(f => f.Id == floorId) ?? floors[0];
            _selectedFloorId = selected.Id;

            var tabs = floors
                .Select(f => new FloorTabDto(f.Id, f.Name, f.Tables.Count, f.Id == selected.Id))
                .ToList();
            var tables = selected.Tables.Select(MapTable).ToList();
            var capabilities = online
                ? FloorPlanCapabilities.ClientOnline
                : FloorPlanCapabilities.ClientOffline;

            string? banner = null;
            if (!online)
            {
                banner = "Showing cached floor layout. Data may be stale while Mother is unreachable.";
            }
            else if (stale)
            {
                banner = "Floor layout may be stale. Last sync is older than expected.";
            }

            return OperationResult<FloorPlanDto>.Ok(new FloorPlanDto(
                selected.Id,
                selected.BackgroundImagePath,
                tabs,
                tables,
                sync,
                capabilities with { ShowStaleWarning = !online || stale },
                StatusBanner: banner));
        }
        catch (Exception ex)
        {
            return OperationResult<FloorPlanDto>.Fail(
                OperationError.Failure("Could not load cached floor plan.", ex.Message));
        }
    }

    public Task<OperationResult<FloorPlanDto>> SelectFloorAsync(
        int floorId,
        CancellationToken cancellationToken = default)
    {
        _selectedFloorId = floorId;
        return GetFloorPlanAsync(floorId, cancellationToken);
    }

    private static (bool Online, bool Stale, FloorSyncStatusDto Sync) BuildSyncStatus(CacheStatus status)
    {
        var online = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        DateTimeOffset? lastSync = null;
        foreach (var candidate in new[] { status.LastSyncTime, status.LastBootstrapTime, status.LastFullRefreshTime })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && DateTimeOffset.TryParse(candidate, out var parsed))
            {
                lastSync = parsed.ToUniversalTime();
                break;
            }
        }

        var stale = !online || lastSync is null || DateTimeOffset.UtcNow - lastSync > StaleAfter;
        var text = !online
            ? "Offline — cached layout"
            : lastSync is null
                ? "Not synced yet"
                : stale
                    ? $"Possibly stale — last sync {lastSync.Value.ToLocalTime():HH:mm:ss}"
                    : $"Synced {lastSync.Value.ToLocalTime():HH:mm:ss}";

        return (online, stale, new FloorSyncStatusDto(online, stale || !online, lastSync, text));
    }

    private static FloorTableDto MapTable(CachedTable table)
    {
        var status = table.Status ?? string.Empty;
        var session = table.SessionStatus ?? string.Empty;
        var hasOrder = !string.IsNullOrWhiteSpace(table.CurrentOrderId);
        var occupied = hasOrder
            || status.Equals("Occupied", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Busy", StringComparison.OrdinalIgnoreCase);
        var reserved = status.Equals("Reserved", StringComparison.OrdinalIgnoreCase);
        var problem = reserved
            || session.Equals("Payment", StringComparison.OrdinalIgnoreCase)
            || session.Equals("Cleaning", StringComparison.OrdinalIgnoreCase);
        var visual = FloorTableVisualStyles.ResolveState(occupied, reserved, problem);
        var shape = string.Equals(table.Shape, "Rectangle", StringComparison.OrdinalIgnoreCase)
            ? FloorTableShapeKind.Rectangle
            : FloorTableShapeKind.Square;

        string? summary = table.CurrentTotal > 0
            ? table.CurrentTotal.ToString("C")
            : table.CurrentOrderId;

        return new FloorTableDto(
            table.Id,
            table.FloorId,
            table.TableNumber,
            table.Seats,
            shape,
            visual,
            string.IsNullOrWhiteSpace(table.DesignIcon) ? "table_1.png" : table.DesignIcon!,
            table.PositionX,
            table.PositionY,
            table.Covers,
            summary,
            string.IsNullOrWhiteSpace(session) ? status : session,
            table.CurrentOrderId,
            null,
            true,
            occupied && Connectivity.Current.NetworkAccess == NetworkAccess.Internet,
            occupied && Connectivity.Current.NetworkAccess == NetworkAccess.Internet);
    }
}
