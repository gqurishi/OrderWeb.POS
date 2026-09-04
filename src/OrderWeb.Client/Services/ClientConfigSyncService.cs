using System.Text.Json;
using OrderWeb.Contracts.Config;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client.Services;

/// <summary>
/// Child config sync (Phase 15). Pulls only changed Mother groups and stores
/// last successfully applied versions. Price/logo/floor/permission/feature/settings
/// updates do not require a Client reinstall.
/// </summary>
public sealed class ClientConfigSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConfigSyncService _mother;
    private readonly ClientCacheService _cache;

    public ClientConfigSyncService(IConfigSyncService mother, ClientCacheService cache)
    {
        _mother = mother ?? throw new ArgumentNullException(nameof(mother));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public Task<ConfigVersionsDto> GetAppliedVersionsAsync() => _cache.GetAppliedConfigVersionsAsync();

    public async Task<OperationResult<ConfigManifestDto>> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        var applied = await _cache.GetAppliedConfigVersionsAsync().ConfigureAwait(false);
        return await _mother.GetManifestAsync(applied, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<ConfigApplyResult>> SyncChangedAsync(CancellationToken cancellationToken = default)
    {
        var applied = await _cache.GetAppliedConfigVersionsAsync().ConfigureAwait(false);
        var pull = await _mother.PullChangedAsync(applied, cancellationToken).ConfigureAwait(false);
        if (!pull.IsSuccess || pull.Value is null)
        {
            return OperationResult<ConfigApplyResult>.Fail(
                pull.Error ?? OperationError.Failure("Config pull failed."));
        }

        var result = await ApplyPullAsync(pull.Value).ConfigureAwait(false);
        return OperationResult<ConfigApplyResult>.Ok(result);
    }

    public async Task<ConfigApplyResult> ApplyPullAsync(ConfigPullResultDto pull)
    {
        var appliedGroups = new List<ConfigGroupKind>();
        var applied = await _cache.GetAppliedConfigVersionsAsync().ConfigureAwait(false);

        if (pull.Branding is not null)
        {
            await PersistGroupAsync(ConfigGroupKind.Branding, pull.Branding).ConfigureAwait(false);
            applied = applied.With(ConfigGroupKind.Branding, pull.Branding.Version);
            appliedGroups.Add(ConfigGroupKind.Branding);
        }

        if (pull.Menu is not null)
        {
            await PersistGroupAsync(ConfigGroupKind.Menu, pull.Menu).ConfigureAwait(false);
            applied = applied.With(ConfigGroupKind.Menu, pull.Menu.Version);
            appliedGroups.Add(ConfigGroupKind.Menu);
        }

        if (pull.Floors is not null)
        {
            await PersistGroupAsync(ConfigGroupKind.Floors, pull.Floors).ConfigureAwait(false);
            applied = applied.With(ConfigGroupKind.Floors, pull.Floors.Version);
            appliedGroups.Add(ConfigGroupKind.Floors);
        }

        if (pull.Permissions is not null)
        {
            await PersistGroupAsync(ConfigGroupKind.Permissions, pull.Permissions).ConfigureAwait(false);
            applied = applied.With(ConfigGroupKind.Permissions, pull.Permissions.Version);
            appliedGroups.Add(ConfigGroupKind.Permissions);
        }

        if (pull.Features is not null)
        {
            await PersistGroupAsync(ConfigGroupKind.Features, pull.Features).ConfigureAwait(false);
            applied = applied.With(ConfigGroupKind.Features, pull.Features.Version);
            appliedGroups.Add(ConfigGroupKind.Features);
        }

        if (pull.Settings is not null)
        {
            await PersistGroupAsync(ConfigGroupKind.Settings, pull.Settings).ConfigureAwait(false);
            applied = applied.With(ConfigGroupKind.Settings, pull.Settings.Version);
            appliedGroups.Add(ConfigGroupKind.Settings);
        }

        await _cache.SaveAppliedConfigVersionsAsync(applied).ConfigureAwait(false);
        return new ConfigApplyResult(applied, appliedGroups);
    }

    private async Task PersistGroupAsync<T>(ConfigGroupKind group, T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await _cache.SaveConfigGroupSnapshotAsync(group, json).ConfigureAwait(false);
    }
}

public sealed record ConfigApplyResult(
    ConfigVersionsDto AppliedVersions,
    IReadOnlyList<ConfigGroupKind> AppliedGroups);
