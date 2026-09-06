namespace OrderWeb.SharedUI.Responsive;

public enum PosLayoutSizeClass
{
    SmallTablet,
    MediumTablet,
    LargeTablet,
    Desktop
}

public enum PosBasketPlacement
{
    Drawer,
    Docked
}

public readonly record struct PosResponsiveProfile(
    PosLayoutSizeClass SizeClass,
    double Width,
    double Height,
    double PagePadding,
    double SectionSpacing,
    double SidebarWidth,
    PosBasketPlacement BasketPlacement,
    bool UseCompactHeader)
{
    public bool IsTablet => SizeClass != PosLayoutSizeClass.Desktop;
}

public static class PosResponsiveLayout
{
    public const double MediumTabletMinimumWidth = 801;
    public const double LargeTabletMinimumWidth = 1281;
    public const double DesktopMinimumWidth = 1601;

    public static PosResponsiveProfile ForSize(double width, double height)
    {
        var safeWidth = Math.Max(0, width);
        var safeHeight = Math.Max(0, height);

        if (safeWidth < MediumTabletMinimumWidth)
        {
            return new(PosLayoutSizeClass.SmallTablet, safeWidth, safeHeight, 12, 10, 72,
                PosBasketPlacement.Drawer, true);
        }

        if (safeWidth < LargeTabletMinimumWidth)
        {
            return new(PosLayoutSizeClass.MediumTablet, safeWidth, safeHeight, 16, 12, 88,
                PosBasketPlacement.Drawer, safeHeight <= 720);
        }

        if (safeWidth < DesktopMinimumWidth)
        {
            return new(PosLayoutSizeClass.LargeTablet, safeWidth, safeHeight, 20, 14, 240,
                PosBasketPlacement.Docked, safeHeight <= 720);
        }

        return new(PosLayoutSizeClass.Desktop, safeWidth, safeHeight, 24, 16, 280,
            PosBasketPlacement.Docked, false);
    }
}
