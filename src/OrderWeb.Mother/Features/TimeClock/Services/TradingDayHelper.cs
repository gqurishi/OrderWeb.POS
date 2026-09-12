namespace POS_in_NET.Services;

/// <summary>
/// Restaurant trading day rolls at 3:00 AM local time (hours before 3 AM belong to the previous day).
/// </summary>
public static class TradingDayHelper
{
    public static readonly TimeSpan DayStartTime = new(3, 0, 0);

    /// <summary>Short clock label for the dashboard, e.g. "3:00am".</summary>
    public static string ResetTimeDisplay =>
        DateTime.Today.Add(DayStartTime).ToString("h:mmtt", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();

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
