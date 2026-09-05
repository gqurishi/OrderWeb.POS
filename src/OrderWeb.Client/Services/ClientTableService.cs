using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using Microsoft.Maui.Networking;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client table operations for the shared floor plan (SQLite + Mother API).
/// </summary>
public sealed class ClientTableService : IFloorTableService
{
    private readonly ClientCacheService _cache;

    public ClientTableService(ClientCacheService? cache = null)
    {
        _cache = cache ?? new ClientCacheService();
    }

    public Task<OperationResult> SaveTablePositionAsync(
        int tableId,
        int positionX,
        int positionY,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Fail(
            OperationError.Forbidden("Table layout can only be edited on Mother POS.")));

    public async Task<OperationResult> OpenTableAsync(
        int tableId,
        int coverCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.InitializeAsync();
            var floors = await _cache.GetFloorsWithTablesAsync();
            var table = floors.SelectMany(f => f.Tables).FirstOrDefault(t => t.Id == tableId);
            if (table is null)
            {
                return OperationResult.Fail(OperationError.NotFound("Table was not found in the local cache."));
            }

            await _cache.UpdateTableCoversAsync(tableId, Math.Max(coverCount, 1));
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.Failure("Could not open table.", ex.Message));
        }
    }

    public Task<OperationResult> MoveTableSessionAsync(
        int sourceTableId,
        int targetTableId,
        CancellationToken cancellationToken = default)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return Task.FromResult(OperationResult.Fail(
                OperationError.Failure("Move table requires an online Mother connection.")));
        }

        return Task.FromResult(OperationResult.Fail(
            OperationError.Failure("Move table is not available on this Client build yet. Use Mother POS.")));
    }

    public Task<OperationResult> MergeTableSessionsAsync(
        int parentTableId,
        int childTableId,
        CancellationToken cancellationToken = default)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return Task.FromResult(OperationResult.Fail(
                OperationError.Failure("Merge tables requires an online Mother connection.")));
        }

        return Task.FromResult(OperationResult.Fail(
            OperationError.Failure("Merge tables is not available on this Client build yet. Use Mother POS.")));
    }
}
