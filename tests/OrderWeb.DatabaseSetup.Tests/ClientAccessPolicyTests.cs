using OrderWeb.Contracts.Access;
using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Features;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class ClientAccessPolicyTests
{
    [Fact]
    public void StandingRules_AreFrozen()
    {
        Assert.True(ClientAccessPolicy.OneMotherPerRestaurant);
        Assert.True(ClientAccessPolicy.ClientConnectsToMotherOnly);
        Assert.True(ClientAccessPolicy.PrintersAreMotherOnly);
        Assert.Contains("Web orders stay on Mother", ClientAccessPolicy.StandingRule, StringComparison.Ordinal);
        Assert.Contains("Reservations may go to Client", ClientAccessPolicy.StandingRule, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("restaurant")]
    [InlineData("collection")]
    [InlineData("delivery")]
    [InlineData("liveorder")]
    [InlineData("reservation")]
    [InlineData("customers")]
    [InlineData("giftcards")]
    [InlineData("loyalty")]
    [InlineData("cashdrawer")]
    public void MotherMayGrant_DailyClientRoutes(string route) =>
        Assert.True(ClientAccessPolicy.IsRouteAllowed(route));

    [Theory]
    [InlineData("weborders")]
    [InlineData("printersetup")]
    [InlineData("foodmenu")]
    [InlineData("settings")]
    [InlineData("report")]
    [InlineData("terminalhealth")]
    [InlineData("inventory")]
    public void EveryClient_IsBlockedFromMotherOnlyRoutes(string route) =>
        Assert.False(ClientAccessPolicy.IsRouteAllowed(route));

    [Fact]
    public void WebOrders_AreMotherOnly_AndNeverGrantableToClient()
    {
        Assert.Contains(PosFeatureKeys.WebOrders, ClientAccessPolicy.MotherOnlyFeatures);
        Assert.DoesNotContain(PosFeatureKeys.WebOrders, ClientAccessPolicy.GrantableFeatures);
        Assert.False(ClientAccessPolicy.IsFeatureGrantable(PosFeatureKeys.WebOrders));
        Assert.False(ClientAccessPolicy.IsRouteAllowed("weborders"));
    }

    [Fact]
    public void Reservations_AreGrantedToClientByDefault_MatchingMotherDashboard()
    {
        Assert.True(ClientAccessPolicy.IsFeatureGrantable(PosFeatureKeys.Reservations));
        Assert.True(ClientAccessPolicy.IsRouteAllowed("reservation"));
        Assert.Contains(PosFeatureKeys.Reservations, ClientAccessPolicy.DefaultGrantedFeatures);
        Assert.Contains("reservation", ClientAccessPolicy.RoutesForFeatures(ClientAccessPolicy.DefaultGrantedFeatures));
        Assert.Contains("reservation", ClientAccessPolicy.RoutesForFeatures([PosFeatureKeys.Reservations]));
    }

    [Fact]
    public void PrinterSetup_IsBlocked_ButPrintingAnOrderIsGrantable()
    {
        Assert.Contains(PosCapabilityKeys.ConfigurePrinters, ClientAccessPolicy.BlockedCapabilities);
        Assert.False(ClientAccessPolicy.IsCapabilityAllowed(PosCapabilityKeys.ConfigurePrinters));
        Assert.True(ClientAccessPolicy.IsCapabilityAllowed(PosCapabilityKeys.PrintReceipts));
        Assert.False(ClientAccessPolicy.IsRouteAllowed("printersetup"));
    }

    [Fact]
    public void FullReports_AreBlockedOnClient()
    {
        Assert.False(ClientAccessPolicy.IsCapabilityAllowed(PosCapabilityKeys.ViewReports));
        Assert.False(ClientAccessPolicy.IsRouteAllowed("report"));
    }

    [Fact]
    public void FilterCapabilities_StripsMotherOnlyAdminCapabilities()
    {
        var filtered = ClientAccessPolicy.FilterCapabilities(
        [
            PosCapabilityKeys.TakeOrders,
            PosCapabilityKeys.ConfigurePrinters,
            PosCapabilityKeys.EditMenu,
            PosCapabilityKeys.ViewReports,
            PosCapabilityKeys.AccessAdmin
        ]);

        Assert.Equal(new[] { PosCapabilityKeys.TakeOrders }, filtered.ToArray());
    }
}
