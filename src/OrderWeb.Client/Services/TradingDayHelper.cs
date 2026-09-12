namespace OrderWeb.Client.Services;

public static class TradingDayHelper
{
    /// <summary>Trading day rolls at 3:00 AM. Hours before 3 AM belong to the previous day.</summary>
    public static readonly TimeSpan DayStartTime = new(3, 0, 0);

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
