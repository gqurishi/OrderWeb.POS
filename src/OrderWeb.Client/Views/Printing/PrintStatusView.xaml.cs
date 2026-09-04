namespace OrderWeb.Client.Views.Printing;

public partial class PrintStatusView : ContentView
{
    public PrintStatusView()
    {
        InitializeComponent();
        SetStatus("queued");
    }

    public void SetStatus(string status, string? message = null)
    {
        var normalized = status.Trim().ToLowerInvariant();
        var (label, displayMessage, color) = normalized switch
        {
            "sent" or "sent to kitchen" => ("Sent", "Sent to kitchen", "#059669"),
            "printed" => ("Printed", "Printed", "#059669"),
            "printer offline" or "offline" or "failed" => ("Offline", "Printer offline", "#DC2626"),
            _ => ("Queued", "Queued by Mother", "#2563EB")
        };

        StatusLabel.Text = label;
        MessageLabel.Text = string.IsNullOrWhiteSpace(message) ? displayMessage : message;
        StatusLabel.TextColor = Color.FromArgb(color);
        StatusBorder.Stroke = Color.FromArgb(color);
    }
}
