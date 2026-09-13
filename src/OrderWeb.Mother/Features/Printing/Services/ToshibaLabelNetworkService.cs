using System.Security.Cryptography;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>Coordinates one durable Toshiba label attempt without bypassing the Mother queue.</summary>
public sealed class ToshibaLabelNetworkService
{
    private readonly ToshibaTpclModule _tpcl;
    private readonly ToshibaRawTcpTransport _transport;
    private readonly LabelPrintQueueDatabaseService _queue;

    public ToshibaLabelNetworkService(ToshibaTpclModule tpcl, ToshibaRawTcpTransport transport, LabelPrintQueueDatabaseService queue)
    {
        _tpcl = tpcl;
        _transport = transport;
        _queue = queue;
    }

    public async Task<ToshibaNetworkResult> SendQueuedJobAsync(
        LabelPrintJob job,
        NetworkPrinter printer,
        LabelMediaProfile media,
        string claimToken,
        string motherInstance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(media);
        if (!printer.IsEnabled) throw new InvalidOperationException("The selected label printer is disabled.");
        if (!string.Equals(printer.ModuleIdentifier, "toshiba_tpcl", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected printer is not assigned to the Toshiba TPCL module.");

        var secondaryLines = job.Content.Modifiers.ToList();
        if (!string.IsNullOrWhiteSpace(job.Content.FixedLabelText))
            secondaryLines.Add(job.Content.FixedLabelText);
        var content = new ToshibaTpclLabelContent(
            job.Content.Name, job.Content.Quantity, secondaryLines,
            job.Content.OrderContext, job.Content.PreparedAt);
        var payload = _tpcl.BuildLabel(content, media, job.QuantityCopies);
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        await _queue.BeginPrintingAsync(job.Id, claimToken, motherInstance, payload, hash);
        var result = await _transport.SendJobAsync(
            printer.IpAddress, printer.Port, payload, _tpcl.BuildStatusQuery(), cancellationToken);
        await _queue.FinishAttemptAsync(job.Id, claimToken, motherInstance, result);
        return result;
    }
}
