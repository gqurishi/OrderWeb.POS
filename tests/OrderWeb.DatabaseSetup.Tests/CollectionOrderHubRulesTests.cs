using OrderWeb.Contracts.Access;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class CollectionOrderHubRulesTests
{
    [Fact]
    public void Phase0_FreezesMotherAsOnlyCollectionAuthority()
    {
        Assert.True(CollectionOrderHubRules.MotherIsAuthoritativeStore);
        Assert.False(CollectionOrderHubRules.ClientCacheIsAuthoritative);
        Assert.False(CollectionOrderHubRules.ClientPeerSyncAllowed);
        Assert.True(CollectionOrderHubRules.RequireMotherOrderId);
    }

    [Fact]
    public void Phase0_AllowsOfflineCachedList_ButBlocksMutations()
    {
        Assert.True(CollectionOrderHubRules.OfflineViewCachedListAllowed);
        Assert.False(CollectionOrderHubRules.OfflineEditOrSaveAllowed);
        Assert.False(CollectionOrderHubRules.OfflinePayAllowed);
        Assert.False(CollectionOrderHubRules.OfflineVoidAllowed);
        Assert.False(CollectionOrderHubRules.OfflinePrintAsSuccessAllowed);
        Assert.False(CollectionOrderHubRules.OfflineSendToKitchenAllowed);
    }

    [Theory]
    [InlineData("Collection")]
    [InlineData("pickup")]
    [InlineData("Delivery")]
    [InlineData("delivery")]
    [InlineData("del")]
    [InlineData("Table")]
    [InlineData("table")]
    [InlineData("tbl")]
    public void CustomerHub_RecognizesCollectionDeliveryAndTableTypes(string orderType)
    {
        Assert.True(CustomerOrderHubRules.IsCustomerHubOrderType(orderType));
    }

    [Fact]
    public void CustomerHub_TableBusyOwnedByMother()
    {
        Assert.True(CustomerOrderHubRules.MotherOwnsTableBusyStatus);
        Assert.True(CustomerOrderHubRules.IsTableOrderType("Table"));
        Assert.False(CustomerOrderHubRules.IsDeliveryOrderType("Table"));
        Assert.False(CustomerOrderHubRules.IsCustomerOrderType("Table"));
    }
}
