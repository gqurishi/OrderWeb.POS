namespace OrderWeb.SharedUI.Controls;

public class HeaderView : ContentView
{
    private readonly Label _eyebrow;
    private readonly Label _title;
    private readonly Label _date;
    private readonly Label _time;
    private readonly ConnectionIndicator _connection;
    private readonly IDispatcherTimer _timer;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(HeaderView), "Restaurant POS", propertyChanged: (b, _, v) => ((HeaderView)b)._title.Text = v?.ToString());
    public static readonly BindableProperty EyebrowProperty = BindableProperty.Create(nameof(Eyebrow), typeof(string), typeof(HeaderView), "Welcome to", propertyChanged: (b, _, v) => ((HeaderView)b)._eyebrow.Text = v?.ToString());
    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(HeaderView), "Connected", propertyChanged: (b, _, v) => ((HeaderView)b)._connection.Status = v?.ToString() ?? "Connected");
    public static readonly BindableProperty ShowConnectionStatusProperty = BindableProperty.Create(nameof(ShowConnectionStatus), typeof(bool), typeof(HeaderView), false, propertyChanged: (b, _, v) => ((HeaderView)b)._connection.IsVisible = (bool)v);
    public static readonly BindableProperty MenuIconProperty = BindableProperty.Create(nameof(MenuIcon), typeof(ImageSource), typeof(HeaderView), ImageSource.FromFile("mian.png"), propertyChanged: (b, _, v) => ((HeaderView)b).MenuButton.Source = (ImageSource?)v);
    public static readonly BindableProperty LogoutIconProperty = BindableProperty.Create(nameof(LogoutIcon), typeof(ImageSource), typeof(HeaderView), ImageSource.FromFile("outred.png"), propertyChanged: (b, _, v) => ((HeaderView)b).LogoutButton.Source = (ImageSource?)v);

    public HeaderView()
    {
        MenuButton = IconButton("mian.png"); LogoutButton = IconButton("outred.png");
        MenuButton.Clicked += (_, _) => MenuClicked?.Invoke(this, EventArgs.Empty);
        LogoutButton.Clicked += (_, _) => LogoutClicked?.Invoke(this, EventArgs.Empty);
        _eyebrow = new Label { Text = "Welcome to", FontSize = 14 }; _eyebrow.Use(Label.TextColorProperty, "PosTextMuted");
        _title = new Label { Text = "Restaurant POS", FontSize = 24, FontAttributes = FontAttributes.Bold }; _title.Use(Label.TextColorProperty, "PosTextStrong");
        _date = new Label { FontSize = 13, HorizontalTextAlignment = TextAlignment.End }; _date.Use(Label.TextColorProperty, "PosTextSecondary");
        _time = new Label { FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End }; _time.Use(Label.TextColorProperty, "PosPrimary");
        _connection = new ConnectionIndicator { IsVisible = false, VerticalOptions = LayoutOptions.Center };
        var titleStack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { _eyebrow, _title } };
        var clockStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { _date, _time } };
        var grid = new Grid { HeightRequest = 88, Padding = new Thickness(18, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16 };
        grid.Add(MenuButton); grid.Add(titleStack, 1); grid.Add(clockStack, 2); grid.Add(_connection, 3); grid.Add(LogoutButton, 4);
        var border = new Border { StrokeThickness = 0, Content = grid }; border.Use(Border.BackgroundColorProperty, "PosSurface"); Content = border;
        _timer = Dispatcher.CreateTimer(); _timer.Interval = TimeSpan.FromSeconds(1); _timer.Tick += (_, _) => UpdateClock(); _timer.Start(); UpdateClock();
    }

    public ImageButton MenuButton { get; }
    public ImageButton LogoutButton { get; }
    public event EventHandler? MenuClicked;
    public event EventHandler? LogoutClicked;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Eyebrow { get => (string)GetValue(EyebrowProperty); set => SetValue(EyebrowProperty, value); }
    public string ConnectionStatus { get => (string)GetValue(ConnectionStatusProperty); set => SetValue(ConnectionStatusProperty, value); }
    public bool ShowConnectionStatus { get => (bool)GetValue(ShowConnectionStatusProperty); set => SetValue(ShowConnectionStatusProperty, value); }
    public ImageSource? MenuIcon { get => (ImageSource?)GetValue(MenuIconProperty); set => SetValue(MenuIconProperty, value); }
    public ImageSource? LogoutIcon { get => (ImageSource?)GetValue(LogoutIconProperty); set => SetValue(LogoutIconProperty, value); }

    protected override void OnParentSet() { base.OnParentSet(); if (Parent == null) _timer.Stop(); else if (!_timer.IsRunning) _timer.Start(); }
    private void UpdateClock() { var now = DateTime.Now; _date.Text = now.ToString("dddd, MMMM d, yyyy"); _time.Text = now.ToString("HH:mm:ss"); }
    private static ImageButton IconButton(string source) => new() { Source = source, WidthRequest = 44, HeightRequest = 44, MinimumWidthRequest = 44, MinimumHeightRequest = 44, Padding = 4, BackgroundColor = Colors.Transparent, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center };
}
