using OrderWeb.Contracts.Config;
using Xunit;

namespace OrderWeb.Contracts.Tests;

public sealed class Phase15MotherConfigTests
{
    [Fact]
    public async Task Manifest_lists_only_groups_newer_than_applied_versions()
    {
        var mother = new MotherConfigCatalogService();
        mother.PublishProductPrice("prod-burger", 15.50m); // bumps Menu only

        var applied = new ConfigVersionsDto(1, 1, 1, 1, 1, 1);
        var manifest = await mother.GetManifestAsync(applied);

        Assert.True(manifest.IsSuccess);
        Assert.Contains(manifest.Value!.ChangedGroups, g => g.Group == ConfigGroupKind.Menu);
        Assert.DoesNotContain(manifest.Value!.ChangedGroups, g => g.Group == ConfigGroupKind.Branding);
        Assert.True(manifest.Value!.Versions.MenuVersion > 1);
    }

    [Fact]
    public async Task PullChanged_returns_separate_group_payloads_not_one_blob()
    {
        var mother = new MotherConfigCatalogService();
        mother.PublishBranding("New Name", "https://cdn/logo.png", "#111111", "#222222");
        mother.PublishProductAvailability("prod-cola", false);
        mother.PublishTablePosition("table-1", 120, 200);
        mother.PublishFeature("loyalty.enabled", true);

        var pull = await mother.PullChangedAsync(ConfigVersionsDto.None);
        Assert.True(pull.IsSuccess);
        Assert.NotNull(pull.Value!.Branding);
        Assert.NotNull(pull.Value!.Menu);
        Assert.NotNull(pull.Value!.Floors);
        Assert.NotNull(pull.Value!.Features);
        // Each group carries its own version envelope.
        Assert.Equal(pull.Value!.MotherVersions.BrandingVersion, pull.Value!.Branding!.Version);
        Assert.Equal(pull.Value!.MotherVersions.MenuVersion, pull.Value!.Menu!.Version);
        Assert.Equal(pull.Value!.MotherVersions.FloorVersion, pull.Value!.Floors!.Version);
        Assert.Equal(pull.Value!.MotherVersions.FeatureVersion, pull.Value!.Features!.Version);
    }

    [Fact]
    public async Task Price_change_does_not_bump_unrelated_group_versions()
    {
        var mother = new MotherConfigCatalogService();
        var before = mother.CurrentVersions;
        mother.PublishProductPrice("prod-steak", 26.00m);
        var after = mother.CurrentVersions;

        Assert.Equal(before.BrandingVersion, after.BrandingVersion);
        Assert.Equal(before.FloorVersion, after.FloorVersion);
        Assert.Equal(before.PermissionsVersion, after.PermissionsVersion);
        Assert.Equal(before.FeatureVersion, after.FeatureVersion);
        Assert.Equal(before.SettingsVersion, after.SettingsVersion);
        Assert.Equal(before.MenuVersion + 1, after.MenuVersion);

        var pull = await mother.PullChangedAsync(before);
        Assert.NotNull(pull.Value!.Menu);
        Assert.Null(pull.Value!.Branding);
        Assert.Null(pull.Value!.Floors);
        Assert.Null(pull.Value!.Permissions);
        Assert.Null(pull.Value!.Features);
        Assert.Null(pull.Value!.Settings);
        Assert.Contains(pull.Value!.Menu!.Products, p => p.Id == "prod-steak" && p.Price == 26.00m);
    }

    [Fact]
    public async Task Up_to_date_child_receives_empty_pull()
    {
        var mother = new MotherConfigCatalogService();
        var pull = await mother.PullChangedAsync(mother.CurrentVersions);
        Assert.True(pull.IsSuccess);
        Assert.Empty(pull.Value!.ChangedGroups);
        Assert.Null(pull.Value.Branding);
        Assert.Null(pull.Value.Menu);
        Assert.Null(pull.Value.Floors);
        Assert.Null(pull.Value.Permissions);
        Assert.Null(pull.Value.Features);
        Assert.Null(pull.Value.Settings);
    }

    [Fact]
    public void Logo_permission_feature_and_settings_changes_are_independent()
    {
        var mother = new MotherConfigCatalogService();
        var baseline = mother.CurrentVersions;

        mother.PublishBranding("Cafe", "https://cdn/logo.svg", "#000", "#fff");
        Assert.Equal(baseline.BrandingVersion + 1, mother.CurrentVersions.BrandingVersion);
        Assert.Equal(baseline.MenuVersion, mother.CurrentVersions.MenuVersion);

        mother.PublishPermission("role:server", "order.void", true);
        Assert.Equal(baseline.PermissionsVersion + 1, mother.CurrentVersions.PermissionsVersion);

        mother.PublishFeature("gift_cards.enabled", true);
        Assert.Equal(baseline.FeatureVersion + 1, mother.CurrentVersions.FeatureVersion);

        mother.PublishSetting("service_charge_percent", "12.5");
        Assert.Equal(baseline.SettingsVersion + 1, mother.CurrentVersions.SettingsVersion);
    }
}
