using POS_in_NET.Models;
using POS_in_NET.Pages;

namespace POS_in_NET.Services;

/// <summary>
/// Background warm-up after login/dashboard so Visual Table / Order Place open
/// without a fullscreen preloader when cache is ready.
/// </summary>
public static class PosStartupPrefetch
{
    private static int _warmInFlight;

    public static void WarmOperationalCaches()
    {
        if (Interlocked.CompareExchange(ref _warmInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = WarmAsync();
    }

    private static async Task WarmAsync()
    {
        try
        {
            await OrderPlacementPageSimple.WarmMenuCacheAsync().ConfigureAwait(false);

            var floorService = ServiceHelper.GetService<FloorService>() ?? new FloorService();
            var tableService = ServiceHelper.GetService<RestaurantTableService>() ?? new RestaurantTableService();
            var sessionService = ServiceHelper.GetService<TableSessionService>();

            var floors = await floorService.GetAllFloorsAsync().ConfigureAwait(false);
            if (floors.Count == 0)
            {
                return;
            }

            PosLayoutCache.SetFloors(floors);

            // Warm every floor's tables so floor-tab switches stay silent too.
            foreach (var floor in floors)
            {
                try
                {
                    List<RestaurantTable> tables;
                    if (sessionService != null)
                    {
                        tables = await sessionService.GetTablesWithSessionsAsync(floor.Id).ConfigureAwait(false);
                        if (tables.Count == 0)
                        {
                            tables = await tableService.GetTablesByFloorAsync(floor.Id).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        tables = await tableService.GetTablesByFloorAsync(floor.Id).ConfigureAwait(false);
                    }

                    PosLayoutCache.SetTables(floor.Id, tables);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Prefetch] Floor {floor.Id} table warm failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Prefetch] WarmOperationalCaches failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _warmInFlight, 0);
        }
    }
}
