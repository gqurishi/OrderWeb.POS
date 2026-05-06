using POS_in_NET.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly BusinessSettingsService _businessService;
    private System.Timers.Timer? _timeTimer;
    private Entry? _currentFocusedEntry;

    public LoginPage()
    {
        InitializeComponent();
        _authService = AuthenticationService.Instance;
        _businessService = new BusinessSettingsService();
        
        // Start time updates
        StartTimeUpdates();
        
        // Load business info in background - fire and forget
        _ = Task.Run(async () =>
        {
            try
            {
                await LoadBusinessInfoAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Business info init: {ex.Message}");
            }
        });
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
        CurrentTimeLabel.Text = now.ToString("h:mm tt");
        CurrentDateLabel.Text = now.ToString("dddd, MMM d, yyyy");
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        await PerformLoginAsync();
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

        // Validate PIN (4 digits)
        if (string.IsNullOrWhiteSpace(PasswordEntry.Text) || PasswordEntry.Text.Length != 4)
        {
            ShowError("Please enter a 4-digit PIN.");
            return;
        }

        // Show loading state
        SetLoadingState(true);

        try
        {
            // Use PIN as both username and password for authentication
            var pin = PasswordEntry.Text.Trim();
            var result = await _authService.LoginAsync(pin, pin);

            System.Diagnostics.Debug.WriteLine($"PIN Login result: Success={result.Success}, Message={result.Message}");
            
            if (result.Success && result.User != null)
            {
                System.Diagnostics.Debug.WriteLine($"Login successful for user: {result.User.Username}, Role: {result.User.Role}");
                
                // Role-based navigation
                string navigationRoute = GetNavigationRouteForRole(result.User.Role);
                System.Diagnostics.Debug.WriteLine($"Navigating to: {navigationRoute}");
                
                // Navigate to appropriate dashboard based on user role
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await Shell.Current.GoToAsync(navigationRoute, true);
                    });
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
                ShowError("Invalid PIN. Please try again.");
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
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
        
        // Also show the ErrorFrame container
        if (ErrorLabel.Parent?.Parent is Frame errorFrame)
        {
            errorFrame.IsVisible = true;
        }
    }

    private void SetLoadingState(bool isLoading)
    {
        LoadingIndicator.IsVisible = isLoading;
        LoginButton.IsEnabled = !isLoading;
        UsernameEntry.IsEnabled = !isLoading;
        PasswordEntry.IsEnabled = !isLoading;

        if (isLoading)
        {
            LoginButton.Text = "Signing In...";
        }
        else
        {
            LoginButton.Text = "Sign In";
            // Reset button color to default
            LoginButton.BackgroundColor = Color.FromArgb("#1E3A8A"); // Royal Blue
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        
        // Focus on username field IMMEDIATELY - don't wait for anything
        Dispatcher.Dispatch(() => UsernameEntry.Focus());
        
        // Load business info in background - non-blocking
        _ = Task.Run(async () =>
        {
            try
            {
                await LoadBusinessInfoAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Business info load error: {ex.Message}");
            }
        });
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
        
        System.Diagnostics.Debug.WriteLine($"📍 UpdatePINDisplay: PIN length = {pinLength}");
        
        // Update dot colors based on PIN length
        Dot1.BackgroundColor = pinLength >= 1 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
        Dot2.BackgroundColor = pinLength >= 2 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
        Dot3.BackgroundColor = pinLength >= 3 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
        Dot4.BackgroundColor = pinLength >= 4 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
        
        // Auto-login when 4 digits entered
        if (pinLength == 4)
        {
            System.Diagnostics.Debug.WriteLine("🔐 4 digits entered! Instant auto-login...");
            
            // INSTANT login - no delay at all!
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
        var currentText = PasswordEntry.Text ?? "";
        if (currentText.Length > 0)
        {
            PasswordEntry.Text = currentText.Substring(0, currentText.Length - 1);
            UpdatePINDisplay();
        }
    }

    private void OnClearClicked(object sender, EventArgs e)
    {
        ClearPIN();
    }

    private void OnDoubleZeroClicked(object sender, EventArgs e)
    {
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
            _ => "//dashboard"                       // Default to admin dashboard
        };
    }

    #endregion
}