using POS_in_NET.Models;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class TableServiceChargeCalculatorTests
{
    [Fact]
    public void TableOrder_CalculatesChargeAfterDiscount()
    {
        var result = TableServiceChargeCalculator.Calculate("dine_in", 60m, 10m, 10m);

        Assert.Equal(50m, result.ChargeBasis);
        Assert.Equal(5m, result.ServiceCharge);
        Assert.Equal(55m, result.OrderTotal);
    }

    [Theory]
    [InlineData("takeaway")]
    [InlineData("collection")]
    [InlineData("delivery")]
    public void NonTableOrder_NeverReceivesServiceCharge(string orderMode)
    {
        var result = TableServiceChargeCalculator.Calculate(orderMode, 60m, 10m, 10m);

        Assert.Equal(50m, result.ChargeBasis);
        Assert.Equal(0m, result.ServiceCharge);
        Assert.Equal(50m, result.OrderTotal);
    }

    [Fact]
    public void Charge_IsRoundedOnceToTwoDecimalPlacesAwayFromZero()
    {
        var result = TableServiceChargeCalculator.Calculate("dine_in", 19.99m, 0m, 12.5m);

        Assert.Equal(2.50m, result.ServiceCharge);
        Assert.Equal(22.49m, result.OrderTotal);
    }

    [Fact]
    public void DiscountCannotCreateNegativeBasisOrCharge()
    {
        var result = TableServiceChargeCalculator.Calculate("dine_in", 10m, 15m, 10m);

        Assert.Equal(0m, result.ChargeBasis);
        Assert.Equal(0m, result.ServiceCharge);
        Assert.Equal(0m, result.OrderTotal);
    }

    [Fact]
    public void RemovedCharge_KeepsBasisButAddsNoCharge()
    {
        var result = TableServiceChargeCalculator.Calculate("dine_in", 60m, 10m, 10m, isApplied: false);

        Assert.Equal(50m, result.ChargeBasis);
        Assert.Equal(0m, result.ServiceCharge);
        Assert.Equal(50m, result.OrderTotal);
    }

    [Fact]
    public void InvalidPercentageIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TableServiceChargeCalculator.Calculate("dine_in", 10m, 0m, 100.01m));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(12.5, 12.5)]
    [InlineData(7.25, 7.25)]
    public void SupportedRates_CalculateExpectedCharge(double percentage, double expected)
    {
        var result = TableServiceChargeCalculator.Calculate(
            "dine_in",
            100m,
            0m,
            (decimal)percentage,
            isApplied: percentage > 0);

        Assert.Equal((decimal)expected, result.ServiceCharge);
    }

    [Fact]
    public void ExistingSnapshot_IsUnaffectedByLaterAdminRate()
    {
        var existingOrder = TableServiceChargeCalculator.Calculate("dine_in", 80m, 0m, 10m);
        var newOrder = TableServiceChargeCalculator.Calculate("dine_in", 80m, 0m, 12.5m);

        Assert.Equal(8m, existingOrder.ServiceCharge);
        Assert.Equal(10m, newOrder.ServiceCharge);
    }

    [Fact]
    public void RemovedConfiguredCharge_DoesNotOfferTip()
    {
        Assert.False(TableOrderFinancialPolicy.ShouldOfferTip(false, TableServiceChargeStatus.Removed, 10m));
        Assert.False(TableOrderFinancialPolicy.ShouldOfferTip(false, TableServiceChargeStatus.Applied, 10m));
        Assert.False(TableOrderFinancialPolicy.ShouldOfferTip(false, TableServiceChargeStatus.NotConfigured, 10m));
        Assert.True(TableOrderFinancialPolicy.ShouldOfferTip(false, TableServiceChargeStatus.NotConfigured, 0m));
        Assert.False(TableOrderFinancialPolicy.ShouldOfferTip(true, TableServiceChargeStatus.Applied, 10m));
        Assert.False(TableOrderFinancialPolicy.ShouldOfferTip(true, TableServiceChargeStatus.NotConfigured, 0m));
    }

    [Fact]
    public void PartiallyPaidOrder_CannotChangeServiceCharge()
    {
        Assert.False(TableOrderFinancialPolicy.CanChangeServiceCharge(
            true, 10m, TableServiceChargeStatus.Applied, true, 0.01m));
        Assert.True(TableOrderFinancialPolicy.CanChangeServiceCharge(
            true, 10m, TableServiceChargeStatus.Applied, true, 0m));
    }

    [Fact]
    public void OpenUnpaidLegacyTableOrder_AdoptsActiveConfiguredCharge()
    {
        Assert.True(TableOrderFinancialPolicy.ShouldAdoptCurrentServiceCharge(
            true, true, TableServiceChargeStatus.NotConfigured, 0m, true, 0m));
    }

    [Theory]
    [InlineData(TableServiceChargeStatus.Applied, 12, true, 0)]
    [InlineData(TableServiceChargeStatus.Removed, 12, true, 0)]
    [InlineData(TableServiceChargeStatus.NotConfigured, 0, false, 0)]
    [InlineData(TableServiceChargeStatus.NotConfigured, 0, true, 1)]
    public void ProtectedExistingOrder_DoesNotAdoptCurrentCharge(
        TableServiceChargeStatus status,
        decimal percentage,
        bool isModifiable,
        decimal totalPaid)
    {
        Assert.False(TableOrderFinancialPolicy.ShouldAdoptCurrentServiceCharge(
            true, true, status, percentage, isModifiable, totalPaid));
    }

    [Fact]
    public void TipAllocation_ReconcilesAcrossSplitPayments()
    {
        var first = TableOrderFinancialPolicy.AllocateTip(10m, 55m, 110m);
        var second = TableOrderFinancialPolicy.AllocateTip(10m - first, 55m, 55m);

        Assert.Equal(5m, first);
        Assert.Equal(5m, second);
        Assert.Equal(10m, first + second);
    }
}
