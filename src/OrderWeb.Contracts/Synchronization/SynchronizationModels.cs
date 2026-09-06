namespace OrderWeb.Contracts.Synchronization;

public static class SyncSectionKeys
{
    public const string Branding = "branding";
    public const string Menu = "menu";
    public const string Categories = "categories";
    public const string Products = "products";
    public const string Availability = "availability";
    public const string Floors = "floors";
    public const string Tables = "tables";
    public const string Permissions = "permissions";
    public const string Features = "features";
    public const string Settings = "settings";
    public const string Images = "images";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Menu, Categories, Products, Availability, Branding, Floors, Tables,
        Permissions, Features, Settings, Images
    };
}

public sealed record SyncVersionSet(
    int SchemaVersion,
    IReadOnlyDictionary<string, string> Sections,
    string? LastEventId = null);

public sealed record SyncEventNotification(
    string EventId,
    string EventType,
    string RestaurantId,
    string? Version,
    DateTimeOffset TimestampUtc,
    string? CorrelationId = null);
