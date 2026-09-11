namespace OrderWeb.SharedUI.Payments;

/// <summary>Result from SharedUI SELECT PAYMENT METHOD (Mother chrome).</summary>
public enum PaymentMethodChoice
{
    Cancelled = 0,
    Cash,
    Card,
    GiftCard,
    Loyalty
}
