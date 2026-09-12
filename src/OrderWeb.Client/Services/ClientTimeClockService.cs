using OrderWeb.Client.Models;
using SQLite;

namespace OrderWeb.Client.Services;

/// <summary>Local cache only. Staff Clock on the login screen uses <see cref="MotherTimeClockClient"/>.</summary>
public sealed class ClientTimeClockService
{
    private readonly SQLiteAsyncConnection _database;

    public ClientTimeClockService()
    {
        SQLitePCL.Batteries_V2.Init();

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "orderweb_client_cache.db3");
        _database = new SQLiteAsyncConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    public async Task InitializeAsync()
    {
        foreach (var statement in ClientCacheSchema.CreateStatements)
        {
            await _database.ExecuteAsync(statement);
        }
    }

    public async Task<TimeClockDashboardState> GetDashboardStateAsync(LoginSession user)
    {
        await InitializeAsync();
        var businessDate = TradingDayHelper.GetBusinessDate();
        await CloseStaleOpenSessionsAsync(businessDate);

        var openSession = await GetOpenSessionAsync(user.UserId, businessDate);
        var todayMinutes = await GetClosedMinutesForBusinessDateAsync(user.UserId, businessDate);

        return new TimeClockDashboardState(user, openSession, todayMinutes, businessDate);
    }

    public async Task<(bool Success, string Message)> ClockInAsync(LoginSession user)
    {
        await InitializeAsync();
        var businessDate = TradingDayHelper.GetBusinessDate();
        await CloseStaleOpenSessionsAsync(businessDate);

        var existing = await GetOpenSessionAsync(user.UserId, businessDate);
        if (existing != null)
        {
            return (false, $"Already clocked in at {existing.ClockInAt:h:mm tt}. Use Clock Out to finish your shift.");
        }

        var now = DateTime.Now;
        var utc = DateTimeOffset.UtcNow.ToString("O");
        await _database.ExecuteAsync(
            """
            INSERT INTO time_clock_sessions
                (user_id, user_name, role, clock_in_at, terminal_in, business_date, status, created_utc, updated_utc)
            VALUES
                (?, ?, ?, ?, ?, ?, 'open', ?, ?)
            """,
            user.UserId,
            user.UserName,
            user.Role,
            now.ToString("O"),
            "Client POS",
            businessDate.ToString("yyyy-MM-dd"),
            utc,
            utc);

        return (true, $"Clocked in at {now:h:mm tt}.");
    }

    public async Task<(bool Success, string Message)> ClockOutAsync(LoginSession user)
    {
        await InitializeAsync();
        var businessDate = TradingDayHelper.GetBusinessDate();
        var openSession = await GetOpenSessionAsync(user.UserId, businessDate);
        if (openSession == null)
        {
            return (false, "You are not clocked in.");
        }

        var now = DateTime.Now;
        var workedMinutes = Math.Max(1, (int)(now - openSession.ClockInAt).TotalMinutes);
        await _database.ExecuteAsync(
            """
            UPDATE time_clock_sessions
            SET clock_out_at = ?,
                terminal_out = ?,
                worked_minutes = ?,
                status = 'closed',
                updated_utc = ?
            WHERE id = ?
              AND user_id = ?
              AND status = 'open'
            """,
            now.ToString("O"),
            "Client POS",
            workedMinutes,
            DateTimeOffset.UtcNow.ToString("O"),
            openSession.Id,
            user.UserId);

        return (true, $"Clocked out at {now:h:mm tt}. Shift: {workedMinutes / 60}h {workedMinutes % 60}m.");
    }

    private async Task<TimeClockSessionState?> GetOpenSessionAsync(string userId, DateTime businessDate)
    {
        var rows = await _database.QueryAsync<TimeClockSessionRow>(
            """
            SELECT *
            FROM time_clock_sessions
            WHERE user_id = ?
              AND business_date = ?
              AND status = 'open'
            ORDER BY clock_in_at DESC
            LIMIT 1
            """,
            userId,
            businessDate.ToString("yyyy-MM-dd"));

        return rows.FirstOrDefault()?.ToState();
    }

    private async Task<int> GetClosedMinutesForBusinessDateAsync(string userId, DateTime businessDate)
    {
        return await _database.ExecuteScalarAsync<int>(
            """
            SELECT COALESCE(SUM(worked_minutes), 0)
            FROM time_clock_sessions
            WHERE user_id = ?
              AND business_date = ?
              AND status = 'closed'
            """,
            userId,
            businessDate.ToString("yyyy-MM-dd"));
    }

    private async Task CloseStaleOpenSessionsAsync(DateTime currentBusinessDate)
    {
        var rows = await _database.QueryAsync<TimeClockSessionRow>(
            """
            SELECT *
            FROM time_clock_sessions
            WHERE status = 'open'
              AND business_date < ?
            """,
            currentBusinessDate.ToString("yyyy-MM-dd"));

        foreach (var row in rows)
        {
            if (!DateTime.TryParse(row.business_date, out var businessDate) ||
                !DateTime.TryParse(row.clock_in_at, out var clockInAt))
            {
                continue;
            }

            var closeAt = TradingDayHelper.GetBusinessDayEnd(businessDate);
            var workedMinutes = Math.Max(1, (int)(closeAt - clockInAt).TotalMinutes);
            await _database.ExecuteAsync(
                """
                UPDATE time_clock_sessions
                SET clock_out_at = ?,
                    terminal_out = COALESCE(NULLIF(terminal_out, ''), terminal_in),
                    worked_minutes = ?,
                    status = 'closed',
                    adjustment_note = COALESCE(adjustment_note, 'Auto-closed at trading day rollover'),
                    updated_utc = ?
                WHERE id = ?
                """,
                closeAt.ToString("O"),
                workedMinutes,
                DateTimeOffset.UtcNow.ToString("O"),
                row.id);
        }
    }

    private sealed class TimeClockSessionRow
    {
        public int id { get; set; }
        public string user_id { get; set; } = string.Empty;
        public string user_name { get; set; } = string.Empty;
        public string role { get; set; } = string.Empty;
        public string clock_in_at { get; set; } = string.Empty;
        public string? clock_out_at { get; set; }
        public string terminal_in { get; set; } = string.Empty;
        public string? terminal_out { get; set; }
        public string business_date { get; set; } = string.Empty;
        public int? worked_minutes { get; set; }
        public string status { get; set; } = string.Empty;

        public TimeClockSessionState ToState() =>
            new(
                id,
                user_id,
                user_name,
                role,
                DateTime.Parse(clock_in_at),
                string.IsNullOrWhiteSpace(clock_out_at) ? null : DateTime.Parse(clock_out_at),
                terminal_in,
                terminal_out,
                DateTime.Parse(business_date),
                worked_minutes,
                status);
    }
}
