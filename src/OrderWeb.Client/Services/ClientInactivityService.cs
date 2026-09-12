using OrderWeb.Contracts.Access;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client idle: 60s back to the role dashboard, then logout after Mother's saved minutes.
/// Staff / Manager / User / Cashier share that logout time. User / Manager / Cashier also
/// return home at 60s. Missing or offline value stays at 3 minutes.
/// </summary>
public sealed class ClientInactivityService
{
    private static readonly TimeSpan DashboardReturnTimeout = TimeSpan.FromSeconds(60);
    private TimeSpan _logoutTimeout = TimeSpan.FromMinutes(ClientTillLogoutStore.GetMinutes());
    private readonly object _sync = new();
    private IDispatcherTimer? _timer;
    private DateTime _lastActivityAt = DateTime.Now;
    private DateTime _suppressActivityResetUntil = DateTime.MinValue;
    private bool _isHandling;
    private bool _hasReturnedToDashboard;
    private Func<string?>? _getRole;
    private Action? _logout;
    private Func<Task>? _returnToDashboard;
    private Func<bool>? _isBusy;
    private bool _windowsHooked;

    public void SetLogoutMinutes(int minutes)
    {
        lock (_sync)
        {
            _logoutTimeout = TimeSpan.FromMinutes(TillLogoutMinutes.Normalize(minutes));
        }

        ScheduleNextCheck();
    }

    public void Start(Func<string?> getRole, Action logout, Func<Task>? returnToDashboard = null, Func<bool>? isBusy = null)
    {
        lock (_sync)
        {
            _logoutTimeout = TimeSpan.FromMinutes(ClientTillLogoutStore.GetMinutes());
        }
        _getRole = getRole ?? throw new ArgumentNullException(nameof(getRole));
        _logout = logout ?? throw new ArgumentNullException(nameof(logout));
        _returnToDashboard = returnToDashboard;
        _isBusy = isBusy;
        ResetActivity();

        if (_timer is null)
        {
            _timer = Application.Current?.Dispatcher.CreateTimer()
                     ?? throw new InvalidOperationException("No dispatcher available for idle logout.");
            _timer.IsRepeating = false;
            _timer.Tick += OnTick;
        }

        ScheduleNextCheck();
        TryHookWindowsInput();
    }

    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
        }

        lock (_sync)
        {
            _getRole = null;
            _logout = null;
            _returnToDashboard = null;
            _isBusy = null;
            _isHandling = false;
            _hasReturnedToDashboard = false;
        }
    }

    public void ResetActivity()
    {
        lock (_sync)
        {
            if (_isHandling || DateTime.Now < _suppressActivityResetUntil)
            {
                return;
            }

            _lastActivityAt = DateTime.Now;
            _hasReturnedToDashboard = false;
        }

        ScheduleNextCheck();
    }

    /// <summary>Call from pages so taps/typing reset the idle clock while logged in.</summary>
    public void TrackPage(Page? page)
    {
        TryHookWindowsInput();
        if (page is null)
        {
            return;
        }

        try
        {
            AttachActivityHandlers(page);
            if (page is ContentPage { Content: VisualElement content })
            {
                AttachActivityHandlers(content);
            }
        }
        catch
        {
            // Tracking is best-effort; timer still runs from last known activity.
        }
    }

    private void ScheduleNextCheck()
    {
        if (_timer is null)
        {
            return;
        }

        TimeSpan remaining;
        string? role;
        lock (_sync)
        {
            if (_getRole is null || _logout is null)
            {
                _timer.Stop();
                return;
            }

            role = _getRole.Invoke();
            var elapsed = DateTime.Now - _lastActivityAt;
            remaining = _logoutTimeout - elapsed;
            if (IsDashboardReturnRole(role) && !_hasReturnedToDashboard)
            {
                var untilHome = DashboardReturnTimeout - elapsed;
                if (untilHome < remaining)
                {
                    remaining = untilHome;
                }
            }
        }

        if (!IsAutoLogoutRole(role))
        {
            // Rarely re-check role changes without waking every second.
            _timer.Stop();
            _timer.Interval = TimeSpan.FromSeconds(60);
            _timer.Start();
            return;
        }

        if (remaining < TimeSpan.FromMilliseconds(50))
        {
            remaining = TimeSpan.FromMilliseconds(50);
        }

        _timer.Stop();
        _timer.Interval = remaining;
        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        string? role;
        TimeSpan elapsed;
        Action? logout;
        Func<Task>? returnHome;

        if (_isBusy?.Invoke() == true)
        {
            // Payment / dialog is open — don't jump home or log out underneath it.
            ResetActivity();
            return;
        }

        lock (_sync)
        {
            role = _getRole?.Invoke();
            logout = _logout;
            returnHome = _returnToDashboard;
            elapsed = DateTime.Now - _lastActivityAt;
            if (_isHandling || logout is null)
            {
                return;
            }

            if (!IsAutoLogoutRole(role))
            {
                ScheduleNextCheck();
                return;
            }

            if (elapsed >= _logoutTimeout)
            {
                _isHandling = true;
                returnHome = null;
            }
            else if (IsDashboardReturnRole(role) && !_hasReturnedToDashboard && elapsed >= DashboardReturnTimeout)
            {
                _isHandling = true;
                _suppressActivityResetUntil = DateTime.Now.AddSeconds(2);
                logout = null;
            }
            else
            {
                ScheduleNextCheck();
                return;
            }
        }

        var loggedOut = false;
        try
        {
            if (logout is not null)
            {
                loggedOut = true;
                logout.Invoke();
                return;
            }

            if (returnHome is not null)
            {
                await returnHome.Invoke();
            }

            lock (_sync)
            {
                _hasReturnedToDashboard = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Idle dashboard return failed: {ex.Message}");
        }
        finally
        {
            lock (_sync)
            {
                _isHandling = false;
            }

            if (!loggedOut)
            {
                ScheduleNextCheck();
            }
        }
    }

    private static bool IsAutoLogoutRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        return role.Equals("Staff", StringComparison.OrdinalIgnoreCase)
               || role.Equals("Manager", StringComparison.OrdinalIgnoreCase)
               || role.Equals("User", StringComparison.OrdinalIgnoreCase)
               || role.Equals("Cashier", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDashboardReturnRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        return role.Equals("Manager", StringComparison.OrdinalIgnoreCase)
               || role.Equals("User", StringComparison.OrdinalIgnoreCase)
               || role.Equals("Cashier", StringComparison.OrdinalIgnoreCase);
    }

    private void AttachActivityHandlers(VisualElement element)
    {
        element.HandlerChanged -= OnElementHandlerChanged;
        element.HandlerChanged += OnElementHandlerChanged;
        WirePlatformInput(element);
    }

    private void OnElementHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            WirePlatformInput(element);
        }
    }

    private void WirePlatformInput(VisualElement element)
    {
#if WINDOWS
        if (element.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement ui)
        {
            ui.PointerPressed -= OnWindowsPointer;
            ui.PointerMoved -= OnWindowsPointer;
            ui.PointerWheelChanged -= OnWindowsPointer;
            ui.KeyDown -= OnWindowsKeyDown;
            ui.PointerPressed += OnWindowsPointer;
            ui.PointerMoved += OnWindowsPointer;
            ui.PointerWheelChanged += OnWindowsPointer;
            ui.KeyDown += OnWindowsKeyDown;
        }
#endif
    }

    private void TryHookWindowsInput()
    {
#if WINDOWS
        if (_windowsHooked)
        {
            return;
        }

        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
        {
            return;
        }

        if (window.Content is Microsoft.UI.Xaml.UIElement root)
        {
            root.PointerPressed -= OnWindowsPointer;
            root.PointerMoved -= OnWindowsPointer;
            root.PointerWheelChanged -= OnWindowsPointer;
            root.KeyDown -= OnWindowsKeyDown;
            root.PointerPressed += OnWindowsPointer;
            root.PointerMoved += OnWindowsPointer;
            root.PointerWheelChanged += OnWindowsPointer;
            root.KeyDown += OnWindowsKeyDown;
            _windowsHooked = true;
        }
#endif
    }

#if WINDOWS
    private void OnWindowsPointer(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ResetActivity();

    private void OnWindowsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e) =>
        ResetActivity();
#endif
}
