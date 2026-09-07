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

public sealed record FloorDto(string Id, string Name, int SortOrder, string? BackgroundImageId = null);

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
    string? Icon = null);

public sealed record TableSnapshotDto(string Version, IReadOnlyList<RestaurantTableDto> Tables);

public sealed record RestaurantLayoutSnapshotDto(
    string Version,
    IReadOnlyList<FloorDto> Floors,
    IReadOnlyList<RestaurantTableDto> Tables);
