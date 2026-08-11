namespace POS_in_NET.Services;

/// <summary>
/// Restaurant trading day rolls at 1:00 AM local time (hours before 1 AM belong to the previous day).
/// </summary>
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

    public static DateTime GetBusinessDayStart(DateTime businessDate) =>
        businessDate.Date.Add(DayStartTime);

    public static DateTime GetBusinessDayEnd(DateTime businessDate) =>
        businessDate.Date.AddDays(1).Add(DayStartTime);
}
