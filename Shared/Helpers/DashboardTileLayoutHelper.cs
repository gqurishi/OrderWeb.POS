namespace POS_in_NET.Helpers;

/// <summary>
/// Packs visible dashboard tiles without leaving holes when an order service
/// is disabled. Dashboards use a maximum of three tiles per row.
/// </summary>
public static class DashboardTileLayoutHelper
{
    public static void Arrange(Grid grid, params View[] orderedTiles)
    {
        var visibleTiles = orderedTiles.Where(tile => tile.IsVisible).ToList();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();

        if (visibleTiles.Count == 0)
        {
            return;
        }

        var columnCount = Math.Min(3, visibleTiles.Count);
        var rowCount = (int)Math.Ceiling(visibleTiles.Count / 3d);
        for (var column = 0; column < columnCount; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (var row = 0; row < rowCount; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var index = 0; index < visibleTiles.Count; index++)
        {
            var row = index / 3;
            var indexInRow = index % 3;
            var remainingInRow = Math.Min(3, visibleTiles.Count - (row * 3));
            var column = indexInRow;

            // With four tiles, keep three on the first row and centre the
            // fourth tile in the second row.
            if (columnCount == 3 && remainingInRow == 1)
            {
                column = 1;
            }

            Grid.SetRow(visibleTiles[index], row);
            Grid.SetColumn(visibleTiles[index], column);
            Grid.SetColumnSpan(visibleTiles[index], 1);
        }
    }
}
