namespace POS_in_NET.Services;

/// <summary>
/// Turns a background-job failure into a short till warning, or nothing.
/// Setup gaps (missing label-job list) stay in the log. Staff only see
/// problems they can act on: database, internet, POS link, printer.
/// </summary>
public readonly record struct StaffSyncNotice(int Rank, string Title, string Message)
{
    public static StaffSyncNotice? Select(IEnumerable<BackgroundSyncJobSnapshot> snapshots)
    {
        StaffSyncNotice? best = null;
        foreach (var job in snapshots)
        {
            if (job.Status is not (BackgroundSyncJobStatus.Failed or BackgroundSyncJobStatus.BackingOff)
                || job.ConsecutiveFailures < 2)
            {
                continue;
            }

            var notice = Classify(job.Name, job.LastError);
            if (notice == null)
            {
                continue;
            }

            if (best == null || notice.Value.Rank < best.Value.Rank)
            {
                best = notice;
            }
        }

        return best;
    }

    public static StaffSyncNotice? Classify(string? jobName, string? error)
    {
        if (IsLabelSetupGap(jobName, error) || IsQuietSetup(error))
        {
            return null;
        }

        if (IsDatabaseConnection(error))
        {
            return new StaffSyncNotice(
                0,
                "Database",
                "Cannot reach the database. Orders may not save.");
        }

        if (IsNoInternet(error))
        {
            return new StaffSyncNotice(
                1,
                "No internet",
                "Online orders and loyalty may not update.");
        }

        if (IsPosConnection(jobName, error))
        {
            return IsOrderWebJob(jobName)
                ? new StaffSyncNotice(2, "POS connection", "Cannot reach OrderWeb. Online orders may wait.")
                : new StaffSyncNotice(2, "POS connection", "This till lost connection to the main POS.");
        }

        if (IsPrinterProblem(jobName, error))
        {
            return new StaffSyncNotice(
                3,
                "Printer",
                "A printer cannot print. Check the printer.");
        }

        return null;
    }

    private static bool IsLabelSetupGap(string? jobName, string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        var text = error;
        var missingTable = Contains(text, "doesn't exist") || Contains(text, "does not exist");
        var labelTable = Contains(text, "label_print_job");
        if (labelTable && (missingTable || Contains(text, "Table '")))
        {
            return true;
        }

        return string.Equals(jobName, "network-print-queue", StringComparison.OrdinalIgnoreCase)
            && missingTable
            && Contains(text, "Table '");
    }

    private static bool IsQuietSetup(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return Contains(error, "not configured")
            || Contains(error, "disabled in settings")
            || Contains(error, "is not configured or disabled")
            || Contains(error, "do not run on child");
    }

    private static bool IsDatabaseConnection(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return Contains(error, "Unable to connect to any of the specified MySQL hosts")
            || Contains(error, "Can't connect to MySQL")
            || Contains(error, "Connect Timeout")
            || Contains(error, "MySQL server has gone away")
            || Contains(error, "Access denied for user")
            || Contains(error, "Authentication to host")
            || Contains(error, "Reading from the stream has failed")
            || (Contains(error, "Connection refused") && (Contains(error, "3306") || Contains(error, "MySQL") || Contains(error, "MariaDB")));
    }

    private static bool IsNoInternet(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return Contains(error, "No such host")
            || Contains(error, "could not be resolved")
            || Contains(error, "Name or service not known")
            || Contains(error, "Network is unreachable")
            || Contains(error, "No route to host")
            || Contains(error, "unreachable network")
            || Contains(error, "No internet")
            || Contains(error, "network subsystem is down");
    }

    private static bool IsPosConnection(string? jobName, string? error)
    {
        if (Contains(error, "Mother terminal disconnected"))
        {
            return true;
        }

        if (!IsOrderWebJob(jobName) || string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return Contains(error, "REST API unavailable")
            || Contains(error, "HTTP ")
            || Contains(error, "connection")
            || Contains(error, "Unable to connect")
            || Contains(error, "No connection could be made")
            || Contains(error, "timed out")
            || Contains(error, "timeout")
            || Contains(error, "refused")
            || Contains(error, "unavailable");
    }

    private static bool IsPrinterProblem(string? jobName, string? error)
    {
        if (string.Equals(jobName, "printer-health-check", StringComparison.OrdinalIgnoreCase)
            && (Contains(error, "cannot print") || Contains(error, "offline")))
        {
            return true;
        }

        return Contains(error, "Printer is offline")
            || Contains(error, "cannot print")
            || Contains(error, "printer network address is unreachable")
            || Contains(error, "Send failed");
    }

    private static bool IsOrderWebJob(string? jobName) =>
        string.Equals(jobName, "orderweb-connection-health", StringComparison.OrdinalIgnoreCase)
        || string.Equals(jobName, "cloud-heartbeat", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? text, string value) =>
        !string.IsNullOrEmpty(text)
        && text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
