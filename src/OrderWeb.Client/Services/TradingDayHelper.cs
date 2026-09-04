namespace OrderWeb.Client.Services;

public static class TradingDayHelper
{
    public static readonly TimeSpan DayStartTime = new(1, 0, 0);

    public static DateTime GetBusinessDate(DateTime? localNow = null)
    {
        var now = localNow ?? DateTime.Now;
        var date = now.Date;
        if (now.TimeOfDay < DayStartTime)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    public static DateTime GetBusinessDayEnd(DateTime businessDate) =>
        businessDate.Date.AddDays(1).Add(DayStartTime);
}
