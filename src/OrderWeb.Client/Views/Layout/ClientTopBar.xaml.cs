using OrderWeb.Client.Services;

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
        true,
        propertyChanged: OnShowConnectionStatusChanged);

    public ClientTopBar()
    {
        InitializeComponent();
        SharedHeader.Title = RestaurantName;
        SharedHeader.ConnectionStatus = ConnectionStatus;
        SharedHeader.ShowConnection = ShowConnectionStatus;
        SharedHeader.ShowMinimize = true;
    }

    public event EventHandler? MenuClicked;
    public event EventHandler? LogoutClicked;
    public event EventHandler? MinimizeClicked;

    /// <summary>Mother TopBar.SetPageTitle — page chrome title (e.g. Reservation).</summary>
    public void SetPageTitle(string title)
    {
        SharedHeader.ShowWelcomeBrand = false;
        SharedHeader.ShowMinimize = true;
        SharedHeader.ShowConnection = ShowConnectionStatus;
        SharedHeader.Title = string.IsNullOrWhiteSpace(title) ? "Order" : title.Trim();
    }

    public void ConfigurePosChrome(
        string title,
        string? userName = null,
        string? terminalName = null,
        string connectionStatus = "Connected")
    {
        ShowConnectionStatus = true;
        ConnectionStatus = connectionStatus;
        SetPageTitle(title);
        SharedHeader.ShowBackButton = false;
        SharedHeader.ShowIdentity = true;
        SharedHeader.ShowMinimize = true;
        SharedHeader.ShowConnection = true;
        SharedHeader.UserName = string.IsNullOrWhiteSpace(userName) ? "user" : userName.Trim();
        SharedHeader.TerminalName = string.IsNullOrWhiteSpace(terminalName) ? "Client" : terminalName.Trim();
        SharedHeader.ConnectionStatus = connectionStatus;
        SharedHeader.MinimizeClicked -= OnMinimizeClicked;
        SharedHeader.MinimizeClicked += OnMinimizeClicked;
    }

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

    private static void OnRestaurantNameChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ClientTopBar topBar)
        {
            topBar.SharedHeader.Title = newValue?.ToString() ?? "Restaurant POS";
        }
    }

    private static void OnConnectionStatusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ClientTopBar topBar)
        {
            topBar.SharedHeader.ConnectionStatus = newValue?.ToString() ?? "Connected";
        }
    }

    private static void OnShowConnectionStatusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ClientTopBar topBar)
        {
            topBar.SharedHeader.ShowConnection = newValue is true;
        }
    }

    private void OnMenuClicked(object sender, EventArgs e)
    {
        MenuClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnLogoutClicked(object sender, EventArgs e)
    {
        LogoutClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnMinimizeClicked(object? sender, EventArgs e)
    {
        MinimizeClicked?.Invoke(this, EventArgs.Empty);
        ClientWindowService.MinimizeMainWindow();
    }
}
