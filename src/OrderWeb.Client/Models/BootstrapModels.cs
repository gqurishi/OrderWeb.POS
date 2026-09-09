namespace OrderWeb.Client.Models;

public sealed record BootstrapPayload(
    BootstrapRestaurantInfo Restaurant,
    BootstrapTerminalConfig Terminal,
    IReadOnlyList<BootstrapCategory> Categories,
    IReadOnlyList<BootstrapProduct> Products,
    IReadOnlyList<BootstrapPrice> Prices,
    IReadOnlyList<BootstrapModifierGroup> ModifierGroups,
    IReadOnlyList<BootstrapModifier> Modifiers,
    IReadOnlyList<BootstrapProductModifier> ProductModifiers,
    IReadOnlyList<BootstrapTaxRate> TaxRates,
    IReadOnlyList<BootstrapFloor> Floors,
    IReadOnlyList<BootstrapTable> Tables,
    IReadOnlyList<BootstrapOpenOrder> OpenOrders,
    IReadOnlyList<BootstrapOrderItem> OrderItems,
    IReadOnlyList<BootstrapOnlineOrder> OnlineOrders,
    IReadOnlyList<BootstrapReservation> Reservations,
    IReadOnlyList<BootstrapCustomer> Customers,
    IReadOnlyList<BootstrapPermission> Permissions,
    BootstrapSyncVersions Sync);

public sealed record BootstrapRestaurantInfo(string MotherId, string Name, string Description, string Currency, string TimeZone);

public sealed record BootstrapTerminalConfig(
    string TerminalId,
    string TerminalName,
    string DeviceRole,
    string ApiBaseUrl,
    string WebSocketUrl,
    string PairingCodeHint,
    string TerminalToken);

public sealed record BootstrapCategory(
    int Id,
    string MotherId,
    string Name,
    string Color,
    int SortOrder,
    bool IsActive,
    string? ParentMotherId = null,
    int? ParentId = null);

public sealed record BootstrapProduct(int Id, string MotherId, int CategoryId, string Name, string Description, string? Sku, bool IsActive);

public sealed record BootstrapPrice(int Id, int ProductId, string PriceType, decimal Amount, string Currency, int? TaxRateId);

public sealed record BootstrapModifierGroup(int Id, string MotherId, string Name, int MinSelect, int MaxSelect, bool IsActive);

public sealed record BootstrapModifier(int Id, string MotherId, int ModifierGroupId, string Name, decimal PriceDelta, bool IsActive);

public sealed record BootstrapProductModifier(int ProductId, int ModifierGroupId, int SortOrder);

public sealed record BootstrapVariant(
    int Id,
    string MotherId,
    int ProductId,
    string Name,
    string? Description,
    decimal TakeawayPrice,
    decimal DineInPrice,
    int SortOrder,
    bool IsActive);

public sealed record BootstrapMealDeal(
    int Id,
    string MotherId,
    string Name,
    string? Description,
    decimal Price,
    string Color,
    int PickCount,
    string VatCategory,
    int SortOrder,
    bool IsActive);

public sealed record BootstrapMealDealChoice(
    int Id,
    string MotherId,
    int MealDealId,
    string Name,
    int SortOrder);

public sealed record BootstrapMealDealCategoryRule(
    int Id,
    string MotherId,
    int MealDealId,
    string Name,
    bool IsRequired,
    int MinSelections,
    int MaxSelections,
    IReadOnlyList<string> MenuItemMotherIds);

public sealed record BootstrapTastingMenu(
    int Id,
    string MotherId,
    string Name,
    string? Description,
    string Color,
    int SortOrder,
    bool IsActive);

public sealed record BootstrapTastingMenuOption(
    int Id,
    string MotherId,
    int TastingMenuId,
    string Name,
    decimal Price,
    bool IncludesWine,
    int CourseCount,
    int SortOrder);

public sealed record BootstrapTastingMenuCourse(
    int Id,
    string MotherId,
    int TastingMenuId,
    string Name,
    string? WineName,
    int CourseNumber,
    bool Required,
    string VatCategory,
    int SortOrder);

public sealed record BootstrapTastingMenuChoice(
    int Id,
    string MotherId,
    int CourseId,
    string Name,
    string? PrintGroupId,
    int SortOrder);

public sealed record BootstrapTaxRate(int Id, string MotherId, string Name, decimal RatePercent, bool IsActive);

public sealed record BootstrapFloor(int Id, string MotherId, string Name, int SortOrder, bool IsActive);

public sealed record BootstrapTable(
    int Id,
    string MotherId,
    int FloorId,
    string TableNumber,
    int Seats,
    string Status,
    decimal CurrentTotal,
    int PositionX = 0,
    int PositionY = 0);

public sealed record BootstrapOpenOrder(
    string Id,
    string MotherId,
    string OrderNumber,
    string OrderType,
    int? TableId,
    int? CustomerId,
    int Guests,
    string Status,
    decimal Total,
    string OpenedUtc);

public sealed record BootstrapOrderItem(
    string Id,
    string OrderId,
    int? ProductId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Notes,
    string Status);

public sealed record BootstrapOnlineOrder(
    string Id,
    string MotherId,
    string OrderNumber,
    string CustomerName,
    string OrderType,
    string DueTime,
    string Status,
    decimal Total);

public sealed record BootstrapReservation(
    string Id,
    string MotherId,
    string CustomerName,
    string Phone,
    int? TableId,
    int PartySize,
    string ReservationUtc,
    string Status);

public sealed record CachedReservation(
    string Id,
    string MotherId,
    string CustomerName,
    string Phone,
    int? TableId,
    string? TableNumber,
    int PartySize,
    string ReservationUtc,
    string Status,
    string? PayloadJson,
    string UpdatedUtc);

public sealed record BootstrapCustomer(
    int Id,
    string MotherId,
    string Name,
    string Phone,
    string? Email,
    string Address,
    string? Postcode,
    int LoyaltyPoints);

public sealed record BootstrapPermission(string UserId, string PermissionKey, bool IsAllowed);

public sealed record BootstrapSyncVersions(
    int SchemaVersion,
    string BootstrapId,
    string LastEventId,
    string GeneratedUtc,
    string MenuVersion,
    string TableVersion,
    string OrderVersion,
    string PermissionVersion);

public sealed record BootstrapRequest(string MotherIpAddress, string PairingCode, string TerminalName);
