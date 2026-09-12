namespace OrderWeb.SharedUI.Views;

/// <summary>Form result from the shared new-booking dialog. Hosts map this onto their own save request.</summary>
public sealed class NewReservationDraft
{
    public DateTime ReservationDate { get; init; }
    public TimeSpan ReservationTime { get; init; }
    public int Covers { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string PromoCode { get; init; } = string.Empty;
    public string TableNumber { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string Allergies { get; init; } = string.Empty;
}
