using POS_in_NET.Models;

namespace POS_in_NET.Services;

public static class OnlineOrderPaymentHelper
{
    public static string NormalizeMethod(string? paymentMethod)
    {
        var value = NormalizeToken(paymentMethod);

        return value switch
        {
            "card" or "creditcard" or "debitcard" or "stripe" => "card",
            "voucher" or "giftcard" or "giftvoucher" => "gift_card",
            "online" or "onlinepayment" or "paidonline" => "online",
            "cod" or "cashondelivery" or "cashoncollection" or "cash" => "cash",
            _ => string.IsNullOrWhiteSpace(paymentMethod) ? "cash" : paymentMethod.Trim().ToLowerInvariant()
        };
    }

    public static bool IsDeferredPaymentMethod(string? paymentMethod)
    {
        return NormalizeMethod(paymentMethod) == "cash";
    }

    public static bool IsPaidFromSource(string? paymentMethod, string? paymentStatus)
    {
        if (IsDeferredPaymentMethod(paymentMethod))
        {
            return false;
        }

        var status = NormalizeToken(paymentStatus);
        if (string.IsNullOrWhiteSpace(status))
        {
            var method = NormalizeMethod(paymentMethod);
            return method is "card" or "gift_card" or "online";
        }

        return status is "paid" or "complete" or "completed" or "captured" or "settled" or "success" or "succeeded";
    }

    public static PaymentStatus ToPaymentStatus(string? paymentMethod, string? paymentStatus)
    {
        return IsPaidFromSource(paymentMethod, paymentStatus)
            ? PaymentStatus.Paid
            : PaymentStatus.Pending;
    }

    public static string GetDisplayMethod(string? paymentMethod)
    {
        return NormalizeMethod(paymentMethod) switch
        {
            "card" => "Card",
            "gift_card" => "Gift Card",
            "online" => "Online Payment",
            "cash" => "Cash",
            var value when !string.IsNullOrWhiteSpace(value) => value,
            _ => "Cash"
        };
    }

    public static string GetStorageMethod(string? paymentMethod)
    {
        var method = NormalizeMethod(paymentMethod);
        return method == "online" ? "card" : method;
    }

    private static string NormalizeToken(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
    }
}
