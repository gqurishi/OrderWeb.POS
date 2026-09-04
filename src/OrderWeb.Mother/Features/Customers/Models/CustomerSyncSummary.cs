namespace POS_in_NET.Models;

public class CustomerSyncSummary
{
    public int TotalRecent { get; set; }

    public int SyncedCount { get; set; }

    public int PendingCount { get; set; }

    public int FailedCount { get; set; }

    public int QueueCount { get; set; }

    public DateTime? LastCloudSyncAt { get; set; }

    public string LastCloudSyncDisplay =>
        LastCloudSyncAt.HasValue ? LastCloudSyncAt.Value.ToLocalTime().ToString("g") : "Never";
}
