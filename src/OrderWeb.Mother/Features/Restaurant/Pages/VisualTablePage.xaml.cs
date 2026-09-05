using OrderWeb.Contracts.Floors;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class VisualTablePage : ContentPage
{
    private readonly MotherFloorService _floorService = new();
    private readonly MotherTableService _tableService = new();
    private readonly TableSessionService _sessionService = new();
    private readonly AuthenticationService _authService = AuthenticationService.Instance;
    private readonly RoleAccessService _roleAccessService = new();
    private readonly OrderServiceAvailabilityService _orderAvailabilityService;
    private readonly Dictionary<int, (int X, int Y)> _pendingMoves = new();
    private bool _busy;

    public VisualTablePage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Restaurant Layout");
        _orderAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
            ?? new OrderServiceAvailabilityService(
                ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(),
                _authService);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var settings = await _orderAvailabilityService.GetAsync(forceRefresh: true);
        if (!settings.TableEnabled)
        {
            await AppAlertService.ShowAlertAsync(
                "Table Service Unavailable",
                "Table service is disabled by the Administrator.");
            await NavigationCoordinator.Shared.NavigateShellAsync(
                _roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync(int? floorId = null)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        FloorPlan.SetLoading(true, "Loading layout...");
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
            case FloorTableActionKind.Move when request.TargetTableId is int moveTarget:
            {
                var move = await _tableService.MoveTableSessionAsync(request.Table.Id, moveTarget);
                FloorPlan.SetStatusBanner(
                    move.IsSuccess ? "Table moved." : move.Error?.Message ?? "Move failed.",
                    move.IsSuccess ? "success" : "error");
                if (move.IsSuccess)
                {
                    await ReloadAsync(request.Table.FloorId);
                }

                break;
            }
            case FloorTableActionKind.Merge when request.TargetTableId is int mergeTarget:
            {
                var merge = await _tableService.MergeTableSessionsAsync(request.Table.Id, mergeTarget);
                FloorPlan.SetStatusBanner(
                    merge.IsSuccess ? "Tables merged." : merge.Error?.Message ?? "Merge failed.",
                    merge.IsSuccess ? "success" : "error");
                if (merge.IsSuccess)
                {
                    await ReloadAsync(request.Table.FloorId);
                }

                break;
            }
        }
    }

    private async Task OpenTableAsync(FloorTableDto table, int covers)
    {
        FloorPlan.SetLoading(true, $"Opening table {table.TableNumber}...");
        try
        {
            var sessionId = table.SessionId;
            var existingOrderId = table.CurrentOrderId;

            if (sessionId is null && string.IsNullOrWhiteSpace(existingOrderId))
            {
                var open = await _tableService.OpenTableAsync(table.Id, Math.Max(covers, 1));
                if (!open.IsSuccess)
                {
                    FloorPlan.SetStatusBanner(open.Error?.Message ?? "Could not open table.", "error");
                    return;
                }

                var session = await _sessionService.GetActiveSessionByTableIdAsync(table.Id);
                sessionId = session?.Id;
                existingOrderId = session?.CurrentOrderId ?? session?.LinkedOrderId;
                covers = session?.PartySize > 0 ? session.PartySize : covers;
            }

            var page = new OrderPlacementPageSimple(
                table.TableNumber,
                Math.Max(covers, 1),
                _authService.CurrentUser?.Name ?? "Current User",
                1,
                sessionId,
                existingOrderId);
            await Navigation.PushAsync(page);
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

    private void OnTableMoved(object? sender, FloorTableMovedEvent args)
    {
        _pendingMoves[args.TableId] = (args.PositionX, args.PositionY);
        FloorPlan.SetStatusBanner("You have unsaved layout changes.", "warning");
    }

    private async void OnSaveLayoutRequested(object? sender, EventArgs e)
    {
        if (_pendingMoves.Count == 0)
        {
            FloorPlan.SetStatusBanner("Layout already saved.", "info");
            return;
        }

        FloorPlan.SetLoading(true, "Saving layout...");
        var failures = 0;
        foreach (var (tableId, pos) in _pendingMoves.ToList())
        {
            var result = await _tableService.SaveTablePositionAsync(tableId, pos.X, pos.Y);
            if (result.IsSuccess)
            {
                _pendingMoves.Remove(tableId);
            }
            else
            {
                failures++;
            }
        }

        FloorPlan.SetLoading(false);
        FloorPlan.SetStatusBanner(
            failures == 0 ? "Layout saved." : $"{failures} table(s) failed to save.",
            failures == 0 ? "success" : "error");
        if (failures == 0)
        {
            await ReloadAsync(FloorPlan.CurrentState.SelectedFloorId);
        }
    }

    private async void OnFloorManagementRequested(object? sender, EventArgs e) =>
        await NavigationCoordinator.Shared.NavigateShellAsync("floor", source: sender as VisualElement);

    private async void OnTableManagementRequested(object? sender, EventArgs e) =>
        await NavigationCoordinator.Shared.NavigateShellAsync("table", source: sender as VisualElement);

    private async void OnSetBackgroundRequested(object? sender, EventArgs e)
    {
        var floorId = FloorPlan.CurrentState.SelectedFloorId;
        if (floorId is null)
        {
            return;
        }

        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select floor background",
                FileTypes = FilePickerFileType.Images
            });
            if (file is null)
            {
                return;
            }

            FloorPlan.SetLoading(true, "Saving background...");
            var result = await _floorService.SaveBackgroundAsync(floorId.Value, file);
            FloorPlan.SetStatusBanner(
                result.IsSuccess ? "Background updated." : result.Error?.Message ?? "Background update failed.",
                result.IsSuccess ? "success" : "error");
            if (result.IsSuccess)
            {
                await ReloadAsync(floorId);
            }
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

    private async void OnRemoveBackgroundRequested(object? sender, EventArgs e)
    {
        var floorId = FloorPlan.CurrentState.SelectedFloorId;
        if (floorId is null)
        {
            return;
        }

        FloorPlan.SetLoading(true, "Removing background...");
        var result = await _floorService.RemoveBackgroundAsync(floorId.Value);
        FloorPlan.SetLoading(false);
        FloorPlan.SetStatusBanner(
            result.IsSuccess ? "Background removed." : result.Error?.Message ?? "Could not remove background.",
            result.IsSuccess ? "success" : "error");
        if (result.IsSuccess)
        {
            await ReloadAsync(floorId);
        }
    }
}
