namespace POS_in_NET.Models;

/// <summary>
/// Printer-neutral content consumed by the Toshiba TPCL label module.
/// Values are captured with the job so a later order edit cannot change a reprint.
/// </summary>
public sealed record ToshibaTpclLabelContent(
    string ItemName,
    int Quantity,
    IReadOnlyList<string> Modifiers,
    string? OrderContext,
    DateTimeOffset? PreparedAt);

