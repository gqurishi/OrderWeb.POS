using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Assets;

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
    private readonly Button _minimize;
    private readonly Button _back;
    private readonly HorizontalStackLayout _contextActions;
    private readonly Border _shell;
    private readonly Grid _grid;
    private readonly ImageButton _menuButton;
    private readonly ImageButton _logoutButton;
    private readonly HorizontalStackLayout _clock;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(ApplicationHeader), string.Empty, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._title.Text = v?.ToString());
    public static readonly BindableProperty RestaurantNameProperty = BindableProperty.Create(nameof(RestaurantName), typeof(string), typeof(ApplicationHeader), string.Empty, propertyChanged: (b, _, v) =>
    {
        var header = (ApplicationHeader)b;
        header._restaurantName.Text = v?.ToString() ?? string.Empty;
        header.ApplyWelcomeBrand();
    });
    public static readonly BindableProperty RestaurantLogoProperty = BindableProperty.Create(nameof(RestaurantLogo), typeof(ImageSource), typeof(ApplicationHeader), propertyChanged: (b, _, v) =>
    {
        var header = (ApplicationHeader)b;
        header._restaurantLogo.Source = (ImageSource?)v;
        header.ApplyWelcomeBrand();
    });
    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(nameof(UserName), typeof(string), typeof(ApplicationHeader), "No user", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.UserName = v?.ToString() ?? "No user");
    public static readonly BindableProperty TerminalNameProperty = BindableProperty.Create(nameof(TerminalName), typeof(string), typeof(ApplicationHeader), "Terminal", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.TerminalName = v?.ToString() ?? "Terminal");
    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(ApplicationHeader), "Connected", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._connection.Status = v?.ToString() ?? "Connected");
    public static readonly BindableProperty ShowIdentityProperty = BindableProperty.Create(nameof(ShowIdentity), typeof(bool), typeof(ApplicationHeader), false, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.IsVisible = (bool)v);
    public static readonly BindableProperty ShowConnectionProperty = BindableProperty.Create(nameof(ShowConnection), typeof(bool), typeof(ApplicationHeader), true, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._connection.IsVisible = (bool)v);
    public static readonly BindableProperty ShowWelcomeBrandProperty = BindableProperty.Create(nameof(ShowWelcomeBrand), typeof(bool), typeof(ApplicationHeader), false, propertyChanged: (b, _, _) => ((ApplicationHeader)b).ApplyWelcomeBrand());
    public static readonly BindableProperty ShowMinimizeProperty = BindableProperty.Create(nameof(ShowMinimize), typeof(bool), typeof(ApplicationHeader), false, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._minimize.IsVisible = (bool)v);
    public static readonly BindableProperty ShowBackButtonProperty = BindableProperty.Create(nameof(ShowBackButton), typeof(bool), typeof(ApplicationHeader), false, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._back.IsVisible = (bool)v);
    public static readonly BindableProperty ContextActionsProperty = BindableProperty.Create(nameof(ContextActions), typeof(IEnumerable<HeaderAction>), typeof(ApplicationHeader), propertyChanged: (b, _, v) => ((ApplicationHeader)b).RebuildContextActions((IEnumerable<HeaderAction>?)v));

    public ApplicationHeader()
    {
        _menuButton = IconButton(SharedImageNames.CompanyLogo, 44);
        _logoutButton = IconButton(SharedImageNames.Logout, 44);
        _back = new Button { Text = "‹", FontSize = 32, Padding = 0, WidthRequest = 38, HeightRequest = 38, MinimumWidthRequest = 44, MinimumHeightRequest = 44, BackgroundColor = Colors.Transparent, BorderWidth = 0 };
        _back.Use(Button.TextColorProperty, "OwTextPrimary");
        _back.IsVisible = false;
        _minimize = new Button
        {
            Text = "-",
            IsVisible = false,
            WidthRequest = 34,
            HeightRequest = 34,
            MinimumWidthRequest = 34,
            MinimumHeightRequest = 34,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            BorderColor = Colors.Transparent,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 0, 10, 0),
            TextColor = Color.FromArgb("#111827")
        };
        _menuButton.Clicked += (_, _) => MenuClicked?.Invoke(this, EventArgs.Empty);
        _back.Clicked += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        _logoutButton.Clicked += (_, _) => LogoutClicked?.Invoke(this, EventArgs.Empty);
        _minimize.Clicked += (_, _) => MinimizeClicked?.Invoke(this, EventArgs.Empty);

        _restaurantLogo = new Image { WidthRequest = 30, HeightRequest = 30, Aspect = Aspect.AspectFit, IsVisible = false };
        _welcome = new Label
        {
            Text = "Welcome to",
            FontSize = 12,
            FontFamily = "InterMedium",
            IsVisible = false,
            HorizontalTextAlignment = TextAlignment.Start,
            TextColor = Color.FromArgb("#6B7280")
        };
        _restaurantName = new Label
        {
            FontSize = 12,
            FontFamily = "InterBold",
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            TextColor = Color.FromArgb("#6B7280")
        };
        _title = new Label { FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _title.Use(Label.TextColorProperty, "OwTextStrong");
        _date = new Label
        {
            FontSize = 12,
            FontFamily = "InterMedium",
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#64748B")
        };
        _time = new Label
        {
            FontSize = 13,
            FontFamily = "InterBold",
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#2563EB")
        };
        _identity = new UserTerminalInfo { VerticalOptions = LayoutOptions.Center, IsVisible = false };
        _connection = new ConnectionIndicator
        {
            VerticalOptions = LayoutOptions.Center,
            Compact = true,
            Margin = new Thickness(0, 0, 12, 0),
            IsVisible = true
        };

        _clock = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Children = { _date, new Label { Text = "·", FontSize = 14, VerticalOptions = LayoutOptions.Center, TextColor = Color.FromArgb("#CBD5E1") }, _time }
        };
        _contextActions = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        var right = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { _contextActions, _identity, _connection, _clock, _minimize, _logoutButton } };
        var left = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { _menuButton, _back } };
        _menuButton.Margin = new Thickness(0, 0, 14, 0);
        _logoutButton.Margin = new Thickness(0, 0, 6, 0);
        var restaurantIdentity = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.Start, Children = { _restaurantLogo, _restaurantName } };
        var titleStack = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Fill, Children = { _welcome, restaurantIdentity, _title } };
        _grid = new Grid
        {
            Padding = new Thickness(14, 0),
            MinimumHeightRequest = 52,
            HeightRequest = 52,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        _grid.Add(left);
        _grid.Add(titleStack, 1);
        _grid.Add(right, 2);
        _shell = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.White,
            VerticalOptions = LayoutOptions.Start,
            Content = new Grid
            {
                Children =
                {
                    _grid,
                    new BoxView
                    {
                        HeightRequest = 1,
                        Color = Color.FromArgb("#E5E7EB"),
                        VerticalOptions = LayoutOptions.End,
                        HorizontalOptions = LayoutOptions.Fill
                    }
                }
            }
        };
        VerticalOptions = LayoutOptions.Start;
        Content = _shell;

        _timer = Dispatcher.CreateTimer();
        ConfigureClockInterval();
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

    /// <summary>Slim page header. Dashboard welcome stays one step taller for the name line.</summary>
    public double PreferredHeight => ShowWelcomeBrand ? 56 : 52;

    protected override void OnParentSet() { base.OnParentSet(); if (Parent == null) _timer.Stop(); else if (!_timer.IsRunning) _timer.Start(); }

    private void ConfigureClockInterval()
    {
        // Welcome brand shows seconds → 1s. Other pages show minutes → 30s is enough.
        _timer.Interval = TimeSpan.FromSeconds(ShowWelcomeBrand ? 1 : 30);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        // Mother User dashboard uses 24-hour clock with seconds.
        if (ShowWelcomeBrand)
        {
            _date.Text = now.ToString("dddd, MMMM dd, yyyy");
            _time.Text = now.ToString("HH:mm:ss");
            return;
        }

        _date.Text = now.ToString("ddd, MMM d");
        _time.Text = now.ToString("h:mm tt");
    }

    private void ApplyWelcomeBrand()
    {
        ConfigureClockInterval();
        UpdateClock();
        var welcome = ShowWelcomeBrand;
        _welcome.IsVisible = welcome;
        _title.IsVisible = !welcome;
        // Welcome brand: left menu is mainlogo; title is text only (Mother User/Manager parity).
        // RestaurantLogo still feeds the sidebar via ApplicationShellFrame.
        _restaurantLogo.IsVisible = false;
        _restaurantName.IsVisible = welcome && !string.IsNullOrWhiteSpace(RestaurantName);
        _restaurantName.FontSize = welcome ? 21 : 12;
        _restaurantName.FontFamily = welcome ? "InterBold" : "OpenSansRegular";
        _restaurantName.FontAttributes = welcome ? FontAttributes.Bold : FontAttributes.None;
        _restaurantName.HorizontalTextAlignment = welcome ? TextAlignment.Start : TextAlignment.Center;
        _restaurantName.TextColor = Color.FromArgb(welcome ? "#1F2937" : "#6B7280");

        _menuButton.WidthRequest = 44;
        _menuButton.HeightRequest = 44;
        _menuButton.MinimumWidthRequest = 44;
        _menuButton.MinimumHeightRequest = 44;
        _menuButton.Padding = 0;
        _menuButton.Margin = new Thickness(0, 0, 8, 0);

        _logoutButton.WidthRequest = 44;
        _logoutButton.HeightRequest = 44;
        _logoutButton.MinimumWidthRequest = 44;
        _logoutButton.MinimumHeightRequest = 44;

        _minimize.WidthRequest = 28;
        _minimize.HeightRequest = 28;
        _minimize.MinimumWidthRequest = 28;
        _minimize.MinimumHeightRequest = 28;
        _minimize.FontSize = 18;
        _minimize.Margin = new Thickness(0, 0, 4, 0);
        _minimize.Text = "−";

        _date.FontSize = welcome ? 11 : 12;
        _date.TextColor = Color.FromArgb("#64748B");
        _date.FontAttributes = FontAttributes.None;
        _time.FontSize = welcome ? 13 : 14;
        _time.TextColor = Color.FromArgb("#2563EB");

        _grid.HeightRequest = PreferredHeight;
        _grid.MinimumHeightRequest = PreferredHeight;
        _grid.Padding = new Thickness(14, 0);
        _shell.BackgroundColor = Colors.White;
        _shell.StrokeThickness = 0;
        _shell.Shadow = null;
        HeightRequest = PreferredHeight;
        VerticalOptions = LayoutOptions.Start;

        HeightRequestChanged?.Invoke(this, PreferredHeight);
        UpdateClock();
    }

    public event EventHandler<double>? HeightRequestChanged;

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

    private static ImageButton IconButton(string source, double size) => new()
    {
        Source = source,
        WidthRequest = size,
        HeightRequest = size,
        MinimumWidthRequest = size,
        MinimumHeightRequest = size,
        Padding = 0,
        BackgroundColor = Colors.Transparent,
        Aspect = Aspect.AspectFit,
        VerticalOptions = LayoutOptions.Center
    };
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
