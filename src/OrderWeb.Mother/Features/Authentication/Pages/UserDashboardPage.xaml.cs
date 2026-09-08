using POS_in_NET.Services;
using POS_in_NET.Models;
using POS_in_NET.Helpers;
using System.Linq;
using System.Timers;

namespace POS_in_NET.Pages;

public partial class UserDashboardPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly InactivityService _inactivityService;
    private readonly BusinessSettingsService _businessSettingsService;
    private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private System.Timers.Timer? _timeTimer;
    private User? _currentUser;
    private bool _isRestaurantNavigationInProgress;

    public UserDashboardPage()
    {
        InitializeComponent();
        _authService = AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _inactivityService = ServiceHelper.GetService<InactivityService>() ?? new InactivityService(_authService, _roleAccessService);
        _businessSettingsService = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
        _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
            ?? new OrderServiceAvailabilityService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(), _authService);
        
        // Load current user info
        LoadCurrentUser();
        HostSharedDashboard();
    }

    private void HostSharedDashboard()
    {
        var capabilities = MotherCapabilityResolver.ForRole(_currentUser?.Role ?? _authService.CurrentUser?.Role);
        var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_orderServiceAvailabilityService.IsEnabled(PosOrderService.Table))
            features.Add(OrderWeb.Contracts.Features.PosFeatureKeys.DineIn);
        if (_orderServiceAvailabilityService.IsEnabled(PosOrderService.Collection))
            features.Add(OrderWeb.Contracts.Features.PosFeatureKeys.Collection);
        if (_orderServiceAvailabilityService.IsEnabled(PosOrderService.Delivery))
            features.Add(OrderWeb.Contracts.Features.PosFeatureKeys.Delivery);
        features.Add(OrderWeb.Contracts.Features.PosFeatureKeys.Reservations);

        var vm = new OrderWeb.SharedUI.ViewModels.DashboardViewModel
        {
            Title = "Dashboard",
            Subtitle = string.Empty
        };
        vm.ApplyCapabilities(capabilities, features, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "restaurant", "collection", "delivery", "reservation"
        });
        vm.TileSelected += async (_, tile) =>
        {
            switch (tile.Route)
            {
                case "restaurant":
                    await OnRestaurantClickedAsync();
                    break;
                case "delivery":
                    await OnDeliveryClickedAsync();
                    break;
                case "collection":
                    await OnCollectionClickedAsync();
                    break;
                case "liveorder":
                    await OnLiveOrderClickedAsync();
                    break;
                case "reservation":
                    await _navigationCoordinator.NavigateShellAsync("reservation", animated: false);
                    break;
            }
        };

        if (MainScrollView is not null)
        {
            MainScrollView.Content = new OrderWeb.SharedUI.Views.DashboardView { ViewModel = vm };
        }
    }

    private async Task OnRestaurantClickedAsync()
    {
        OnRestaurantClicked(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    private async Task OnDeliveryClickedAsync()
    {
        OnDeliveryClicked(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    private async Task OnCollectionClickedAsync()
    {
        OnCollectionClicked(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    private async Task OnLiveOrderClickedAsync()
    {
        OnLiveOrderClicked(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    private async Task LoadBusinessNameAsync()
    {
        try
        {
            var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
            var businessName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
                ? "Restaurant POS"
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
                    headerTitle.Text = "Restaurant POS";

                Title = "Restaurant POS";
            });
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _inactivityService.Start();
        _inactivityService.ResetActivity();
        _inactivityService.TrackPage(this);
        StartTimeUpdates();
        await Task.WhenAll(LoadBusinessNameAsync(), RefreshOrderServiceButtonsAsync());
    }

    private async Task RefreshOrderServiceButtonsAsync()
    {
        try
        {
            var settings = await _orderServiceAvailabilityService.GetAsync();
            RestaurantServiceButton.IsVisible = settings.TableEnabled;
            CollectionServiceButton.IsVisible = settings.CollectionEnabled;
            DeliveryServiceButton.IsVisible = settings.DeliveryEnabled;
            ArrangeDashboardTiles();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dashboard service availability error: {ex.Message}");
        }
    }

    private void ArrangeDashboardTiles()
    {
        DashboardTileLayoutHelper.Arrange(
            ButtonGrid,
            RestaurantServiceButton,
            DeliveryServiceButton,
            CollectionServiceButton,
            LiveOrderServiceButton);
    }

    private void StartTimeUpdates()
    {
        _timeTimer?.Stop();
        _timeTimer?.Dispose();

        // Update time immediately
        UpdateTimeDisplay();
        
        // The displayed seconds are not operational data; a one-minute update avoids
        // forcing the full dashboard to redraw continuously.
        _timeTimer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
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
        _inactivityService.ResetActivity();

        try
        {
            System.Diagnostics.Debug.WriteLine("Collection button clicked - User Dashboard");
            await NavigateToAllowedRouteAsync("collection", isModal: true, source: sender as VisualElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Collection navigation error: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to open Collection module");
        }
    }

    private async void OnDeliveryClicked(object sender, EventArgs e)
    {
        _inactivityService.ResetActivity();

        try
        {
            System.Diagnostics.Debug.WriteLine("Delivery button clicked - User Dashboard");
            await NavigateToAllowedRouteAsync("delivery", isModal: true, source: sender as VisualElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delivery navigation error: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to open Delivery module");
        }
    }

    private async void OnRestaurantClicked(object sender, EventArgs e)
    {
        if (_isRestaurantNavigationInProgress)
        {
            return;
        }

        _inactivityService.ResetActivity();

        try
        {
            _isRestaurantNavigationInProgress = true;
            System.Diagnostics.Debug.WriteLine("Restaurant button clicked - User Dashboard - Navigating directly to visual tables");
            await NavigateToAllowedRouteAsync("restaurant", resetShellStacks: true, source: sender as VisualElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restaurant navigation error: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to open Restaurant module");
        }
        finally
        {
            _isRestaurantNavigationInProgress = false;
        }
    }

    private async void OnLiveOrderClicked(object sender, EventArgs e)
    {
        _inactivityService.ResetActivity();

        try
        {
            System.Diagnostics.Debug.WriteLine("Live Order button clicked - User Dashboard");
            await NavigateToAllowedRouteAsync("liveorder", source: sender as VisualElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live order navigation error: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to open Live Order module");
        }
    }

    private void OnMenuClicked(object sender, EventArgs e)
    {
        _inactivityService.ResetActivity();
        Shell.Current.FlyoutIsPresented = true;
    }

    private void OnMinimizeClicked(object sender, EventArgs e)
    {
        _inactivityService.ResetActivity();
        PosWindowService.MinimizeMainWindow();
    }

    private async Task NavigateToAllowedRouteAsync(
        string requestedRoute,
        bool isModal = false,
        bool resetShellStacks = false,
        VisualElement? source = null)
    {
        _inactivityService.ResetActivity();

        var route = _roleAccessService.ResolveRouteForRole(_authService.CurrentUser?.Role, requestedRoute);
        await _orderServiceAvailabilityService.GetAsync();
        if (!_orderServiceAvailabilityService.IsRouteEnabled(route))
        {
            await AppAlertService.ShowAlertAsync("Service Unavailable", "This order service is disabled by the Administrator.");
            return;
        }

        if (!_roleAccessService.CanAccessRoute(_authService.CurrentUser?.Role, route))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access this module.");
            return;
        }

        if (isModal)
        {
            await _navigationCoordinator.NavigateTemporaryRouteAsync(route, source: source);
            return;
        }

        if (resetShellStacks)
        {
            ClearShellDetailStacks();
        }

        await _navigationCoordinator.NavigateShellAsync(route, animated: false, source: source);
    }

    private static void ClearShellDetailStacks()
    {
        NavigationCoordinator.PruneTemporaryPages();
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        try
        {
            _inactivityService.ResetActivity();
            System.Diagnostics.Debug.WriteLine("User logging out from User Dashboard");

            // Stop timer
            _timeTimer?.Stop();
            _timeTimer?.Dispose();

            // Logout user immediately
            await _authService.LogoutAsync();

            // Navigate back to login
            await _navigationCoordinator.NavigateShellAsync("login", animated: false, source: sender as VisualElement);
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
        _timeTimer = null;
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

            // Calculate screen size category. A 1366x768 POS display should stay compact.
            double minDimension = Math.Min(screenWidth, screenHeight);

            double scaleFactor = 1.0;
            
            if (minDimension < 600)
                scaleFactor = 0.78;
            else if (minDimension < 768)
                scaleFactor = 0.88;
            else if (minDimension < 900)
                scaleFactor = 1.0;
            else if (minDimension < 1080)
                scaleFactor = 1.05;
            else if (minDimension < 1400)
                scaleFactor = 1.1;
            else
                scaleFactor = 1.2;

            // Update header sizes using FindByName
            var headerTitle = this.FindByName<Label>("HeaderTitleLabel");
            if (headerTitle != null)
                headerTitle.FontSize = 21 * scaleFactor;

            var headerSubtitle = this.FindByName<Label>("HeaderSubtitleLabel");
            if (headerSubtitle != null)
                headerSubtitle.FontSize = 12 * scaleFactor;

            var dateLabel = this.FindByName<Label>("DateLabel");
            if (dateLabel != null)
                dateLabel.FontSize = 12 * scaleFactor;

            var timeLabel = this.FindByName<Label>("TimeLabel");
            if (timeLabel != null)
                timeLabel.FontSize = 16 * scaleFactor;

            // Update title
            var titleLabel = this.FindByName<Label>("TitleLabel");
            if (titleLabel != null)
            {
                titleLabel.FontSize = 28 * scaleFactor;
                titleLabel.Margin = new Thickness(0, 0, 0, 28 * scaleFactor);
            }

            // Calculate responsive icon and spacing sizes
            double iconSize = 150 * scaleFactor;
            double rowSpacing = 38 * scaleFactor;
            double columnSpacing = 80 * scaleFactor;

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
            if (restaurantLabel != null) restaurantLabel.FontSize = 20 * scaleFactor;

            var deliveryLabel = this.FindByName<Label>("DeliveryLabel");
            if (deliveryLabel != null) deliveryLabel.FontSize = 20 * scaleFactor;

            var collectionLabel = this.FindByName<Label>("CollectionLabel");
            if (collectionLabel != null) collectionLabel.FontSize = 20 * scaleFactor;

            var liveOrderLabel = this.FindByName<Label>("LiveOrderLabel");
            if (liveOrderLabel != null) liveOrderLabel.FontSize = 20 * scaleFactor;

            // Update button grid spacing
            var grid = this.FindByName<Grid>("ButtonGrid");
            if (grid != null)
            {
                TabletLayoutHelper.ApplyDashboardGrid(
                    grid,
                    new View[] { RestaurantServiceButton, DeliveryServiceButton, CollectionServiceButton, LiveOrderServiceButton },
                    screenWidth,
                    screenHeight);
            }

            // Update main stack layout padding and margins
            var mainStack = this.FindByName<StackLayout>("MainStackLayout");
            if (mainStack != null)
            {
                mainStack.Padding = new Thickness(18 * scaleFactor);
                mainStack.Margin = new Thickness(0, 14 * scaleFactor, 0, 14 * scaleFactor);
            }

            System.Diagnostics.Debug.WriteLine($"Responsive sizing updated: Scale={scaleFactor:F2}, MinDim={minDimension}, Screen={screenWidth}x{screenHeight}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Responsive sizing error: {ex.Message}");
        }
    }



}
