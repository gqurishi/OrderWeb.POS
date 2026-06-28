namespace POS_in_NET.Models;

public sealed class LabourReportRow
{
    public int SessionId { get; set; }
    public int UserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public DateTime ClockInAt { get; set; }
    public DateTime? ClockOutAt { get; set; }
    public string TerminalIn { get; set; } = string.Empty;
    public string? TerminalOut { get; set; }
    public int WorkedMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsSynced { get; set; }

    public string ClockInDisplay => ClockInAt.ToString("dd/MM HH:mm");
    public string ClockOutDisplay => ClockOutAt?.ToString("dd/MM HH:mm") ?? "Open";
    public string HoursDisplay
    {
        get
        {
            var minutes = WorkedMinutes;
            if (!ClockOutAt.HasValue && ClockInAt.Date <= DateTime.Today)
            {
                minutes += (int)Math.Max(0, (DateTime.Now - ClockInAt).TotalMinutes);
            }

            return $"{minutes / 60}h {minutes % 60}m";
        }
    }

    public string SyncDisplay => IsSynced ? "Synced" : "Pending";

    public bool IsOpenShift => string.Equals(Status, "open", StringComparison.OrdinalIgnoreCase);

    public string ShiftStatusDisplay => IsOpenShift ? "On shift" : "Clocked out";
}

public sealed class LabourReportSummary
{
    public int StaffCount { get; set; }
    public int SessionCount { get; set; }
    public int TotalWorkedMinutes { get; set; }
    public int OpenSessionCount { get; set; }

    public string TotalHoursDisplay => $"{TotalWorkedMinutes / 60}h {TotalWorkedMinutes % 60}m";
}

public sealed class OrderWebLabourUploadPayload
{
    public string BusinessDate { get; set; } = string.Empty;
    public int StaffCount { get; set; }
    public int SessionCount { get; set; }
    public decimal TotalHours { get; set; }
    public List<OrderWebLabourSessionUploadPayload> Sessions { get; set; } = [];
}

public sealed class OrderWebLabourSessionUploadPayload
{
    public int SessionId { get; set; }
    public int UserId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ClockInAt { get; set; } = string.Empty;
    public string? ClockOutAt { get; set; }
    public decimal Hours { get; set; }
    public string TerminalIn { get; set; } = string.Empty;
    public string? TerminalOut { get; set; }
    public string Status { get; set; } = string.Empty;
}
