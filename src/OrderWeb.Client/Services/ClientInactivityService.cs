namespace OrderWeb.Client.Services;

/// <summary>
/// Client idle auto-logout. Staff / Manager / User / Cashier → 3 minutes.
/// </summary>
public sealed class ClientInactivityService
{
    private static readonly TimeSpan StaffManagerUserLogoutTimeout = TimeSpan.FromMinutes(3);
    private readonly object _sync = new();
    private IDispatcherTimer? _timer;
    private DateTime _lastActivityAt = DateTime.Now;
    private bool _isHandling;
    private Func<string?>? _getRole;
    private Action? _logout;
    private bool _windowsHooked;

    public void Start(Func<string?> getRole, Action logout)
    {
        _getRole = getRole ?? throw new ArgumentNullException(nameof(getRole));
        _logout = logout ?? throw new ArgumentNullException(nameof(logout));
        ResetActivity();

        if (_timer is null)
        {
            _timer = Application.Current?.Dispatcher.CreateTimer()
                     ?? throw new InvalidOperationException("No dispatcher available for idle logout.");
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTick;
        }

        if (!_timer.IsRunning)
        {
            _timer.Start();
        }

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
            _isHandling = false;
        }
    }

    public void ResetActivity()
    {
        lock (_sync)
        {
            _lastActivityAt = DateTime.Now;
            _isHandling = false;
        }
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

    private void OnTick(object? sender, EventArgs e)
    {
        string? role;
        TimeSpan elapsed;
        Action? logout;

        lock (_sync)
        {
            role = _getRole?.Invoke();
            logout = _logout;
            elapsed = DateTime.Now - _lastActivityAt;
            if (_isHandling || logout is null || !IsAutoLogoutRole(role))
            {
                return;
            }

            if (elapsed < StaffManagerUserLogoutTimeout)
            {
                return;
            }

            _isHandling = true;
        }

        try
        {
            logout.Invoke();
        }
        finally
        {
            lock (_sync)
            {
                _isHandling = false;
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
