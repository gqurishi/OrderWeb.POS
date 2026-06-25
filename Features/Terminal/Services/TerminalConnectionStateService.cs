namespace POS_in_NET.Services;

public sealed class TerminalConnectionStateChangedEventArgs : EventArgs
{
    public bool IsConnected { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime CheckedAt { get; init; } = DateTime.Now;
}

public static class TerminalConnectionStateService
{
    private static readonly object SyncRoot = new();
    private static bool _isConnected = true;
    private static string _message = "Shared database connected";
    private static DateTime _checkedAt = DateTime.Now;

    public static event EventHandler<TerminalConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public static bool IsConnected
    {
        get
        {
            lock (SyncRoot)
            {
                return _isConnected;
            }
        }
    }

    public static bool IsMotherDisconnectedBannerVisible =>
        TerminalConfigurationService.IsChildTerminal && !IsConnected;

    public static string Message
    {
        get
        {
            lock (SyncRoot)
            {
                return _message;
            }
        }
    }

    public static DateTime CheckedAt
    {
        get
        {
            lock (SyncRoot)
            {
                return _checkedAt;
            }
        }
    }

    public static void ReportConnected()
    {
        Update(true, "Shared mother database connected");
    }

    public static void ReportDisconnected(string message)
    {
        Update(false, string.IsNullOrWhiteSpace(message)
            ? "Mother terminal disconnected. Check network and mother terminal power."
            : message.Trim());
    }

    private static void Update(bool isConnected, string message)
    {
        TerminalConnectionStateChangedEventArgs? args = null;

        lock (SyncRoot)
        {
            var changed = _isConnected != isConnected || !string.Equals(_message, message, StringComparison.Ordinal);
            _isConnected = isConnected;
            _message = message;
            _checkedAt = DateTime.Now;

            if (changed)
            {
                args = new TerminalConnectionStateChangedEventArgs
                {
                    IsConnected = _isConnected,
                    Message = _message,
                    CheckedAt = _checkedAt
                };
            }
        }

        if (args != null)
        {
            MainThread.BeginInvokeOnMainThread(() => ConnectionStateChanged?.Invoke(null, args));
        }
    }
}
