using OrderWeb.Contracts.Printing;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Views.Printing;

/// <summary>
/// Thin Client host for SharedUI <see cref="PrintResultView"/>. Status comes only from Mother.
/// </summary>
public partial class PrintStatusView : ContentView
{
    private readonly PrintResultView _resultView = new();

    public PrintStatusView()
    {
        InitializeComponent();
        Content = _resultView;
        _resultView.ShowIdle("Waiting for Mother print response.");
    }

    public void Bind(PrintResultDto? result) => _resultView.Bind(result);

    public void ShowIdle(string message = "Waiting for Mother print response.") =>
        _resultView.ShowIdle(message);

    /// <summary>
    /// Legacy entry point. Prefer <see cref="Bind"/> with a Mother <see cref="PrintResultDto"/>.
    /// Does not invent Printed — only displays the supplied status string.
    /// </summary>
    public void SetStatus(string status, string? message = null)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        var printStatus = normalized switch
        {
            "queued" => PrintStatus.Queued,
            "printing" => PrintStatus.Printing,
            "printed" => PrintStatus.Printed,
            "partial failure" or "partial_failure" => PrintStatus.PartialFailure,
            "printer unavailable" or "printer_unavailable" or "printer offline" or "offline" => PrintStatus.PrinterUnavailable,
            "failed" => PrintStatus.Failed,
            _ => PrintStatus.Failed
        };

        _resultView.Bind(new PrintResultDto(
            RequestId: "local-display",
            AuditId: string.Empty,
            Kind: PrintKind.CustomerReceipt,
            Status: printStatus,
            Message: string.IsNullOrWhiteSpace(message) ? normalized : message!,
            OrderId: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            IsTerminal: true));
    }
}
