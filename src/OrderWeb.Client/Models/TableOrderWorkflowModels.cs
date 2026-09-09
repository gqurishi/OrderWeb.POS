namespace OrderWeb.Client.Models;

public sealed record CachedFloor(
    int Id,
    string Name,
    int SortOrder,
    IReadOnlyList<CachedTable> Tables,
    string? BackgroundImageId = null);

public sealed record CachedTable(
    int Id,
    int FloorId,
    string TableNumber,
    int Seats,
    string Status,
    decimal CurrentTotal,
    string? CurrentOrderId,
    int Covers,
    string? ServerName,
    string? SessionStatus,
    int MinutesOccupied,
    int Version,
    int PositionX = 0,
    int PositionY = 0,
    string? DesignIcon = null);

public sealed record CachedMenuCategory(int Id, string Name, string Color, int SortOrder);

public sealed record CachedProduct(int Id, int CategoryId, string Name, decimal Price, string Currency, IReadOnlyList<CachedModifierGroup> ModifierGroups, string? MotherId = null);

public sealed record CachedModifierGroup(int Id, string Name, int MinSelect, int MaxSelect, IReadOnlyList<CachedModifier> Modifiers);

public sealed record CachedModifier(int Id, string Name, decimal PriceDelta);

public sealed record MotherOrderState(
    string OrderId,
    string OrderNumber,
    string OrderType,
    int? TableId,
    string? TableNumber,
    int Guests,
    string Status,
    IReadOnlyList<MotherOrderLine> Lines,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    int Version,
    string UpdatedUtc,
    string? ServerName = null,
    string? ConflictMessage = null,
    string? CustomerName = null,
    string? CustomerPhone = null);

public sealed record MotherOrderLine(
    string Id,
    int? ProductId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Notes,
    IReadOnlyList<string> Modifiers,
    string? ProductMotherId = null,
    string? VariantId = null,
    string? VariantName = null,
    decimal? VariantPrice = null,
    string? MealDealId = null,
    IReadOnlyList<string>? MealDealChoices = null,
    string? TastingMenuId = null);

public sealed record MotherCommandResult(MotherOrderState State, bool ConflictDetected, string Message);
