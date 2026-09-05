using OrderWeb.Contracts.Customers;

namespace OrderWeb.Contracts.Tests;

public class CustomerFieldProjectorTests
{
    private static CustomerSummaryDto FullCustomer() =>
        new(
            Id: "42",
            MotherId: "m-42",
            Name: "Ada Lovelace",
            Phone: "07123456789",
            Email: "ada@example.com",
            Address: "10 Downing Street",
            City: "London",
            County: "Greater London",
            Postcode: "SW1A 2AA",
            LoyaltyPoints: 120,
            OrderKind: CustomerOrderKind.Both,
            DisplayLine: null);

    [Fact]
    public void ClientDefault_cache_keeps_only_name_and_phone()
    {
        var projected = CustomerFieldProjector.Project(
            FullCustomer(),
            CustomerFieldAccessPolicy.ClientDefault,
            CustomerFieldAccessScope.Cache);

        Assert.Equal("Ada Lovelace", projected.Name);
        Assert.Equal("07123456789", projected.Phone);
        Assert.Null(projected.Email);
        Assert.Null(projected.Address);
        Assert.Null(projected.City);
        Assert.Null(projected.County);
        Assert.Null(projected.Postcode);
        Assert.Null(projected.LoyaltyPoints);
        Assert.Equal("Ada Lovelace · 07123456789", projected.DisplayLine);
    }

    [Fact]
    public void ClientWithDeliveryAddress_search_includes_address_and_postcode()
    {
        var projected = CustomerFieldProjector.Project(
            FullCustomer(),
            CustomerFieldAccessPolicy.ClientWithDeliveryAddress,
            CustomerFieldAccessScope.Search);

        Assert.Equal("Ada Lovelace", projected.Name);
        Assert.Equal("07123456789", projected.Phone);
        Assert.Equal("10 Downing Street", projected.Address);
        Assert.Equal("SW1A 2AA", projected.Postcode);
        Assert.Null(projected.Email);
        Assert.Null(projected.LoyaltyPoints);
        Assert.Contains("SW1A 2AA", projected.DisplayLine);
    }

    [Fact]
    public void MotherFull_detail_keeps_all_fields()
    {
        var projected = CustomerFieldProjector.Project(
            FullCustomer(),
            CustomerFieldAccessPolicy.MotherFull,
            CustomerFieldAccessScope.Detail);

        Assert.Equal("ada@example.com", projected.Email);
        Assert.Equal("London", projected.City);
        Assert.Equal(120, projected.LoyaltyPoints);
    }

    [Fact]
    public void ClientDefault_denies_order_history()
    {
        Assert.False(CustomerFieldAccessPolicy.ClientDefault.AllowOrderHistory);
        Assert.True(CustomerFieldAccessPolicy.MotherFull.AllowOrderHistory);
        Assert.True(CustomerFieldAccessPolicy.ClientDefault.AllowAssignCustomer);
    }
}
