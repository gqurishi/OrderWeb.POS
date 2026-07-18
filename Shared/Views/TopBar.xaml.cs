using POS_in_NET.Services;
using POS_in_NET.Pages;

namespace POS_in_NET.Views;

public partial class TopBar : ContentView
{
    private static readonly object ActiveTopBarsLock = new();
    private static readonly List<WeakReference<TopBar>> ActiveTopBars = new();
    private static System.Timers.Timer? _sharedTimer;
    private bool _isRegisteredForSharedUpdates;
    private bool _isSubscribedToConnectionState;

    public TopBar()
    {
        InitializeComponent();

        SizeChanged += OnTopBarSizeChanged;
        ApplyResponsiveLayout(Width);

        RegisterForSharedUpdates();
        UpdateDateTime();

        SubscribeToTerminalConnectionState();
        UpdateMotherDisconnectedBanner();
    }

    private void SubscribeToTerminalConnectionState()
    {
        if (_isSubscribedToConnectionState)
        {
            return;
        }

        TerminalConnectionStateService.ConnectionStateChanged += OnTerminalConnectionStateChanged;
        _isSubscribedToConnectionState = true;
    }

    private void UnsubscribeFromTerminalConnectionState()
    {
        if (!_isSubscribedToConnectionState)
        {
            return;
        }

        TerminalConnectionStateService.ConnectionStateChanged -= OnTerminalConnectionStateChanged;
        _isSubscribedToConnectionState = false;
    }

    private void RegisterForSharedUpdates()
    {
        if (_isRegisteredForSharedUpdates)
        {
            return;
        }

        lock (ActiveTopBarsLock)
        {
            ActiveTopBars.Add(new WeakReference<TopBar>(this));
            _isRegisteredForSharedUpdates = true;

            if (_sharedTimer == null)
            {
                _sharedTimer = new System.Timers.Timer(1000);
                _sharedTimer.Elapsed += (_, _) => UpdateAllTopBars();
                _sharedTimer.Start();
            }
        }
    }

    private void UnregisterFromSharedUpdates()
    {
        if (!_isRegisteredForSharedUpdates)
        {
            return;
        }

        lock (ActiveTopBarsLock)
        {
            ActiveTopBars.RemoveAll(reference =>
                !reference.TryGetTarget(out var topBar) || ReferenceEquals(topBar, this));
            _isRegisteredForSharedUpdates = false;

            if (ActiveTopBars.Count == 0)
            {
                _sharedTimer?.Stop();
                _sharedTimer?.Dispose();
                _sharedTimer = null;
            }
        }
    }

    private static void UpdateAllTopBars()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            List<TopBar> topBars;
            lock (ActiveTopBarsLock)
            {
                ActiveTopBars.RemoveAll(reference => !reference.TryGetTarget(out _));
                topBars = ActiveTopBars
                    .Select(reference => reference.TryGetTarget(out var topBar) ? topBar : null)
                    .Where(topBar => topBar?.Handler != null)
                    .Cast<TopBar>()
                    .ToList();
            }

            foreach (var topBar in topBars)
            {
                topBar.UpdateDateTimeOnMainThread();
            }
        });
    }

    private void UpdateDateTime()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateDateTimeOnMainThread();
        });
    }

    private void UpdateDateTimeOnMainThread()
    {
        if (DateTimeLabel == null || TimeLabel == null)
        {
            return;
        }

        var now = DateTime.Now;
        DateTimeLabel.Text = Width > 0 && Width < 1450
            ? now.ToString("ddd, dd MMM yyyy")
            : now.ToString("dddd, MMMM dd, yyyy");
        TimeLabel.Text = now.ToString("HH:mm:ss");
        UpdateMotherDisconnectedBanner();
    }

    private void OnTopBarSizeChanged(object? sender, EventArgs e)
    {
        ApplyResponsiveLayout(Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (HeaderBorder == null || HeaderGrid == null || PageTitleLabel == null)
        {
            return;
        }

        var compact = width > 0 && width < 1450;
        HeaderBorder.HeightRequest = compact ? 82 : 88;
        HeaderGrid.Padding = compact ? new Thickness(12, 0) : new Thickness(16, 0);
        HeaderGrid.ColumnSpacing = compact ? 8 : 12;
        PageTitleLabel.FontSize = compact ? 23 : 28;
        DateTimeLabel.FontSize = compact ? 12 : 15;
        TimeLabel.FontSize = compact ? 20 : 24;

        var now = DateTime.Now;
        DateTimeLabel.Text = compact
            ? now.ToString("ddd, dd MMM yyyy")
            : now.ToString("dddd, MMMM dd, yyyy");
    }

    private void OnTerminalConnectionStateChanged(object? sender, TerminalConnectionStateChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(UpdateMotherDisconnectedBanner);
    }

    private void UpdateMotherDisconnectedBanner()
    {
        if (MotherDisconnectedBanner == null || MotherDisconnectedLabel == null)
        {
            return;
        }

        var isVisible = TerminalConnectionStateService.IsMotherDisconnectedBannerVisible;
        MotherDisconnectedBanner.IsVisible = isVisible;
        MotherDisconnectedLabel.Text = isVisible
            ? $"{TerminalConnectionStateService.Message} Last checked {TerminalConnectionStateService.CheckedAt:HH:mm:ss}"
            : string.Empty;
    }

    private void OnMenuClicked(object sender, EventArgs e)
    {
        ServiceHelper.GetService<InactivityService>()?.ResetActivity();

        // Open the Shell flyout menu
        Shell.Current.FlyoutIsPresented = true;
    }

    private void OnMinimizeClicked(object sender, EventArgs e)
    {
        ServiceHelper.GetService<InactivityService>()?.ResetActivity();
        PosWindowService.MinimizeMainWindow();
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        try
        {
            ServiceHelper.GetService<InactivityService>()?.ResetActivity();
            System.Diagnostics.Debug.WriteLine(" Logout button clicked");
            
            // Clear authentication immediately - no confirmation
            var authService = ServiceHelper.GetService<AuthenticationService>();
            if (authService != null)
            {
                await authService.LogoutAsync();
                System.Diagnostics.Debug.WriteLine(" Logout successful");
            }
            
            // Navigate to login page
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Logout error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            // Still navigate to login even if logout service fails
            try
            {
                await Shell.Current.GoToAsync("//login");
            }
            catch (Exception navEx)
            {
                System.Diagnostics.Debug.WriteLine($" Navigation error: {navEx.Message}");
            }
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        
        // Stop timer when control is removed
        if (Handler == null)
        {
            UnsubscribeFromTerminalConnectionState();
            UnregisterFromSharedUpdates();
            return;
        }

        SubscribeToTerminalConnectionState();
        RegisterForSharedUpdates();
        UpdateDateTime();
    }

    public void SetPageTitle(string title)
    {
        if (PageTitleLabel != null)
        {
            PageTitleLabel.Text = title;
        }
    }

    public void SetCustomContent(View? content)
    {
        if (content != null)
        {
            CustomContentPresenter.Content = content;
            CustomContentPresenter.IsVisible = true;
        }
        else
        {
            CustomContentPresenter.Content = null;
            CustomContentPresenter.IsVisible = false;
        }
    }
}
