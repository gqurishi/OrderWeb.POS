using Microsoft.Maui.Storage;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.ViewModels;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly MotherAuthenticationService _contractAuth;
    private readonly BusinessSettingsService _businessService;
    private readonly LoginViewModel _loginViewModel;
    private bool _businessInfoLoaded;
    private bool _authCacheWarmStarted;
    private bool _terminalConnectionOk;
    private Task<TerminalConnectionTestResult>? _connectionCheckTask;
    private static readonly TimeSpan ConnectionCheckCache = TimeSpan.FromSeconds(45);
    private DateTime _lastConnectionCheckUtc = DateTime.MinValue;

    public LoginPage()
    {
        InitializeComponent();
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _contractAuth = ServiceHelper.GetService<MotherAuthenticationService>()
                        ?? new MotherAuthenticationService(_authService);
        _businessService = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
        _loginViewModel = new LoginViewModel(_contractAuth);
        SharedLogin.ViewModel = _loginViewModel;
        _loginViewModel.LoginSucceeded += OnLoginSucceeded;
        _loginViewModel.ClockInOutRequested += async (_, _) => await OnClockInOutAsync();
        _loginViewModel.MinimizeRequested += (_, _) => PosWindowService.MinimizeMainWindow();
        StartAuthCacheWarmup();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (TerminalConfigurationService.IsConfigured &&
            TerminalConfigurationService.IsMotherTerminal)
        {
            var databaseCheck = await _authService.TestDatabaseConnectionAsync();
            if (!databaseCheck.Success)
            {
                await NavigationCoordinator.Shared.NavigateShellAsync("terminalsetup", animated: false);
                return;
            }
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
                _loginViewModel.SetRestaurantName("Restaurant POS");
            }
        }

        _connectionCheckTask ??= RefreshTerminalConnectionStatusAsync();
        if (!_authCacheWarmStarted) StartAuthCacheWarmup();
        if (!_businessInfoLoaded) _ = LoadBusinessInfoOnceAsync();
    }

    private async void OnLoginSucceeded(object? sender, UserSession session)
    {
        try
        {
            if (TerminalConfigurationService.IsConfigured == false)
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
                    await _authService.LogoutAsync();
                    return;
                }

                Preferences.Default.Remove("child_schema_gate_message");
            }

            var role = Enum.TryParse<UserRole>(session.User.Role, true, out var parsed)
                ? parsed
                : _authService.CurrentUser?.Role ?? UserRole.User;
            var navigationRoute = GetNavigationRouteForRole(role);
            if (!await NavigateAfterLoginAsync(navigationRoute))
            {
                var recoveryShell = new AppShell();
                Application.Current!.MainPage = recoveryShell;
                await recoveryShell.GoToAsync(navigationRoute, false);
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"[Login] Shared login navigation failed: {ex}");
        }
    }

    private async Task OnClockInOutAsync()
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            await NavigationCoordinator.Shared.NavigateShellAsync("terminalsetup", animated: false);
            return;
        }

        await Navigation.PushModalAsync(new ClockTimeModal(), false);
    }

    private static async Task<bool> NavigateAfterLoginAsync(string navigationRoute)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (Shell.Current is null)
                {
                    await Task.Delay(50);
                    continue;
                }

                await NavigationCoordinator.Shared.NavigateShellAsync(navigationRoute, animated: false);
                return true;
            }
            catch
            {
                await Task.Delay(50);
            }
        }

        return false;
    }

    private async Task<TerminalConnectionTestResult> RefreshTerminalConnectionStatusAsync(bool forceRefresh = false)
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            _terminalConnectionOk = false;
            return new TerminalConnectionTestResult(false, "Terminal Not Setup", "Please complete terminal setup first.");
        }

        if (!TerminalConfigurationService.IsChildTerminal)
        {
            _terminalConnectionOk = true;
            return new TerminalConnectionTestResult(true, "Connected", string.Empty);
        }

        if (!forceRefresh &&
            _terminalConnectionOk &&
            DateTime.UtcNow - _lastConnectionCheckUtc < ConnectionCheckCache)
        {
            return new TerminalConnectionTestResult(true, "Connected", string.Empty);
        }

        var result = await TerminalConnectionTestService.TestAsync();
        _terminalConnectionOk = result.Success;
        _lastConnectionCheckUtc = DateTime.UtcNow;
        _connectionCheckTask = null;
        return result;
    }

    private async Task LoadBusinessInfoOnceAsync()
    {
        try
        {
            var businessInfo = await _businessService.GetBusinessInfoAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (businessInfo != null)
                {
                    _loginViewModel.SetRestaurantName(string.IsNullOrWhiteSpace(businessInfo.RestaurantName)
                        ? "Restaurant POS"
                        : businessInfo.RestaurantName);
                }
            });
            _businessInfoLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Business info load error: {ex.Message}");
        }
    }

    private void StartAuthCacheWarmup()
    {
        if (_authCacheWarmStarted) return;
        _authCacheWarmStarted = true;
        _ = _authService.WarmAuthenticationCacheAsync();
    }

    private static string GetNavigationRouteForRole(UserRole role) => role switch
    {
        UserRole.User => "//userdashboard",
        UserRole.Manager => "//managerdashboard",
        UserRole.Admin => "//dashboard",
        UserRole.Staff => "//login",
        _ => "//dashboard"
    };
}
