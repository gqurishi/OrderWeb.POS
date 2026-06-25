namespace POS_in_NET.Models;

public class CustomerDataRecord
{
    public int Id { get; set; }

    public string OrderTypes { get; set; } = "collection";

    public string Name { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string FullAddress { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string County { get; set; } = string.Empty;

    public string Postcode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastCollectionOrderDate { get; set; }

    public DateTime? LastDeliveryOrderDate { get; set; }

    public string CloudCustomerId { get; set; } = string.Empty;

    public string PhoneNormalized { get; set; } = string.Empty;

    public string SyncStatus { get; set; } = "pending";

    public string SyncError { get; set; } = string.Empty;

    public DateTime CachedAt { get; set; }

    public DateTime LastUsedAt { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public int PointsBalance { get; set; }

    public string TierLevel { get; set; } = string.Empty;

    public string SyncStatusDisplay => SyncStatus switch
    {
        "synced" => "Synced",
        "failed" => "Failed",
        _ => "Pending"
    };

    public bool ShowSyncedBadge => SyncStatus.Equals("synced", StringComparison.OrdinalIgnoreCase);

    public bool ShowPendingSyncBadge =>
        !SyncStatus.Equals("synced", StringComparison.OrdinalIgnoreCase)
        && !SyncStatus.Equals("failed", StringComparison.OrdinalIgnoreCase);

    public bool ShowFailedSyncBadge => SyncStatus.Equals("failed", StringComparison.OrdinalIgnoreCase);

    public string LastUsedDisplay =>
        LastUsedAt == default ? "—" : LastUsedAt.ToLocalTime().ToString("g");

    public string OrderTypesDisplay => OrderTypes switch
    {
        "both" => "Collection & Delivery",
        "delivery" => "Delivery",
        _ => "Collection"
    };

    public string OrderTypesShort => OrderTypes switch
    {
        "both" => "collection & delivery",
        "delivery" => "delivery",
        _ => "collection"
    };

    public bool ShowCollectionBadge =>
        OrderTypes.Equals("collection", StringComparison.OrdinalIgnoreCase)
        || OrderTypes.Equals("both", StringComparison.OrdinalIgnoreCase);

    public bool ShowDeliveryBadge =>
        OrderTypes.Equals("delivery", StringComparison.OrdinalIgnoreCase)
        || OrderTypes.Equals("both", StringComparison.OrdinalIgnoreCase);

    public string ContactDetail
    {
        get
        {
            var parts = new List<string> { PhoneNumber.Trim() };
            var address = CompactAddress;
            if (!string.IsNullOrWhiteSpace(address))
            {
                parts.Add(address);
            }

            return " - " + string.Join(" - ", parts);
        }
    }

    /// <summary>Single-line list display: Name - type - phone - address</summary>
    public string OneLineSummary
    {
        get
        {
            var parts = new List<string>
            {
                Name.Trim(),
                OrderTypesShort,
                PhoneNumber.Trim()
            };

            var address = CompactAddress;
            if (!string.IsNullOrWhiteSpace(address))
            {
                parts.Add(address);
            }

            return string.Join(" - ", parts);
        }
    }

    private string CompactAddress
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FullAddress))
            {
                return FullAddress
                    .Replace('\n', ' ')
                    .Replace('\r', ' ')
                    .Replace("  ", " ")
                    .Trim();
            }

            return string.Join(" ", new[] { City, County, Postcode }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    public string AddressSummary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FullAddress))
            {
                return FullAddress.Replace("\n", ", ");
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(City)) parts.Add(City);
            if (!string.IsNullOrWhiteSpace(County)) parts.Add(County);
            if (!string.IsNullOrWhiteSpace(Postcode)) parts.Add(Postcode);
            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }
    }

    public DateTime? LastOrderDate
    {
        get
        {
            if (LastCollectionOrderDate.HasValue && LastDeliveryOrderDate.HasValue)
            {
                return LastCollectionOrderDate > LastDeliveryOrderDate
                    ? LastCollectionOrderDate
                    : LastDeliveryOrderDate;
            }

            return LastCollectionOrderDate ?? LastDeliveryOrderDate;
        }
    }
}
