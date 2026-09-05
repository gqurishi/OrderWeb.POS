namespace OrderWeb.Contracts.Floors;

/// <summary>
/// Presentation models for the shared Mother-style floor/table plan.
/// Hosts map domain services into these DTOs; SharedUI never branches on host type.
/// </summary>
public enum FloorTableVisualState
{
    Free,
    Occupied,
    Reserved,
    Problem
}

public enum FloorTableShapeKind
{
    Square,
    Rectangle
}

public enum FloorTableActionKind
{
    Open,
    Move,
    Merge,
    SelectCovers
}

public sealed record FloorTabDto(
    int Id,
    string Name,
    int TableCount,
    bool IsSelected);

public sealed record FloorTableDto(
    int Id,
    int FloorId,
    string TableNumber,
    int Capacity,
    FloorTableShapeKind Shape,
    FloorTableVisualState VisualState,
    string DesignIcon,
    int PositionX,
    int PositionY,
    int GuestCount,
    string? OrderSummary,
    string? SessionStatus,
    string? CurrentOrderId,
    int? SessionId,
    bool CanOpen,
    bool CanMove,
    bool CanMerge);

public sealed record FloorSyncStatusDto(
    bool IsOnline,
    bool IsStale,
    DateTimeOffset? LastSyncedAtUtc,
    string DisplayText);

public sealed record FloorPlanCapabilities(
    bool AllowLayoutEdit,
    bool AllowBackgroundEdit,
    bool AllowOpen,
    bool AllowMove,
    bool AllowMerge,
    bool ShowAdminTools,
    bool ShowStaleWarning)
{
    public static FloorPlanCapabilities MotherAdmin { get; } = new(
        AllowLayoutEdit: true,
        AllowBackgroundEdit: true,
        AllowOpen: true,
        AllowMove: true,
        AllowMerge: true,
        ShowAdminTools: true,
        ShowStaleWarning: false);

    public static FloorPlanCapabilities MotherStaff { get; } = new(
        AllowLayoutEdit: false,
        AllowBackgroundEdit: false,
        AllowOpen: true,
        AllowMove: true,
        AllowMerge: true,
        ShowAdminTools: false,
        ShowStaleWarning: false);

    public static FloorPlanCapabilities ClientOnline { get; } = new(
        AllowLayoutEdit: false,
        AllowBackgroundEdit: false,
        AllowOpen: true,
        AllowMove: true,
        AllowMerge: true,
        ShowAdminTools: false,
        ShowStaleWarning: false);

    public static FloorPlanCapabilities ClientOffline { get; } = new(
        AllowLayoutEdit: false,
        AllowBackgroundEdit: false,
        AllowOpen: true,
        AllowMove: false,
        AllowMerge: false,
        ShowAdminTools: false,
        ShowStaleWarning: true);
}

public sealed record FloorPlanDto(
    int? SelectedFloorId,
    string? BackgroundImagePath,
    IReadOnlyList<FloorTabDto> Floors,
    IReadOnlyList<FloorTableDto> Tables,
    FloorSyncStatusDto SyncStatus,
    FloorPlanCapabilities Capabilities,
    bool IsLoading = false,
    string? LoadingMessage = null,
    string? StatusBanner = null,
    string StatusBannerTone = "warning");

public sealed record FloorTableMovedEvent(
    int TableId,
    int PositionX,
    int PositionY);

public sealed record FloorTableActionRequest(
    FloorTableDto Table,
    FloorTableActionKind Action,
    int? CoverCount = null,
    int? TargetTableId = null);

public static class FloorTableVisualStyles
{
    public const double TileSize = 120;
    public const double RectangleWidth = 148;
    public const double RectangleHeight = 100;
    public const int SnapGrid = 20;

    public static (string Background, string Border, string Text) ColorsFor(FloorTableVisualState state) =>
        state switch
        {
            FloorTableVisualState.Problem => ("#FEE2E2", "#EF4444", "#991B1B"),
            FloorTableVisualState.Occupied => ("#FEF3C7", "#F59E0B", "#92400E"),
            FloorTableVisualState.Reserved => ("#FEE2E2", "#EF4444", "#991B1B"),
            _ => ("#D1FAE5", "#10B981", "#065F46")
        };

    public static (double Width, double Height) SizeFor(FloorTableShapeKind shape) =>
        shape == FloorTableShapeKind.Rectangle
            ? (RectangleWidth, RectangleHeight)
            : (TileSize, TileSize);

    public static FloorTableVisualState ResolveState(
        bool hasActiveSession,
        bool isReserved,
        bool isProblem) =>
        isProblem || isReserved
            ? FloorTableVisualState.Problem
            : hasActiveSession
                ? FloorTableVisualState.Occupied
                : FloorTableVisualState.Free;
}
