namespace OrderWeb.Client.Views.Layout;

public partial class ClientTopBar : ContentView
{
    public static readonly BindableProperty RestaurantNameProperty = BindableProperty.Create(
        nameof(RestaurantName),
        typeof(string),
        typeof(ClientTopBar),
        "Restaurant POS",
        propertyChanged: OnRestaurantNameChanged);

    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(
        nameof(ConnectionStatus),
        typeof(string),
        typeof(ClientTopBar),
        "Connected",
        propertyChanged: OnConnectionStatusChanged);

    public static readonly BindableProperty ShowConnectionStatusProperty = BindableProperty.Create(
        nameof(ShowConnectionStatus),
        typeof(bool),
        typeof(ClientTopBar),
        false,
        propertyChanged: OnShowConnectionStatusChanged);

    private readonly IDispatcherTimer _clockTimer;

    public ClientTopBar()
    {
        InitializeComponent();
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        ConnectionPill.Status = ConnectionStatus;
        ConnectionPill.IsVisible = ShowConnectionStatus;
    }

    public event EventHandler? MenuClicked;
    public event EventHandler? LogoutClicked;

    public string RestaurantName
    {
        get => (string)GetValue(RestaurantNameProperty);
        set => SetValue(RestaurantNameProperty, value);
    }

    public string ConnectionStatus
    {
        get => (string)GetValue(ConnectionStatusProperty);
        set => SetValue(ConnectionStatusProperty, value);
    }

    public bool ShowConnectionStatus
    {
        get => (bool)GetValue(ShowConnectionStatusProperty);
        set => SetValue(ShowConnectionStatusProperty, value);
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent == null)
        {
            _clockTimer.Stop();
        }
        else if (!_clockTimer.IsRunning)
        {
            _clockTimer.Start();
        }
    }

    private static void OnRestaurantNameChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ClientTopBar topBar)
        {
            topBar.RestaurantNameLabel.Text = newValue?.ToString() ?? "Restaurant POS";
        }
    }

    private static void OnConnectionStatusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ClientTopBar topBar)
        {
            topBar.ConnectionPill.Status = newValue?.ToString() ?? "Connected";
        }
    }

    private static void OnShowConnectionStatusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ClientTopBar topBar)
        {
            topBar.ConnectionPill.IsVisible = newValue is true;
        }
    }

    private void UpdateClock()
    {
        DateLabel.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        TimeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private void OnMenuClicked(object sender, EventArgs e)
    {
        MenuClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnLogoutClicked(object sender, EventArgs e)
    {
        LogoutClicked?.Invoke(this, EventArgs.Empty);
    }
}
