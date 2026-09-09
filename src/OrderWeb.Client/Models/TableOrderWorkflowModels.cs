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

public sealed record CachedMenuCategory(int Id, string Name, string Color, int SortOrder, int? ParentId = null, string? MotherId = null);

public sealed record CachedProduct(
    int Id,
    int CategoryId,
    string Name,
    decimal Price,
    string Currency,
    IReadOnlyList<CachedModifierGroup> ModifierGroups,
    string? MotherId = null,
    IReadOnlyList<CachedProductVariant>? Variants = null,
    IReadOnlyList<string>? QuickNotes = null)
{
    public IReadOnlyList<CachedProductVariant> ActiveVariants =>
        Variants ?? Array.Empty<CachedProductVariant>();

    public IReadOnlyList<string> ActiveQuickNotes =>
        QuickNotes ?? Array.Empty<string>();
}

public sealed record CachedProductVariant(
    string MotherId,
    string Name,
    string? Description,
    decimal TakeawayPrice,
    decimal DineInPrice,
    int SortOrder)
{
    public decimal PriceFor(bool takeaway) => takeaway ? TakeawayPrice : DineInPrice;
}

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
    string? CustomerPhone = null,
    string? Notes = null,
    decimal Discount = 0m,
    decimal ServiceCharge = 0m,
    string? ServiceChargeStatus = null,
    decimal ServiceChargePercent = 0m);

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
    string? TastingMenuId = null,
    bool IsSent = false,
    string? SendStatus = null);

public sealed record MotherCommandResult(MotherOrderState State, bool ConflictDetected, string Message);

public sealed record MotherPreviousOrder(
    int OrderDatabaseId,
    string? OrderNumber,
    string CreatedAtUtc,
    string OrderType,
    decimal TotalAmount,
    string Status,
    string ItemsText,
    string? OrderNotes,
    bool IsMostRecent);

public sealed record CachedMealDeal(
    int Id,
    string MotherId,
    string Name,
    string? Description,
    decimal Price,
    int PickCount,
    IReadOnlyList<string> Choices);

public sealed record CachedTastingMenu(
    int Id,
    string MotherId,
    string Name,
    string? Description,
    IReadOnlyList<CachedTastingMenuOption> Options,
    IReadOnlyList<CachedTastingMenuCourse> Courses);

public sealed record CachedTastingMenuOption(
    string MotherId,
    string Name,
    decimal Price,
    bool IncludesWine,
    int CourseCount,
    int SortOrder);

public sealed record CachedTastingMenuCourse(
    string MotherId,
    string Name,
    string? WineName,
    int CourseNumber);
