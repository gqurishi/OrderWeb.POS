using System.Diagnostics;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>Mother-only durable queue processor for label jobs.</summary>
public sealed class LabelPrintQueueService
{
    private readonly LabelPrintQueueDatabaseService _queue;
    private readonly NetworkPrinterDatabaseService _printers;
    private readonly LabelPrintDatabaseService _labels;
    private readonly ToshibaLabelNetworkService _toshiba;
    private readonly XprinterLabelNetworkService _xprinter;
    private readonly BrotherRasterNetworkService _brother;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly string _instance = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public LabelPrintQueueService(
        LabelPrintQueueDatabaseService queue,
        NetworkPrinterDatabaseService printers,
        LabelPrintDatabaseService labels,
        ToshibaLabelNetworkService toshiba,
        XprinterLabelNetworkService xprinter,
        BrotherRasterNetworkService brother)
    {
        _queue = queue;
        _printers = printers;
        _labels = labels;
        _toshiba = toshiba;
        _xprinter = xprinter;
        _brother = brother;
    }

    public async Task<int> ProcessQueueAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken)) return 0;
        try
        {
            await _labels.EnsureBuiltInProfilesAsync();
            await _queue.RecoverStaleClaimsAsync(_instance);
            var processed = 0;
            while (processed < 10 && !cancellationToken.IsCancellationRequested)
            {
                var claimed = await _queue.ClaimNextAsync(_instance);
                if (claimed == null) break;
                await ProcessClaimAsync(claimed, cancellationToken);
                processed++;
            }
            return processed;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task ProcessClaimAsync(ClaimedLabelPrintJob claimed, CancellationToken cancellationToken)
    {
        var job = claimed.Job;
        try
        {
            var printer = await _printers.GetPrinterByIdAsync(job.PrinterId)
                ?? throw new InvalidOperationException("The configured label printer no longer exists.");
            var media = (await _labels.GetActiveMediaProfilesAsync()).FirstOrDefault(profile => profile.Id == job.MediaProfileId)
                ?? throw new InvalidOperationException("The selected label media profile is missing or inactive.");
            if (string.Equals(printer.ModuleIdentifier, "toshiba_tpcl", StringComparison.OrdinalIgnoreCase))
                await _toshiba.SendQueuedJobAsync(job, printer, media, claimed.ClaimToken, _instance, cancellationToken);
            else if (string.Equals(printer.ModuleIdentifier, LabelPrinterProfiles.XprinterTsplModuleId, StringComparison.OrdinalIgnoreCase))
                await _xprinter.SendQueuedJobAsync(job, printer, media, claimed.ClaimToken, _instance, cancellationToken);
            else if (string.Equals(printer.ModuleIdentifier, LabelPrinterProfiles.BrotherRasterModuleId, StringComparison.OrdinalIgnoreCase))
                await _brother.SendQueuedJobAsync(job, printer, media, claimed.ClaimToken, _instance, cancellationToken);
            else
                throw new NotSupportedException($"Printer module '{printer.ModuleIdentifier ?? "missing"}' is not supported by this label worker.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LABEL QUEUE] Job {job.Id}: {ex.Message}");
            var failure = ToshibaNetworkResult.Failed(
                $"printer:{job.PrinterId}", ToshibaNetworkPhase.Connect,
                DateTimeOffset.UtcNow, FriendlyConfigurationError(ex));
            try
            {
                // If generation/configuration failed before BeginPrinting, first move
                // the valid database claim into Printing so one transition path owns retries.
                await _queue.BeginPrintingAsync(job.Id, claimed.ClaimToken, _instance, new byte[] { 0 }, new string('0', 64));
                await _queue.FinishAttemptAsync(job.Id, claimed.ClaimToken, _instance, failure);
            }
            catch (InvalidOperationException)
            {
                // BeginPrinting may already have happened before a transport exception.
                await _queue.FinishAttemptAsync(job.Id, claimed.ClaimToken, _instance, failure);
            }
        }
    }

    private static string FriendlyConfigurationError(Exception exception) => exception switch
    {
        NotSupportedException => $"Label printer configuration is unsupported: {exception.Message}",
        ArgumentException => $"Label configuration is invalid: {exception.Message}",
        _ => exception.Message
    };
}
