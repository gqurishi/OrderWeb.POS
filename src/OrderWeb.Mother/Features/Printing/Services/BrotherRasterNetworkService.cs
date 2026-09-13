using System.Security.Cryptography;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>Sends one queued TD-4420DN job as Brother Raster. It does not use a Windows driver.</summary>
public sealed class BrotherRasterNetworkService
{
    private readonly BrotherRasterModule _raster;
    private readonly ToshibaRawTcpTransport _transport;
    private readonly LabelPrintQueueDatabaseService _queue;

    public BrotherRasterNetworkService(BrotherRasterModule raster, ToshibaRawTcpTransport transport, LabelPrintQueueDatabaseService queue)
    {
        _raster = raster;
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
        if (!string.Equals(printer.ModuleIdentifier, LabelPrinterProfiles.BrotherRasterModuleId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected printer is not assigned to the Brother Raster module.");

        var secondaryLines = job.Content.Modifiers.ToList();
        if (!string.IsNullOrWhiteSpace(job.Content.FixedLabelText))
            secondaryLines.Add(job.Content.FixedLabelText);
        var content = new ToshibaTpclLabelContent(
            job.Content.Name, job.Content.Quantity, secondaryLines,
            job.Content.OrderContext, job.Content.PreparedAt);
        var payload = _raster.BuildLabel(content, media, job.QuantityCopies);
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        await _queue.BeginPrintingAsync(job.Id, claimToken, motherInstance, payload, hash);
        var started = DateTimeOffset.UtcNow;
        var result = await _transport.SendJobAsync(
            printer.IpAddress, printer.Port, payload, ReadOnlyMemory<byte>.Empty, cancellationToken);
        if (result.DataAccepted)
            result = ToshibaNetworkResult.Accepted(result.Endpoint, started);
        await _queue.FinishAttemptAsync(job.Id, claimToken, motherInstance, result);
        return result;
    }
}
