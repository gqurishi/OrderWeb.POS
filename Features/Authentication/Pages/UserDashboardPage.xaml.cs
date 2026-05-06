using POS_in_NET.Services;
using POS_in_NET.Models;
using System.Timers;

namespace POS_in_NET.Pages;

public partial class UserDashboardPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly BusinessSettingsService _businessSettingsService;
    private System.Timers.Timer? _timeTimer;
    private User? _currentUser;

    public UserDashboardPage()
    {
        InitializeComponent();
        _authService = AuthenticationService.Instance;
        _businessSettingsService = new BusinessSettingsService();
        
        // Start time updates
        StartTimeUpdates();
        
        // Load current user info
        LoadCurrentUser();

        // Load business branding from settings
        _ = LoadBusinessNameAsync();
    }

    private async Task LoadBusinessNameAsync()
    {
        try
        {
            var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
            var businessName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
                ? "Dashboard"
                : businessInfo.RestaurantName.Trim();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var headerTitle = this.FindByName<Label>("HeaderTitleLabel");
                if (headerTitle != null)
                    headerTitle.Text = businessName;

                Title = businessName;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Load business name error: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var headerTitle = this.FindByName<Label>("HeaderTitleLabel");
                if (headerTitle != null)
                    headerTitle.Text = "Dashboard";

                Title = "Dashboard";
            });
        }
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
        try
        {
            var now = DateTime.Now;
            var dateLabel = this.FindByName<Label>("DateLabel");
            var timeLabel = this.FindByName<Label>("TimeLabel");
            
            if (dateLabel != null)
                dateLabel.Text = now.ToString("dddd, MMMM dd, yyyy");
            if (timeLabel != null)
                timeLabel.Text = now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Time update error: {ex.Message}");
        }
    }

    private void LoadCurrentUser()
    {
        try
        {
            _currentUser = _authService.GetCurrentUser();
            var welcomeLabel = this.FindByName<Label>("WelcomeLabel");
            
            if (welcomeLabel != null)
                welcomeLabel.Text = "Welcome to";

            if (_currentUser != null)
                System.Diagnostics.Debug.WriteLine($"User dashboard loaded for: {_currentUser.Name}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Load user error: {ex.Message}");
            var welcomeLabel = this.FindByName<Label>("WelcomeLabel");
            if (welcomeLabel != null)
                welcomeLabel.Text = "Welcome to";
        }
    }

    private async void OnCollectionClicked(object sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Collection button clicked - User Dashboard");
            
            // Navigate to collection page
            await Shell.Current.GoToAsync("//collection");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Collection navigation error: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to open Collection module");
        }
    }

    private async void OnDeliveryClicked(object sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Delivery button clicked - User Dashboard");
            
            // Navigate to delivery page
            await Shell.Current.GoToAsync("//delivery");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delivery navigation error: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to open Delivery module");
        }
    }

    private async void OnRestaurantClicked(object sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Restaurant button clicked - User Dashboard - Navigating directly to visual tables");
            
            // Navigate directly to visual table layout root.
            await Shell.Current.GoToAsync("//visuallayout");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restaurant navigation error: {ex.Message}");
            
            // Fallback: Navigate to regular restaurant page
            try
            {
                await Shell.Current.GoToAsync("//restaurant");
            }
            catch (Exception fallbackEx)
            {
                System.Diagnostics.Debug.WriteLine($"Restaurant fallback navigation error: {fallbackEx.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to open Restaurant module");
            }
        }
    }

    private async void OnLiveOrderClicked(object sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Live Order button clicked - User Dashboard");
            await Shell.Current.GoToAsync("//liveorder");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live order navigation error: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to open Live Order module");
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("User logging out from User Dashboard");

            // Stop timer
            _timeTimer?.Stop();
            _timeTimer?.Dispose();

            // Logout user immediately
            await _authService.LogoutAsync();

            // Navigate back to login
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logout error: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to logout properly");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Clean up timer
        _timeTimer?.Stop();
        _timeTimer?.Dispose();
    }

    // Responsive sizing based on screen dimensions
    private void OnPageSizeChanged(object sender, EventArgs e)
    {
        try
        {
            // Get screen width and height
            double screenWidth = this.Width;
            double screenHeight = this.Height;

            // Guard against invalid dimensions
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            // Calculate screen size category (smallest dimension matters for iPad Mini compatibility)
            double minDimension = Math.Min(screenWidth, screenHeight);

            // iPad Mini: ~768px | iPad: ~834px | iPad Pro: ~1024px | Desktop 17-19": ~1366-1536px
            double scaleFactor = 1.0;
            
            if (minDimension < 600)          // Small phones
                scaleFactor = 0.75;
            else if (minDimension < 768)     // Large phones
                scaleFactor = 0.85;
            else if (minDimension < 834)     // iPad Mini
                scaleFactor = 1.0;
            else if (minDimension < 1024)    // iPad
                scaleFactor = 1.2;
            else if (minDimension < 1366)    // iPad Pro
                scaleFactor = 1.4;
            else                              // Large desktops (17-19")
                scaleFactor = 1.7;

            // Update header sizes using FindByName
            var headerTitle = this.FindByName<Label>("HeaderTitleLabel");
            if (headerTitle != null)
                headerTitle.FontSize = 24 * scaleFactor;

            var headerSubtitle = this.FindByName<Label>("HeaderSubtitleLabel");
            if (headerSubtitle != null)
                headerSubtitle.FontSize = 14 * scaleFactor;

            var dateLabel = this.FindByName<Label>("DateLabel");
            if (dateLabel != null)
                dateLabel.FontSize = 13 * scaleFactor;

            var timeLabel = this.FindByName<Label>("TimeLabel");
            if (timeLabel != null)
                timeLabel.FontSize = 18 * scaleFactor;

            // Update title
            var titleLabel = this.FindByName<Label>("TitleLabel");
            if (titleLabel != null)
            {
                titleLabel.FontSize = 28 * scaleFactor;
                titleLabel.Margin = new Thickness(0, 0, 0, 60 * scaleFactor);
            }

            // Calculate responsive icon and spacing sizes
            double iconSize = 220 * scaleFactor;
            double spacing = 60 * scaleFactor;

            // Update icons using FindByName
            var restaurantIcon = this.FindByName<Image>("RestaurantIcon");
            if (restaurantIcon != null) { restaurantIcon.WidthRequest = iconSize; restaurantIcon.HeightRequest = iconSize; }

            var deliveryIcon = this.FindByName<Image>("DeliveryIcon");
            if (deliveryIcon != null) { deliveryIcon.WidthRequest = iconSize; deliveryIcon.HeightRequest = iconSize; }

            var collectionIcon = this.FindByName<Image>("CollectionIcon");
            if (collectionIcon != null) { collectionIcon.WidthRequest = iconSize; collectionIcon.HeightRequest = iconSize; }

            var liveOrderIcon = this.FindByName<Image>("LiveOrderIcon");
            if (liveOrderIcon != null) { liveOrderIcon.WidthRequest = iconSize; liveOrderIcon.HeightRequest = iconSize; }

            // Update icon labels
            var restaurantLabel = this.FindByName<Label>("RestaurantLabel");
            if (restaurantLabel != null) restaurantLabel.FontSize = 16 * scaleFactor;

            var deliveryLabel = this.FindByName<Label>("DeliveryLabel");
            if (deliveryLabel != null) deliveryLabel.FontSize = 16 * scaleFactor;

            var collectionLabel = this.FindByName<Label>("CollectionLabel");
            if (collectionLabel != null) collectionLabel.FontSize = 16 * scaleFactor;

            var liveOrderLabel = this.FindByName<Label>("LiveOrderLabel");
            if (liveOrderLabel != null) liveOrderLabel.FontSize = 16 * scaleFactor;

            // Update button grid spacing
            var grid = this.FindByName<Grid>("ButtonGrid");
            if (grid != null)
            {
                grid.RowSpacing = spacing;
                grid.ColumnSpacing = spacing;
            }

            // Update main stack layout padding and margins
            var mainStack = this.FindByName<StackLayout>("MainStackLayout");
            if (mainStack != null)
            {
                mainStack.Padding = new Thickness(30 * scaleFactor);
                mainStack.Margin = new Thickness(0, 40 * scaleFactor, 0, 40 * scaleFactor);
            }

            System.Diagnostics.Debug.WriteLine($"Responsive sizing updated: Scale={scaleFactor:F2}, MinDim={minDimension}, Screen={screenWidth}x{screenHeight}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Responsive sizing error: {ex.Message}");
        }
    }



}
