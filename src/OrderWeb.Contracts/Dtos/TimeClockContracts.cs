namespace OrderWeb.Contracts.Dtos;

/// <summary>Client → Mother staff clock. Mother MySQL is the shift ledger.</summary>
public static class TimeClockErrorCodes
{
    public const string OfflineMother = "time_clock.offline_mother";
    public const string Validation = "time_clock.validation";
    public const string WrongPin = "time_clock.wrong_pin";
    public const string Unavailable = "time_clock.unavailable";
    public const string AlreadyClockedIn = "time_clock.already_in";
    public const string NotClockedIn = "time_clock.not_in";
}

public sealed record ClientTimeClockRequestDto(string Pin, string? TerminalToken = null);

public sealed record ClientTimeClockResponseDto(
    bool Success,
    string? Message,
    string? Error = null,
    string? ErrorCode = null,
    string? StaffName = null,
    bool IsClockedIn = false,
    string? ClockInTimeDisplay = null,
    string? TodayHoursDisplay = null);
