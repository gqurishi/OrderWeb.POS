using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>
/// Single sync path for login / Update All / Restaurant / Live Order / reconnect.
/// Pairing only stores credentials; this is the real menu + layout + open-orders hydrate.
/// Concurrent callers share one in-flight pull so overlapping refreshes cannot tear the cache.
/// </summary>
public sealed class MotherOperationalSyncClient
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ClientCacheService _cache;
    private readonly MotherMenuClient _menuClient;
    private readonly MotherLayoutClient _layoutClient;
    private readonly MotherOrderClient _orderClient;

    public MotherOperationalSyncClient(ClientCacheService cache)
    {
        _cache = cache;
        _menuClient = new MotherMenuClient(cache);
        _layoutClient = new MotherLayoutClient(cache);
        _orderClient = new MotherOrderClient(cache);
    }

    public MotherOperationalSyncResult? LastResult { get; private set; }

    public async Task<MotherOperationalSyncResult> PullAllAsync(
        IProgress<MotherSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            progress?.Report(new MotherSyncProgress("menu", 0.05, "Pulling menu from Mother…"));
            var menu = await PullMenuAsync(cancellationToken);
            progress?.Report(new MotherSyncProgress(
                "menu",
                0.35,
                menu.Ok
                    ? $"Menu OK — {menu.Categories} categories, {menu.Products} products"
                    : $"Menu failed — {menu.Error ?? "kept last good cache"}"));

            progress?.Report(new MotherSyncProgress("layout", 0.40, "Pulling floors and tables…"));
            var layout = await PullLayoutAsync(cancellationToken);
            progress?.Report(new MotherSyncProgress(
                "layout",
                0.70,
                layout.Ok
                    ? $"Tables OK — {layout.Tables} tables on {layout.Floors} floor(s)"
                    : $"Tables failed — {layout.Error ?? "kept last good layout"}"));

            progress?.Report(new MotherSyncProgress("orders", 0.75, "Pulling open orders…"));
            var orders = await PullOpenOrdersAsync(cancellationToken);
            progress?.Report(new MotherSyncProgress(
                "orders",
                1.0,
                orders.Ok
                    ? $"Orders OK — {orders.Count} open"
                    : $"Orders failed — {orders.Error ?? "unknown"}"));

            var result = new MotherOperationalSyncResult(
                menu.Ok,
                menu.Categories,
                menu.Products,
                menu.Error,
                layout.Ok,
                layout.Floors,
                layout.Tables,
                layout.Error,
                orders.Ok,
                orders.Count,
                orders.Error);
            LastResult = result;
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<(bool Ok, int Categories, int Products, string? Error)> PullMenuAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var (snapshot, error) = await _menuClient.TryGetMenuAsync(cancellationToken);
            if (snapshot is null)
            {
                var message = error ?? "Mother POS did not return the menu.";
                await _cache.RecordOperationalSectionAsync("menu", false, message);
                return (false, 0, 0, message);
            }

            var replace = await _cache.ReplaceMenuAsync(snapshot);
            if (!replace.Applied)
            {
                var status = await _cache.GetStatusAsync();
                var message = replace.Message ?? "Menu replace skipped — kept last good cache.";
                await _cache.RecordOperationalSectionAsync("menu", false, message);
                return (false, status.Categories, status.Products, message);
            }

            var after = await _cache.GetStatusAsync();
            if (after.Categories <= 0)
            {
                const string emptyMessage = "Mother menu pull returned no categories — order place has nothing to show.";
                await _cache.RecordOperationalSectionAsync("menu", false, emptyMessage);
                return (false, 0, 0, emptyMessage);
            }

            await _cache.RecordOperationalSectionAsync(
                "menu",
                true,
                replace.Message ?? $"{after.Categories} categories, {after.Products} products");
            return (true, after.Categories, after.Products, null);
        }
        catch (Exception ex)
        {
            await _cache.RecordOperationalSectionAsync("menu", false, ex.Message);
            return (false, 0, 0, ex.Message);
        }
    }

    private async Task<(bool Ok, int Floors, int Tables, string? Error)> PullLayoutAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var (layout, error) = await _layoutClient.TryGetLayoutAsync(cancellationToken);
            if (layout is null)
            {
                var message = error ?? "Mother POS did not return floors/tables.";
                await _cache.RecordOperationalSectionAsync("layout", false, message);
                return (false, 0, 0, message);
            }

            var replace = await _cache.ReplaceLayoutAsync(
                new FloorSnapshotDto(layout.Version, layout.Floors),
                new TableSnapshotDto(layout.Version, layout.Tables));
            if (!replace.Applied)
            {
                var status = await _cache.GetStatusAsync();
                var message = replace.Message ?? "Layout replace skipped — kept last good cache.";
                await _cache.RecordOperationalSectionAsync("layout", false, message);
                return (false, 0, status.Tables, message);
            }

            await _cache.RecordOperationalSectionAsync(
                "layout",
                true,
                replace.Message ?? $"{layout.Floors.Count} floors, {layout.Tables.Count} tables");
            var after = await _cache.GetStatusAsync();
            var floors = await _cache.GetFloorsWithTablesAsync();
            return (true, floors.Count, after.Tables, null);
        }
        catch (Exception ex)
        {
            await _cache.RecordOperationalSectionAsync("layout", false, ex.Message);
            return (false, 0, 0, ex.Message);
        }
    }

    private async Task<(bool Ok, int Count, string? Error)> PullOpenOrdersAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var (orders, error) = await _orderClient.TryGetOpenOrdersAsync(cancellationToken: cancellationToken);
            if (orders is null)
            {
                var message = error ?? "Mother POS did not return open orders.";
                await _cache.RecordOperationalSectionAsync("orders", false, message);
                return (false, 0, message);
            }

            await _cache.ReplaceOperationalOrdersAsync(orders);
            await _cache.RecordOperationalSectionAsync("orders", true, $"{orders.Count} open orders");
            return (true, orders.Count, null);
        }
        catch (Exception ex)
        {
            await _cache.RecordOperationalSectionAsync("orders", false, ex.Message);
            return (false, 0, ex.Message);
        }
    }
}

public sealed record MotherOperationalSyncResult(
    bool MenuOk,
    int MenuCategories,
    int MenuProducts,
    string? MenuError,
    bool LayoutOk,
    int Floors,
    int Tables,
    string? LayoutError,
    bool OrdersOk,
    int OpenOrders,
    string? OrdersError)
{
    public bool AllOk => MenuOk && LayoutOk && OrdersOk;

    public bool AnyOk => MenuOk || LayoutOk || OrdersOk;

    /// <summary>Menu + tables must succeed (or already be cached) before POS is considered ready.</summary>
    public bool RequiredOk => MenuOk && LayoutOk;

    public IReadOnlyList<string> SectionLines()
    {
        return
        [
            MenuOk
                ? $"Menu: OK ({MenuCategories} categories, {MenuProducts} products)"
                : $"Menu: FAILED — {MenuError ?? "unknown"}",
            LayoutOk
                ? $"Tables: OK ({Tables} tables, {Floors} floors)"
                : $"Tables: FAILED — {LayoutError ?? "unknown"}",
            OrdersOk
                ? $"Orders: OK ({OpenOrders} open)"
                : $"Orders: FAILED — {OrdersError ?? "unknown"}"
        ];
    }

    public string SummaryMessage()
    {
        if (AllOk)
        {
            return $"Synced from Mother — menu {MenuCategories}/{MenuProducts}, tables {Tables} on {Floors} floor(s), open orders {OpenOrders}.";
        }

        return string.Join(" · ", SectionLines());
    }
}

public sealed record MotherSyncProgress(string Section, double Progress, string Message);
