namespace OrderWeb.Contracts.Config;

/// <summary>
/// Separate Mother-controlled configuration groups (Phase 15).
/// Never shipped as one enormous combined payload.
/// </summary>
public enum ConfigGroupKind
{
    Branding = 1,
    Menu = 2,
    Floors = 3,
    Permissions = 4,
    Features = 5,
    Settings = 6
}

/// <summary>
/// Mother-maintained versions. Child stores the last successfully applied values.
/// </summary>
public sealed record ConfigVersionsDto(
    long BrandingVersion,
    long MenuVersion,
    long FloorVersion,
    long PermissionsVersion,
    long FeatureVersion,
    long SettingsVersion)
{
    public static ConfigVersionsDto None { get; } = new(0, 0, 0, 0, 0, 0);

    public long Get(ConfigGroupKind group) => group switch
    {
        ConfigGroupKind.Branding => BrandingVersion,
        ConfigGroupKind.Menu => MenuVersion,
        ConfigGroupKind.Floors => FloorVersion,
        ConfigGroupKind.Permissions => PermissionsVersion,
        ConfigGroupKind.Features => FeatureVersion,
        ConfigGroupKind.Settings => SettingsVersion,
        _ => 0
    };

    public ConfigVersionsDto With(ConfigGroupKind group, long version) => group switch
    {
        ConfigGroupKind.Branding => this with { BrandingVersion = version },
        ConfigGroupKind.Menu => this with { MenuVersion = version },
        ConfigGroupKind.Floors => this with { FloorVersion = version },
        ConfigGroupKind.Permissions => this with { PermissionsVersion = version },
        ConfigGroupKind.Features => this with { FeatureVersion = version },
        ConfigGroupKind.Settings => this with { SettingsVersion = version },
        _ => this
    };
}

/// <summary>Lightweight versions-only envelope. Safe to poll frequently.</summary>
public sealed record ConfigManifestDto(
    string RestaurantId,
    ConfigVersionsDto Versions,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<ConfigGroupChangeDto> ChangedGroups);

public sealed record ConfigGroupChangeDto(
    ConfigGroupKind Group,
    long Version,
    bool RequiresChildReinstall = false);

public sealed record ConfigChangedEventDto(
    ConfigGroupKind Group,
    long Version,
    DateTimeOffset ChangedUtc);

// --- Per-group payloads (independent downloads) ---

public sealed record BrandingConfigDto(
    long Version,
    string RestaurantName,
    string? LogoUrl,
    string? PrimaryColour,
    string? AccentColour,
    string? ReceiptHeader,
    string? ReceiptFooter);

public sealed record MenuConfigDto(
    long Version,
    IReadOnlyList<ConfigMenuCategoryDto> Categories,
    IReadOnlyList<ConfigMenuProductDto> Products);

public sealed record ConfigMenuCategoryDto(
    string Id,
    string Name,
    int SortOrder,
    string? Colour,
    bool IsActive);

public sealed record ConfigMenuProductDto(
    string Id,
    string CategoryId,
    string Name,
    decimal Price,
    bool IsAvailable,
    string? Colour,
    string? ImageUrl,
    int SortOrder,
    IReadOnlyList<string> ModifierGroupIds);

public sealed record FloorConfigDto(
    long Version,
    IReadOnlyList<ConfigFloorDto> Floors,
    IReadOnlyList<ConfigTableDto> Tables);

public sealed record ConfigFloorDto(string Id, string Name, int SortOrder, bool IsActive);

public sealed record ConfigTableDto(
    string Id,
    string FloorId,
    string TableNumber,
    int Seats,
    int PositionX,
    int PositionY,
    string Status);

public sealed record PermissionsConfigDto(
    long Version,
    IReadOnlyList<ConfigPermissionDto> Permissions);

public sealed record ConfigPermissionDto(
    string RoleOrUserKey,
    string PermissionKey,
    bool IsAllowed);

public sealed record FeaturesConfigDto(
    long Version,
    IReadOnlyDictionary<string, bool> Flags);

public sealed record SettingsConfigDto(
    long Version,
    IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Pull only groups the Child has not yet applied. Null group payloads mean "already up to date".
/// </summary>
public sealed record ConfigPullResultDto(
    ConfigVersionsDto MotherVersions,
    BrandingConfigDto? Branding,
    MenuConfigDto? Menu,
    FloorConfigDto? Floors,
    PermissionsConfigDto? Permissions,
    FeaturesConfigDto? Features,
    SettingsConfigDto? Settings)
{
    public IEnumerable<ConfigGroupKind> ChangedGroups
    {
        get
        {
            if (Branding is not null) yield return ConfigGroupKind.Branding;
            if (Menu is not null) yield return ConfigGroupKind.Menu;
            if (Floors is not null) yield return ConfigGroupKind.Floors;
            if (Permissions is not null) yield return ConfigGroupKind.Permissions;
            if (Features is not null) yield return ConfigGroupKind.Features;
            if (Settings is not null) yield return ConfigGroupKind.Settings;
        }
    }
}
