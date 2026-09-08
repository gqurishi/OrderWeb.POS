using OrderWeb.Contracts.Access;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class CollectionOrderConcurrencyTests
{
    [Fact]
    public void OrderVersion_ChangesWhenUpdatedAtChanges()
    {
        var first = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var second = first.AddSeconds(1);

        Assert.NotEqual(
            CollectionOrderHubRules.ComputeOrderVersion(first),
            CollectionOrderHubRules.ComputeOrderVersion(second));
    }

    [Fact]
    public void OrderVersion_IsAtLeastOne()
    {
        Assert.Equal(1, CollectionOrderHubRules.ComputeOrderVersion(default));
    }
}
