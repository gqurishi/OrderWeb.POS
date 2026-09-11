using POS_in_NET.Services;
using POS_in_NET.Pages;

namespace POS_in_NET.Views;

public partial class TopBar : ContentView
{
    private bool _isSubscribedToConnectionState;

    public TopBar()
    {
        InitializeComponent();

        SizeChanged += OnTopBarSizeChanged;
        ApplyResponsiveLayout(Width);

        SubscribeToTerminalConnectionState();
        RefreshHeaderIdentity();
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

    private void RefreshHeaderIdentity()
    {
        SharedHeader.UserName = ServiceHelper.GetService<AuthenticationService>()?.CurrentUser?.Name ?? "No user";
        SharedHeader.TerminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
        UpdateMotherDisconnectedBanner();
    }

    private void OnTopBarSizeChanged(object? sender, EventArgs e)
    {
        ApplyResponsiveLayout(Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width > 0 && width < 1450;
        SharedHeader.HeightRequest = compact ? 82 : 88;
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
        SharedHeader.ConnectionStatus = isVisible ? "Mother Offline" : "Connected";
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
            await NavigationCoordinator.Shared.NavigateShellAsync("login", animated: false, source: sender as VisualElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Logout error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            // Still navigate to login even if logout service fails
            try
            {
                await NavigationCoordinator.Shared.NavigateShellAsync("login", animated: false);
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
        
        if (Handler == null)
        {
            UnsubscribeFromTerminalConnectionState();
            return;
        }

        SubscribeToTerminalConnectionState();
        RefreshHeaderIdentity();
    }

    public void SetPageTitle(string title)
    {
        SharedHeader.ShowWelcomeBrand = false;
        SharedHeader.Title = string.IsNullOrWhiteSpace(title) ? "Order" : title.Trim();
    }

    public void SetContextActions(IEnumerable<OrderWeb.SharedUI.Controls.HeaderAction>? actions)
    {
        SharedHeader.ContextActions = actions;
    }

    public void SetCustomContent(View? content)
    {
        // Kept for source compatibility; the shared Phase 8 header owns the frame chrome.
    }
}
