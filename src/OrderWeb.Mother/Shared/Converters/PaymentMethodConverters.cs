using System.Globalization;

namespace POS_in_NET.Converters
{
    /// <summary>
    /// Converts payment method string to display text
    /// </summary>
    public class PaymentMethodTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return "Cash";

            var paymentMethod = value.ToString()?.ToLower() ?? "cash";

            return paymentMethod switch
            {
                "cash" => "Cash",
                "card" => "Card",
                "credit_card" => "Credit Card",
                "debit_card" => "Debit Card",
                "voucher" => "Gift Card",
                "gift_card" => "Gift Card",
                "giftcard" => "Gift Card",
                "online" => "Online",
                "online_payment" => "Online",
                "cod" => "COD",
                _ => "Cash" // Default fallback
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts payment method string to badge color
    /// </summary>
    public class PaymentMethodColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return Color.FromArgb("#10b981"); // Green for Cash (default)

            var paymentMethod = value.ToString()?.ToLower() ?? "cash";

            return paymentMethod switch
            {
                "cash" => Color.FromArgb("#10b981"),        // Green
                "card" => Color.FromArgb("#3b82f6"),        // Blue
                "credit_card" => Color.FromArgb("#3b82f6"), // Blue
                "debit_card" => Color.FromArgb("#3b82f6"),  // Blue
                "voucher" => Color.FromArgb("#a855f7"),     // Purple (Gift Card)
                "gift_card" => Color.FromArgb("#a855f7"),   // Purple
                "giftcard" => Color.FromArgb("#a855f7"),    // Purple
                "online" => Color.FromArgb("#f59e0b"),      // Orange
                "online_payment" => Color.FromArgb("#f59e0b"), // Orange
                "cod" => Color.FromArgb("#10b981"),         // Green
                _ => Color.FromArgb("#10b981")              // Default Green
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
