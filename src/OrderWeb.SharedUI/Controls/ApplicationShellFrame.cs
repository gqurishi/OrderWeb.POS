using System.Collections;

namespace OrderWeb.SharedUI.Controls;

public class ApplicationShellFrame : ContentView
{
    private readonly Grid _root;
    private readonly ContentView _contentHost;
    private readonly ApplicationHeader _header;
    private readonly ApplicationSidebar _sidebar;
    private readonly Grid _navigationLayer;
    private readonly Grid _loadingLayer;
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

    public static readonly BindableProperty MainContentProperty = BindableProperty.Create(nameof(MainContent), typeof(View), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._contentHost.Content = (View?)v);
    public static readonly BindableProperty PageTitleProperty = BindableProperty.Create(nameof(PageTitle), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._header.Title = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(nameof(UserName), typeof(string), typeof(ApplicationShellFrame), "No user", propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty UserRoleProperty = BindableProperty.Create(nameof(UserRole), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty TerminalNameProperty = BindableProperty.Create(nameof(TerminalName), typeof(string), typeof(ApplicationShellFrame), "Terminal", propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(ApplicationShellFrame), "Connected", propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty MenuItemsProperty = BindableProperty.Create(nameof(MenuItems), typeof(IEnumerable), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.ItemsSource = (IEnumerable?)v);
    public static readonly BindableProperty SelectedRouteProperty = BindableProperty.Create(nameof(SelectedRoute), typeof(string), typeof(ApplicationShellFrame), string.Empty, BindingMode.TwoWay, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.SelectedRoute = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty IsNavigationOpenProperty = BindableProperty.Create(nameof(IsNavigationOpen), typeof(bool), typeof(ApplicationShellFrame), false, BindingMode.TwoWay, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyNavigation((bool)v));
    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(nameof(IsLoading), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._loadingLayer.IsVisible = (bool)v);
    public static readonly BindableProperty LoadingMessageProperty = BindableProperty.Create(nameof(LoadingMessage), typeof(string), typeof(ApplicationShellFrame), "Loading…", propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._loadingMessage.Text = v?.ToString() ?? "Loading…");
    public static readonly BindableProperty ErrorTitleProperty = BindableProperty.Create(nameof(ErrorTitle), typeof(string), typeof(ApplicationShellFrame), "Something went wrong", propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._errorTitle.Text = v?.ToString() ?? "Something went wrong");
    public static readonly BindableProperty ErrorMessageProperty = BindableProperty.Create(nameof(ErrorMessage), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyError(v?.ToString()));
    public static readonly BindableProperty ShowUpdateButtonProperty = BindableProperty.Create(nameof(ShowUpdateButton), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.ShowUpdateButton = (bool)v);
    public static readonly BindableProperty AvailableCapabilitiesProperty = BindableProperty.Create(nameof(AvailableCapabilities), typeof(IReadOnlySet<string>), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.AvailableCapabilities = (IReadOnlySet<string>?)v);
    public static readonly BindableProperty AvailableFeaturesProperty = BindableProperty.Create(nameof(AvailableFeatures), typeof(IReadOnlySet<string>), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._sidebar.AvailableFeatures = (IReadOnlySet<string>?)v);
    public static readonly BindableProperty IsSessionExpiredProperty = BindableProperty.Create(nameof(IsSessionExpired), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplySessionExpired((bool)v));
    public static readonly BindableProperty ToastTitleProperty = BindableProperty.Create(nameof(ToastTitle), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toast.Title = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty ToastMessageProperty = BindableProperty.Create(nameof(ToastMessage), typeof(string), typeof(ApplicationShellFrame), string.Empty, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toast.Message = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty ToastKindProperty = BindableProperty.Create(nameof(ToastKind), typeof(StatusKind), typeof(ApplicationShellFrame), StatusKind.Info, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toast.Kind = (StatusKind)v);
    public static readonly BindableProperty IsToastVisibleProperty = BindableProperty.Create(nameof(IsToastVisible), typeof(bool), typeof(ApplicationShellFrame), false, BindingMode.TwoWay, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toastLayer.IsVisible = (bool)v);
    public static readonly BindableProperty IsToastRetryVisibleProperty = BindableProperty.Create(nameof(IsToastRetryVisible), typeof(bool), typeof(ApplicationShellFrame), false, propertyChanged: (b, _, v) => ((ApplicationShellFrame)b)._toast.IsRetryVisible = (bool)v);
    public static readonly BindableProperty DialogContentProperty = BindableProperty.Create(nameof(DialogContent), typeof(View), typeof(ApplicationShellFrame), propertyChanged: (b, _, v) => ((ApplicationShellFrame)b).ApplyDialog((View?)v));
    public static readonly BindableProperty RestaurantNameProperty = BindableProperty.Create(nameof(RestaurantName), typeof(string), typeof(ApplicationShellFrame), "Order Web", propertyChanged: (b, _, _) => ((ApplicationShellFrame)b).ApplyIdentity());
    public static readonly BindableProperty RestaurantLogoProperty = BindableProperty.Create(nameof(RestaurantLogo), typeof(ImageSource), typeof(ApplicationShellFrame), propertyChanged: (b, _, _) => ((ApplicationShellFrame)b).ApplyIdentity());

    public ApplicationShellFrame()
    {
        _header = new ApplicationHeader();
        _header.MenuClicked += (_, _) => IsNavigationOpen = !IsNavigationOpen;
        _header.LogoutClicked += (_, _) => LogoutRequested?.Invoke(this, EventArgs.Empty);
        _contentHost = new ContentView();

        _sidebar = new ApplicationSidebar { HorizontalOptions = LayoutOptions.Start, TranslationX = -280 };
        _sidebar.NavigationRequested += (_, e) => { SelectedRoute = e.Route; IsNavigationOpen = false; NavigationRequested?.Invoke(this, e); };
        _sidebar.LogoutRequested += (_, _) => LogoutRequested?.Invoke(this, EventArgs.Empty);
        _sidebar.UpdateRequested += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        var dismiss = new BoxView { BackgroundColor = Color.FromArgb("#01000000"), Margin = new Thickness(280, 0, 0, 0) };
        var dismissTap = new TapGestureRecognizer(); dismissTap.Tapped += (_, _) => IsNavigationOpen = false; dismiss.GestureRecognizers.Add(dismissTap);
        _navigationLayer = new Grid { IsVisible = false, ZIndex = 20, Children = { dismiss, _sidebar } };

        _loadingMessage = new Label { Text = "Loading…", FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        _loadingMessage.Use(Label.TextColorProperty, "OwTextStrong");
        var loadingCard = new Border { Padding = 24, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = new VerticalStackLayout { Spacing = 14, Children = { new ActivityIndicator { IsRunning = true, WidthRequest = 42, HeightRequest = 42, Color = Color.FromArgb("#2563EB") }, _loadingMessage } } };
        loadingCard.Use(Border.BackgroundColorProperty, "OwSurface"); loadingCard.Use(Border.StrokeProperty, "OwBorder");
        _loadingLayer = new Grid { IsVisible = false, ZIndex = 30, BackgroundColor = Color.FromArgb("#66F8FAFC"), Children = { loadingCard } };

        _errorTitle = new Label { Text = "Something went wrong", FontSize = 22, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; _errorTitle.Use(Label.TextColorProperty, "OwTextStrong");
        _errorMessage = new Label { FontSize = 15, HorizontalTextAlignment = TextAlignment.Center }; _errorMessage.Use(Label.TextColorProperty, "OwTextMuted");
        var retry = new SharedButton { Text = "Try Again" }; retry.Clicked += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);
        var close = new SharedButton { Text = "Close", Variant = ButtonVariant.Secondary }; close.Clicked += (_, _) => ErrorMessage = string.Empty;
        var errorButtons = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 }; errorButtons.Add(close); errorButtons.Add(retry, 1);
        var errorCard = new Border { WidthRequest = 460, MaximumWidthRequest = 460, Padding = 24, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = new VerticalStackLayout { Spacing = 18, Children = { _errorTitle, _errorMessage, errorButtons } } };
        errorCard.Use(Border.BackgroundColorProperty, "OwSurface"); errorCard.Use(Border.StrokeProperty, "OwErrorBorder");
        _errorLayer = new Grid { IsVisible = false, ZIndex = 40, BackgroundColor = Color.FromArgb("#80000000"), Padding = 20, Children = { errorCard } };

        _sessionExpiredDialog = new SessionExpiredDialog();
        _sessionExpiredDialog.LoginAgainRequested += (_, _) => { IsSessionExpired = false; SessionExpiredLoginRequested?.Invoke(this, EventArgs.Empty); };
        _sessionExpiredDialog.DismissRequested += (_, _) => { IsSessionExpired = false; LogoutRequested?.Invoke(this, EventArgs.Empty); };
        _sessionExpiredLayer = new Grid { IsVisible = false, ZIndex = 50, Children = { _sessionExpiredDialog } };

        _toast = new PosToast();
        _toast.DismissRequested += (_, _) => IsToastVisible = false;
        _toast.RetryRequested += (_, _) => ToastRetryRequested?.Invoke(this, EventArgs.Empty);
        _toastLayer = new Grid { IsVisible = false, ZIndex = 45, Padding = 20, VerticalOptions = LayoutOptions.Start, HorizontalOptions = LayoutOptions.End, MaximumWidthRequest = 460, Children = { _toast } };
        _dialogHost = new ContentView { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _dialogLayer = new Grid { IsVisible = false, ZIndex = 46, Padding = 20, BackgroundColor = Color.FromArgb("#66000000"), Children = { _dialogHost } };

        var app = new Grid { RowDefinitions = { new RowDefinition(88), new RowDefinition(GridLength.Star) } };
        app.Use(Grid.BackgroundColorProperty, "OwBackground"); app.Add(_header); app.Add(_contentHost, 0, 1);
        _root = new Grid { Children = { app, _navigationLayer, _loadingLayer, _errorLayer, _toastLayer, _dialogLayer, _sessionExpiredLayer } };
        Content = _root;
        ApplyIdentity();
    }

    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;
    public event EventHandler? LogoutRequested;
    public event EventHandler? UpdateRequested;
    public event EventHandler? RetryRequested;
    public event EventHandler? SessionExpiredLoginRequested;
    public event EventHandler? ToastRetryRequested;
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
    public bool IsUpdating { get => _sidebar.IsUpdating; set => _sidebar.IsUpdating = value; }
    public void RefreshMenu() => _sidebar.Refresh();

    private void ApplyIdentity()
    {
        if (_header == null || _sidebar == null) return;
        _header.UserName = UserName; _header.TerminalName = TerminalName; _header.ConnectionStatus = ConnectionStatus; _header.RestaurantName = RestaurantName; _header.RestaurantLogo = RestaurantLogo;
        _sidebar.UserName = UserName; _sidebar.TerminalName = TerminalName; _sidebar.ConnectionStatus = ConnectionStatus; _sidebar.CurrentRole = UserRole; _sidebar.RestaurantName = RestaurantName; _sidebar.RestaurantLogo = RestaurantLogo;
    }
    private async void ApplyNavigation(bool open)
    {
        if (_navigationLayer == null) return;
        if (open) { _navigationLayer.IsVisible = true; await _sidebar.TranslateToAsync(0, 0, 220, Easing.CubicOut); }
        else if (_navigationLayer.IsVisible) { await _sidebar.TranslateToAsync(-280, 0, 180, Easing.CubicIn); _navigationLayer.IsVisible = false; }
    }
    private void ApplyError(string? message) { if (_errorLayer == null) return; _errorMessage.Text = message ?? string.Empty; _errorLayer.IsVisible = !string.IsNullOrWhiteSpace(message); }
    private void ApplySessionExpired(bool expired) { if (_sessionExpiredLayer == null) return; _sessionExpiredLayer.IsVisible = expired; }
    private void ApplyDialog(View? content) { if (_dialogHost == null || _dialogLayer == null) return; _dialogHost.Content = content; _dialogLayer.IsVisible = content is not null; }
}
