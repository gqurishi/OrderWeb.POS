namespace POS_in_NET.Models;

public sealed class CreatePosReservationRequest
{
    public DateTime ReservationDate { get; set; }
    public TimeSpan ReservationTime { get; set; }
    public int Covers { get; set; } = 2;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string PromoCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Allergies { get; set; } = string.Empty;
    public string TableNumber { get; set; } = string.Empty;
    /// <summary>OrderWeb channel/source for local till bookings.</summary>
    public string Channel { get; set; } = "pos";
}
