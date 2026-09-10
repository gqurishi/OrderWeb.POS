using OrderWeb.Contracts.Synchronization;

namespace POS_in_NET.Services;

/// <summary>
/// Quiet Mother → Client tip safety-net. Re-publishes WS tips when configuration
/// versions or tip events advance, without bumping versions or touching UI.
/// </summary>
public sealed class ClientTipSafetyNetService
{
    private readonly ClientWebSocketBroadcastService _broadcast;
    private readonly object _gate = new();
    private Dictionary<string, string>? _lastVersions;
    private long _lastEventId = -1;

    public ClientTipSafetyNetService(ClientWebSocketBroadcastService broadcast)
    {
        _broadcast = broadcast;
    }

    public async Task<BackgroundSyncRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TerminalConfigurationService.IsConfigured || !TerminalConfigurationService.IsMotherTerminal)
        {
            return BackgroundSyncRunResult.Skip("Client tip safety-net runs on Mother only.");
        }

        if (_broadcast.ConnectedClientCount <= 0)
        {
            return BackgroundSyncRunResult.Skip("No Client connected.");
        }

        var versions = await _broadcast.GetSyncVersionsAsync().ConfigureAwait(false);
        var eventId = await _broadcast.GetLatestTipEventIdAsync().ConfigureAwait(false);

        Dictionary<string, string>? previousVersions;
        long previousEventId;
        lock (_gate)
        {
            previousVersions = _lastVersions;
            previousEventId = _lastEventId;
        }

        // First tick: seed snapshot only — avoid tip storm at Mother startup.
        if (previousVersions == null)
        {
            SeedSnapshot(versions, eventId);
            return BackgroundSyncRunResult.Skip("Client tip safety-net snapshot seeded.");
        }

        if (VersionsEqual(previousVersions, versions) && eventId == previousEventId)
        {
            return BackgroundSyncRunResult.Skip("No Client tip needed.");
        }

        var tipped = new List<string>();
        foreach (var section in SyncSectionKeys.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            previousVersions.TryGetValue(section, out var previous);
            versions.TryGetValue(section, out var current);
            previous ??= "0";
            current ??= "0";
            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                continue;
            }

            await _broadcast.RepublishDataChangedAsync($"{section}.updated", current).ConfigureAwait(false);
            tipped.Add(section);
        }

        // Order/layout tips do not bump configuration_versions — catch via event watermark.
        if (eventId > previousEventId && tipped.Count == 0)
        {
            var watermark = eventId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _broadcast.RepublishDataChangedAsync("order.updated", watermark).ConfigureAwait(false);
            await _broadcast.RepublishDataChangedAsync("layout.updated", watermark).ConfigureAwait(false);
            tipped.Add("order");
            tipped.Add("layout");
        }

        // Include any events we just wrote so the next tick does not re-tip.
        var latestEventId = await _broadcast.GetLatestTipEventIdAsync().ConfigureAwait(false);
        SeedSnapshot(versions, Math.Max(eventId, latestEventId));

        return tipped.Count == 0
            ? BackgroundSyncRunResult.Skip("No Client tip needed.")
            : BackgroundSyncRunResult.Completed($"Client tip safety-net: {string.Join(", ", tipped)}");
    }

    private void SeedSnapshot(IReadOnlyDictionary<string, string> versions, long eventId)
    {
        lock (_gate)
        {
            _lastVersions = new Dictionary<string, string>(versions, StringComparer.OrdinalIgnoreCase);
            _lastEventId = eventId;
        }
    }

    private static bool VersionsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        foreach (var section in SyncSectionKeys.All)
        {
            left.TryGetValue(section, out var a);
            right.TryGetValue(section, out var b);
            if (!string.Equals(a ?? "0", b ?? "0", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
