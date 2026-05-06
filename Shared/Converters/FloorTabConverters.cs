using Microsoft.Maui.Graphics;
using System.Globalization;

namespace POS_in_NET.Converters
{
    /// <summary>
    /// Converts boolean selection state to background color for floor tabs
    /// </summary>
    public class BoolToFloorTabBackgroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSelected)
            {
                return isSelected ? Color.FromArgb("#5E81AC") : Color.FromArgb("#E5E9F0"); // Blue for selected, light gray for unselected
            }
            return Color.FromArgb("#E5E9F0");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts boolean selection state to text color for floor tabs
    /// </summary>
    public class BoolToFloorTabTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSelected)
            {
                return isSelected ? Colors.White : Color.FromArgb("#2E3440"); // White for selected, dark for unselected
            }
            return Color.FromArgb("#2E3440");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts boolean selection state to border color for floor tabs
    /// </summary>
    public class BoolToFloorTabBorderConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSelected)
            {
                return isSelected ? Color.FromArgb("#4C566A") : Color.FromArgb("#D8DEE9"); // Dark border for selected, light for unselected
            }
            return Color.FromArgb("#D8DEE9");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}