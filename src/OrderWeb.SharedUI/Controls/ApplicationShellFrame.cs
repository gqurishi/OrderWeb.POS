using System.Collections;

namespace OrderWeb.SharedUI.Controls;

public class ApplicationShellFrame : ContentView
{
    private readonly Grid _root;
    private readonly Grid _appGrid;
    private readonly ContentView _contentHost;
    private readonly ApplicationHeader _header;
    private readonly ApplicationSidebar _sidebar;
    private readonly Grid _navigationLayer;
    private readonly Grid _loadingLayer;
    private readonly ChefLoaderView _chefLoader;
    private readonly Label _loadingMessage;
    private readonly Grid _errorLayer;
    private readonly Label _errorTitle;
    private readonly Label _errorMessage;
    private readonly Grid _sessionExpiredLayer;
    private readonly SessionExpiredDialog _sessionExpiredDialog;
    private readonly PosToast _toast;
    private readonly Grid _toastLayer;
    private readonly ContentView _dialogHost;
    private readonly Grid _dialogLayer;
    private readonly Border _banner;
    private readonly Label _bannerMessage;
    private CancellationTokenSource? _toastAutoHideCts;
    private const int ToastAutoHideMs = 2000;

    public static readonly BindableProperty MainContentProperty = BindableProperty.Create(nameof(MainContent), typeof(View), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._contentHost.Content = (View?)v);
    public static readonly BindableProperty PageTitleProperty = BindableProperty.Create(nameof(PageTitle), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._header.Title = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(nameof(UserName), typeof(string), typeof(ApplicationShellFrame), "No user", propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty UserRoleProperty = BindableProperty.Create(nameof(UserRole), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty TerminalNameProperty = BindableProperty.Create(nameof(TerminalName), typeof(string), typeof(ApplicationShellFrame), "Terminal", propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(ApplicationShellFrame), "Connected", propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty MenuItemsProperty = BindableProperty.Create(nameof(MenuItems), typeof(IEnumerable), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.ItemsSource = (IEnumerable?)v);
    public static readonly BindableProperty SelectedRouteProperty = BindableProperty.Create(nameof(SelectedRoute), typeof(string), typeof(ApplicationShellFrame), string.Empty, BindingMode.TwoWay, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.SelectedRoute = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty IsNavigationOpenProperty = BindableProperty.Create(nameof(IsNavigationOpen), typeof(bool), typeof(ApplicationShellFrame), false, BindingMode.TwoWay, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyNavigation((bool)v));
    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(nameof(IsLoading), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) =>
    {
        var frame = (ApplicationShellFrame)b;
        var loading = (bool)v;
        frame._chefLoader.IsLoading = loading;
        frame.ApplyLoadingChrome();
    });
    public static readonly BindableProperty LoadingMessageProperty = BindableProperty.Create(nameof(LoadingMessage), typeof(string), typeof(ApplicationShellFrame), "Cooking up your data…", propertyChanged: (b, _, v) =>
    {
        var text = string.IsNullOrWhiteSpace(v?.ToString()) ? "Cooking up your data…" : v!.ToString()!;
        var frame = (ApplicationShellFrame)b;
        frame._loadingMessage.Text = text;
        frame._chefLoader.Message = text;
    });
    public static readonly BindableProperty ErrorTitleProperty = BindableProperty.Create(nameof(ErrorTitle), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._errorTitle.Text = string.IsNullOrWhiteSpace(v?.ToString()) ? "Something went wrong" : v!.ToString()!);
    public static readonly BindableProperty ErrorMessageProperty = BindableProperty.Create(nameof(ErrorMessage), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyError(v?.ToString()));
    public static readonly BindableProperty ShowUpdateButtonProperty = BindableProperty.Create(nameof(ShowUpdateButton), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.ShowUpdateButton = (bool)v);
    public static readonly BindableProperty AvailableCapabilitiesProperty = BindableProperty.Create(nameof(AvailableCapabilities), typeof(IReadOnlySet<string>), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.AvailableCapabilities = (IReadOnlySet<string>?)v);
    public static readonly BindableProperty AvailableFeaturesProperty = BindableProperty.Create(nameof(AvailableFeatures), typeof(IReadOnlySet<string>), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.AvailableFeatures = (IReadOnlySet<string>?)v);
    public static readonly BindableProperty IsSessionExpiredProperty = BindableProperty.Create(nameof(IsSessionExpired), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplySessionExpired((bool)v));
    public static readonly BindableProperty ToastTitleProperty = BindableProperty.Create(nameof(ToastTitle), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toast.Title = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty ToastMessageProperty = BindableProperty.Create(nameof(ToastMessage), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toast.Message = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty ToastKindProperty = BindableProperty.Create(nameof(ToastKind), typeof(StatusKind), typeof(ApplicationShellFrame), StatusKind.Info, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toast.Kind = (StatusKind)v);
    public static readonly BindableProperty IsToastVisibleProperty = BindableProperty.Create(nameof(IsToastVisible), typeof(bool), typeof(ApplicationShellFrame), false, BindingMode.TwoWay, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyToastVisible((bool)v));
    public static readonly BindableProperty IsToastRetryVisibleProperty = BindableProperty.Create(nameof(IsToastRetryVisible), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toast.IsRetryVisible = (bool)v);
    public static readonly BindableProperty DialogContentProperty = BindableProperty.Create(nameof(DialogContent), typeof(View), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyDialog((View?)v));
    public static readonly BindableProperty RestaurantNameProperty = BindableProperty.Create(nameof(RestaurantName), typeof(string), typeof(ApplicationShellFrame), "Order Web", propertyChanged: (b, _, _) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty RestaurantLogoProperty = BindableProperty.Create(nameof(RestaurantLogo), typeof(ImageSource), typeof(ApplicationShellFrame), propertyChanged: (b, _, _) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty ShowIdentityProperty = BindableProperty.Create(nameof(ShowIdentity), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._header.ShowIdentity = (bool)v);
    public static readonly BindableProperty ShowConnectionProperty = BindableProperty.Create(nameof(ShowConnection), typeof(bool), typeof(ApplicationShellFrame), true, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._header.ShowConnection = (bool)v);
    public static readonly BindableProperty ShowWelcomeBrandProperty = BindableProperty.Create(nameof(ShowWelcomeBrand), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, _) => ((ApplicationShellFrame)b).ApplyWelcomeBrandLayout());
    public static readonly BindableProperty ShowMinimizeProperty = BindableProperty.Create(nameof(ShowMinimize), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._header.ShowMinimize = (bool)v);
    public static readonly BindableProperty BannerMessageProperty = BindableProperty.Create(nameof(BannerMessage), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyBanner(v?.ToString()));
    public static readonly BindableProperty IsBannerVisibleProperty = BindableProperty.Create(nameof(IsBannerVisible), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyBannerVisible((bool)v));

    public ApplicationShellFrame()
    {
        _header = new ApplicationHeader();
        _header.MenuClicked += (_, _) => IsNavigationOpen = !IsNavigationOpen;
        _header.LogoutClicked += (_, _) => LogoutRequested?.Invoke(this, EventArgs.Empty);
        _header.MinimizeClicked += (_, _) => MinimizeRequested?.Invoke(this, EventArgs.Empty);
        _header.HeightRequestChanged += (_, height) =>
        {
            if (_appGrid.RowDefinitions.Count > 0)
            {
                _appGrid.RowDefinitions[0].Height = height;
            }
        };
        _contentHost = new ContentView { BackgroundColor = Colors.White };

        _sidebar = new ApplicationSidebar { HorizontalOptions = LayoutOptions.Start, TranslationX = -280 };
        _sidebar.NavigationRequested += (_, e) => { SelectedRoute = e.Route; IsNavigationOpen = false; NavigationRequested?.Invoke(this, e); };
        _sidebar.LogoutRequested += (_, _) => LogoutRequested?.Invoke(this, EventArgs.Empty);
        _sidebar.UpdateRequested += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        var dismiss = new BoxView
        {
            BackgroundColor = Color.FromArgb("#660F172A"),
            Margin = new Thickness(280, 0, 0, 0),
            InputTransparent = false
        };
        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += (_, _) => IsNavigationOpen = false;
        dismiss.GestureRecognizers.Add(dismissTap);
        _sidebar.InputTransparent = false;
        // Layer itself stays InputTransparent so empty chrome does not steal taps;
        // only the dismiss scrim + sidebar receive input while open.
        _navigationLayer = new Grid
        {
            IsVisible = false,
            ZIndex = 20,
            InputTransparent = true,
            Children = { dismiss, _sidebar }
        };

        _loadingMessage = new Label { Text = "Cooking up your data…", FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, IsVisible = false };
        _loadingMessage.Use(Label.TextColorProperty, "OwTextStrong");
        _chefLoader = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Fullscreen,
            Size = ChefLoaderSize.Md,
            Message = "Cooking up your data…",
            // Short waits never flash; only real long loads reveal fullscreen chef.
            DelayMilliseconds = 280,
            IsLoading = false
        };
        _chefLoader.LoadingChromeChanged += (_, _) => ApplyLoadingChrome();
        _loadingLayer = new Grid { IsVisible = false, InputTransparent = true, ZIndex = 30, Children = { _chefLoader } };

        _errorTitle = new Label { Text = "Something went wrong", FontSize = 22, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; _errorTitle.Use(Label.TextColorProperty, "OwTextStrong");
        _errorMessage = new Label { FontSize = 15, HorizontalTextAlignment = TextAlignment.Center }; _errorMessage.Use(Label.TextColorProperty, "OwTextMuted");
        var retry = new SharedButton { Text = "Try Again" }; retry.Clicked += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);
        var close = new SharedButton { Text = "Close", Variant = ButtonVariant.Secondary }; close.Clicked += (_, _) => ErrorMessage = string.Empty;
        var errorButtons = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 }; errorButtons.Add(close); errorButtons.Add(retry, 1);
        var errorCard = new Border { WidthRequest = 460, MaximumWidthRequest = 460, Padding = 24, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = new VerticalStackLayout { Spacing = 18, Children = { _errorTitle, _errorMessage, errorButtons } } };
        errorCard.Use(Border.BackgroundColorProperty, "OwSurface"); errorCard.Use(Border.StrokeProperty, "OwErrorBorder");
        _errorLayer = new Grid { IsVisible = false, InputTransparent = true, ZIndex = 40, BackgroundColor = Color.FromArgb("#80000000"), Padding = 20, Children = { errorCard } };

        _sessionExpiredDialog = new SessionExpiredDialog();
        _sessionExpiredDialog.LoginAgainRequested += (_, _) => { IsSessionExpired = false; SessionExpiredLoginRequested?.Invoke(this, EventArgs.Empty); };
        _sessionExpiredDialog.DismissRequested += (_, _) => { IsSessionExpired = false; LogoutRequested?.Invoke(this, EventArgs.Empty); };
        _sessionExpiredLayer = new Grid { IsVisible = false, InputTransparent = true, ZIndex = 50, Children = { _sessionExpiredDialog } };

        _toast = new PosToast();
        _toast.DismissRequested += (_, _) => IsToastVisible = false;
        _toast.RetryRequested += (_, _) => ToastRetryRequested?.Invoke(this, EventArgs.Empty);
        _toastLayer = new Grid { IsVisible = false, InputTransparent = true, ZIndex = 45, Padding = 20, VerticalOptions = LayoutOptions.Start, HorizontalOptions = LayoutOptions.End, MaximumWidthRequest = 460, Children = { _toast } };
        _dialogHost = new ContentView { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _dialogLayer = new Grid { IsVisible = false, InputTransparent = true, ZIndex = 46, Padding = 20, BackgroundColor = Color.FromArgb("#66000000"), Children = { _dialogHost } };

        _bannerMessage = new Label
        {
            FontSize = 14,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Fill,
            LineBreakMode = LineBreakMode.WordWrap
        };
        _bannerMessage.Use(Label.TextColorProperty, "OwTextStrong");
        var bannerDetails = new SharedButton { Text = "Details", Variant = ButtonVariant.Secondary };
        bannerDetails.Clicked += (_, _) => BannerDetailsRequested?.Invoke(this, EventArgs.Empty);
        var bannerRetry = new SharedButton { Text = "Retry" };
        bannerRetry.Clicked += (_, _) => BannerRetryRequested?.Invoke(this, EventArgs.Empty);
        var bannerDismiss = new SharedButton { Text = "Dismiss", Variant = ButtonVariant.Secondary };
        bannerDismiss.Clicked += (_, _) =>
        {
            IsBannerVisible = false;
            BannerDismissRequested?.Invoke(this, EventArgs.Empty);
        };
        var bannerActions = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { bannerDetails, bannerRetry, bannerDismiss }
        };
        _banner = new Border
        {
            IsVisible = false,
            Padding = new Thickness(16, 10),
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#FEF3C7"),
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 12,
                Children = { _bannerMessage, bannerActions }
            }
        };
        Grid.SetColumn(bannerActions, 1);

        _appGrid = new Grid
        {
            BackgroundColor = Colors.White,
            RowDefinitions =
            {
                new RowDefinition(64),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        _appGrid.Add(_header);
        _appGrid.Add(_banner, 0, 1);
        _appGrid.Add(_contentHost, 0, 2);
        _root = new Grid { BackgroundColor = Colors.White, Children = { _appGrid, _navigationLayer, _loadingLayer, _errorLayer, _toastLayer, _dialogLayer, _sessionExpiredLayer } };
        Content = _root;
        ApplyIdentity();
        ApplyWelcomeBrandLayout();
    }

    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;
    public event EventHandler? LogoutRequested;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? UpdateRequested;
    public event EventHandler? RetryRequested;
    public event EventHandler? SessionExpiredLoginRequested;
    public event EventHandler? ToastRetryRequested;
    public event EventHandler? BannerDetailsRequested;
    public event EventHandler? BannerRetryRequested;
    public event EventHandler? BannerDismissRequested;
    public View? MainContent { get => (View?)GetValue(MainContentProperty); set => SetValue(MainContentProperty, value); }
    public string PageTitle { get => (string)GetValue(PageTitleProperty); set => SetValue(PageTitleProperty, value); }
    public string UserName { get => (string)GetValue(UserNameProperty); set => SetValue(UserNameProperty, value); }
    public string UserRole { get => (string)GetValue(UserRoleProperty); set => SetValue(UserRoleProperty, value); }
    public string TerminalName { get => (string)GetValue(TerminalNameProperty); set => SetValue(TerminalNameProperty, value); }
    public string ConnectionStatus { get => (string)GetValue(ConnectionStatusProperty); set => SetValue(ConnectionStatusProperty, value); }
    public IEnumerable? MenuItems { get => (IEnumerable?)GetValue(MenuItemsProperty); set => SetValue(MenuItemsProperty, value); }
    public string SelectedRoute { get => (string)GetValue(SelectedRouteProperty); set => SetValue(SelectedRouteProperty, value); }
    public bool IsNavigationOpen { get => (bool)GetValue(IsNavigationOpenProperty); set => SetValue(IsNavigationOpenProperty, value); }
    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public string LoadingMessage { get => (string)GetValue(LoadingMessageProperty); set => SetValue(LoadingMessageProperty, value); }
    public string ErrorTitle { get => (string)GetValue(ErrorTitleProperty); set => SetValue(ErrorTitleProperty, value); }
    public string ErrorMessage { get => (string)GetValue(ErrorMessageProperty); set => SetValue(ErrorMessageProperty, value); }
    public bool ShowUpdateButton { get => (bool)GetValue(ShowUpdateButtonProperty); set => SetValue(ShowUpdateButtonProperty, value); }
    public IReadOnlySet<string>? AvailableCapabilities { get => (IReadOnlySet<string>?)GetValue(AvailableCapabilitiesProperty); set => SetValue(AvailableCapabilitiesProperty, value); }
    public IReadOnlySet<string>? AvailableFeatures { get => (IReadOnlySet<string>?)GetValue(AvailableFeaturesProperty); set => SetValue(AvailableFeaturesProperty, value); }
    public bool IsSessionExpired { get => (bool)GetValue(IsSessionExpiredProperty); set => SetValue(IsSessionExpiredProperty, value); }
    public string ToastTitle { get => (string)GetValue(ToastTitleProperty); set => SetValue(ToastTitleProperty, value); }
    public string ToastMessage { get => (string)GetValue(ToastMessageProperty); set => SetValue(ToastMessageProperty, value); }
    public StatusKind ToastKind { get => (StatusKind)GetValue(ToastKindProperty); set => SetValue(ToastKindProperty, value); }
    public bool IsToastVisible { get => (bool)GetValue(IsToastVisibleProperty); set => SetValue(IsToastVisibleProperty, value); }
    public bool IsToastRetryVisible { get => (bool)GetValue(IsToastRetryVisibleProperty); set => SetValue(IsToastRetryVisibleProperty, value); }
    public View? DialogContent { get => (View?)GetValue(DialogContentProperty); set => SetValue(DialogContentProperty, value); }
    public string RestaurantName { get => (string)GetValue(RestaurantNameProperty); set => SetValue(RestaurantNameProperty, value); }
    public ImageSource? RestaurantLogo { get => (ImageSource?)GetValue(RestaurantLogoProperty); set => SetValue(RestaurantLogoProperty, value); }
    public bool ShowIdentity { get => (bool)GetValue(ShowIdentityProperty); set => SetValue(ShowIdentityProperty, value); }
    public bool ShowConnection { get => (bool)GetValue(ShowConnectionProperty); set => SetValue(ShowConnectionProperty, value); }
    public bool ShowWelcomeBrand { get => (bool)GetValue(ShowWelcomeBrandProperty); set => SetValue(ShowWelcomeBrandProperty, value); }
    public bool ShowMinimize { get => (bool)GetValue(ShowMinimizeProperty); set => SetValue(ShowMinimizeProperty, value); }
    public string BannerMessage { get => (string)GetValue(BannerMessageProperty); set => SetValue(BannerMessageProperty, value); }
    public bool IsBannerVisible { get => (bool)GetValue(IsBannerVisibleProperty); set => SetValue(IsBannerVisibleProperty, value); }
    public bool IsUpdating { get => _sidebar.IsUpdating; set => _sidebar.IsUpdating = value; }
    public void RefreshMenu() => _sidebar.Refresh();
    public void SetSidebarVisible(bool visible)
    {
        _sidebar.IsVisible = visible;
        _header.SetMenuVisible(visible);
    }

    private void ApplyIdentity()
    {
        if (_header == null || _sidebar == null) return;
        _header.UserName = UserName; _header.TerminalName = TerminalName; _header.ConnectionStatus = ConnectionStatus; _header.RestaurantName = RestaurantName; _header.RestaurantLogo = RestaurantLogo;
        _sidebar.UserName = UserName; _sidebar.TerminalName = TerminalName; _sidebar.ConnectionStatus = ConnectionStatus; _sidebar.CurrentRole = UserRole; _sidebar.RestaurantName = "Order Web"; _sidebar.RestaurantLogo = RestaurantLogo;
    }

    private void ApplyWelcomeBrandLayout()
    {
        if (_header == null || _appGrid == null) return;
        _header.ShowWelcomeBrand = ShowWelcomeBrand;
        var height = _header.PreferredHeight;
        if (_appGrid.RowDefinitions.Count > 0)
        {
            _appGrid.RowDefinitions[0].Height = height;
        }

        _appGrid.BackgroundColor = ShowWelcomeBrand ? Colors.White : Color.FromArgb("#F8FAFC");
        _contentHost.BackgroundColor = ShowWelcomeBrand ? Colors.White : Color.FromArgb("#F8FAFC");
        _root.BackgroundColor = ShowWelcomeBrand ? Colors.White : Color.FromArgb("#F8FAFC");
    }

    private async void ApplyNavigation(bool open)
    {
        if (_navigationLayer == null)
        {
            return;
        }

        if (open)
        {
            _navigationLayer.InputTransparent = false;
            foreach (var child in _navigationLayer.Children)
            {
                if (child is VisualElement element)
                {
                    element.InputTransparent = false;
                }
            }

            _navigationLayer.IsVisible = true;
            await _sidebar.TranslateToAsync(0, 0, 180, Easing.CubicOut);
            return;
        }

        if (!_navigationLayer.IsVisible)
        {
            return;
        }

        // Drop hit-testing immediately so the first content tap is never eaten by the closing scrim.
        _navigationLayer.InputTransparent = true;
        foreach (var child in _navigationLayer.Children)
        {
            if (child is VisualElement element)
            {
                element.InputTransparent = true;
            }
        }

        await _sidebar.TranslateToAsync(-280, 0, 100, Easing.CubicIn);
        _navigationLayer.IsVisible = false;
    }

    private void ApplyLoadingChrome()
    {
        if (_loadingLayer == null || _chefLoader == null)
        {
            return;
        }

        var loading = IsLoading || _chefLoader.IsLoading;
        var block = _chefLoader.BlocksInput;
        _loadingLayer.IsVisible = loading;
        // Never block taps while opacity is 0 / delay phase; only after chef reveals.
        _loadingLayer.InputTransparent = !block;
        if (!loading)
        {
            _chefLoader.InputTransparent = true;
        }
    }

    private void ApplyError(string? message)
    {
        if (_errorLayer == null)
        {
            return;
        }

        _errorMessage.Text = message ?? string.Empty;
        var visible = !string.IsNullOrWhiteSpace(message);
        _errorLayer.IsVisible = visible;
        _errorLayer.InputTransparent = !visible;
    }

    private void ApplySessionExpired(bool expired)
    {
        if (_sessionExpiredLayer == null)
        {
            return;
        }

        _sessionExpiredLayer.IsVisible = expired;
        _sessionExpiredLayer.InputTransparent = !expired;
    }

    private void ApplyDialog(View? content)
    {
        if (_dialogHost == null || _dialogLayer == null)
        {
            return;
        }

        _dialogHost.Content = content;
        var visible = content is not null;
        _dialogLayer.IsVisible = visible;
        _dialogLayer.InputTransparent = !visible;
    }

    private void ApplyBanner(string? message)
    {
        if (_bannerMessage == null)
        {
            return;
        }

        _bannerMessage.Text = message ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(message))
        {
            IsBannerVisible = true;
        }
    }

    private void ApplyBannerVisible(bool visible)
    {
        if (_banner == null)
        {
            return;
        }

        _banner.IsVisible = visible;
    }

    private void ApplyToastVisible(bool visible)
    {
        if (_toastLayer == null)
        {
            return;
        }

        _toastLayer.IsVisible = visible;
        _toastLayer.InputTransparent = !visible;
        CancelToastAutoHide();
        if (!visible)
        {
            return;
        }

        _toastAutoHideCts = new CancellationTokenSource();
        var token = _toastAutoHideCts.Token;
        _ = AutoHideToastAsync(token);
    }

    private async Task AutoHideToastAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ToastAutoHideMs, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    IsToastVisible = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelToastAutoHide()
    {
        if (_toastAutoHideCts is null)
        {
            return;
        }

        try
        {
            _toastAutoHideCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _toastAutoHideCts.Dispose();
        _toastAutoHideCts = null;
    }
}
