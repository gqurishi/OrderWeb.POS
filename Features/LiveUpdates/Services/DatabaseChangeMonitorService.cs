using MySqlConnector;

namespace POS_in_NET.Services;

public static class DatabaseChangeMonitorService
{
    private static readonly object SyncRoot = new();
    private static Timer? _timer;
    private static bool _isPolling;
    private static bool _isInitialized;
    private static long _lastSeenEventId;
    private static DateTime _lastPrunedAt = DateTime.MinValue;

    public static void Start()
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new Timer(
                _ =>
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await PollAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LiveUpdate] Timer error: {ex.Message}");
                        }
                    });
                },
                null,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(2.5));
        }
    }

    public static void Stop()
    {
        lock (SyncRoot)
        {
            _timer?.Dispose();
            _timer = null;
            _isInitialized = false;
            _lastSeenEventId = 0;
        }
    }

    private static async Task PollAsync()
    {
        if (_isPolling || !TerminalConfigurationService.IsConfigured)
        {
            return;
        }

        _isPolling = true;
        try
        {
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString(
                connectionTimeoutSeconds: 2,
                defaultCommandTimeoutSeconds: 3));
            await connection.OpenAsync();
            TerminalConnectionStateService.ReportConnected();

            if (!_isInitialized)
            {
                _lastSeenEventId = await TerminalEventSyncService.GetLatestEventIdAsync(connection);
                _isInitialized = true;
                return;
            }

            if ((DateTime.UtcNow - _lastPrunedAt).TotalHours >= 6)
            {
                await TerminalEventSyncService.PruneOldEventsAsync(connection);
                _lastPrunedAt = DateTime.UtcNow;
            }

            var events = await TerminalEventSyncService.GetEventsAfterAsync(connection, _lastSeenEventId);
            foreach (var terminalEvent in events)
            {
                _lastSeenEventId = Math.Max(_lastSeenEventId, terminalEvent.Id);
                if (IsFromCurrentTerminal(terminalEvent.SourceTerminalName))
                {
                    continue;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                    AppDataRefreshService.RequestRefresh(
                        terminalEvent.Kind,
                        terminalEvent.SourceTerminalName,
                        terminalEvent.OrderNumber));
            }
        }
        catch (Exception ex)
        {
            if (TerminalConfigurationService.IsChildTerminal)
            {
                TerminalConnectionStateService.ReportDisconnected("Mother terminal disconnected. Check the mother terminal, network, and MariaDB.");
            }

            System.Diagnostics.Debug.WriteLine($"[LiveUpdate] Poll skipped: {ex.Message}");
        }
        finally
        {
            _isPolling = false;
        }
    }

    private static bool IsFromCurrentTerminal(string? sourceTerminalName)
    {
        if (string.IsNullOrWhiteSpace(sourceTerminalName))
        {
            return false;
        }

        try
        {
            var currentTerminal = TerminalConfigurationService.GetConfiguration().TerminalName;
            return !string.IsNullOrWhiteSpace(currentTerminal)
                && string.Equals(sourceTerminalName.Trim(), currentTerminal.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
