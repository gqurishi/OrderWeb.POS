namespace POS_in_NET.Models;

public sealed class CloudReservation
{
    public int Id { get; set; }
    public string CloudId { get; set; } = string.Empty;
    public string? LocalId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime ReservationDate { get; set; }
    public TimeSpan ReservationTime { get; set; }
    public int Covers { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string PromoCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Allergies { get; set; } = string.Empty;
    public string Status { get; set; } = "confirmed";
    public string Source { get; set; } = string.Empty;
    public string TableNumber { get; set; } = string.Empty;
    public int DepositAmountPence { get; set; }
    public DateTime? PosSeenAt { get; set; }
    public string? PosPrintStatus { get; set; }
    public string UploadStatus { get; set; } = "synced";
    public DateTime? CloudCreatedAt { get; set; }
    public DateTime? CloudUpdatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }

    public bool IsPendingUpload => string.Equals(UploadStatus, "pending", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(UploadStatus, "failed", StringComparison.OrdinalIgnoreCase);
}
