using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

public sealed class MotherPrintClient
{
    private int _requestCount;

    public async Task<PrintRequestState> RequestPrintAsync(string printType, string? orderId, LoginSession? session)
    {
        await Task.Delay(180);
        _requestCount++;
        var now = DateTimeOffset.UtcNow.ToString("O");
        var status = _requestCount % 7 == 0 ? "printer offline" : "queued";
        var message = status == "printer offline"
            ? "Mother says target printer is offline."
            : $"Mother queued {printType} for routing.";

        return new PrintRequestState(Guid.NewGuid().ToString("N"), printType, orderId, status, message, now, now);
    }

    public async Task<PrintRequestState> AdvanceStatusAsync(PrintRequestState request)
    {
        await Task.Delay(120);
        var next = request.Status switch
        {
            "queued" => "printing",
            "printing" => "printed",
            "printer offline" => "failed",
            _ => request.Status
        };

        return request with
        {
            Status = next,
            Message = next == "printed" ? "Mother confirmed print complete." : request.Message,
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("O")
        };
    }
}
