using System.Collections.Concurrent;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Contracts.Config;

/// <summary>
/// Mother-side authoritative configuration catalog (Phase 15).
/// Each group has its own version. Price, availability, colours, images, logo,
/// floor positions, permissions, features, and settings bump only their group —
/// Child terminals pull the dirty group without reinstalling.
/// </summary>
public sealed class MotherConfigCatalogService : IConfigSyncService
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<ConfigChangedEventDto> _recentChanges = new();

    private string _restaurantId = "restaurant-1";
    private ConfigVersionsDto _versions = new(1, 1, 1, 1, 1, 1);
    private BrandingConfigDto _branding;
    private MenuConfigDto _menu;
    private FloorConfigDto _floors;
    private PermissionsConfigDto _permissions;
    private FeaturesConfigDto _features;
    private SettingsConfigDto _settings;

    public MotherConfigCatalogService()
    {
        _branding = CreateDefaultBranding(1);
        _menu = CreateDefaultMenu(1);
        _floors = CreateDefaultFloors(1);
        _permissions = CreateDefaultPermissions(1);
        _features = CreateDefaultFeatures(1);
        _settings = CreateDefaultSettings(1);
    }

    public ConfigVersionsDto CurrentVersions
    {
        get { lock (_gate) return _versions; }
    }

    public Task<OperationResult<ConfigManifestDto>> GetManifestAsync(
        ConfigVersionsDto? appliedVersions = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var applied = appliedVersions ?? ConfigVersionsDto.None;
            var changed = Enum.GetValues<ConfigGroupKind>()
                .Where(group => _versions.Get(group) > applied.Get(group))
                .Select(group => new ConfigGroupChangeDto(group, _versions.Get(group)))
                .ToList();

            return Task.FromResult(OperationResult<ConfigManifestDto>.Ok(new ConfigManifestDto(
                _restaurantId,
                _versions,
                DateTimeOffset.UtcNow,
                changed)));
        }
    }

    public Task<OperationResult<BrandingConfigDto>> GetBrandingAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(OperationResult<BrandingConfigDto>.Ok(_branding));
    }

    public Task<OperationResult<MenuConfigDto>> GetMenuAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(OperationResult<MenuConfigDto>.Ok(_menu));
    }

    public Task<OperationResult<FloorConfigDto>> GetFloorsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(OperationResult<FloorConfigDto>.Ok(_floors));
    }

    public Task<OperationResult<PermissionsConfigDto>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(OperationResult<PermissionsConfigDto>.Ok(_permissions));
    }

    public Task<OperationResult<FeaturesConfigDto>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(OperationResult<FeaturesConfigDto>.Ok(_features));
    }

    public Task<OperationResult<SettingsConfigDto>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(OperationResult<SettingsConfigDto>.Ok(_settings));
    }

    public Task<OperationResult<ConfigPullResultDto>> PullChangedAsync(
        ConfigVersionsDto appliedVersions,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var result = new ConfigPullResultDto(
                _versions,
                Branding: _versions.BrandingVersion > appliedVersions.BrandingVersion ? _branding : null,
                Menu: _versions.MenuVersion > appliedVersions.MenuVersion ? _menu : null,
                Floors: _versions.FloorVersion > appliedVersions.FloorVersion ? _floors : null,
                Permissions: _versions.PermissionsVersion > appliedVersions.PermissionsVersion ? _permissions : null,
                Features: _versions.FeatureVersion > appliedVersions.FeatureVersion ? _features : null,
                Settings: _versions.SettingsVersion > appliedVersions.SettingsVersion ? _settings : null);

            return Task.FromResult(OperationResult<ConfigPullResultDto>.Ok(result));
        }
    }

    public BrandingConfigDto PublishBranding(
        string restaurantName,
        string? logoUrl,
        string? primaryColour,
        string? accentColour,
        string? receiptHeader = null,
        string? receiptFooter = null)
    {
        lock (_gate)
        {
            var next = _versions.BrandingVersion + 1;
            _branding = new BrandingConfigDto(next, restaurantName, logoUrl, primaryColour, accentColour, receiptHeader, receiptFooter);
            Bump(ConfigGroupKind.Branding, next);
            return _branding;
        }
    }

    public MenuConfigDto PublishProductPrice(string productId, decimal price)
    {
        lock (_gate)
        {
            var products = MutateProduct(productId, p => p with { Price = price });
            return ReplaceMenuUnlocked(products, _menu.Categories);
        }
    }

    public MenuConfigDto PublishProductAvailability(string productId, bool isAvailable)
    {
        lock (_gate)
        {
            var products = MutateProduct(productId, p => p with { IsAvailable = isAvailable });
            return ReplaceMenuUnlocked(products, _menu.Categories);
        }
    }

    public MenuConfigDto PublishProductAppearance(string productId, string? colour, string? imageUrl)
    {
        lock (_gate)
        {
            var products = MutateProduct(productId, p => p with { Colour = colour, ImageUrl = imageUrl });
            return ReplaceMenuUnlocked(products, _menu.Categories);
        }
    }

    public FloorConfigDto PublishTablePosition(string tableId, int positionX, int positionY)
    {
        lock (_gate)
        {
            var tables = _floors.Tables.Select(t =>
                t.Id == tableId ? t with { PositionX = positionX, PositionY = positionY } : t).ToList();
            if (tables.All(t => t.Id != tableId))
                throw new InvalidOperationException($"Table '{tableId}' was not found.");

            var next = _versions.FloorVersion + 1;
            _floors = new FloorConfigDto(next, _floors.Floors, tables);
            Bump(ConfigGroupKind.Floors, next);
            return _floors;
        }
    }

    public PermissionsConfigDto PublishPermission(string roleOrUserKey, string permissionKey, bool isAllowed)
    {
        lock (_gate)
        {
            var list = _permissions.Permissions
                .Where(p => !(p.RoleOrUserKey == roleOrUserKey && p.PermissionKey == permissionKey))
                .Append(new ConfigPermissionDto(roleOrUserKey, permissionKey, isAllowed))
                .ToList();

            var next = _versions.PermissionsVersion + 1;
            _permissions = new PermissionsConfigDto(next, list);
            Bump(ConfigGroupKind.Permissions, next);
            return _permissions;
        }
    }

    public FeaturesConfigDto PublishFeature(string featureKey, bool enabled)
    {
        lock (_gate)
        {
            var flags = new Dictionary<string, bool>(_features.Flags, StringComparer.OrdinalIgnoreCase)
            {
                [featureKey] = enabled
            };
            var next = _versions.FeatureVersion + 1;
            _features = new FeaturesConfigDto(next, flags);
            Bump(ConfigGroupKind.Features, next);
            return _features;
        }
    }

    public SettingsConfigDto PublishSetting(string key, string value)
    {
        lock (_gate)
        {
            var values = new Dictionary<string, string>(_settings.Values, StringComparer.OrdinalIgnoreCase)
            {
                [key] = value
            };
            var next = _versions.SettingsVersion + 1;
            _settings = new SettingsConfigDto(next, values);
            Bump(ConfigGroupKind.Settings, next);
            return _settings;
        }
    }

    public IReadOnlyList<ConfigChangedEventDto> DrainRecentChanges()
    {
        var list = new List<ConfigChangedEventDto>();
        while (_recentChanges.TryDequeue(out var item))
            list.Add(item);
        return list;
    }

    private List<ConfigMenuProductDto> MutateProduct(string productId, Func<ConfigMenuProductDto, ConfigMenuProductDto> mutate)
    {
        var products = _menu.Products.Select(p => p.Id == productId ? mutate(p) : p).ToList();
        if (products.All(p => p.Id != productId))
            throw new InvalidOperationException($"Product '{productId}' was not found.");
        return products;
    }

    private MenuConfigDto ReplaceMenuUnlocked(
        IReadOnlyList<ConfigMenuProductDto> products,
        IReadOnlyList<ConfigMenuCategoryDto> categories)
    {
        var next = _versions.MenuVersion + 1;
        _menu = new MenuConfigDto(next, categories.ToList(), products.ToList());
        Bump(ConfigGroupKind.Menu, next);
        return _menu;
    }

    private void Bump(ConfigGroupKind group, long version)
    {
        _versions = _versions.With(group, version);
        _recentChanges.Enqueue(new ConfigChangedEventDto(group, version, DateTimeOffset.UtcNow));
        while (_recentChanges.Count > 100 && _recentChanges.TryDequeue(out _))
        {
        }
    }

    private static BrandingConfigDto CreateDefaultBranding(long version) =>
        new(version, "OrderWeb Restaurant", null, "#0F766E", "#F59E0B", "Welcome", "Thank you");

    private static MenuConfigDto CreateDefaultMenu(long version) =>
        new(
            version,
            new[]
            {
                new ConfigMenuCategoryDto("cat-mains", "Mains", 1, "#DC2626", true),
                new ConfigMenuCategoryDto("cat-drinks", "Drinks", 2, "#2563EB", true)
            },
            new[]
            {
                new ConfigMenuProductDto("prod-burger", "cat-mains", "House Burger", 14.00m, true, "#F97316", null, 1, Array.Empty<string>()),
                new ConfigMenuProductDto("prod-steak", "cat-mains", "Ribeye Steak", 24.50m, true, "#7C2D12", null, 2, Array.Empty<string>()),
                new ConfigMenuProductDto("prod-cola", "cat-drinks", "Cola", 2.50m, true, "#1D4ED8", null, 1, Array.Empty<string>())
            });

    private static FloorConfigDto CreateDefaultFloors(long version) =>
        new(
            version,
            new[] { new ConfigFloorDto("floor-1", "Main", 1, true) },
            new[]
            {
                new ConfigTableDto("table-1", "floor-1", "1", 4, 40, 40, "Free"),
                new ConfigTableDto("table-12", "floor-1", "12", 6, 180, 80, "Free")
            });

    private static PermissionsConfigDto CreateDefaultPermissions(long version) =>
        new(
            version,
            new[]
            {
                new ConfigPermissionDto("role:server", "order.void", false),
                new ConfigPermissionDto("role:manager", "order.void", true),
                new ConfigPermissionDto("role:manager", "order.discount", true)
            });

    private static FeaturesConfigDto CreateDefaultFeatures(long version) =>
        new(
            version,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["tables.enabled"] = true,
                ["online_orders.enabled"] = true,
                ["loyalty.enabled"] = false,
                ["gift_cards.enabled"] = false
            });

    private static SettingsConfigDto CreateDefaultSettings(long version) =>
        new(
            version,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["currency"] = "GBP",
                ["tax_rate"] = "0.20",
                ["service_charge_percent"] = "0",
                ["locale"] = "en-GB"
            });
}
