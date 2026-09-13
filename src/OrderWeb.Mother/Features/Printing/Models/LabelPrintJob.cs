namespace POS_in_NET.Models;

/// <summary>
/// Immutable printable content captured when the job is created.
/// </summary>
public sealed record LabelContentSnapshot(
    string ContentType,
    string Name,
    int Quantity,
    IReadOnlyList<string> Modifiers,
    string? OrderContext,
    DateTimeOffset? PreparedAt,
    string? FixedLabelText)
{
    public static LabelContentSnapshot Item(string name) => Create("item", name);
    public static LabelContentSnapshot Component(string name) => Create("component", name);

    private static LabelContentSnapshot Create(string contentType, string name)
    {
        var printableName = name?.Trim();
        if (string.IsNullOrWhiteSpace(printableName))
            throw new ArgumentException("A label must have an item or component name.", nameof(name));
        if (printableName.Length > 300)
            throw new ArgumentOutOfRangeException(nameof(name), "A printable label name cannot exceed 300 characters.");
        return new LabelContentSnapshot(contentType, printableName, 1, Array.Empty<string>(), null, null, null);
    }
}

public sealed class LabelMediaProfile
{
    public string Id { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = "toshiba";
    public string ModelCode { get; set; } = "b-fv4d-gs14";
    public decimal WidthMm { get; set; }
    public decimal HeightMm { get; set; }
    public decimal GapMm { get; set; }
    public LabelSensorType SensorType { get; set; }
    public int Darkness { get; set; }
    public int SpeedIps { get; set; }
    public decimal HorizontalOffsetMm { get; set; }
    public decimal VerticalOffsetMm { get; set; }
    public LabelFinishingMode FinishingMode { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LabelPrintJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public int PrinterId { get; set; }
    public string MediaProfileId { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public int? OrderItemId { get; set; }
    public string SourceTerminal { get; set; } = string.Empty;
    public string? ClientRequestId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public int SendRevision { get; set; } = 1;
    public LabelJobType JobType { get; set; }
    public LabelContentSnapshot Content { get; set; } = LabelContentSnapshot.Item("Label");
    public string LabelTemplateVersion { get; set; } = "toshiba-text-v1";
    public int QuantityCopies { get; set; } = 1;
    public byte[]? GeneratedTpclData { get; set; }
    public string? GeneratedPayloadSha256 { get; set; }
    public LabelJobStatus Status { get; set; } = LabelJobStatus.Pending;
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public string? ClaimedByInstance { get; set; }
    public string? ClaimToken { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? AttentionAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? NetworkReachable { get; set; }
    public bool? PortReachable { get; set; }
    public bool? ProtocolConfirmed { get; set; }
    public bool? DataAccepted { get; set; }
    public bool PhysicalLabelConfirmed { get; set; }
    public byte[]? PrinterStatusResponse { get; set; }
    public ToshibaNetworkPhase? TransportPhase { get; set; }
    public DateTime? TransportCompletedAt { get; set; }
    public DateTime? PhysicalConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReprintOfJobId { get; set; }
}

public enum LabelJobType { Item, Component, Test }
public enum LabelJobStatus
{
    Created, Pending, Claimed, Printing, Completed, Failed,
    RetryWaiting, NeedsAttention, Cancelled, OutcomeUnknown
}
