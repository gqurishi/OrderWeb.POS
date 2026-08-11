using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class TimeClockService
{
    private readonly DatabaseService _databaseService;

    public TimeClockService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public TimeClockService() : this(new DatabaseService())
    {
    }

    public async Task<(bool Success, string Message)> EnsureSchemaAsync()
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged)
            return (true, "Schema verified by startup migrations.");
        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            const string sql = """
                CREATE TABLE IF NOT EXISTS time_clock_sessions (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    user_id INT NOT NULL,
                    clock_in_at DATETIME NOT NULL,
                    clock_out_at DATETIME NULL,
                    terminal_in VARCHAR(100) NOT NULL DEFAULT '',
                    terminal_out VARCHAR(100) NULL,
                    business_date DATE NOT NULL,
                    worked_minutes INT NULL,
                    status VARCHAR(20) NOT NULL DEFAULT 'open',
                    synced_at DATETIME NULL,
                    adjustment_note VARCHAR(500) NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                    INDEX idx_time_clock_user_status (user_id, status),
                    INDEX idx_time_clock_business_date (business_date),
                    INDEX idx_time_clock_clock_in (clock_in_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
                """;

            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
            return (true, "Time clock schema ready.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("TimeClockEnsureSchema", ex);
            return (false, "Time clock is not available. Run OrderWeb.DatabaseSetup.exe migrate on the mother terminal.");
        }
    }

    public async Task<TimeClockDashboardState?> GetDashboardStateAsync(User user)
    {
        var schema = await EnsureSchemaAsync();
        if (!schema.Success)
        {
            return null;
        }

        var businessDate = TradingDayHelper.GetBusinessDate();
        await using var connection = await _databaseService.GetConnectionAsync();
        await CloseStaleOpenSessionsAsync(connection, businessDate);
        var openSession = await GetOpenSessionAsync(connection, user.Id, businessDate);
        var todayMinutes = await GetClosedMinutesForBusinessDateAsync(connection, user.Id, businessDate);

        return new TimeClockDashboardState
        {
            User = user,
            OpenSession = openSession,
            TodayWorkedMinutes = todayMinutes,
            BusinessDate = businessDate
        };
    }

    public async Task<(bool Success, string Message)> ClockInAsync(User user)
    {
        var schema = await EnsureSchemaAsync();
        if (!schema.Success)
        {
            return schema;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        var businessDate = TradingDayHelper.GetBusinessDate();
        await CloseStaleOpenSessionsAsync(connection, businessDate);
        var existing = await GetOpenSessionAsync(connection, user.Id, businessDate);
        if (existing != null)
        {
            return (false, $"Already clocked in at {existing.ClockInAt:h:mm tt}. Use Clock Out to finish your shift.");
        }

        var terminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
        const string sql = """
            INSERT INTO time_clock_sessions
                (user_id, clock_in_at, terminal_in, business_date, status)
            VALUES
                (@userId, NOW(), @terminalName, @businessDate, 'open')
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@userId", user.Id);
        command.Parameters.AddWithValue("@terminalName", terminalName);
        command.Parameters.AddWithValue("@businessDate", businessDate);
        await command.ExecuteNonQueryAsync();

        return (true, $"Clocked in at {DateTime.Now:h:mm tt}.");
    }

    public async Task<(bool Success, string Message)> ClockOutAsync(User user)
    {
        var schema = await EnsureSchemaAsync();
        if (!schema.Success)
        {
            return schema;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        var businessDate = TradingDayHelper.GetBusinessDate();
        var openSession = await GetOpenSessionAsync(connection, user.Id, businessDate);
        if (openSession == null)
        {
            return (false, "You are not clocked in.");
        }

        var terminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
        const string sql = """
            UPDATE time_clock_sessions
            SET clock_out_at = NOW(),
                terminal_out = @terminalOut,
                worked_minutes = TIMESTAMPDIFF(MINUTE, clock_in_at, NOW()),
                status = 'closed',
                updated_at = NOW()
            WHERE id = @sessionId
              AND user_id = @userId
              AND status = 'open'
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@terminalOut", terminalName);
        command.Parameters.AddWithValue("@sessionId", openSession.Id);
        command.Parameters.AddWithValue("@userId", user.Id);
        var rows = await command.ExecuteNonQueryAsync();
        if (rows <= 0)
        {
            return (false, "Could not clock out. Please try again.");
        }

        await using var minutesCommand = new MySqlCommand(
            "SELECT worked_minutes FROM time_clock_sessions WHERE id = @sessionId",
            connection);
        minutesCommand.Parameters.AddWithValue("@sessionId", openSession.Id);
        var worked = Convert.ToInt32(await minutesCommand.ExecuteScalarAsync() ?? 0);
        var hours = worked / 60;
        var minutes = worked % 60;

        return (true, $"Clocked out at {DateTime.Now:h:mm tt}. Shift: {hours}h {minutes}m.");
    }

    public async Task<(List<LabourReportRow> Rows, LabourReportSummary Summary)> GetLabourReportAsync(
        DateTime startDate,
        DateTime endDate)
    {
        var schema = await EnsureSchemaAsync();
        if (!schema.Success)
        {
            return ([], new LabourReportSummary());
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        await CloseStaleOpenSessionsAsync(connection, TradingDayHelper.GetBusinessDate());
        const string sql = """
            SELECT s.id, s.user_id, u.name, u.username, s.business_date, s.clock_in_at, s.clock_out_at,
                   s.terminal_in, s.terminal_out, s.worked_minutes, s.status, s.synced_at
            FROM time_clock_sessions s
            INNER JOIN users u ON u.id = s.user_id
            WHERE s.business_date >= @startDate
              AND s.business_date <= @endDate
            ORDER BY s.business_date DESC, u.name, s.clock_in_at
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@startDate", startDate.Date);
        command.Parameters.AddWithValue("@endDate", endDate.Date);

        var rows = new List<LabourReportRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var clockOutAt = reader.IsDBNull(reader.GetOrdinal("clock_out_at"))
                ? (DateTime?)null
                : reader.GetDateTime(reader.GetOrdinal("clock_out_at"));
            var workedMinutes = reader.IsDBNull(reader.GetOrdinal("worked_minutes"))
                ? 0
                : reader.GetInt32(reader.GetOrdinal("worked_minutes"));
            var clockInAt = reader.GetDateTime(reader.GetOrdinal("clock_in_at"));
            if (!clockOutAt.HasValue && reader.GetString(reader.GetOrdinal("status")) == "open")
            {
                workedMinutes = (int)Math.Max(0, (DateTime.Now - clockInAt).TotalMinutes);
            }

            var username = reader.GetString(reader.GetOrdinal("username"));
            var name = reader.IsDBNull(reader.GetOrdinal("name"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("name"));

            rows.Add(new LabourReportRow
            {
                SessionId = reader.GetInt32(reader.GetOrdinal("id")),
                UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                StaffName = string.IsNullOrWhiteSpace(name) ? username : name,
                Username = username,
                BusinessDate = reader.GetDateTime(reader.GetOrdinal("business_date")),
                ClockInAt = clockInAt,
                ClockOutAt = clockOutAt,
                TerminalIn = reader.GetString(reader.GetOrdinal("terminal_in")),
                TerminalOut = reader.IsDBNull(reader.GetOrdinal("terminal_out"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("terminal_out")),
                WorkedMinutes = workedMinutes,
                Status = reader.GetString(reader.GetOrdinal("status")),
                IsSynced = !reader.IsDBNull(reader.GetOrdinal("synced_at"))
            });
        }

        var summary = new LabourReportSummary
        {
            SessionCount = rows.Count,
            StaffCount = rows.Select(r => r.UserId).Distinct().Count(),
            OpenSessionCount = rows.Count(r => r.Status == "open"),
            TotalWorkedMinutes = rows.Sum(r => r.WorkedMinutes)
        };

        return (rows, summary);
    }

    public async Task<OrderWebLabourUploadPayload> BuildCloudLabourPayloadAsync(DateTime businessDate)
    {
        var (rows, summary) = await GetLabourReportAsync(businessDate.Date, businessDate.Date);
        var closedRows = rows.Where(r => r.Status == "closed" || r.ClockOutAt.HasValue).ToList();

        return new OrderWebLabourUploadPayload
        {
            BusinessDate = businessDate.ToString("yyyy-MM-dd"),
            StaffCount = closedRows.Select(r => r.UserId).Distinct().Count(),
            SessionCount = closedRows.Count,
            TotalHours = Math.Round(closedRows.Sum(r => r.WorkedMinutes) / 60m, 2),
            Sessions = closedRows.Select(r => new OrderWebLabourSessionUploadPayload
            {
                SessionId = r.SessionId,
                UserId = r.UserId,
                EmployeeName = r.StaffName,
                Username = r.Username,
                ClockInAt = r.ClockInAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ClockOutAt = r.ClockOutAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                Hours = Math.Round(r.WorkedMinutes / 60m, 2),
                TerminalIn = r.TerminalIn,
                TerminalOut = r.TerminalOut,
                Status = r.Status
            }).ToList()
        };
    }

    public async Task<int> CloseOpenSessionsForDailyUploadAsync(DateTime businessDate)
    {
        var schema = await EnsureSchemaAsync();
        if (!schema.Success)
        {
            return 0;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        const string sql = """
            UPDATE time_clock_sessions
            SET clock_out_at = CASE
                    WHEN business_date < @currentBusinessDate
                        THEN TIMESTAMP(DATE_ADD(business_date, INTERVAL 1 DAY), '01:00:00')
                    ELSE NOW()
                END,
                worked_minutes = GREATEST(
                    1,
                    TIMESTAMPDIFF(
                        MINUTE,
                        clock_in_at,
                        CASE
                            WHEN business_date < @currentBusinessDate
                                THEN TIMESTAMP(DATE_ADD(business_date, INTERVAL 1 DAY), '01:00:00')
                            ELSE NOW()
                        END)),
                status = 'closed',
                terminal_out = COALESCE(NULLIF(terminal_out, ''), terminal_in),
                adjustment_note = COALESCE(
                    adjustment_note,
                    CASE
                        WHEN business_date < @currentBusinessDate
                            THEN 'Auto-closed before daily OrderWeb upload at trading day rollover'
                        ELSE 'Auto-closed before daily OrderWeb upload'
                    END),
                updated_at = NOW()
            WHERE status = 'open'
              AND business_date <= @businessDate
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@businessDate", businessDate.Date);
        command.Parameters.AddWithValue("@currentBusinessDate", TradingDayHelper.GetBusinessDate().Date);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> HasPendingClosedSessionsAsync(DateTime businessDate)
    {
        var schema = await EnsureSchemaAsync();
        if (!schema.Success)
        {
            return false;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        const string sql = """
            SELECT COUNT(*)
            FROM time_clock_sessions
            WHERE business_date = @businessDate
              AND status = 'closed'
              AND synced_at IS NULL
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@businessDate", businessDate.Date);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
        return count > 0;
    }

    public async Task MarkSessionsSyncedForBusinessDateAsync(DateTime businessDate)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        const string sql = """
            UPDATE time_clock_sessions
            SET synced_at = NOW(),
                updated_at = NOW()
            WHERE business_date = @businessDate
              AND status = 'closed'
              AND synced_at IS NULL
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@businessDate", businessDate.Date);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string> ExportLabourCsvAsync(DateTime startDate, DateTime endDate)
    {
        var (rows, summary) = await GetLabourReportAsync(startDate, endDate);
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Staff,Username,BusinessDate,ClockIn,ClockOut,Hours,TerminalIn,TerminalOut,Status,CloudSync");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',',
                Csv(row.StaffName),
                Csv(row.Username),
                row.BusinessDate.ToString("yyyy-MM-dd"),
                Csv(row.ClockInDisplay),
                Csv(row.ClockOutDisplay),
                Csv(row.HoursDisplay),
                Csv(row.TerminalIn),
                Csv(row.TerminalOut ?? string.Empty),
                Csv(row.Status),
                Csv(row.SyncDisplay)));
        }

        builder.AppendLine();
        builder.AppendLine($"Total staff,{summary.StaffCount}");
        builder.AppendLine($"Total sessions,{summary.SessionCount}");
        builder.AppendLine($"Total hours,{summary.TotalHoursDisplay}");
        return builder.ToString();
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static async Task CloseStaleOpenSessionsAsync(
        MySqlConnection connection,
        DateTime currentBusinessDate)
    {
        const string sql = """
            UPDATE time_clock_sessions
            SET clock_out_at = TIMESTAMP(DATE_ADD(business_date, INTERVAL 1 DAY), '01:00:00'),
                worked_minutes = GREATEST(
                    1,
                    TIMESTAMPDIFF(
                        MINUTE,
                        clock_in_at,
                        TIMESTAMP(DATE_ADD(business_date, INTERVAL 1 DAY), '01:00:00'))),
                status = 'closed',
                terminal_out = COALESCE(NULLIF(terminal_out, ''), terminal_in),
                adjustment_note = COALESCE(adjustment_note, 'Auto-closed at 1:00 AM trading day rollover'),
                updated_at = NOW()
            WHERE status = 'open'
              AND business_date < @currentBusinessDate
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@currentBusinessDate", currentBusinessDate.Date);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<TimeClockSessionRecord?> GetOpenSessionAsync(
        MySqlConnection connection,
        int userId,
        DateTime businessDate)
    {
        const string sql = """
            SELECT id, user_id, clock_in_at, clock_out_at, terminal_in, terminal_out,
                   business_date, worked_minutes, status
            FROM time_clock_sessions
            WHERE user_id = @userId
              AND status = 'open'
              AND business_date = @businessDate
            ORDER BY clock_in_at DESC
            LIMIT 1
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@businessDate", businessDate.Date);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return MapSession(reader);
    }

    private static async Task<int> GetClosedMinutesForBusinessDateAsync(
        MySqlConnection connection,
        int userId,
        DateTime businessDate)
    {
        const string sql = """
            SELECT COALESCE(SUM(worked_minutes), 0)
            FROM time_clock_sessions
            WHERE user_id = @userId
              AND business_date = @businessDate
              AND status = 'closed'
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@businessDate", businessDate.Date);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    }

    private static TimeClockSessionRecord MapSession(MySqlDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
            ClockInAt = reader.GetDateTime(reader.GetOrdinal("clock_in_at")),
            ClockOutAt = reader.IsDBNull(reader.GetOrdinal("clock_out_at"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("clock_out_at")),
            TerminalIn = reader.GetString(reader.GetOrdinal("terminal_in")),
            TerminalOut = reader.IsDBNull(reader.GetOrdinal("terminal_out"))
                ? null
                : reader.GetString(reader.GetOrdinal("terminal_out")),
            BusinessDate = reader.GetDateTime(reader.GetOrdinal("business_date")),
            WorkedMinutes = reader.IsDBNull(reader.GetOrdinal("worked_minutes"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("worked_minutes")),
            Status = reader.GetString(reader.GetOrdinal("status"))
        };
}
