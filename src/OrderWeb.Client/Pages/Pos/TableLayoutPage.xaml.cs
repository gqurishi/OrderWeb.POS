using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Floors;

namespace OrderWeb.Client.Pages.Pos;

public partial class TableLayoutPage : ContentPage
{
    private readonly ClientFloorService _floorService = new();
    private readonly ClientTableService _tableService = new();
    private readonly ClientCacheService _cache = new();
    private readonly IDispatcherTimer _clockTimer;
    private bool _busy;

    public TableLayoutPage()
    {
        InitializeComponent();
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        _ = ReloadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer.Stop();
    }

    private async Task ReloadAsync(int? floorId = null)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        FloorPlan.SetLoading(true, "Loading tables...");
        try
        {
            var result = floorId is null
                ? await _floorService.GetFloorPlanAsync()
                : await _floorService.SelectFloorAsync(floorId.Value);

            if (!result.IsSuccess || result.Value is null)
            {
                FloorPlan.SetStatusBanner(result.Error?.Message ?? "Could not load floor plan.", "error");
                return;
            }

            FloorPlan.Apply(result.Value);
        }
        finally
        {
            FloorPlan.SetLoading(false);
            _busy = false;
        }
    }

    private async void OnFloorSelected(object? sender, int floorId) =>
        await ReloadAsync(floorId);

    private async void OnTableActionRequested(object? sender, FloorTableActionRequest request)
    {
        switch (request.Action)
        {
            case FloorTableActionKind.SelectCovers:
                await OpenTableAsync(request.Table, request.CoverCount ?? 1);
                break;
            case FloorTableActionKind.Open:
                await OpenTableAsync(request.Table, Math.Max(request.Table.GuestCount, 1));
                break;
            case FloorTableActionKind.Move:
            case FloorTableActionKind.Merge:
            {
                var result = request.Action == FloorTableActionKind.Move
                    ? await _tableService.MoveTableSessionAsync(request.Table.Id, request.TargetTableId ?? 0)
                    : await _tableService.MergeTableSessionsAsync(request.Table.Id, request.TargetTableId ?? 0);
                FloorPlan.SetStatusBanner(
                    result.IsSuccess ? "Done." : result.Error?.Message ?? "Action failed.",
                    result.IsSuccess ? "success" : "error");
                break;
            }
        }
    }

    private async Task OpenTableAsync(FloorTableDto table, int covers)
    {
        FloorPlan.SetLoading(true, $"Opening table {table.TableNumber}...");
        try
        {
            var open = await _tableService.OpenTableAsync(table.Id, Math.Max(covers, 1));
            if (!open.IsSuccess)
            {
                FloorPlan.SetStatusBanner(open.Error?.Message ?? "Could not open table.", "error");
                return;
            }

            await _cache.InitializeAsync();
            var floors = await _cache.GetFloorsWithTablesAsync();
            var cached = floors.SelectMany(f => f.Tables).FirstOrDefault(t => t.Id == table.Id);
            if (cached is null)
            {
                FloorPlan.SetStatusBanner("Table is not available in the local cache.", "error");
                return;
            }

            await Navigation.PushAsync(new SharedOrderEntryPage(cached, Math.Max(covers, 1)), false);
        }
        catch (Exception ex)
        {
            FloorPlan.SetStatusBanner(ex.Message, "error");
        }
        finally
        {
            FloorPlan.SetLoading(false);
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync(false);

    private void UpdateClock()
    {
        DateLabel.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        TimeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
    }
}
