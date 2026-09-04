using POS_in_NET.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class RiderOperationPolicyTests
{
    [Fact]
    public void OperationalDay_BeforeTwoAm_BelongsToPreviousDate()
    {
        var now = new DateTime(2026, 8, 11, 1, 30, 0);

        Assert.Equal(new DateTime(2026, 8, 10, 2, 0, 0), RiderOperationPolicy.GetDayStart(now));
        Assert.Equal(new DateTime(2026, 8, 11, 2, 0, 0), RiderOperationPolicy.GetDayEnd(now));
    }

    [Fact]
    public void OperationalDay_AtTwoAm_StartsNewDate()
    {
        var now = new DateTime(2026, 8, 11, 2, 0, 0);

        Assert.Equal(now, RiderOperationPolicy.GetDayStart(now));
    }

    [Theory]
    [InlineData("card", "paid", 22.50, 0)]
    [InlineData("cash", "pending", 22.50, 22.50)]
    [InlineData("gift_card", "partial", 22.50, 7.50)]
    public void CashCollection_UsesSettlementNotTenderLabel(string method, string status, decimal total, decimal expected)
    {
        var amountPaid = method == "gift_card" ? 15m : (decimal?)null;

        Assert.Equal(expected, RiderOperationPolicy.CalculateCashCollection(total, amountPaid, method, status));
    }

    [Fact]
    public void Quote_CannotBeConfirmedAfterExpiry()
    {
        var now = new DateTime(2026, 8, 11, 20, 0, 0);

        Assert.False(RiderOperationPolicy.CanConfirmQuote("quote_available", "quote-1", now.AddSeconds(-1), now));
        Assert.True(RiderOperationPolicy.CanConfirmQuote("quote_available", "quote-1", now.AddMinutes(2), now));
    }
}
