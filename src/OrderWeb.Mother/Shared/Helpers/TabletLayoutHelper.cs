using Microsoft.Maui.Controls;

namespace POS_in_NET.Helpers;

/// <summary>
/// Keeps overlay cards inside the usable window area on small Windows tablets.
/// The values are in MAUI device-independent units, so they also remain usable
/// when Windows display scaling is enabled.
/// </summary>
public static class TabletLayoutHelper
{
    public static ResponsiveLayoutProfile GetProfile(double width, double height) =>
        ResponsiveLayoutProfile.ForSize(width, height);

    public static bool IsTablet(double width, double height) =>
        width > 0 && GetProfile(width, height).SizeClass != ResponsiveSizeClass.Large;

    public static bool IsShort(double height) => height > 0 && height <= 720;

    public static void AttachDialog(
        ContentView overlay,
        Border card,
        double preferredWidth,
        double preferredHeight,
        Action<bool, bool>? updateContent = null)
    {
        void Apply()
        {
            var width = overlay.Width;
            var height = overlay.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var profile = GetProfile(width, height);
            var tablet = profile.SizeClass != ResponsiveSizeClass.Large;
            var shortWindow = profile.IsShort;
            var margin = profile.DialogMargin;

            var availableWidth = Math.Max(280, width - (margin * 2));
            var cardWidth = Math.Min(preferredWidth, availableWidth);

            // Center explicitly. On WinUI, Fill combined with MaximumWidthRequest can
            // arrange an overlay card against the start of a multi-column page.
            card.HorizontalOptions = LayoutOptions.Center;
            card.VerticalOptions = LayoutOptions.Center;
            card.WidthRequest = cardWidth;
            card.Margin = new Thickness(margin);
            card.Padding = new Thickness(profile.DialogPadding);
            card.MaximumWidthRequest = cardWidth;
            card.MaximumHeightRequest = Math.Max(
                280,
                Math.Min(preferredHeight, height - (margin * 2) - profile.SafeBottom));

            updateContent?.Invoke(tablet, shortWindow);
        }

        overlay.SizeChanged += (_, _) => Apply();
        Apply();
    }

    public static void ApplyDashboardGrid(Grid grid, IReadOnlyList<View> tiles, double width, double height)
    {
        var profile = GetProfile(width, height);
        var columns = profile.IsCompact ? 2 : profile.SizeClass == ResponsiveSizeClass.Standard ? 3 : 4;

        grid.ColumnDefinitions.Clear();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        grid.RowDefinitions.Clear();
        var rows = (int)Math.Ceiling(tiles.Count / (double)columns);
        for (var row = 0; row < rows; row++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var index = 0; index < tiles.Count; index++)
        {
            Grid.SetRow(tiles[index], index / columns);
            Grid.SetColumn(tiles[index], index % columns);
        }

        grid.ColumnSpacing = profile.IsCompact ? 28 : profile.SizeClass == ResponsiveSizeClass.Standard ? 52 : 72;
        grid.RowSpacing = profile.IsCompact ? 20 : 32;
    }
}
