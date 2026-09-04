namespace OrderWeb.SharedUI.Controls;

public class ConnectionIndicator : StatusBadge
{
    public static readonly BindableProperty StatusProperty = BindableProperty.Create(nameof(Status), typeof(string), typeof(ConnectionIndicator), "Connected", propertyChanged: (b, _, _) => ((ConnectionIndicator)b).ApplyStatus());
    public ConnectionIndicator() => ApplyStatus();
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    private void ApplyStatus()
    {
        var value = Status?.Trim() ?? "Connected";
        Kind = value.ToLowerInvariant() switch
        {
            "connected" or "online" => StatusKind.Success,
            "offline" or "mother offline" or "mother_offline" or "disconnected" => StatusKind.Error,
            "syncing" or "reconnecting" or "connecting" => StatusKind.Warning,
            _ => StatusKind.Info
        };
        Text = value;
    }
}
