namespace OrderWeb.Client.Views.Layout;

public partial class ConnectionStatusView : ContentView
{
    public static readonly BindableProperty StatusProperty = BindableProperty.Create(
        nameof(Status),
        typeof(string),
        typeof(ConnectionStatusView),
        "Connected",
        propertyChanged: OnStatusChanged);

    public ConnectionStatusView()
    {
        InitializeComponent();
        ApplyStatus();
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private static void OnStatusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ConnectionStatusView view)
        {
            view.ApplyStatus();
        }
    }

    private void ApplyStatus()
    {
        SharedIndicator.Status = NormalizeStatus(Status);
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "connected" => "Connected",
            "syncing" => "Syncing",
            "reconnecting" => "Reconnecting",
            "mother offline" or "mother_offline" or "offline" => "Mother Offline",
            _ => "Connected"
        };
    }
}
