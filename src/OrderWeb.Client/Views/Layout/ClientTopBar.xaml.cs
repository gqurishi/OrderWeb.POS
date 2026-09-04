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

    public ClientTopBar()
    {
        InitializeComponent();
        SharedHeader.Title = RestaurantName;
        SharedHeader.ConnectionStatus = ConnectionStatus;
        SharedHeader.ShowConnection = ShowConnectionStatus;
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
}
