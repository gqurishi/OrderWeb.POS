using Microsoft.Maui.Storage;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class LoginPage : ContentPage
{
    private readonly MotherAuthenticationService _authService;
    private readonly BusinessSettingsService _businessService;
    private IDispatcherTimer? _clockTimer;
    private bool _businessInfoLoaded;
    private bool _authCacheWarmStarted;
    private bool _terminalConnectionOk;
    private Task<TerminalConnectionTestResult>? _connectionCheckTask;
    private DateTime _lastConnectionCheckUtc = DateTime.MinValue;
    private bool _isLoginInProgress;
    private static readonly TimeSpan ConnectionCheckCache = TimeSpan.FromSeconds(45);

    public LoginPage()
    {
        InitializeComponent();
        _authService = MotherAuthenticationService.Instance;
        _businessService = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
        StartAuthCacheWarmup();
        StartClockUpdates();
    }

    private void StartClockUpdates()
    {
        LoginView.UpdateClock();
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => LoginView.UpdateClock();
        _clockTimer.Start();
    }

    private async void OnPinCompleted(object? sender, string pin)
        => await PerformLoginAsync(pin);

    private async void OnClockActionRequested(object? sender, EventArgs e)
    {
        if (LoginView.IsBusy)
        {
            return;
        }

        if (!TerminalConfigurationService.IsConfigured)
        {
            await NavigationCoordinator.Shared.NavigateShellAsync("terminalsetup", animated: false);
            return;
        }

        await Navigation.PushModalAsync(new ClockTimeModal(), false);
    }

    private void OnMinimizeRequested(object? sender, EventArgs e)
        => PosWindowService.MinimizeMainWindow();

    private async Task PerformLoginAsync(string pin)
    {
        if (_isLoginInProgress)
        {
            return;
        }

        pin = pin?.Trim() ?? string.Empty;
        LoginView.ClearMessages();
        LoginView.BannerMessage = string.Empty;

        if (pin.Length != 4 || !pin.All(char.IsDigit))
        {
            LoginView.ErrorMessage = "Please enter a 4-digit PIN.";
            LoginView.ClearPin();
            return;
        }

        _isLoginInProgress = true;
        LoginView.BusyMessage = "Logging in...";
        LoginView.IsBusy = true;
        await Task.Yield();

        try
        {
            if (!TerminalConfigurationService.IsConfigured)
            {
                await NavigationCoordinator.Shared.NavigateShellAsync("terminalsetup", animated: false);
                return;
            }

            if (TerminalConfigurationService.IsChildTerminal)
            {
                var connectionResult = _connectionCheckTask != null
                    ? await _connectionCheckTask
                    : await RefreshTerminalConnectionStatusAsync();
                if (!connectionResult.Success)
                {
                    ShowChildTerminalStatus(connectionResult.Message);
                    LoginView.ErrorMessage = connectionResult.Message;
                    LoginView.ClearPin();
                    return;
                }

                Preferences.Default.Remove("child_schema_gate_message");
            }

            var result = await _authService.LoginWithPinAsync(pin);
            if (result.Success && result.User != null)
            {
                if (result.User.Role == UserRole.Staff)
                {
                    await _authService.LogoutAsync();
                    LoginView.ErrorMessage = "Staff PIN is for Clock In/Out only.";
                    LoginView.ClearPin();
                    return;
                }

                LoginView.BusyMessage = "Logging in...";
                var navigationRoute = GetNavigationRouteForRole(result.User.Role);
                try
                {
                    LoginView.ClearPin();
                    if (!await NavigateAfterLoginAsync(navigationRoute))
                    {
                        throw new InvalidOperationException("The dashboard navigation did not complete.");
                    }
                }
                catch (Exception navEx)
                {
                    AppDiagnostics.Log($"[Login] Dashboard navigation failed after successful PIN: {navEx}");
                    if (Application.Current != null)
                    {
                        var recoveryShell = new AppShell();
                        Application.Current.MainPage = recoveryShell;
                        await recoveryShell.GoToAsync(navigationRoute, false);
                    }
                }
            }
            else
            {
                LoginView.ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "Wrong PIN. Try again."
                    : result.Message;
                LoginView.ClearPin();
            }
        }
        catch (Exception ex)
        {
            LoginView.ErrorMessage = $"Login error: {ex.Message}";
            LoginView.ClearPin();
            System.Diagnostics.Debug.WriteLine($"Login error: {ex}");
        }
        finally
        {
            _isLoginInProgress = false;
            LoginView.IsBusy = false;
        }
    }

    private static async Task<bool> NavigateAfterLoginAsync(string navigationRoute)
    {
        for (var attempt = 0; attempt < 20 && NavigationCoordinator.Shared.IsNavigating; attempt++)
        {
            await Task.Delay(50);
        }

        var navigated = await NavigationCoordinator.Shared.NavigateShellAsync(navigationRoute, animated: false);
        if (navigated)
        {
            return true;
        }

        var expectedRoute = navigationRoute.Trim('/');
        var currentLocation = Shell.Current?.CurrentState?.Location?.OriginalString ?? string.Empty;
        if (currentLocation.Contains(expectedRoute, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await Task.Delay(150);
        return await NavigationCoordinator.Shared.NavigateShellAsync(navigationRoute, animated: false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isLoginInProgress = false;
        LoginView.IsBusy = false;
        LoginView.ClearPin();
        LoginView.ClearMessages();
        LoginView.UpdateClock();

        if (_clockTimer is { IsRunning: false })
        {
            _clockTimer.Start();
        }

        if (TerminalConfigurationService.IsConfigured &&
            TerminalConfigurationService.IsMotherTerminal &&
            !await _authService.HasAnyUserAsync())
        {
            await NavigationCoordinator.Shared.NavigateShellAsync("initialadminsetup", animated: false);
            return;
        }

        if (TerminalConfigurationService.IsChildTerminal)
        {
            var startupMessage = Preferences.Default.Get("child_schema_gate_message", string.Empty);
            if (!string.IsNullOrWhiteSpace(startupMessage))
            {
                ShowChildTerminalStatus(startupMessage);
            }
        }

        _connectionCheckTask ??= RefreshTerminalConnectionStatusAsync();

        if (!_authCacheWarmStarted)
        {
            StartAuthCacheWarmup();
        }

        if (!_businessInfoLoaded)
        {
            _ = LoadBusinessInfoOnceAsync();
        }
    }

    private async Task<TerminalConnectionTestResult> RefreshTerminalConnectionStatusAsync(bool forceRefresh = false)
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            _terminalConnectionOk = false;
            await MainThread.InvokeOnMainThreadAsync(() => LoginView.BannerMessage = string.Empty);
            return new TerminalConnectionTestResult(false, "Terminal Not Setup", "Please complete terminal setup first.");
        }

        if (!TerminalConfigurationService.IsChildTerminal)
        {
            _terminalConnectionOk = true;
            await MainThread.InvokeOnMainThreadAsync(() => LoginView.BannerMessage = string.Empty);
            return new TerminalConnectionTestResult(true, "Connected", string.Empty);
        }

        if (!forceRefresh &&
            _terminalConnectionOk &&
            DateTime.UtcNow - _lastConnectionCheckUtc < ConnectionCheckCache)
        {
            await MainThread.InvokeOnMainThreadAsync(() => LoginView.BannerMessage = string.Empty);
            return new TerminalConnectionTestResult(true, "Connected", string.Empty);
        }

        var result = await TerminalConnectionTestService.TestAsync();
        _terminalConnectionOk = result.Success;
        _lastConnectionCheckUtc = DateTime.UtcNow;
        _connectionCheckTask = null;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!TerminalConfigurationService.IsChildTerminal || result.Success)
            {
                LoginView.BannerMessage = string.Empty;
                return;
            }

            ShowChildTerminalStatus(result.Message);
        });

        return result;
    }

    private void ShowChildTerminalStatus(string message)
        => LoginView.BannerMessage = message;

    private async Task LoadBusinessInfoOnceAsync()
    {
        try
        {
            await LoadBusinessInfoAsync();
            _businessInfoLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Business info load error: {ex.Message}");
        }
    }

    private void StartAuthCacheWarmup()
    {
        if (_authCacheWarmStarted)
        {
            return;
        }

        _authCacheWarmStarted = true;
        _ = _authService.WarmCacheAsync();
    }

    private async Task LoadBusinessInfoAsync()
    {
        try
        {
            var businessInfo = await _businessService.GetBusinessInfoAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                LoginView.RestaurantName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
                    ? "Restaurant POS"
                    : businessInfo!.RestaurantName;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading business info: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(() => LoginView.RestaurantName = "Restaurant POS");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_clockTimer is not null)
        {
            _clockTimer.Stop();
        }
    }

    private static string GetNavigationRouteForRole(UserRole role)
        => role switch
        {
            UserRole.User => "//userdashboard",
            UserRole.Manager => "//managerdashboard",
            UserRole.Admin => "//dashboard",
            UserRole.Staff => "//login",
            _ => "//dashboard"
        };
}
