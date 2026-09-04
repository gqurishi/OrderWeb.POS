using System.Globalization;

namespace POS_in_NET.Converters
{
    public class BoolToBorderColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                return Color.FromArgb("#3B82F6"); // Blue border when selected
            }
            return Color.FromArgb("#E2E8F0"); // Light gray border when not selected
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
