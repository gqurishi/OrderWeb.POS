using OrderWeb.Contracts.Navigation;
using Xunit;

namespace OrderWeb.Contracts.Tests;

public class PosNavigationCatalogTests
{
    [Fact]
    public void Client_user_sees_operational_routes_but_not_settings()
    {
        var items = PosNavigationCatalog.Resolve(
            new PosNavigationContext(
                Role: "User",
                Capabilities: PosNavigationCatalog.DefaultClientCapabilitiesForRole("User"),
                IsMotherConnected: true),
            forClient: true);

        Assert.Contains(items, item => item.Route == "dashboard");
        Assert.Contains(items, item => item.Route == "restaurant");
        Assert.Contains(items, item => item.Route == "collection");
        Assert.Contains(items, item => item.Route == "payment");
        Assert.DoesNotContain(items, item => item.Route == "settings");
        Assert.DoesNotContain(items, item => item.Route == "report");
        Assert.DoesNotContain(items, item => item.Route == "foodmenu");
    }

    [Fact]
    public void Payment_is_disabled_when_mother_is_offline()
    {
        var items = PosNavigationCatalog.Resolve(
            new PosNavigationContext(
                Role: "Manager",
                Capabilities: PosNavigationCatalog.DefaultClientCapabilitiesForRole("Manager"),
                IsMotherConnected: false),
            forClient: true);

        var payment = Assert.Single(items, item => item.Route == "payment");
        Assert.False(payment.IsEnabled);
        Assert.True(payment.RequiresMotherConnection);

        var restaurant = Assert.Single(items, item => item.Route == "restaurant");
        Assert.True(restaurant.IsEnabled);
        Assert.True(restaurant.AllowsCachedOffline);
    }

    [Fact]
    public void Explicit_capability_hides_routes_not_granted_by_mother()
    {
        var items = PosNavigationCatalog.Resolve(
            new PosNavigationContext(
                Role: "Manager",
                Capabilities:
                [
                    PosCapabilityKeys.Dashboard,
                    PosCapabilityKeys.OpenOrders
                ],
                IsMotherConnected: true),
            forClient: true);

        Assert.Contains(items, item => item.Route == "dashboard");
        Assert.Contains(items, item => item.Route == "liveorder");
        Assert.DoesNotContain(items, item => item.Route == "restaurant");
        Assert.DoesNotContain(items, item => item.Route == "report");
        Assert.DoesNotContain(items, item => item.Route == "settings");
    }

    [Fact]
    public void Feature_flag_can_hide_floor_route()
    {
        var items = PosNavigationCatalog.Resolve(
            new PosNavigationContext(
                Role: "User",
                Capabilities: PosNavigationCatalog.DefaultClientCapabilitiesForRole("User"),
                IsMotherConnected: true,
                FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tables.enabled"] = false
                }),
            forClient: true);

        Assert.DoesNotContain(items, item => item.Route == "restaurant");
    }

    [Fact]
    public void Projector_prefers_explicit_client_capabilities()
    {
        var effective = ClientCapabilityProjector.EffectiveCapabilities(
            "Admin",
            [PosCapabilityKeys.Dashboard, "dashboard.admin.view"]);

        Assert.Equal([PosCapabilityKeys.Dashboard], effective);
    }

    [Fact]
    public void Mother_login_merge_emits_client_capabilities()
    {
        var merged = ClientCapabilityProjector.MergeWithRolePermissions(
            "User",
            ["weborders.view"]);

        Assert.Contains(PosCapabilityKeys.Dashboard, merged);
        Assert.Contains(PosCapabilityKeys.Payment, merged);
        Assert.Contains("weborders.view", merged);
        Assert.DoesNotContain(PosCapabilityKeys.Settings, merged);
    }
}
