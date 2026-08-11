using POS_in_NET.Models;

namespace POS_in_NET.Services;

public static class OnlineOrderPaymentHelper
{
    public static string NormalizeMethod(string? paymentMethod)
    {
        var value = NormalizeToken(paymentMethod);

        return value switch
        {
            "card" or "creditcard" or "debitcard" or "visa" or "mastercard" or "mastercarddebit" or "amex" or "americanexpress" => "card",
            "voucher" or "giftcard" or "giftvoucher" => "gift_card",
            "online" or "onlinepayment" or "paidonline" => "online",
            "applepay" => "apple_pay",
            "googlepay" => "google_pay",
            "paypal" => "paypal",
            "cod" or "cashondelivery" or "cashoncollection" or "cash" => "cash",
            _ => string.IsNullOrWhiteSpace(paymentMethod) ? "unknown" : paymentMethod.Trim().ToLowerInvariant()
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
            return method is "card" or "gift_card" or "online" or "apple_pay" or "google_pay" or "paypal";
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
            "apple_pay" => "Apple Pay",
            "google_pay" => "Google Pay",
            "paypal" => "PayPal",
            "cash" => "Cash",
            "unknown" => "Not supplied",
            var value when !string.IsNullOrWhiteSpace(value) => Humanize(value),
            _ => "Cash"
        };
    }

    public static string GetStorageMethod(string? paymentMethod)
    {
        return NormalizeMethod(paymentMethod);
    }

    public static string GetStatusDisplay(string? paymentMethod, string? paymentStatus)
    {
        if (IsPaidFromSource(paymentMethod, paymentStatus))
        {
            return "Paid";
        }

        var status = NormalizeToken(paymentStatus);
        return status switch
        {
            "failed" or "declined" or "cancelled" or "canceled" => "Payment failed",
            "refunded" => "Refunded",
            "partiallypaid" or "partial" => "Partially paid",
            _ when IsDeferredPaymentMethod(paymentMethod) => "Pay on collection/delivery",
            _ => "Payment pending"
        };
    }

    private static string Humanize(string value)
    {
        var words = value.Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    public static string? FormatReceiptReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var value = reference.Trim();
        return value.Length <= 24 ? value : $"...{value[^21..]}";
    }

    public static string? MaskVoucherCode(string? voucherCode)
    {
        if (string.IsNullOrWhiteSpace(voucherCode)) return null;
        var value = voucherCode.Trim();
        return value.Length <= 4 ? value : $"****{value[^4..]}";
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
