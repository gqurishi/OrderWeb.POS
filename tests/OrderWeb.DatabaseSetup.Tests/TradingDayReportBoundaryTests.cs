using POS_in_NET.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class TradingDayReportBoundaryTests
{
    [Theory]
    [InlineData(2026, 7, 22, 0, 59, 2026, 7, 21)]
    [InlineData(2026, 7, 22, 1, 0, 2026, 7, 22)]
    [InlineData(2026, 7, 22, 23, 59, 2026, 7, 22)]
    public void BusinessDate_RollsOverAtOneAm(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var actual = TradingDayHelper.GetBusinessDate(new DateTime(year, month, day, hour, minute, 0));

        Assert.Equal(new DateTime(expectedYear, expectedMonth, expectedDay), actual);
    }

    [Fact]
    public void BusinessDayRange_IsOneAmToNextOneAm()
    {
        var businessDate = new DateTime(2026, 7, 21);

        Assert.Equal(new DateTime(2026, 7, 21, 1, 0, 0), TradingDayHelper.GetBusinessDayStart(businessDate));
        Assert.Equal(new DateTime(2026, 7, 22, 1, 0, 0), TradingDayHelper.GetBusinessDayEnd(businessDate));
    }
}
