using POS_in_NET.Services;
using POS_in_NET.Helpers;
using System.Linq;

namespace POS_in_NET.Pages;

public partial class ManagerDashboardPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly InactivityService _inactivityService;
    private readonly BusinessSettingsService _businessSettingsService;
    private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private IDispatcherTimer? _timer;

    public ManagerDashboardPage()
    {
        InitializeComponent();
        _authService = AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _inactivityService = ServiceHelper.GetService<InactivityService>() ?? new InactivityService(_authService, _roleAccessService);
        _businessSettingsService = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
        _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
            ?? new OrderServiceAvailabilityService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(), _authService);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _inactivityService.Start();
        _inactivityService.ResetActivity();
        _inactivityService.TrackPage(this);

        var user = _authService.CurrentUser;

        if (!_roleAccessService.IsManagerOrAdmin(user?.Role))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access Manager Dashboard.");
            await _navigationCoordinator.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(user?.Role));
            return;
        }

        StartTimeUpdates();
        OnPageSizeChanged(this, null);
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

    private async Task LoadBusinessNameAsync()
    {
        try
        {
            var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
            var businessName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
                ? "Restaurant POS"
                : businessInfo.RestaurantName.Trim();

            HeaderTitleLabel.Text = businessName;
            WelcomeLabel.Text = "Welcome to";
            Title = businessName;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Load business name error: {ex.Message}");
            HeaderTitleLabel.Text = "Restaurant POS";
            WelcomeLabel.Text = "Welcome to";
            Title = "Restaurant POS";
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }

    private void StartTimeUpdates()
    {
        _timer?.Stop();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += (s, e) =>
        {
            DateLabel.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
            TimeLabel.Text = DateTime.Now.ToString("h:mm tt");
        };
        _timer.Start();

        // Set initial values
        DateLabel.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        TimeLabel.Text = DateTime.Now.ToString("h:mm tt");
    }

    private void OnPageSizeChanged(object sender, EventArgs e)
    {
        try
        {
            double screenWidth = this.Width;
            double screenHeight = this.Height;

            if (screenWidth <= 0 || screenHeight <= 0)
                return;

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

            double iconSize = 150 * scaleFactor;

            var restaurantIcon = this.FindByName<Image>("RestaurantIcon");
            if (restaurantIcon != null) { restaurantIcon.WidthRequest = iconSize; restaurantIcon.HeightRequest = iconSize; }

            var deliveryIcon = this.FindByName<Image>("DeliveryIcon");
            if (deliveryIcon != null) { deliveryIcon.WidthRequest = iconSize; deliveryIcon.HeightRequest = iconSize; }

            var collectionIcon = this.FindByName<Image>("CollectionIcon");
            if (collectionIcon != null) { collectionIcon.WidthRequest = iconSize; collectionIcon.HeightRequest = iconSize; }

            var liveOrderIcon = this.FindByName<Image>("LiveOrderIcon");
            if (liveOrderIcon != null) { liveOrderIcon.WidthRequest = iconSize; liveOrderIcon.HeightRequest = iconSize; }

            var webOrdersIcon = this.FindByName<Image>("WebOrdersIcon");
            if (webOrdersIcon != null) { webOrdersIcon.WidthRequest = iconSize; webOrdersIcon.HeightRequest = iconSize; }

            var giftCardsIcon = this.FindByName<Image>("GiftCardsIcon");
            if (giftCardsIcon != null) { giftCardsIcon.WidthRequest = iconSize; giftCardsIcon.HeightRequest = iconSize; }

            var loyaltyIcon = this.FindByName<Image>("LoyaltyIcon");
            if (loyaltyIcon != null) { loyaltyIcon.WidthRequest = iconSize; loyaltyIcon.HeightRequest = iconSize; }

            var reservationIcon = this.FindByName<Image>("ReservationIcon");
            if (reservationIcon != null) { reservationIcon.WidthRequest = iconSize; reservationIcon.HeightRequest = iconSize; }

            var orderHistoryIcon = this.FindByName<Image>("OrderHistoryIcon");
            if (orderHistoryIcon != null) { orderHistoryIcon.WidthRequest = iconSize; orderHistoryIcon.HeightRequest = iconSize; }

            var restaurantLabel = this.FindByName<Label>("RestaurantLabel");
            if (restaurantLabel != null) restaurantLabel.FontSize = 20 * scaleFactor;

            var deliveryLabel = this.FindByName<Label>("DeliveryLabel");
            if (deliveryLabel != null) deliveryLabel.FontSize = 20 * scaleFactor;

            var collectionLabel = this.FindByName<Label>("CollectionLabel");
            if (collectionLabel != null) collectionLabel.FontSize = 20 * scaleFactor;

            var liveOrderLabel = this.FindByName<Label>("LiveOrderLabel");
            if (liveOrderLabel != null) liveOrderLabel.FontSize = 20 * scaleFactor;

            var webOrdersLabel = this.FindByName<Label>("WebOrdersLabel");
            if (webOrdersLabel != null) webOrdersLabel.FontSize = 16 * scaleFactor;

            var giftCardsLabel = this.FindByName<Label>("GiftCardsLabel");
            if (giftCardsLabel != null) giftCardsLabel.FontSize = 16 * scaleFactor;

            var loyaltyLabel = this.FindByName<Label>("LoyaltyLabel");
            if (loyaltyLabel != null) loyaltyLabel.FontSize = 16 * scaleFactor;

            var reservationLabel = this.FindByName<Label>("ReservationLabel");
            if (reservationLabel != null) reservationLabel.FontSize = 16 * scaleFactor;

            var orderHistoryLabel = this.FindByName<Label>("OrderHistoryLabel");
            if (orderHistoryLabel != null) orderHistoryLabel.FontSize = 16 * scaleFactor;

            var welcomeLabel = this.FindByName<Label>("WelcomeLabel");
            if (welcomeLabel != null) welcomeLabel.FontSize = 12 * scaleFactor;

            var headerTitleLabel = this.FindByName<Label>("HeaderTitleLabel");
            if (headerTitleLabel != null) headerTitleLabel.FontSize = 21 * scaleFactor;

            var dateLabel = this.FindByName<Label>("DateLabel");
            if (dateLabel != null) dateLabel.FontSize = 12 * scaleFactor;

            var timeLabel = this.FindByName<Label>("TimeLabel");
            if (timeLabel != null) timeLabel.FontSize = 16 * scaleFactor;

            var grid = this.FindByName<Grid>("ButtonGrid");
            if (grid != null)
            {
                TabletLayoutHelper.ApplyDashboardGrid(
                    grid,
                    new View[] { RestaurantServiceButton, DeliveryServiceButton, CollectionServiceButton, LiveOrderServiceButton },
                    screenWidth,
                    screenHeight);
            }

            var mainStack = this.FindByName<StackLayout>("MainStackLayout");
            if (mainStack != null)
            {
                mainStack.Padding = new Thickness(18 * scaleFactor);
                mainStack.Margin = new Thickness(0, 14 * scaleFactor, 0, 14 * scaleFactor);
            }

            System.Diagnostics.Debug.WriteLine($"Manager dashboard responsive sizing: Scale={scaleFactor:F2}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Responsive sizing error: {ex.Message}");
        }
    }

    private async Task NavigateToAsync(string route, bool isModal = false, VisualElement? source = null)
    {
        try
        {
            _inactivityService.ResetActivity();

            var resolvedRoute = _roleAccessService.ResolveRouteForRole(_authService.CurrentUser?.Role, route);
            await _orderServiceAvailabilityService.GetAsync();
            if (!_orderServiceAvailabilityService.IsRouteEnabled(resolvedRoute))
            {
                await AppAlertService.ShowAlertAsync("Service Unavailable", "This order service is disabled by the Administrator.");
                return;
            }

            if (!_roleAccessService.CanAccessRoute(_authService.CurrentUser?.Role, resolvedRoute))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access this module.");
                return;
            }

            if (isModal)
            {
                await _navigationCoordinator.NavigateTemporaryRouteAsync(resolvedRoute, source: source);
            }
            else
            {
                if (resolvedRoute.Equals("visuallayout", StringComparison.OrdinalIgnoreCase))
                {
                    ClearShellDetailStacks();
                }

                await _navigationCoordinator.NavigateShellAsync(resolvedRoute, animated: false, source: source);
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Navigation Error", ex.Message);
        }
    }

    private async void OnRestaurantClicked(object sender, EventArgs e) => await NavigateToAsync("visuallayout", source: sender as VisualElement);
    private async void OnCollectionClicked(object sender, EventArgs e) => await NavigateToAsync("collection", isModal: true, source: sender as VisualElement);
    private async void OnDeliveryClicked(object sender, EventArgs e) => await NavigateToAsync("delivery", isModal: true, source: sender as VisualElement);
    private async void OnLiveOrderClicked(object sender, EventArgs e) => await NavigateToAsync("liveorder", source: sender as VisualElement);
    private async void OnWebOrdersClicked(object sender, EventArgs e) => await NavigateToAsync("weborders");
    private async void OnGiftCardsClicked(object sender, EventArgs e) => await NavigateToAsync("giftcards");
    private async void OnLoyaltyClicked(object sender, EventArgs e) => await NavigateToAsync("loyalty");
    private async void OnReservationClicked(object sender, EventArgs e) => await NavigateToAsync("reservation");
    private async void OnOrderHistoryClicked(object sender, EventArgs e) => await NavigateToAsync("orderhistory");

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

    private static void ClearShellDetailStacks()
    {
        NavigationCoordinator.PruneTemporaryPages();
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        _inactivityService.ResetActivity();
        _timer?.Stop();
        await _authService.LogoutAsync();
        await _navigationCoordinator.NavigateShellAsync("login", animated: false, source: sender as VisualElement);
    }
}
