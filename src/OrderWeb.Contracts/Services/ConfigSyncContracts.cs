using OrderWeb.Contracts.Config;
using OrderWeb.Contracts.Results;

namespace OrderWeb.Contracts.Services;

/// <summary>
/// Mother-controlled configuration sync. Groups are versioned and fetched separately
/// so price/logo/floor/permission/feature/settings changes do not require a Child reinstall.
/// </summary>
public interface IConfigSyncService
{
    Task<OperationResult<ConfigManifestDto>> GetManifestAsync(
        ConfigVersionsDto? appliedVersions = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<BrandingConfigDto>> GetBrandingAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<MenuConfigDto>> GetMenuAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<FloorConfigDto>> GetFloorsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<PermissionsConfigDto>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<FeaturesConfigDto>> GetFeaturesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<SettingsConfigDto>> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only groups whose Mother version is newer than the Child's applied version.
    /// </summary>
    Task<OperationResult<ConfigPullResultDto>> PullChangedAsync(
        ConfigVersionsDto appliedVersions,
        CancellationToken cancellationToken = default);
}
