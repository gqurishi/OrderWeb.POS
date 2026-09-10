namespace OrderWeb.Contracts.Dtos;

public sealed record MenuCategoryDto(string Id, string Name, int SortOrder, bool IsAvailable);

public sealed record MenuProductDto(
    string Id,
    string CategoryId,
    string Name,
    decimal Price,
    bool IsAvailable,
    string? ImageId = null,
    string? Colour = null);

public sealed record MenuSnapshotDto(
    string Version,
    IReadOnlyList<MenuCategoryDto> Categories,
    IReadOnlyList<MenuProductDto> Products);

public sealed record FloorDto(
    string Id,
    string Name,
    int SortOrder,
    string? BackgroundImageId = null,
    /// <summary>When set, floor tab uses this count instead of counting tables in the snapshot.</summary>
    int? TableCount = null);

public sealed record FloorSnapshotDto(string Version, IReadOnlyList<FloorDto> Floors);

public sealed record RestaurantTableDto(
    string Id,
    string FloorId,
    string Name,
    int Capacity,
    string Status,
    double X,
    double Y,
    string? OpenOrderId = null,
    long Revision = 0,
    int GuestCount = 0,
    decimal CurrentTotal = 0m,
    string? SessionStatus = null,
    string? Icon = null,
    /// <summary>Mother problem state (stale draft / reserved / cleaning) → red card.</summary>
    bool IsProblem = false,
    /// <summary>Mother active session → amber card when not a problem.</summary>
    bool HasActiveSession = false);

public sealed record TableSnapshotDto(string Version, IReadOnlyList<RestaurantTableDto> Tables);

public sealed record RestaurantLayoutSnapshotDto(
    string Version,
    IReadOnlyList<FloorDto> Floors,
    IReadOnlyList<RestaurantTableDto> Tables);
