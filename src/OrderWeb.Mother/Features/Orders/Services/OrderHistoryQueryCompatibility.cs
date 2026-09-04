namespace POS_in_NET.Services;

public static class OrderHistoryQueryCompatibility
{
    public static string PaymentStatusProjection(bool columnAvailable) =>
        columnAvailable ? "o.payment_status" : "NULL";
}
