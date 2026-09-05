using OrderWeb.Contracts.Floors;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother floor-plan host over FloorService / RestaurantTableService / TableSessionService (MariaDB).
/// </summary>
public sealed class MotherFloorService : IFloorPlanService
{
    private readonly FloorService _floorService;
    private readonly RestaurantTableService _tableService;
    private readonly TableSessionService _sessionService;
    private readonly RoleAccessService _roleAccessService;
    private readonly AuthenticationService _authService;
    private int? _selectedFloorId;

    public MotherFloorService(
        FloorService? floorService = null,
        RestaurantTableService? tableService = null,
        TableSessionService? sessionService = null,
        RoleAccessService? roleAccessService = null,
        AuthenticationService? authService = null)
    {
        _floorService = floorService ?? new FloorService();
        _tableService = tableService ?? new RestaurantTableService();
        _sessionService = sessionService ?? new TableSessionService();
        _roleAccessService = roleAccessService ?? new RoleAccessService();
        _authService = authService ?? AuthenticationService.Instance;
    }

    public async Task<OperationResult<FloorPlanDto>> GetFloorPlanAsync(
        int? selectedFloorId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var floors = await _floorService.GetAllFloorsAsync();
            if (floors.Count == 0)
            {
                return OperationResult<FloorPlanDto>.Ok(new FloorPlanDto(
                    null,
                    null,
                    Array.Empty<FloorTabDto>(),
                    Array.Empty<FloorTableDto>(),
                    LiveSyncStatus(),
                    ResolveCapabilities(),
                    StatusBanner: "No floors found. Add floors in Floor Management."));
            }

            var floorId = selectedFloorId ?? _selectedFloorId ?? floors[0].Id;
            var selected = floors.FirstOrDefault(f => f.Id == floorId) ?? floors[0];
            _selectedFloorId = selected.Id;

            var backgroundPath = await _floorService.ResolveFloorBackgroundImageAsync(selected);
            var tables = await _sessionService.GetTablesWithSessionsAsync(selected.Id);
            if (tables.Count == 0)
            {
                tables = await _tableService.GetTablesByFloorAsync(selected.Id);
            }

            var tabs = floors
                .Select(f => new FloorTabDto(f.Id, f.Name, f.TableCount, f.Id == selected.Id))
                .ToList();

            return OperationResult<FloorPlanDto>.Ok(new FloorPlanDto(
                selected.Id,
                backgroundPath,
                tabs,
                tables.Select(MapTable).ToList(),
                LiveSyncStatus(),
                ResolveCapabilities()));
        }
        catch (Exception ex)
        {
            return OperationResult<FloorPlanDto>.Fail(
                OperationError.Failure("Could not load floor plan.", ex.Message));
        }
    }

    public Task<OperationResult<FloorPlanDto>> SelectFloorAsync(
        int floorId,
        CancellationToken cancellationToken = default)
    {
        _selectedFloorId = floorId;
        return GetFloorPlanAsync(floorId, cancellationToken);
    }

    public async Task<OperationResult> SaveBackgroundAsync(int floorId, FileResult file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var path = await _floorService.SaveFloorBackgroundImageAsync(
                floorId,
                file.FileName ?? "floor-background.jpg",
                file.ContentType ?? "image/jpeg",
                memory.ToArray());
            if (string.IsNullOrWhiteSpace(path))
            {
                return OperationResult.Fail(OperationError.Failure("Could not save floor background."));
            }

            var updated = await _floorService.UpdateFloorBackgroundAsync(floorId, path);
            return updated
                ? OperationResult.Ok()
                : OperationResult.Fail(OperationError.Failure("Could not update floor background."));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.Failure("Could not save floor background.", ex.Message));
        }
    }

    public async Task<OperationResult> RemoveBackgroundAsync(int floorId)
    {
        try
        {
            var ok = await _floorService.RemoveFloorBackgroundImageAsync(floorId);
            return ok
                ? OperationResult.Ok()
                : OperationResult.Fail(OperationError.Failure("Could not remove floor background."));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.Failure("Could not remove floor background.", ex.Message));
        }
    }

    private FloorPlanCapabilities ResolveCapabilities()
    {
        var isAdmin = _roleAccessService.IsAdmin(_authService.CurrentUser?.Role);
        return isAdmin ? FloorPlanCapabilities.MotherAdmin : FloorPlanCapabilities.MotherStaff;
    }

    private static FloorSyncStatusDto LiveSyncStatus() =>
        new(true, false, DateTimeOffset.UtcNow, $"Synced {DateTime.Now:HH:mm:ss}");

    private static FloorTableDto MapTable(RestaurantTable table)
    {
        var session = table.CurrentSession;
        var hasSession = session != null;
        var isReserved = table.Status == TableStatus.Reserved;
        var isProblem = session?.LinkedOrderIsStaleDraft == true
            || session?.Status == TableSessionStatus.Cleaning
            || isReserved;
        var visual = FloorTableVisualStyles.ResolveState(hasSession, isReserved, isProblem);
        var shape = table.Shape == TableShape.Rectangle
            ? FloorTableShapeKind.Rectangle
            : FloorTableShapeKind.Square;

        string? orderSummary = null;
        if (!string.IsNullOrWhiteSpace(session?.LinkedOrderNumber))
        {
            orderSummary = session!.LinkedOrderNumber;
        }
        else if (!string.IsNullOrWhiteSpace(session?.CurrentOrderId))
        {
            orderSummary = session!.CurrentOrderId;
        }
        else if (session?.LinkedOrderTotalAmount is decimal amount && amount > 0)
        {
            orderSummary = amount.ToString("C");
        }

        return new FloorTableDto(
            table.Id,
            table.FloorId,
            table.TableNumber,
            table.Capacity,
            shape,
            visual,
            string.IsNullOrWhiteSpace(table.TableDesignIcon) ? "table_1.png" : table.TableDesignIcon,
            table.PositionX,
            table.PositionY,
            session?.PartySize ?? 0,
            orderSummary,
            session?.StatusDisplay,
            session?.CurrentOrderId ?? session?.LinkedOrderId,
            session?.Id,
            true,
            hasSession,
            hasSession);
    }
}
