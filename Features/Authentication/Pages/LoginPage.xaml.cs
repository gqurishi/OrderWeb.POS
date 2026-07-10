using Microsoft.Maui.Storage;
using POS_in_NET.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly BusinessSettingsService _businessService;
    private System.Timers.Timer? _timeTimer;
    private Entry? _currentFocusedEntry;
    private bool _businessInfoLoaded;
    private bool _authCacheWarmStarted;
    private bool _terminalConnectionOk;
    private Task<TerminalConnectionTestResult>? _connectionCheckTask;
    private DateTime _lastConnectionCheckUtc = DateTime.MinValue;
    private static readonly TimeSpan ConnectionCheckCache = TimeSpan.FromSeconds(45);

    public LoginPage()
    {
        InitializeComponent();
        _authService = AuthenticationService.Instance;
        _businessService = new BusinessSettingsService();
        StartAuthCacheWarmup();
        
        // Start time updates
        StartTimeUpdates();
    }

    private void StartTimeUpdates()
    {
        // Update time immediately
        UpdateTimeDisplay();
        
        // Update every second
        _timeTimer = new System.Timers.Timer(1000);
        _timeTimer.Elapsed += (sender, e) =>
        {
            MainThread.BeginInvokeOnMainThread(UpdateTimeDisplay);
        };
        _timeTimer.Start();
    }

    private void UpdateTimeDisplay()
    {
        var now = DateTime.Now;
        CurrentTimeLabel.Text = now.ToString("h:mm tt").ToLowerInvariant();
        CurrentDateLabel.Text = now.ToString("dddd, MMM d, yyyy");
    }

    private async void OnClockInOutClicked(object sender, EventArgs e)
    {
        if (LoadingIndicator.IsVisible)
        {
            return;
        }

        if (!TerminalConfigurationService.IsConfigured)
        {
            await Shell.Current.GoToAsync("//terminalsetup", false);
            return;
        }

        await Navigation.PushModalAsync(new ClockTimeModal(), false);
    }

    private void OnMinimizeClicked(object sender, EventArgs e)
    {
        PosWindowService.MinimizeMainWindow();
    }

    private void OnUsernameCompleted(object sender, EventArgs e)
    {
        // Not used in PIN mode
    }

    private async void OnPasswordCompleted(object sender, EventArgs e)
    {
        // Auto-login when 4 digits entered
        if (PasswordEntry.Text?.Length == 4)
        {
            await PerformLoginAsync();
        }
    }

    private async Task PerformLoginAsync()
    {
        // Prevent multiple simultaneous login attempts
        if (LoadingIndicator.IsVisible) return;

        // Reset error message
        ErrorFrame.IsVisible = false;
        ErrorLabel.Text = "";
        LoginStatusLabel.IsVisible = false;

        // Validate PIN (4 digits)
        if (string.IsNullOrWhiteSpace(PasswordEntry.Text) || PasswordEntry.Text.Length != 4 || !PasswordEntry.Text.All(char.IsDigit))
        {
            ShowError("Please enter a 4-digit PIN.");
            return;
        }

        SetLoadingState(true, "Logging in...");
        await Task.Yield();

        if (!TerminalConfigurationService.IsConfigured)
        {
            SetLoadingState(false);
            await Shell.Current.GoToAsync("//terminalsetup", false);
            return;
        }

        if (TerminalConfigurationService.IsChildTerminal)
        {
            var connectionResult = await TerminalConnectionTestService.TestAsync();
            if (!connectionResult.Success)
            {
                SetLoadingState(false);
                ShowChildTerminalStatus(connectionResult.Message);
                ShowError(connectionResult.Message);
                return;
            }

            Preferences.Default.Remove("child_schema_gate_message");
        }

        try
        {
            // Use PIN as both username and password for authentication
            var pin = PasswordEntry.Text.Trim();
            var result = await _authService.LoginAsync(pin, pin);

            if (result.Success && result.User != null)
            {
                if (result.User.Role == UserRole.Staff)
                {
                    ShowError("Staff PIN is for Clock In/Out only.");
                    ClearPIN();
                    return;
                }

                SetLoadingState(true, "Logging in...");

                // Role-based navigation
                string navigationRoute = GetNavigationRouteForRole(result.User.Role);

                // Navigate to appropriate dashboard based on user role
                try
                {
                    // No animation for a faster PIN-to-dashboard transition.
                    await Shell.Current.GoToAsync(navigationRoute, false);
                }
                catch (Exception navEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Navigation error: {navEx.Message}");
                    // Fallback: Try direct navigation
                    if (Application.Current != null)
                    {
                        if (result.User.Role == UserRole.User)
                        {
                            // For User role, create a simple shell with just user dashboard
                            var userShell = new AppShell();
                            Application.Current.MainPage = userShell;
                            await userShell.GoToAsync("//userdashboard");
                        }
                        else
                        {
                            // For Admin/Manager, use full shell
                            Application.Current.MainPage = new AppShell();
                        }
                    }
                }
            }
            else
            {
                ShowError("Wrong PIN. Try again.");
                ClearPIN();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Login error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Login error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Full exception: {ex}");
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private void ShowError(string message)
    {
        LoginStatusLabel.IsVisible = false;
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
        ErrorFrame.IsVisible = true;
    }

    private void SetLoadingState(bool isLoading, string? message = null)
    {
        LoadingIndicator.IsVisible = isLoading;
        ClockInOutButton.IsEnabled = !isLoading;
        UsernameEntry.IsEnabled = !isLoading;
        PasswordEntry.IsEnabled = !isLoading;
        LoginStatusLabel.Text = message ?? "Checking PIN...";
        LoginStatusLabel.IsVisible = isLoading;

        if (!isLoading)
        {
            LoginStatusLabel.IsVisible = false;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (TerminalConfigurationService.IsConfigured &&
            TerminalConfigurationService.IsMotherTerminal &&
            !await _authService.HasAnyUserAsync())
        {
            await Shell.Current.GoToAsync("//initialadminsetup", false);
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

        // Focus on username field IMMEDIATELY - don't wait for anything
        Dispatcher.Dispatch(() => UsernameEntry.Focus());
        
        if (!_authCacheWarmStarted)
        {
            StartAuthCacheWarmup();
        }

        if (_businessInfoLoaded)
        {
            return;
        }

        // Load business info once in background.
        _ = LoadBusinessInfoOnceAsync();
    }

    private async Task<TerminalConnectionTestResult> RefreshTerminalConnectionStatusAsync(bool forceRefresh = false)
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            _terminalConnectionOk = false;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TerminalStatusFrame.IsVisible = false;
                TerminalStatusLabel.Text = string.Empty;
            });
            return new TerminalConnectionTestResult(false, "Terminal Not Setup", "Please complete terminal setup first.");
        }

        if (!TerminalConfigurationService.IsChildTerminal)
        {
            _terminalConnectionOk = true;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TerminalStatusFrame.IsVisible = false;
                TerminalStatusLabel.Text = string.Empty;
            });
            return new TerminalConnectionTestResult(true, "Connected", string.Empty);
        }

        if (!forceRefresh &&
            _terminalConnectionOk &&
            DateTime.UtcNow - _lastConnectionCheckUtc < ConnectionCheckCache)
        {
            await MainThread.InvokeOnMainThreadAsync(() => TerminalStatusFrame.IsVisible = false);
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
                TerminalStatusFrame.IsVisible = false;
                TerminalStatusLabel.Text = string.Empty;
                return;
            }

            ShowChildTerminalStatus(result.Message);
        });

        return result;
    }

    private void ShowChildTerminalStatus(string message)
    {
        TerminalStatusFrame.IsVisible = true;
        TerminalStatusFrame.BackgroundColor = Color.FromArgb("#FEF2F2");
        TerminalStatusFrame.Stroke = Color.FromArgb("#EF4444");
        TerminalStatusLabel.TextColor = Color.FromArgb("#B91C1C");
        TerminalStatusLabel.Text = message;
    }

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
        _ = _authService.WarmAuthenticationCacheAsync();
    }

    private async Task LoadBusinessInfoAsync()
    {
        try
        {
            var businessInfo = await _businessService.GetBusinessInfoAsync();
            
            // Update UI on main thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (businessInfo != null)
                {
                    WelcomeSubtitleLabel.Text = string.IsNullOrWhiteSpace(businessInfo.RestaurantName)
                        ? "Restaurant POS"
                        : businessInfo.RestaurantName;

                    RestaurantNameLabel.Text = businessInfo.RestaurantName;
                    RestaurantDescriptionLabel.Text = string.IsNullOrWhiteSpace(businessInfo.Description) 
                        ? "Premium Dining Experience" 
                        : businessInfo.Description;
                }
                else
                {
                    // Keep default values if no business info found
                    WelcomeSubtitleLabel.Text = "Restaurant POS";
                    RestaurantNameLabel.Text = "Restaurant POS";
                    RestaurantDescriptionLabel.Text = "Premium Dining Experience";
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading business info: {ex.Message}");
            
            // Keep default values on error
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                WelcomeSubtitleLabel.Text = "Restaurant POS";
                RestaurantNameLabel.Text = "Restaurant POS";
                RestaurantDescriptionLabel.Text = "Premium Dining Experience";
            });
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Stop timer when leaving page
        _timeTimer?.Stop();
        _timeTimer?.Dispose();
        _timeTimer = null;
    }

    #region PIN Entry Methods

    private void UpdatePINDisplay()
    {
        var pinLength = PasswordEntry.Text?.Length ?? 0;

        // Update dot colors based on PIN length
        Dot1.BackgroundColor = pinLength >= 1 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
        Dot2.BackgroundColor = pinLength >= 2 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
        Dot3.BackgroundColor = pinLength >= 3 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
        Dot4.BackgroundColor = pinLength >= 4 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
        
        // Auto-login when 4 digits entered
        if (pinLength == 4)
        {
            // Instant login once 4 digits are entered.
            _ = PerformLoginAsync();
        }
    }

    private void ClearPIN()
    {
        PasswordEntry.Text = "";
        UpdatePINDisplay();
    }

    #endregion

    #region Virtual Keyboard Events

    private void OnEntryFocused(object sender, FocusEventArgs e)
    {
        if (sender is Entry entry)
        {
            _currentFocusedEntry = entry;
        }
    }

    private void OnEntryUnfocused(object sender, FocusEventArgs e)
    {
        // Keep keyboard visible
    }

    private void OnKeyClicked(object sender, EventArgs e)
    {
        if (LoadingIndicator.IsVisible) return;

        if (sender is Button button)
        {
            var key = button.Text;
            var currentText = PasswordEntry.Text ?? "";
            
            // Only allow 4 digits
            if (currentText.Length < 4)
            {
                PasswordEntry.Text = currentText + key;
                UpdatePINDisplay();
            }
        }
    }

    private void OnBackspaceClicked(object sender, EventArgs e)
    {
        if (LoadingIndicator.IsVisible) return;

        var currentText = PasswordEntry.Text ?? "";
        if (currentText.Length > 0)
        {
            PasswordEntry.Text = currentText.Substring(0, currentText.Length - 1);
            UpdatePINDisplay();
        }
    }

    private void OnClearClicked(object sender, EventArgs e)
    {
        if (LoadingIndicator.IsVisible) return;

        ClearPIN();
    }

    private void OnDoubleZeroClicked(object sender, EventArgs e)
    {
        if (LoadingIndicator.IsVisible) return;

        var currentText = PasswordEntry.Text ?? "";
        
        // Only add if we have room for 2 more digits
        if (currentText.Length <= 2)
        {
            PasswordEntry.Text = currentText + "00";
            UpdatePINDisplay();
        }
    }

    /// <summary>
    /// Determines the navigation route based on user role
    /// </summary>
    private string GetNavigationRouteForRole(UserRole role)
    {
        return role switch
        {
            UserRole.User => "//userdashboard",      // Simple 3-button dashboard
            UserRole.Manager => "//managerdashboard", // Manager operations dashboard
            UserRole.Admin => "//dashboard",         // Full admin dashboard
            UserRole.Staff => "//login",             // Clock-only staff do not enter POS screens
            _ => "//dashboard"                       // Default to admin dashboard
        };
    }

    #endregion
}
