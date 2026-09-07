using System.Windows.Input;

namespace OrderWeb.SharedUI.Controls;

public class ApplicationHeader : ContentView
{
    private readonly Label _welcome;
    private readonly Label _title;
    private readonly Label _restaurantName;
    private readonly Image _restaurantLogo;
    private readonly Label _date;
    private readonly Label _time;
    private readonly UserTerminalInfo _identity;
    private readonly ConnectionIndicator _connection;
    private readonly IDispatcherTimer _timer;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(ApplicationHeader), string.Empty, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._title.Text = v?.ToString());
    public static readonly BindableProperty RestaurantNameProperty = BindableProperty.Create(nameof(RestaurantName), typeof(string), typeof(ApplicationHeader), string.Empty, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._restaurantName.Text = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty RestaurantLogoProperty = BindableProperty.Create(nameof(RestaurantLogo), typeof(ImageSource), typeof(ApplicationHeader), propertyChanged: (b, _, v) =>
    {
        var header = (ApplicationHeader)b;
        header._restaurantLogo.Source = (ImageSource?)v;
        header._restaurantLogo.IsVisible = !header.ShowWelcomeBrand && v is ImageSource;
    });
    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(nameof(UserName), typeof(string), typeof(ApplicationHeader), "No user", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.UserName = v?.ToString() ?? "No user");
    public static readonly BindableProperty TerminalNameProperty = BindableProperty.Create(nameof(TerminalName), typeof(string), typeof(ApplicationHeader), "Terminal", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.TerminalName = v?.ToString() ?? "Terminal");
    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(ApplicationHeader), "Connected", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._connection.Status = v?.ToString() ?? "Connected");
    public static readonly BindableProperty ShowIdentityProperty = BindableProperty.Create(nameof(ShowIdentity), typeof(bool), typeof(ApplicationHeader), true, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.IsVisible = (bool)v);
    public static readonly BindableProperty ShowConnectionProperty = BindableProperty.Create(nameof(ShowConnection), typeof(bool), typeof(ApplicationHeader), true, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._connection.IsVisible = (bool)v);
    public static readonly BindableProperty ShowWelcomeBrandProperty = BindableProperty.Create(nameof(ShowWelcomeBrand), typeof(bool), typeof(ApplicationHeader), false, propertyChanged: (b, _, _) => ((ApplicationHeader)b).ApplyWelcomeBrand());
    public static readonly BindableProperty ShowMinimizeProperty = BindableProperty.Create(nameof(ShowMinimize), typeof(bool), typeof(ApplicationHeader), false, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._minimize.IsVisible = (bool)v);
    public static readonly BindableProperty ShowBackButtonProperty = BindableProperty.Create(nameof(ShowBackButton), typeof(bool), typeof(ApplicationHeader), false, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._back.IsVisible = (bool)v);
    public static readonly BindableProperty ContextActionsProperty = BindableProperty.Create(nameof(ContextActions), typeof(IEnumerable<HeaderAction>), typeof(ApplicationHeader), propertyChanged: (b, _, v) => ((ApplicationHeader)b).RebuildContextActions((IEnumerable<HeaderAction>?)v));
    private readonly Button _minimize;
    private readonly Button _back;
    private readonly HorizontalStackLayout _contextActions;
    private ImageButton? _menuButton;

    public ApplicationHeader()
    {
        var menu = IconButton("mian.png", 38);
        _menuButton = menu;
        _back = new Button { Text = "‹", FontSize = 32, Padding = 0, WidthRequest = 38, HeightRequest = 38, MinimumWidthRequest = 44, MinimumHeightRequest = 44, BackgroundColor = Colors.Transparent, BorderWidth = 0 };
        _back.Use(Button.TextColorProperty, "OwTextPrimary");
        _back.IsVisible = false;
        var logout = IconButton("outred.png", 38);
        _minimize = new Button { Text = "−", IsVisible = false, WidthRequest = 36, HeightRequest = 36, MinimumWidthRequest = 36, MinimumHeightRequest = 36, Padding = 0, BackgroundColor = Colors.Transparent, BorderWidth = 0, FontSize = 22, FontAttributes = FontAttributes.Bold };
        _minimize.Use(Button.TextColorProperty, "OwTextPrimary");
        menu.Clicked += (_, _) => MenuClicked?.Invoke(this, EventArgs.Empty);
        _back.Clicked += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        logout.Clicked += (_, _) => LogoutClicked?.Invoke(this, EventArgs.Empty);
        _minimize.Clicked += (_, _) => MinimizeClicked?.Invoke(this, EventArgs.Empty);

        _restaurantLogo = new Image { WidthRequest = 30, HeightRequest = 30, Aspect = Aspect.AspectFit, IsVisible = false };
        _welcome = new Label { Text = "Welcome to", FontSize = 12, IsVisible = false, HorizontalTextAlignment = TextAlignment.Start };
        _welcome.Use(Label.TextColorProperty, "OwTextMuted");
        _restaurantName = new Label { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _restaurantName.Use(Label.TextColorProperty, "OwTextMuted");
        _title = new Label { FontSize = 28, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _title.Use(Label.TextColorProperty, "OwTextStrong");
        _date = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End };
        _date.Use(Label.TextColorProperty, "OwTextStrong");
        _time = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End };
        _time.Use(Label.TextColorProperty, "OwPrimary");
        _identity = new UserTerminalInfo { VerticalOptions = LayoutOptions.Center };
        _connection = new ConnectionIndicator { VerticalOptions = LayoutOptions.Center };

        var clock = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { _date, _time } };
        _contextActions = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        var right = new HorizontalStackLayout { Spacing = 14, VerticalOptions = LayoutOptions.Center, Children = { _contextActions, _identity, _connection, clock, _minimize, logout } };
        var left = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Children = { menu, _back } };
        var restaurantIdentity = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.Start, Children = { _restaurantLogo, _restaurantName } };
        var titleStack = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Fill, Children = { _welcome, restaurantIdentity, _title } };
        var grid = new Grid { Padding = new Thickness(16, 0), ColumnDefinitions = { new ColumnDefinition(98), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
        grid.Use(Grid.HeightRequestProperty, "PosHeaderHeight");
        grid.Add(left); grid.Add(titleStack, 1); grid.Add(right, 2);
        var shell = new Border { StrokeThickness = 0, Content = grid };
        shell.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        Content = shell;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateClock();
        _timer.Start();
        UpdateClock();
        ApplyWelcomeBrand();
    }

    public event EventHandler? MenuClicked;
    public event EventHandler? BackRequested;
    public event EventHandler<HeaderActionEventArgs>? ContextActionRequested;
    public event EventHandler? LogoutClicked;
    public event EventHandler? MinimizeClicked;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string RestaurantName { get => (string)GetValue(RestaurantNameProperty); set => SetValue(RestaurantNameProperty, value); }
    public ImageSource? RestaurantLogo { get => (ImageSource?)GetValue(RestaurantLogoProperty); set => SetValue(RestaurantLogoProperty, value); }
    public string UserName { get => (string)GetValue(UserNameProperty); set => SetValue(UserNameProperty, value); }
    public string TerminalName { get => (string)GetValue(TerminalNameProperty); set => SetValue(TerminalNameProperty, value); }
    public string ConnectionStatus { get => (string)GetValue(ConnectionStatusProperty); set => SetValue(ConnectionStatusProperty, value); }
    public bool ShowIdentity { get => (bool)GetValue(ShowIdentityProperty); set => SetValue(ShowIdentityProperty, value); }
    public bool ShowConnection { get => (bool)GetValue(ShowConnectionProperty); set => SetValue(ShowConnectionProperty, value); }
    public bool ShowWelcomeBrand { get => (bool)GetValue(ShowWelcomeBrandProperty); set => SetValue(ShowWelcomeBrandProperty, value); }
    public bool ShowMinimize { get => (bool)GetValue(ShowMinimizeProperty); set => SetValue(ShowMinimizeProperty, value); }
    public bool ShowBackButton { get => (bool)GetValue(ShowBackButtonProperty); set => SetValue(ShowBackButtonProperty, value); }
    public IEnumerable<HeaderAction>? ContextActions { get => (IEnumerable<HeaderAction>?)GetValue(ContextActionsProperty); set => SetValue(ContextActionsProperty, value); }
    public void SetMenuVisible(bool visible) { if (_menuButton != null) _menuButton.IsVisible = visible; }

    protected override void OnParentSet() { base.OnParentSet(); if (Parent == null) _timer.Stop(); else if (!_timer.IsRunning) _timer.Start(); }
    private void UpdateClock()
    {
        var now = DateTime.Now;
        _date.Text = now.ToString("dddd, MMMM d, yyyy");
        _time.Text = now.ToString("h:mm tt");
    }

    private void ApplyWelcomeBrand()
    {
        var welcome = ShowWelcomeBrand;
        _welcome.IsVisible = welcome;
        _title.IsVisible = !welcome;
        _restaurantLogo.IsVisible = !welcome && RestaurantLogo is ImageSource;
        _restaurantName.FontSize = welcome ? 21 : 12;
        _restaurantName.FontAttributes = welcome ? FontAttributes.Bold : FontAttributes.None;
        _restaurantName.HorizontalTextAlignment = welcome ? TextAlignment.Start : TextAlignment.Center;
        _restaurantName.TextColor = Color.FromArgb(welcome ? "#1F2937" : "#6B7280");
    }
    private void RebuildContextActions(IEnumerable<HeaderAction>? actions)
    {
        if (_contextActions is null) return;
        _contextActions.Children.Clear();
        foreach (var action in actions ?? [])
        {
            var button = new Button { Text = action.Text, IsEnabled = action.IsEnabled, Padding = new Thickness(10, 4), FontSize = 13 };
            button.Use(Button.BackgroundColorProperty, action.IsPrimary ? "OwPrimary" : "OwSurface");
            button.Use(Button.TextColorProperty, action.IsPrimary ? "OwTextOnPrimary" : "OwTextPrimary");
            button.Clicked += (_, _) => { action.Command?.Execute(action.Parameter); ContextActionRequested?.Invoke(this, new HeaderActionEventArgs(action)); };
            _contextActions.Children.Add(button);
        }
    }
    private static ImageButton IconButton(string source, double size) => new() { Source = source, WidthRequest = size, HeightRequest = size, MinimumWidthRequest = 44, MinimumHeightRequest = 44, Padding = 3, BackgroundColor = Colors.Transparent, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center };
}

public sealed class HeaderAction(string text, ICommand? command = null, object? parameter = null, bool isPrimary = false, bool isEnabled = true)
{
    public string Text { get; } = text;
    public ICommand? Command { get; } = command;
    public object? Parameter { get; } = parameter;
    public bool IsPrimary { get; } = isPrimary;
    public bool IsEnabled { get; } = isEnabled;
}

public sealed class HeaderActionEventArgs(HeaderAction action) : EventArgs
{
    public HeaderAction Action { get; } = action;
}

/// <summary>The canonical Mother-derived application header used by both POS hosts.</summary>
public class PosHeader : ApplicationHeader
{
}
