namespace POS_in_NET.Models;

public sealed record ClaimedLabelPrintJob(LabelPrintJob Job, string ClaimToken);

public sealed record LabelQueueActionResult(bool Success, string Message, string? JobId = null);

