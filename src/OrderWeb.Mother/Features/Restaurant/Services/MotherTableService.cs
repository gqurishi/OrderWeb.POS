using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace POS_in_NET.Services;

/// <summary>
/// Mother table operations for the shared floor plan (MariaDB).
/// </summary>
public sealed class MotherTableService : IFloorTableService
{
    private readonly RestaurantTableService _tableService;
    private readonly TableSessionService _sessionService;
    private readonly AuthenticationService _authService;

    public MotherTableService(
        RestaurantTableService? tableService = null,
        TableSessionService? sessionService = null,
        AuthenticationService? authService = null)
    {
        _tableService = tableService ?? new RestaurantTableService();
        _sessionService = sessionService ?? new TableSessionService();
        _authService = authService ?? AuthenticationService.Instance;
    }

    public async Task<OperationResult> SaveTablePositionAsync(
        int tableId,
        int positionX,
        int positionY,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ok = await _tableService.UpdateTablePositionAsync(tableId, positionX, positionY);
            return ok
                ? OperationResult.Ok()
                : OperationResult.Fail(OperationError.Failure("Could not save table position."));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.Failure("Could not save table position.", ex.Message));
        }
    }

    public async Task<OperationResult> OpenTableAsync(
        int tableId,
        int coverCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _sessionService.OpenTableWithSessionAsync(tableId, Math.Max(coverCount, 1));
            return result.success
                ? OperationResult.Ok()
                : OperationResult.Fail(OperationError.Failure(result.message));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.Failure("Could not open table.", ex.Message));
        }
    }

    public async Task<OperationResult> MoveTableSessionAsync(
        int sourceTableId,
        int targetTableId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _sessionService.GetActiveSessionByTableIdAsync(sourceTableId);
            if (session is null)
            {
                return OperationResult.Fail(OperationError.NotFound("Source table has no active session."));
            }

            var actor = _authService.CurrentUser?.Name ?? "system";
            var result = await _sessionService.TransferSessionAsync(
                session.Id,
                targetTableId,
                actor,
                $"Moved from table {sourceTableId} to {targetTableId}");
            return result.success
                ? OperationResult.Ok()
                : OperationResult.Fail(OperationError.Failure(result.message));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.Failure("Could not move table session.", ex.Message));
        }
    }

    public async Task<OperationResult> MergeTableSessionsAsync(
        int parentTableId,
        int childTableId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parent = await _sessionService.GetActiveSessionByTableIdAsync(parentTableId);
            var child = await _sessionService.GetActiveSessionByTableIdAsync(childTableId);
            if (parent is null || child is null)
            {
                return OperationResult.Fail(OperationError.NotFound("Both tables must have active sessions to merge."));
            }

            var actor = _authService.CurrentUser?.Name ?? "system";
            var result = await _sessionService.MergeSessionsAsync(
                parent.Id,
                child.Id,
                actor,
                $"Merged table {childTableId} into {parentTableId}");
            return result.success
                ? OperationResult.Ok()
                : OperationResult.Fail(OperationError.Failure(result.message));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.Failure("Could not merge table sessions.", ex.Message));
        }
    }
}
