namespace POS_in_NET.Services;

public static class RefundAmountPolicy
{
    public static decimal Remaining(decimal originalTotal, decimal alreadyRefunded) =>
        Math.Max(0m, originalTotal - Math.Max(0m, alreadyRefunded));

    public static (bool IsValid, string Message) Validate(
        decimal requestedAmount,
        decimal originalTotal,
        decimal alreadyRefunded)
    {
        var remaining = Remaining(originalTotal, alreadyRefunded);
        if (requestedAmount <= 0)
        {
            return (false, "Refund amount must be greater than zero.");
        }

        if (requestedAmount > remaining)
        {
            return (false, remaining <= 0
                ? "This order has already been fully refunded."
                : $"Only £{remaining:F2} remains available to refund.");
        }

        return (true, "Refund amount is within the remaining order total.");
    }
}
