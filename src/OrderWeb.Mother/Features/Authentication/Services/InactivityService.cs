using System.Runtime.CompilerServices;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class InactivityService
{
    private static readonly TimeSpan DashboardReturnTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaffLogoutTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AdminLogoutTimeout = TimeSpan.FromMinutes(2);
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly ConditionalWeakTable<VisualElement, object> _trackedElements = new();
    private readonly List<Func<Task>> _beforeIdleReturnHandlers = new();
    private readonly object _sync = new();
    private System.Timers.Timer? _timer;
    private DateTime _lastActivityAt = DateTime.Now;
    private DateTime _suppressActivityResetUntil = DateTime.MinValue;
    private int _criticalActivityDepth;
    private bool _isHandlingIdle;
    private bool _hasReturnedToDashboardForIdlePeriod;
    private BackgroundSyncManager? _backgroundSyncManager;

    public InactivityService(AuthenticationService authService, RoleAccessService roleAccessService)
    {
        _authService = authService;
        _roleAccessService = roleAccessService;
    }

    public void Start()
    {
        if (_timer != null)
        {
            return;
        }

        _timer = new System.Timers.Timer(5000)
        {
            AutoReset = true,
            Enabled = true
        };
        _timer.Elapsed += (_, _) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await CheckIdleAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Inactivity timer error: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                }
            });
        };
        _timer.Start();
    }

    public void ResetActivity()
    {
        lock (_sync)
        {
            if (DateTime.Now < _suppressActivityResetUntil)
            {
                return;
            }

            _lastActivityAt = DateTime.Now;
            _hasReturnedToDashboardForIdlePeriod = false;
        }

        GetBackgroundSyncManager()?.NotifyUserActivity();
    }

    public IDisposable BeginCriticalActivity()
    {
        lock (_sync)
        {
            _criticalActivityDepth++;
            _lastActivityAt = DateTime.Now;
            _hasReturnedToDashboardForIdlePeriod = false;
        }

        var backgroundSyncScope = GetBackgroundSyncManager()?.BeginCriticalActivity();
        return new CriticalActivityScope(this, backgroundSyncScope);
    }

    private BackgroundSyncManager? GetBackgroundSyncManager()
    {
        return _backgroundSyncManager ??= ServiceHelper.GetService<BackgroundSyncManager>();
    }

    public IDisposable RegisterBeforeIdleReturnHandler(Func<Task> handler)
    {
        lock (_sync)
        {
            _beforeIdleReturnHandlers.Add(handler);
        }

        return new RegisteredHandler(this, handler);
    }

    public void TrackPage(Page? page)
    {
        try
        {
            if (page is ContentPage contentPage)
            {
                TrackElement(contentPage.Content);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TrackPage skipped: {ex.Message}");
        }
    }

    private void TrackElement(Element? element, int depth = 0)
    {
        if (element == null || depth > 8)
        {
            return;
        }

        try
        {
            if (ShouldSkipElement(element))
            {
                return;
            }

            if (element is VisualElement visualElement && !_trackedElements.TryGetValue(visualElement, out _))
            {
                _trackedElements.Add(visualElement, new object());
                AttachActivityHandlers(visualElement);
            }

            foreach (var child in GetChildElements(element))
            {
                TrackElement(child, depth + 1);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TrackElement skipped: {ex.Message}");
        }
    }

    private static bool ShouldSkipElement(Element element)
    {
        var typeName = element.GetType().FullName ?? string.Empty;
        return typeName.Contains("Syncfusion", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("CollectionView", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("SfDataGrid", StringComparison.OrdinalIgnoreCase);
    }

    private void AttachActivityHandlers(VisualElement element)
    {
        if (element is View view)
        {
            foreach (var gesture in view.GestureRecognizers)
            {
                if (gesture is TapGestureRecognizer tapGesture)
                {
                    tapGesture.Tapped += (_, _) => ResetActivity();
                }
            }
        }

        switch (element)
        {
            case Button button:
                button.Clicked += (_, _) => ResetActivity();
                break;
            case ImageButton imageButton:
                imageButton.Clicked += (_, _) => ResetActivity();
                break;
            case Entry entry:
                entry.TextChanged += (_, _) => ResetActivity();
                entry.Focused += (_, _) => ResetActivity();
                break;
            case Editor editor:
                editor.TextChanged += (_, _) => ResetActivity();
                editor.Focused += (_, _) => ResetActivity();
                break;
            case SearchBar searchBar:
                searchBar.TextChanged += (_, _) => ResetActivity();
                searchBar.SearchButtonPressed += (_, _) => ResetActivity();
                break;
            case Picker picker:
                picker.SelectedIndexChanged += (_, _) => ResetActivity();
                break;
            case CollectionView collectionView:
                collectionView.SelectionChanged += (_, _) => ResetActivity();
                break;
            case DatePicker datePicker:
                datePicker.DateSelected += (_, _) => ResetActivity();
                break;
            case Slider slider:
                slider.ValueChanged += (_, _) => ResetActivity();
                break;
            case Stepper stepper:
                stepper.ValueChanged += (_, _) => ResetActivity();
                break;
            case Switch switchControl:
                switchControl.Toggled += (_, _) => ResetActivity();
                break;
            case CheckBox checkBox:
                checkBox.CheckedChanged += (_, _) => ResetActivity();
                break;
            case RadioButton radioButton:
                radioButton.CheckedChanged += (_, _) => ResetActivity();
                break;
        }
    }

    private static IEnumerable<Element> GetChildElements(Element element)
    {
        switch (element)
        {
            case ContentPage contentPage when contentPage.Content != null:
                yield return contentPage.Content;
                break;
            case ContentView contentView when contentView.Content != null:
                yield return contentView.Content;
                break;
            case ScrollView scrollView when scrollView.Content != null:
                yield return scrollView.Content;
                break;
            case Border border when border.Content != null:
                yield return border.Content;
                break;
            case Frame frame when frame.Content != null:
                yield return frame.Content;
                break;
            case Layout layout:
                foreach (var child in layout.Children)
                {
                    if (child is Element childElement)
                    {
                        yield return childElement;
                    }
                }
                break;
        }
    }

    private async Task CheckIdleAsync()
    {
        if (_isHandlingIdle || _authService.CurrentUser == null)
        {
            return;
        }

        UserRole role;
        TimeSpan elapsed;
        IdleAction idleAction;

        lock (_sync)
        {
            role = _authService.CurrentUser.Role;
            elapsed = DateTime.Now - _lastActivityAt;

            // Only walk the visual tree when idle is near a threshold — not every tick.
            var nearIdle = elapsed >= DashboardReturnTimeout - TimeSpan.FromSeconds(10)
                || elapsed >= StaffLogoutTimeout - TimeSpan.FromSeconds(10)
                || (role == UserRole.Admin && elapsed >= AdminLogoutTimeout - TimeSpan.FromSeconds(10));
            if (nearIdle && HasActiveModalOrPopup())
            {
                _lastActivityAt = DateTime.Now;
                return;
            }

            if (_criticalActivityDepth > 0)
            {
                _lastActivityAt = DateTime.Now;
                return;
            }

            idleAction = ResolveIdleAction(role, elapsed);
            if (idleAction == IdleAction.None)
            {
                return;
            }

            _isHandlingIdle = true;
        }

        try
        {
            await HandleIdleAsync(idleAction);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Idle action failed ({idleAction}): {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
        }
        finally
        {
            lock (_sync)
            {
                _isHandlingIdle = false;
            }
        }
    }

    private IdleAction ResolveIdleAction(UserRole role, TimeSpan elapsed)
    {
        if (role == UserRole.Admin)
        {
            return elapsed >= AdminLogoutTimeout ? IdleAction.Logout : IdleAction.None;
        }

        if (elapsed >= StaffLogoutTimeout)
        {
            return IdleAction.Logout;
        }

        if (!_hasReturnedToDashboardForIdlePeriod && elapsed >= DashboardReturnTimeout)
        {
            return IdleAction.ReturnToDashboard;
        }

        return IdleAction.None;
    }

    private async Task HandleIdleAsync(IdleAction idleAction)
    {
        var user = _authService.CurrentUser;
        if (user == null)
        {
            return;
        }

        if (idleAction == IdleAction.Logout)
        {
            await LogoutForIdleAsync();
            return;
        }

        await ReturnToDashboardForIdleAsync();
    }

    public async Task ReturnToDashboardForIdleAsync()
    {
        try
        {
            var user = _authService.CurrentUser;
            if (user == null)
            {
                return;
            }

            var handlers = GetBeforeIdleReturnHandlersSnapshot();
            foreach (var handler in handlers)
            {
                try
                {
                    await handler();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Idle pre-return handler failed: {ex.Message}");
                }
            }

            var dashboardRoute = _roleAccessService.ResolveDashboardRoute(user.Role);
            if (IsAlreadyOnRoute(dashboardRoute))
            {
                lock (_sync)
                {
                    _hasReturnedToDashboardForIdlePeriod = true;
                }

                return;
            }

            SuppressAutomaticNavigationActivity();
            if (Shell.Current != null)
            {
                await NavigationCoordinator.Shared.NavigateShellAsync(dashboardRoute, animated: false);
            }

            lock (_sync)
            {
                _hasReturnedToDashboardForIdlePeriod = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Idle dashboard return failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
        }
    }

    private async Task LogoutForIdleAsync()
    {
        try
        {
            if (IsAlreadyOnRoute("login"))
            {
                if (_authService.CurrentUser != null)
                {
                    await _authService.LogoutAsync();
                }

                return;
            }

            await RunBeforeIdleHandlersAsync();
            SuppressAutomaticNavigationActivity();
            await _authService.LogoutAsync();

            if (Shell.Current != null)
            {
                await NavigationCoordinator.Shared.NavigateShellAsync("login", animated: false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Idle logout failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);

            try
            {
                await _authService.LogoutAsync();
            }
            catch (Exception logoutEx)
            {
                System.Diagnostics.Debug.WriteLine($"Idle logout cleanup failed: {logoutEx.Message}");
            }
        }
    }

    private static bool IsAlreadyOnRoute(string route)
    {
        try
        {
            var location = Shell.Current?.CurrentState?.Location?.OriginalString;
            if (string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            return location.Contains(route, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task RunBeforeIdleHandlersAsync()
    {
        var handlers = GetBeforeIdleReturnHandlersSnapshot();
        foreach (var handler in handlers)
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Idle pre-return handler failed: {ex.Message}");
            }
        }
    }

    private void SuppressAutomaticNavigationActivity()
    {
        lock (_sync)
        {
            _suppressActivityResetUntil = DateTime.Now.AddSeconds(2);
        }
    }

    private static bool HasActiveModalOrPopup()
    {
        try
        {
            if (Shell.Current?.Navigation?.ModalStack?.Count > 0)
            {
                return true;
            }

            return Shell.Current?.CurrentPage is ContentPage contentPage && HasVisiblePopupElement(contentPage.Content);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasVisiblePopupElement(Element? element)
    {
        if (element == null)
        {
            return false;
        }

        var visualElement = element as VisualElement;
        if (visualElement is { IsVisible: false })
        {
            return false;
        }

        if (visualElement != null && IsActivePopupOrOverlayElement(element, visualElement))
        {
            return true;
        }

        foreach (var child in GetChildElements(element))
        {
            if (HasVisiblePopupElement(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActivePopupOrOverlayElement(Element element, VisualElement visualElement)
    {
        if (visualElement.InputTransparent)
        {
            return false;
        }

        var typeName = element.GetType().Name;
        var namedLikePopup = typeName.Contains("Dialog", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Popup", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Overlay", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Modal", StringComparison.OrdinalIgnoreCase);

        if (element is not Page && namedLikePopup)
        {
            return true;
        }

        return false;
    }

    private List<Func<Task>> GetBeforeIdleReturnHandlersSnapshot()
    {
        lock (_sync)
        {
            return _beforeIdleReturnHandlers.ToList();
        }
    }

    private void EndCriticalActivity()
    {
        lock (_sync)
        {
            _criticalActivityDepth = Math.Max(0, _criticalActivityDepth - 1);
            _lastActivityAt = DateTime.Now;
            _hasReturnedToDashboardForIdlePeriod = false;
        }
    }

    private void UnregisterBeforeIdleReturnHandler(Func<Task> handler)
    {
        lock (_sync)
        {
            _beforeIdleReturnHandlers.Remove(handler);
        }
    }

    private enum IdleAction
    {
        None,
        ReturnToDashboard,
        Logout
    }

    private sealed class CriticalActivityScope : IDisposable
    {
        private readonly InactivityService _service;
        private readonly IDisposable? _backgroundSyncScope;
        private bool _disposed;

        public CriticalActivityScope(InactivityService service, IDisposable? backgroundSyncScope)
        {
            _service = service;
            _backgroundSyncScope = backgroundSyncScope;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _service.EndCriticalActivity();
            _backgroundSyncScope?.Dispose();
        }
    }

    private sealed class RegisteredHandler : IDisposable
    {
        private readonly InactivityService _service;
        private readonly Func<Task> _handler;
        private bool _disposed;

        public RegisteredHandler(InactivityService service, Func<Task> handler)
        {
            _service = service;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _service.UnregisterBeforeIdleReturnHandler(_handler);
        }
    }
}
