using POS_in_NET.Pages;

namespace POS_in_NET.Services;

/// <summary>
/// Background warm-up after login/dashboard so Visual Table / Order Place open quickly.
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
            var floors = await floorService.GetAllFloorsAsync().ConfigureAwait(false);
            if (floors.Count > 0)
            {
                await tableService.GetTablesByFloorAsync(floors[0].Id).ConfigureAwait(false);
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
