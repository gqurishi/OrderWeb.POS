using POS_in_NET.Models;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public class TableServiceChargePolicyTests
{
    [Fact]
    public void DisabledMode_NormalizesPercentageToZero()
    {
        var result = TableServiceChargePolicy.Validate(false, 12.5m);

        Assert.True(result.IsValid);
        Assert.Equal(0m, result.NormalizedPercentage);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("10")]
    [InlineData("12.50")]
    [InlineData("30.00")]
    public void EnabledMode_AcceptsSupportedPercentages(string value)
    {
        var percentage = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        var result = TableServiceChargePolicy.Validate(true, percentage);

        Assert.True(result.IsValid);
        Assert.Equal(percentage, result.NormalizedPercentage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("30.01")]
    [InlineData("100")]
    [InlineData("12.345")]
    public void EnabledMode_RejectsInvalidPercentages(string value)
    {
        var percentage = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(TableServiceChargePolicy.Validate(true, percentage).IsValid);
    }

    [Fact]
    public void ExampleCalculation_RoundsToPence()
    {
        Assert.Equal(12.50m, TableServiceChargePolicy.CalculateExampleCharge(12.5m));
        Assert.Equal(3.34m, TableServiceChargePolicy.CalculateExampleCharge(3.335m));
    }
}
