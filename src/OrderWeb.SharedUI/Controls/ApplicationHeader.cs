namespace OrderWeb.SharedUI.Controls;

public class ApplicationHeader : ContentView
{
    private readonly Label _title;
    private readonly Label _date;
    private readonly Label _time;
    private readonly UserTerminalInfo _identity;
    private readonly ConnectionIndicator _connection;
    private readonly IDispatcherTimer _timer;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(ApplicationHeader), string.Empty, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._title.Text = v?.ToString());
    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(nameof(UserName), typeof(string), typeof(ApplicationHeader), "No user", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.UserName = v?.ToString() ?? "No user");
    public static readonly BindableProperty TerminalNameProperty = BindableProperty.Create(nameof(TerminalName), typeof(string), typeof(ApplicationHeader), "Terminal", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.TerminalName = v?.ToString() ?? "Terminal");
    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(ApplicationHeader), "Connected", propertyChanged: (b, _, v) => ((ApplicationHeader)b)._connection.Status = v?.ToString() ?? "Connected");
    public static readonly BindableProperty ShowIdentityProperty = BindableProperty.Create(nameof(ShowIdentity), typeof(bool), typeof(ApplicationHeader), true, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._identity.IsVisible = (bool)v);
    public static readonly BindableProperty ShowConnectionProperty = BindableProperty.Create(nameof(ShowConnection), typeof(bool), typeof(ApplicationHeader), true, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._connection.IsVisible = (bool)v);
    public static readonly BindableProperty ShowMinimizeProperty = BindableProperty.Create(nameof(ShowMinimize), typeof(bool), typeof(ApplicationHeader), false, propertyChanged: (b, _, v) => ((ApplicationHeader)b)._minimize.IsVisible = (bool)v);
    private readonly Button _minimize;

    public ApplicationHeader()
    {
        var menu = IconButton("mian.png", 38);
        var logout = IconButton("outred.png", 38);
        _minimize = new Button { Text = "−", IsVisible = false, WidthRequest = 36, HeightRequest = 36, MinimumWidthRequest = 36, MinimumHeightRequest = 36, Padding = 0, BackgroundColor = Colors.Transparent, BorderWidth = 0, FontSize = 22, FontAttributes = FontAttributes.Bold };
        _minimize.Use(Button.TextColorProperty, "OwTextPrimary");
        menu.Clicked += (_, _) => MenuClicked?.Invoke(this, EventArgs.Empty);
        logout.Clicked += (_, _) => LogoutClicked?.Invoke(this, EventArgs.Empty);
        _minimize.Clicked += (_, _) => MinimizeClicked?.Invoke(this, EventArgs.Empty);

        _title = new Label { FontSize = 28, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _title.Use(Label.TextColorProperty, "OwTextStrong");
        _date = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End };
        _date.Use(Label.TextColorProperty, "OwTextStrong");
        _time = new Label { FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End };
        _time.Use(Label.TextColorProperty, "OwPrimary");
        _identity = new UserTerminalInfo { VerticalOptions = LayoutOptions.Center };
        _connection = new ConnectionIndicator { VerticalOptions = LayoutOptions.Center };

        var clock = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { _date, _time } };
        var right = new HorizontalStackLayout { Spacing = 14, VerticalOptions = LayoutOptions.Center, Children = { _identity, _connection, clock, _minimize, logout } };
        var grid = new Grid { HeightRequest = 88, Padding = new Thickness(16, 0), ColumnDefinitions = { new ColumnDefinition(54), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
        grid.Add(menu); grid.Add(_title, 1); grid.Add(right, 2);
        var shell = new Border { StrokeThickness = 0, Content = grid };
        shell.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        Content = shell;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateClock();
        _timer.Start();
        UpdateClock();
    }

    public event EventHandler? MenuClicked;
    public event EventHandler? LogoutClicked;
    public event EventHandler? MinimizeClicked;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string UserName { get => (string)GetValue(UserNameProperty); set => SetValue(UserNameProperty, value); }
    public string TerminalName { get => (string)GetValue(TerminalNameProperty); set => SetValue(TerminalNameProperty, value); }
    public string ConnectionStatus { get => (string)GetValue(ConnectionStatusProperty); set => SetValue(ConnectionStatusProperty, value); }
    public bool ShowIdentity { get => (bool)GetValue(ShowIdentityProperty); set => SetValue(ShowIdentityProperty, value); }
    public bool ShowConnection { get => (bool)GetValue(ShowConnectionProperty); set => SetValue(ShowConnectionProperty, value); }
    public bool ShowMinimize { get => (bool)GetValue(ShowMinimizeProperty); set => SetValue(ShowMinimizeProperty, value); }

    protected override void OnParentSet() { base.OnParentSet(); if (Parent == null) _timer.Stop(); else if (!_timer.IsRunning) _timer.Start(); }
    private void UpdateClock() { var now = DateTime.Now; _date.Text = now.ToString("ddd, dd MMM yyyy"); _time.Text = now.ToString("HH:mm:ss"); }
    private static ImageButton IconButton(string source, double size) => new() { Source = source, WidthRequest = size, HeightRequest = size, MinimumWidthRequest = 44, MinimumHeightRequest = 44, Padding = 3, BackgroundColor = Colors.Transparent, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center };
}
