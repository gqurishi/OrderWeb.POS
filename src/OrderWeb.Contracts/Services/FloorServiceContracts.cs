using OrderWeb.Contracts.Floors;
using OrderWeb.Contracts.Results;

namespace OrderWeb.Contracts.Services;

/// <summary>
/// Host floor plan data access. Mother → MariaDB; Client → SQLite + Mother API.
/// </summary>
public interface IFloorPlanService
{
    Task<OperationResult<FloorPlanDto>> GetFloorPlanAsync(
        int? selectedFloorId = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<FloorPlanDto>> SelectFloorAsync(
        int floorId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Host table operations used by the shared floor plan.
/// </summary>
public interface IFloorTableService
{
    Task<OperationResult> SaveTablePositionAsync(
        int tableId,
        int positionX,
        int positionY,
        CancellationToken cancellationToken = default);

    Task<OperationResult> OpenTableAsync(
        int tableId,
        int coverCount,
        CancellationToken cancellationToken = default);

    Task<OperationResult> MoveTableSessionAsync(
        int sourceTableId,
        int targetTableId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> MergeTableSessionsAsync(
        int parentTableId,
        int childTableId,
        CancellationToken cancellationToken = default);
}
