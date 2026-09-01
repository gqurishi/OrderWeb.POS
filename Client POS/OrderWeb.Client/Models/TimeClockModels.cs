namespace OrderWeb.Client.Models;

public sealed record TimeClockSessionState(
    int Id,
    string UserId,
    string UserName,
    string Role,
    DateTime ClockInAt,
    DateTime? ClockOutAt,
    string TerminalIn,
    string? TerminalOut,
    DateTime BusinessDate,
    int? WorkedMinutes,
    string Status);

public sealed record TimeClockDashboardState(
    LoginSession User,
    TimeClockSessionState? OpenSession,
    int TodayWorkedMinutes,
    DateTime BusinessDate)
{
    public bool IsClockedIn => OpenSession != null;

    public string TodayHoursDisplay
    {
        get
        {
            var totalMinutes = TodayWorkedMinutes;
            if (OpenSession != null)
            {
                totalMinutes += Math.Max(0, (int)(DateTime.Now - OpenSession.ClockInAt).TotalMinutes);
            }

            return $"{totalMinutes / 60}h {totalMinutes % 60}m";
        }
    }
}
