namespace POS_in_NET.Models;

public sealed class TimeClockSessionRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime ClockInAt { get; set; }
    public DateTime? ClockOutAt { get; set; }
    public string TerminalIn { get; set; } = string.Empty;
    public string? TerminalOut { get; set; }
    public DateTime BusinessDate { get; set; }
    public int? WorkedMinutes { get; set; }
    public string Status { get; set; } = "open";
}

public sealed class TimeClockDashboardState
{
    public User User { get; set; } = null!;
    public TimeClockSessionRecord? OpenSession { get; set; }
    public int TodayWorkedMinutes { get; set; }
    public DateTime BusinessDate { get; set; }

    public bool IsClockedIn => OpenSession != null;

    public string TodayHoursDisplay
    {
        get
        {
            var total = TodayWorkedMinutes;
            if (IsClockedIn && OpenSession != null)
            {
                total += (int)Math.Max(0, (DateTime.Now - OpenSession.ClockInAt).TotalMinutes);
            }

            var hours = total / 60;
            var minutes = total % 60;
            return $"{hours}h {minutes}m";
        }
    }
}
