namespace POS_in_NET.Helpers;

public enum ResponsiveSizeClass
{
    Compact,
    Standard,
    Large
}

public readonly record struct ResponsiveLayoutProfile(
    ResponsiveSizeClass SizeClass,
    double Width,
    double Height,
    double PagePadding,
    double DialogMargin,
    double DialogPadding,
    double SafeBottom,
    double Spacing,
    double TextScale)
{
    public bool IsCompact => SizeClass == ResponsiveSizeClass.Compact;
    public bool IsShort => Height > 0 && Height <= 720;

    public static ResponsiveLayoutProfile ForSize(double width, double height)
    {
        var sizeClass = width <= 1280 || height <= 720
            ? ResponsiveSizeClass.Compact
            : width <= 1600 || height <= 900
                ? ResponsiveSizeClass.Standard
                : ResponsiveSizeClass.Large;

        return sizeClass switch
        {
            ResponsiveSizeClass.Compact => new(sizeClass, width, height, 12, 8, 16, 12, 10, 0.90),
            ResponsiveSizeClass.Standard => new(sizeClass, width, height, 16, 12, 20, 14, 14, 1.0),
            _ => new(sizeClass, width, height, 20, 16, 28, 16, 18, 1.08)
        };
    }
}
