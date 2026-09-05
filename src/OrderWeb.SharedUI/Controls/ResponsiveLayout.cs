namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Shared responsive breakpoints and helpers so Mother and Client use the same layout rules.
/// </summary>
public static class ResponsiveLayout
{
    public const double CompactMaxWidth = 900;
    public const double RegularMaxWidth = 1280;

    public enum SizeClass
    {
        Compact,
        Regular,
        Expanded
    }

    public static SizeClass Classify(double width) =>
        width switch
        {
            <= CompactMaxWidth => SizeClass.Compact,
            <= RegularMaxWidth => SizeClass.Regular,
            _ => SizeClass.Expanded
        };

    public static Thickness PagePadding(SizeClass size) =>
        size switch
        {
            SizeClass.Compact => ControlResources.Value("PosPagePaddingCompact", new Thickness(16)),
            _ => ControlResources.Value("PosPagePadding", new Thickness(24))
        };

    public static double SidebarWidth(SizeClass size) =>
        size == SizeClass.Compact
            ? ControlResources.Value("PosSidebarWidthCompact", 240d)
            : ControlResources.Value("PosSidebarWidth", 280d);

    public static bool PreferSingleColumn(SizeClass size) => size == SizeClass.Compact;

    /// <summary>
    /// Applies shared page padding from Pos* tokens based on the current width.
    /// </summary>
    public static void ApplyPagePadding(View view, double width) =>
        view.Padding = PagePadding(Classify(width));
}
